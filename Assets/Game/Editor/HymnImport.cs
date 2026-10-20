using UnityEditor;
using UnityEngine;

// Import settings for the rendered hymn clips (Tools/hymns/render-hymns.mjs -> Assets/Resources/Hymns/*.ogg, which is
// gitignored: the hymns are private). Applied on every (re)import, so a re-rendered clip needs nothing more:
// decoded once on demand (HymnMeterGame.Load), never streamed or decoded per frame on the WebGL main thread.
public sealed class HymnImport : AssetPostprocessor
{
    public const string Dir = "Assets/Resources/Hymns";

    void OnPreprocessAudio()
    {
        if (!assetPath.StartsWith(Dir + "/")) return;
        var imp = (AudioImporter)assetImporter;
        var s = imp.defaultSampleSettings;
        s.loadType = AudioClipLoadType.DecompressOnLoad;
        s.compressionFormat = AudioCompressionFormat.Vorbis;
        s.quality = 0.4f;   // organ tones: small at this quality (the build re-encodes the rendered OGG)
        s.sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate;
        s.preloadAudioData = false;   // HymnMeterGame.Load decodes the six clips of its round
        imp.defaultSampleSettings = s;
        imp.forceToMono = false;      // rendered mono already
        imp.loadInBackground = false;
        imp.ambisonic = false;
        if (imp.ContainsSampleSettingsOverride("WebGL")) imp.ClearSampleSettingOverride("WebGL");
    }
}
