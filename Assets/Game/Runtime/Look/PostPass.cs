// The post chain: bloom and the composite grade (web render/look/post.js).
//
//   scene (HDR) -> copy -> bright cut (1/4 res) -> blur H -> blur V
//                       -> [high tier: 1/8 res blur H -> V for a wide halo]
//                       -> composite into the camera colour
//
// One render-graph pass class, enqueued per Game camera from
// RenderPipelineManager.beginCameraRendering (registered once, globally; no
// renderer-asset sub-asset needed). It only runs while a LookRig is alive
// (PostState.Active), so games that are not on the look kit render untouched.
// Bloom is skipped entirely on the Low tier.

using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;
using UnityEngine.Rendering.Universal;
using UnityEngine.Experimental.Rendering;

namespace Bonanza.Look
{
    /// What the LookRig hands the post chain each frame.
    public static class PostState
    {
        public static int Frame = -1;           // Time.frameCount of the last LookRig update
        public static bool Active => Frame >= Time.frameCount - 1;
        public static float Bloom;               // palette Bloom
        public static float Threshold = 1.35f;   // palette BloomThreshold
        public const float Knee = 0.45f;
        /// Bloom chain on (Medium and High).
        public static bool BloomOn => Active && Bloom > 0.001f && RenderQuality.Level.Tier != QualityTier.Low;
        /// Wide (1/8 res) halo, High tier only.
        public static bool Wide => RenderQuality.Level.Tier == QualityTier.High;
        /// Opt-in edge smoothing in the composite (0 = off, the default): a game whose silhouettes stair-step at a
        /// reduced render scale (Bounce Brigade's clouds) sets it while it runs and puts it back to 0 when it ends.
        public static float EdgeAA;
    }

    public sealed class PostPass : ScriptableRenderPass
    {
        static PostPass _inst;
        static Material _mat;
        static bool _hooked, _logged;

        static readonly int BlitTex = Shader.PropertyToID("_BlitTexture");
        static readonly int BlitScaleBias = Shader.PropertyToID("_BlitScaleBias");
        static readonly int Texel = Shader.PropertyToID("_BonTexel");
        static readonly int BloomA = Shader.PropertyToID("_BonBloomA");
        static readonly int BloomB = Shader.PropertyToID("_BonBloomB");
        static readonly int BloomParams = Shader.PropertyToID("_BonBloomParams");

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Hook()
        {
            if (_hooked) return;
            _hooked = true;
            RenderPipelineManager.beginCameraRendering += OnBegin;
        }

#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
        static void EditorHook() => Hook();
#endif

        public static Material Mat => _mat ? _mat : _mat = Resources.Load<Material>("Look/Composite");

        static void OnBegin(ScriptableRenderContext ctx, Camera cam)
        {
            if (cam.cameraType != CameraType.Game || !PostState.Active || Mat == null) return;
            var data = cam.GetUniversalAdditionalCameraData();
            if (data == null || data.scriptableRenderer == null) return;
            if (_inst == null)
            {
                _inst = new PostPass();
                _inst.profilingSampler = new ProfilingSampler("Bonanza Post");
                _inst.renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing;
                _inst.requiresIntermediateTexture = true;
            }
            data.scriptableRenderer.EnqueuePass(_inst);
        }

        sealed class Data
        {
            public Material Mat; public int Pass; public TextureHandle Src, Dst, A, B; public Vector4 Texel; public bool Bloom;
            public MaterialPropertyBlock Mpb = new MaterialPropertyBlock();
        }

        static readonly System.Collections.Generic.Stack<Data> _pool = new System.Collections.Generic.Stack<Data>();

