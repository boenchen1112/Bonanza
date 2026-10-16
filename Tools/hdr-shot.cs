// Editor screenshot through an HDR target (run in Play Mode):
//   sed 's#__OUT__#/abs/path/shot.png#' Tools/hdr-shot.cs > /tmp/hdr-shot.cs
//   unity command eval_file --file /tmp/hdr-shot.cs
// `unity command screenshot` renders the camera into an 8-bit target, which makes URP's
// intermediate colour buffer 8-bit too: lit whites clamp at 1.0 before the composite pass
// (a white plate reads 195 instead of ~220). Here the camera renders into a half-float
// target and the finished frame is blitted to sRGB 8-bit, so the grade sees real HDR values.
var cam = UnityEngine.Camera.main;
int W = 1280, H = 720;
var hdr = new UnityEngine.RenderTexture(W, H, 24, UnityEngine.RenderTextureFormat.DefaultHDR);
var old = cam.targetTexture;
cam.targetTexture = hdr;
cam.Render();
cam.targetTexture = old;
var ldr = new UnityEngine.RenderTexture(W, H, 0, UnityEngine.RenderTextureFormat.ARGB32, UnityEngine.RenderTextureReadWrite.sRGB);
UnityEngine.Graphics.Blit(hdr, ldr);
var prev = UnityEngine.RenderTexture.active;
UnityEngine.RenderTexture.active = ldr;
var tex = new UnityEngine.Texture2D(W, H, UnityEngine.TextureFormat.RGB24, false);
tex.ReadPixels(new UnityEngine.Rect(0, 0, W, H), 0, 0);
tex.Apply();
UnityEngine.RenderTexture.active = null;
System.IO.File.WriteAllBytes("__OUT__", UnityEngine.ImageConversion.EncodeToPNG(tex));
UnityEngine.Object.DestroyImmediate(tex); hdr.Release(); ldr.Release();
UnityEngine.Object.DestroyImmediate(hdr); UnityEngine.Object.DestroyImmediate(ldr);
return "ok";
