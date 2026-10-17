// The baked SFX clips (Assets/Game/Audio/Sfx/*.ogg, from Tools/sfx/render-sfx.mjs), referenced from one asset in
// Resources so the WebGL build keeps them. Built by Editor SfxSetup.Run; clips are imported with preloadAudioData
// off, so loading this asset costs no memory until Sfx.Prepare asks for a game's set.

using System;
using System.Collections.Generic;
using UnityEngine;

namespace Bonanza
{
    [CreateAssetMenu(menuName = "Bonanza/Sfx Library")]
    public sealed class SfxLibrary : ScriptableObject
    {
        [Serializable]
        public sealed class Entry
        {
            public string id;
            public AudioClip clip;
        }

        public Entry[] entries;

        Dictionary<string, AudioClip> _map;

        public AudioClip Find(string id)
        {
            if (_map == null)
            {
                _map = new Dictionary<string, AudioClip>();
                if (entries != null) foreach (var e in entries) if (e != null && e.clip != null) _map[e.id] = e.clip;
            }
            return _map.TryGetValue(id, out var c) ? c : null;
        }
    }
}
