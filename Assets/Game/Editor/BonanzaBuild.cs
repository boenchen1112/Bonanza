using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

// Batch-mode entry points driven by Tools/unity-batch.sh. Every method is a static
// -executeMethod target; failures throw or call EditorApplication.Exit(1) so the
// process exit code is non-zero. PlayerSettings are not rewritten here except the WebGL page
// template (Assets/WebGLTemplates/Bonanza): the other WebGL settings stored in ProjectSettings
// (Brotli, no decompression fallback, ...) are used as they are, and the build only asserts the
// ones that matter.
public static class BonanzaBuild
{
    static string ProjectRoot => Directory.GetParent(Application.dataPath).FullName;

    static string Arg(string name, string fallback = null)
    {
        var a = Environment.GetCommandLineArgs();
        for (int i = 0; i < a.Length - 1; i++) if (a[i] == name) return a[i + 1];
        return fallback;
    }

    // ---- build ------------------------------------------------------------------

    public static void BuildWebGL()
    {
        var scenes = Array.FindAll(EditorBuildSettings.scenes, s => s.enabled);
        if (scenes.Length == 0) throw new Exception("No enabled scenes in EditorBuildSettings.");
        var outDir = Arg("-outputPath", Path.Combine(ProjectRoot, "Builds", "WebGL"));
        if (PlayerSettings.WebGL.compressionFormat != WebGLCompressionFormat.Brotli)
            throw new Exception("WebGL compression is " + PlayerSettings.WebGL.compressionFormat + ", expected Brotli.");
        if (PlayerSettings.WebGL.decompressionFallback)
            throw new Exception("WebGL decompression fallback is on; the gates expect .br files.");

        const string Template = "PROJECT:Bonanza";
        if (PlayerSettings.WebGL.template != Template)
        {
            PlayerSettings.WebGL.template = Template;
            Debug.Log("[BonanzaBuild] WebGL template -> " + Template);
        }

        var opts = new BuildPlayerOptions
        {
            scenes = Array.ConvertAll(scenes, s => s.path),
            locationPathName = outDir,
            target = BuildTarget.WebGL,
            targetGroup = BuildTargetGroup.WebGL,
            options = BuildOptions.None,
        };
        Debug.Log("[BonanzaBuild] Building " + string.Join(", ", opts.scenes) + " -> " + outDir);
        var report = BuildPipeline.BuildPlayer(opts);
        var sum = report.summary;
        Debug.Log("[BonanzaBuild] result=" + sum.result + " errors=" + sum.totalErrors + " warnings=" + sum.totalWarnings
                  + " size=" + sum.totalSize + " time=" + sum.totalTime);
        if (sum.result != BuildResult.Succeeded)
            throw new Exception("WebGL build " + sum.result + " (" + sum.totalErrors + " errors).");
    }

    // ---- import -----------------------------------------------------------------

    public static void Import()
    {
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        AssetDatabase.SaveAssets();
        Debug.Log("[BonanzaBuild] import done");
    }

    // ---- screenshot -------------------------------------------------------------
    // args: -scene <path> -out <png> [-width N -height M]
    // Opens the scene in edit mode and renders the main camera into a half-float target,
    // then blits to sRGB 8-bit (same as Tools/hdr-shot.cs) so URP's grade sees real HDR.
    // Screen-space-overlay canvases (uGUI HUD) are NOT part of a camera render.

    public static void Screenshot()
    {
        var scene = Arg("-scene", "Assets/Scenes/SwingKings.unity");
        var outPath = Arg("-out", Path.Combine(ProjectRoot, "Captures", "batch-shot.png"));
        int w = int.Parse(Arg("-width", "1280")), h = int.Parse(Arg("-height", "720"));
        try
        {
            EditorSceneManager.OpenScene(scene, OpenSceneMode.Single);
            var cam = Camera.main;
            if (cam == null) cam = UnityEngine.Object.FindFirstObjectByType<Camera>();
            if (cam == null) throw new Exception("No camera in " + scene);
            SavePng(cam, w, h, outPath);
            Debug.Log("[BonanzaBuild] screenshot ok " + outPath);
        }
        catch (Exception e)
        {
            Debug.LogError("[BonanzaBuild] screenshot failed: " + e);
            EditorApplication.Exit(1);
        }
    }

