using UnityEditor;

// Batch-mode twin of Tools/music/unity-setup.cs (which needs the Editor's eval_file):
//   Tools/unity-batch.sh exec MusicSetup.Run
// Imports the music OGGs with WebGL-suited settings and rebuilds Resources/MusicLibrary.asset.
public static class MusicSetup
{
    public static void Run()
    {
        var Dir = "Assets/Game/Audio/Music";
        UnityEditor.AssetDatabase.Refresh();
        var report = new System.Collections.Generic.List<string>();
        foreach (var path in System.IO.Directory.GetFiles(Dir, "*.ogg"))
        {
            var imp = (UnityEditor.AudioImporter)UnityEditor.AssetImporter.GetAtPath(path.Replace('\\', '/'));
            var s = imp.defaultSampleSettings;
            var before = s;
            s.loadType = UnityEngine.AudioClipLoadType.DecompressOnLoad;   // no per-frame Vorbis decode on the WebGL main thread
            s.compressionFormat = UnityEngine.AudioCompressionFormat.Vorbis;
            s.quality = 0.7f;
            s.sampleRateSetting = UnityEditor.AudioSampleRateSetting.PreserveSampleRate;
            s.preloadAudioData = false;   // decoded on demand (MusicPlayer.Prepare), not when the library loads
            bool same = before.loadType == s.loadType && before.compressionFormat == s.compressionFormat && before.quality == s.quality
                && before.sampleRateSetting == s.sampleRateSetting && before.preloadAudioData == s.preloadAudioData
                && !imp.forceToMono && !imp.loadInBackground && !imp.ContainsSampleSettingsOverride("WebGL");
            if (same) continue;   // already imported this way (a reimport of all clips outlasts the CLI's 5 s main-thread limit)
            imp.defaultSampleSettings = s;
            imp.forceToMono = false;
            imp.loadInBackground = false;
            imp.ambisonic = false;
            if (imp.ContainsSampleSettingsOverride("WebGL")) imp.ClearSampleSettingOverride("WebGL");
            imp.SaveAndReimport();
        }
        UnityEditor.AssetDatabase.Refresh();

        var entries = new System.Collections.Generic.List<Bonanza.MusicLibrary.Entry>();
        foreach (var t in Bonanza.MusicTracks.All)
        {
            var mixes = new System.Collections.Generic.List<UnityEngine.AudioClip>();
            if (t.Levels == 4) for (int i = 0; i < 4; i++) mixes.Add(UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.AudioClip>($"{Dir}/{t.Stem}.i{i}.ogg"));
            else mixes.Add(UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.AudioClip>($"{Dir}/{t.Id}.{(t.Id == "title" ? "i1" : "i0")}.ogg"));
            foreach (var c in mixes) if (c == null) report.Add("MISSING clip for " + t.Id);
            entries.Add(new Bonanza.MusicLibrary.Entry { id = t.Id, mixes = mixes.ToArray() });
        }
        var LibPath = "Assets/Game/Resources/MusicLibrary.asset";
        var lib = UnityEditor.AssetDatabase.LoadAssetAtPath<Bonanza.MusicLibrary>(LibPath);
        if (lib == null) { lib = UnityEngine.ScriptableObject.CreateInstance<Bonanza.MusicLibrary>(); UnityEditor.AssetDatabase.CreateAsset(lib, LibPath); }
        lib.entries = entries.ToArray();
        UnityEditor.EditorUtility.SetDirty(lib);
        UnityEditor.AssetDatabase.SaveAssets();
        foreach (var e in lib.entries) report.Add($"{e.id}: {e.mixes.Length} clips" + (e.mixes.Length > 0 && e.mixes[0] != null ? $" samples={e.mixes[0].samples} freq={e.mixes[0].frequency}" : ""));
        UnityEngine.Debug.Log("[MusicSetup]\n" + string.Join("\n", report));
    }
}
