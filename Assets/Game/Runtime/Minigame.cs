// The minigame contract (web/ARCHITECTURE.md "The minigame interface"), in
// Unity form. Every game implements it, because the host, the pause menu, the
// results screen and the autoplay check all drive games generically.
//
//   Load()          build the stage. Must not start the transport.
//   Begin(now)      the transport starts; the game anchors its beat map here.
//   Tick(now, dt)   per frame, after this frame's presses. `now` is GAME time
//                   (audio time minus time spent paused), never frame time.
//   Press(a, t)     one press, action in {a, left, down, up, right}, at game time t.
//   Result()        null until the round is over.
//   TestChart()     the presses a perfect bot makes (action, game time); valid after Begin.
//   DrawHud(s)      the game's own HUD, from the host's OnGUI. s = Screen.height/720.
//
// Dispose is Destroy(gameObject): everything a game builds hangs under its own
// transform, and Mat() copies are released in OnDestroy.

using System.Collections.Generic;
using Bonanza.Core;
using Bonanza.Look;
using UnityEngine;

namespace Bonanza
{
    public abstract class Minigame : MonoBehaviour
    {
        public abstract string Id { get; }
        public abstract string Title { get; }
        public abstract string Blurb { get; }
        /// Shown on the rules card, e.g. "SPACE / click / tap".
        public abstract string Controls { get; }

        protected GameHost Host { get; private set; }
        protected bool Autoplay => Host.Autoplay;
        protected Rng Rng;

        /// True once a game has moved to the shared toon look (Look/): Mat() then
        /// returns toon materials and the game gets a LookRig (sky, lights, fog, blobs).
        protected virtual bool UseLook => false;
        /// The palette this game starts in (a key of Palettes).
        protected virtual string PaletteName => Id;
        protected LookRig Look { get; private set; }

        public void Bind(GameHost host, uint seed)
        {
            Host = host;
            Rng = new Rng(seed);
            if (UseLook) Look = LookRig.Create(transform, PaletteName);
        }

        public abstract void Load();
        public abstract void Begin(double now);
        public abstract void Tick(double now, float dt);
        public abstract void Press(string action, double time);
        public abstract Outcome Result();
        public abstract IEnumerable<KeyValuePair<string, double>> TestChart();
        public virtual void DrawHud(float s) { }
        public virtual void OnPause() { }
        public virtual void OnResume() { }
        /// The tempo the pause menu counts back in at.
        public virtual double SecondsPerBeat => 0.5;

        // ------------------------------------------------------------ stage helpers

        readonly List<Material> _mats = new List<Material>();

        /// A tinted copy of the toon material (or the URP Lit greybox for games not yet on the look kit).
        /// `lift` is self-lit glow, `rim` and `pulse` scale the rim light and the beat reaction.
        protected Material Mat(Color c, float lift = 0, float rim = 1, float pulse = 0.12f, float bands = 3)
        {
            Material m;
            if (UseLook)
            {
                m = new Material(LookRig.Toon);
                m.SetFloat("_Lift", lift); m.SetFloat("_Rim", rim); m.SetFloat("_Pulse", pulse); m.SetFloat("_Bands", bands);
            }
            else m = new Material(Host.Greybox);
            m.SetColor("_BaseColor", c);
            _mats.Add(m);
            return m;
        }

        protected GameObject MeshObject(PrimitiveType t, Material m, Transform parent = null)
        {
            var go = new GameObject(t.ToString(), typeof(MeshFilter), typeof(MeshRenderer));
            go.GetComponent<MeshFilter>().sharedMesh = GameHost.BuiltinMesh(t);
            go.GetComponent<MeshRenderer>().sharedMaterial = m;
            go.transform.SetParent(parent != null ? parent : transform, false);
            return go;
        }

        protected GameObject Prim(PrimitiveType t, Vector3 pos, Vector3 scale, Color c, Transform parent = null)
        {
            var go = MeshObject(t, Mat(c), parent);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            return go;
        }

        protected GameObject Prim(PrimitiveType t, Vector3 pos, Vector3 scale, Material m, Transform parent = null)
        {
            var go = MeshObject(t, m, parent);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            return go;
        }

        protected static Color Hex(uint rgb) =>
            new Color(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f);

        /// Frame-rate independent approach (web util.js damp). Never a fixed-factor lerp.
        protected static float Damp(float a, float b, float lambda, float dt) => Mathf.Lerp(a, b, 1 - Mathf.Exp(-lambda * dt));
        protected static Vector3 Damp(Vector3 a, Vector3 b, float lambda, float dt) => Vector3.Lerp(a, b, 1 - Mathf.Exp(-lambda * dt));

        /// 0..1 within the beat. Never x % 1: beats run negative through the lead-in.
        protected static float Phase(double beat) => (float)(beat - System.Math.Floor(beat));

        protected virtual void OnDestroy()
        {
            foreach (var m in _mats) if (m) Destroy(m);
            _mats.Clear();
        }
    }
}