    static void SavePng(Camera cam, int w, int h, string outPath)
    {
        var hdr = new RenderTexture(w, h, 24, RenderTextureFormat.DefaultHDR);
        var ldr = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
        var oldTarget = cam.targetTexture;
        var oldActive = RenderTexture.active;
        try
        {
            cam.targetTexture = hdr;
            cam.Render();
            cam.targetTexture = oldTarget;
            Graphics.Blit(hdr, ldr);
            RenderTexture.active = ldr;
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath)));
            File.WriteAllBytes(outPath, tex.EncodeToPNG());
        }
        finally
        {
            cam.targetTexture = oldTarget;
            RenderTexture.active = oldActive;
            UnityEngine.Object.DestroyImmediate(tex);
            hdr.Release(); ldr.Release();
            UnityEngine.Object.DestroyImmediate(hdr); UnityEngine.Object.DestroyImmediate(ldr);
        }
    }

    // ---- play capture -----------------------------------------------------------
    // args: -scene <path> -outDir <dir> -seconds N -every S -width -height
    // Enters Play Mode (domain reload runs, so state lives in SessionState), lets the
    // game run for N real seconds and writes a camera render every S seconds, then exits.
    // BONANZA_AUTOPLAY / BONANZA_GAME / BONANZA_PARTY are read from the process env.

    const string KeyActive = "Bonanza.PlayCapture.Active";
    const string KeyConfig = "Bonanza.PlayCapture.Config";

    [Serializable] class CaptureConfig { public string outDir; public double seconds, every; public int w, h; }

    public static void PlayCapture()
    {
        var scene = Arg("-scene", "Assets/Scenes/SwingKings.unity");
        var cfg = new CaptureConfig
        {
            outDir = Arg("-outDir", Path.Combine(ProjectRoot, "Captures", "play")),
            seconds = double.Parse(Arg("-seconds", "10")),
            every = double.Parse(Arg("-every", "2")),
            w = int.Parse(Arg("-width", "1280")), h = int.Parse(Arg("-height", "720")),
        };
        Directory.CreateDirectory(cfg.outDir);
        EditorSceneManager.OpenScene(scene, OpenSceneMode.Single);
        SessionState.SetString(KeyConfig, JsonUtility.ToJson(cfg));
        SessionState.SetBool(KeyActive, true);
        PlayCaptureRunner.Arm(); // needed when "Enter Play Mode Options" disables the domain reload (this project does)
        EditorApplication.EnterPlaymode();
    }

    [InitializeOnLoad]
    public static class PlayCaptureRunner
    {
        static CaptureConfig _cfg;
        static double _start, _next;
        static int _n;

        static PlayCaptureRunner()
        {
            if (SessionState.GetBool(KeyActive, false)) Arm();
        }

        public static void Arm()
        {
            _cfg = JsonUtility.FromJson<CaptureConfig>(SessionState.GetString(KeyConfig, "{}"));
            EditorApplication.playModeStateChanged -= OnState;
            EditorApplication.playModeStateChanged += OnState;
            if (EditorApplication.isPlaying) Begin();
        }

        static void OnState(PlayModeStateChange s)
        {
            if (s == PlayModeStateChange.EnteredPlayMode) Begin();
        }

        static void Begin()
        {
            Application.runInBackground = true;
            _start = EditorApplication.timeSinceStartup;
            _next = 0; _n = 0;
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
            Debug.Log("[BonanzaBuild] play capture started");
        }

        static void Tick()
        {
            double t = EditorApplication.timeSinceStartup - _start;
            if (t >= _next)
            {
                _next += _cfg.every;
                try
                {
                    var cam = Camera.main ? Camera.main : UnityEngine.Object.FindFirstObjectByType<Camera>();
                    if (cam != null)
                        SavePng(cam, _cfg.w, _cfg.h, Path.Combine(_cfg.outDir, "frame-" + (_n++).ToString("D3") + ".png"));
                }
                catch (Exception e) { Debug.LogError("[BonanzaBuild] capture failed: " + e.Message); }
            }
            if (t >= _cfg.seconds)
            {
                EditorApplication.update -= Tick;
                SessionState.SetBool(KeyActive, false);
                Debug.Log("[BonanzaBuild] play capture done frames=" + _n);
                EditorApplication.Exit(0);
            }
        }
    }
}
