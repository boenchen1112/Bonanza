using UnityEditor;
using UnityEngine;

// Imports the baked SFX (Tools/sfx/render-sfx.mjs -> Assets/Game/Audio/Sfx/*.ogg) with WebGL-suited settings and
// rebuilds Resources/SfxLibrary.asset, which references them so the player build keeps the clips:
//   Tools/unity-batch.sh exec SfxSetup.Run
// The import settings are applied by SfxImport on every (re)import, so a re-rendered clip needs nothing more.
public sealed class SfxImport : AssetPostprocessor
{
    public const string Dir = "Assets/Game/Audio/Sfx";

    void OnPreprocessAudio()
    {
        if (!assetPath.StartsWith(Dir + "/")) return;
        var imp = (AudioImporter)assetImporter;
        var s = imp.defaultSampleSettings;
        s.loadType = AudioClipLoadType.DecompressOnLoad;   // short clips: decoded once, no per-play Vorbis decode on the WebGL main thread
        s.compressionFormat = AudioCompressionFormat.Vorbis;
        s.quality = 0.5f;
        s.sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate;
        s.preloadAudioData = false;   // decoded on demand (Sfx.Prepare), not when the library loads
        imp.defaultSampleSettings = s;
        imp.forceToMono = false;
        imp.loadInBackground = false;
        imp.ambisonic = false;
        if (imp.ContainsSampleSettingsOverride("WebGL")) imp.ClearSampleSettingOverride("WebGL");
    }
}

public static class SfxSetup
{
    public static void Run()
    {
        AssetDatabase.Refresh();
        var report = new System.Collections.Generic.List<string>();
        var entries = new System.Collections.Generic.List<Bonanza.SfxLibrary.Entry>();
        foreach (var c in Bonanza.Core.SfxCatalog.All())
        {
            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>($"{SfxImport.Dir}/{c.Name}.ogg");
            if (clip == null) { report.Add("MISSING clip " + c.Name); continue; }
            entries.Add(new Bonanza.SfxLibrary.Entry { id = c.Name, clip = clip });
        }
        const string LibPath = "Assets/Game/Resources/SfxLibrary.asset";
        var lib = AssetDatabase.LoadAssetAtPath<Bonanza.SfxLibrary>(LibPath);
        if (lib == null) { lib = ScriptableObject.CreateInstance<Bonanza.SfxLibrary>(); AssetDatabase.CreateAsset(lib, LibPath); }
        lib.entries = entries.ToArray();
        EditorUtility.SetDirty(lib);
        AssetDatabase.SaveAssets();
        report.Add($"SfxLibrary: {entries.Count} clips");
        Debug.Log("[SfxSetup]\n" + string.Join("\n", report));
        if (report.Count > 1) throw new System.Exception("SfxSetup: " + string.Join("; ", report));
    }
}