        public override void RecordRenderGraph(RenderGraph rg, ContextContainer frameData)
        {
            var res = frameData.Get<UniversalResourceData>();
            if (!res.cameraColor.IsValid()) return;
            var m = Mat;
            if (m == null) return;

            var desc = rg.GetTextureDesc(res.cameraColor);
            if (!_logged) { _logged = true; Debug.Log($"[POST] camera colour {desc.colorFormat} {desc.width}x{desc.height} msaa={desc.msaaSamples} bloom={PostState.BloomOn} wide={PostState.Wide} tier={RenderQuality.Level}"); }
            var srcDesc = desc;
            srcDesc.name = "_BonanzaSceneCopy";
            srcDesc.clearBuffer = false;
            var copy = rg.CreateTexture(srcDesc);
            rg.AddBlitPass(res.cameraColor, copy, Vector2.one, Vector2.zero, passName: "Bonanza Copy Color");

            // Pin the bloom threshold/knee for the bright pass (globals are read by the material).
            Shader.SetGlobalVector(BloomParams, new Vector4(PostState.Threshold, PostState.Knee, 0, 0));

            TextureHandle a = TextureHandle.nullHandle, b = TextureHandle.nullHandle;
            if (PostState.BloomOn)
            {
                int w = Mathf.Max(2, desc.width / 4), h = Mathf.Max(2, desc.height / 4);
                var qd = new TextureDesc(w, h)
                {
                    colorFormat = desc.colorFormat, clearBuffer = false, filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp, msaaSamples = MSAASamples.None, name = "_BonanzaBloomQ0",
                };
                var q0 = rg.CreateTexture(qd);
                qd.name = "_BonanzaBloomQ1";
                var q1 = rg.CreateTexture(qd);
                Raster(rg, "Bonanza Bright", m, 0, copy, q0, default, default, new Vector4(1f / desc.width, 1f / desc.height, 0, 0));
                Raster(rg, "Bonanza BlurH", m, 1, q0, q1, default, default, new Vector4(1f / w, 1f / h, 0, 0));
                Raster(rg, "Bonanza BlurV", m, 2, q1, q0, default, default, new Vector4(1f / w, 1f / h, 0, 0));
                a = q0;
                b = q0;   // overwritten below on High; Medium weights it 0
                if (PostState.Wide)
                {
                    int w8 = Mathf.Max(2, w / 2), h8 = Mathf.Max(2, h / 2);
                    var ed = qd; ed.width = w8; ed.height = h8;
                    ed.name = "_BonanzaBloomE0"; var e0 = rg.CreateTexture(ed);
                    ed.name = "_BonanzaBloomE1"; var e1 = rg.CreateTexture(ed);
                    Raster(rg, "Bonanza Wide H", m, 1, q0, e0, default, default, new Vector4(1f / w8, 1f / h8, 0, 0));
                    Raster(rg, "Bonanza Wide V", m, 2, e0, e1, default, default, new Vector4(1f / w8, 1f / h8, 0, 0));
                    b = e1;
                }
            }

            // Composite into the camera colour. With bloom off the bloom inputs are bound to black.
            Raster(rg, "Bonanza Composite", m, 3, copy, res.activeColorTexture, a, b,
                   new Vector4(1f / desc.width, 1f / desc.height, PostState.EdgeAA, 0), bloomActive: PostState.BloomOn);
        }

        static void Raster(RenderGraph rg, string name, Material m, int pass, TextureHandle src, TextureHandle dst,
                           TextureHandle a, TextureHandle b, Vector4 texel, bool bloomActive = false)
        {
            using (var builder = rg.AddRasterRenderPass<Data>(name, out var d))
            {
                d.Mat = m; d.Pass = pass; d.Src = src; d.Dst = dst; d.A = a; d.B = b; d.Texel = texel; d.Bloom = bloomActive;
                builder.UseTexture(src, AccessFlags.Read);
                if (bloomActive)
                {
                    if (a.IsValid()) builder.UseTexture(a, AccessFlags.Read);
                    if (b.IsValid() && !b.Equals(a)) builder.UseTexture(b, AccessFlags.Read);
                }
                builder.SetRenderAttachment(dst, 0, AccessFlags.Write);
                builder.AllowPassCulling(false);
                builder.SetRenderFunc(static (Data data, RasterGraphContext c) =>
                {
                    var mpb = data.Mpb;
                    mpb.Clear();
                    RTHandle srcH = data.Src;
                    mpb.SetTexture(BlitTex, srcH);
                    bool flip = c.GetTextureUVOrigin(in data.Src) != c.GetTextureUVOrigin(in data.Dst);
                    mpb.SetVector(BlitScaleBias, flip ? new Vector4(1, -1, 0, 1) : new Vector4(1, 1, 0, 0));
                    mpb.SetVector(Texel, data.Texel);
                    if (data.Pass == 3)
                    {
                        if (data.Bloom && data.A.IsValid()) { RTHandle h = data.A; mpb.SetTexture(BloomA, h); }
                        else mpb.SetTexture(BloomA, Texture2D.blackTexture);
                        if (data.Bloom && data.B.IsValid()) { RTHandle h = data.B; mpb.SetTexture(BloomB, h); }
                        else mpb.SetTexture(BloomB, Texture2D.blackTexture);
                    }
                    c.cmd.DrawProcedural(Matrix4x4.identity, data.Mat, data.Pass, MeshTopology.Triangles, 3, 1, mpb);
                });
            }
        }
    }
}
