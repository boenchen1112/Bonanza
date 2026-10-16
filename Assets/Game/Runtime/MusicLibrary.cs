// The music clips, referenced from one asset in Resources so the WebGL build
// keeps them (files under Assets/Game/Audio/Music/ that nothing references are
// stripped). Built by Tools/music/unity-setup.cs (run with eval_file); clips are imported with
// preloadAudioData off, so loading this asset costs no memory until
// MusicPlayer.Prepare asks for a track's samples.

using System;
using UnityEngine;

namespace Bonanza
{
    [CreateAssetMenu(menuName = "Bonanza/Music Library")]
    public sealed class MusicLibrary : ScriptableObject
    {
        [Serializable]
        public sealed class Entry
        {
            public string id;
            /// Cumulative mixes i0..i3 (title and results: just the one that plays).
            public AudioClip[] mixes;
        }

        public Entry[] entries;

        public Entry Find(string id)
        {
            if (entries != null) foreach (var e in entries) if (e.id == id) return e;
            return null;
        }
    }
}
