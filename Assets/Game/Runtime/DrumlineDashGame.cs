// Drumline Dash in grey boxes: four marchers in lanes, a drum major on a podium
// with a five-drum rack. Rules live in Bonanza.Core (DrumlineRound); this is
// stage, camera, sound and HUD only. Web source: games/drumlineDash/index.js.
//
// The ear leads. The major's call is tuned toms panned right; your answer is a
// dry snare panned left. The rack lights are deliberately worse than the sound:
// they start 70 ms early, ramp and linger, and the head that lights is a fixed
// sweep, not the note. Whose turn it is shows four ways, none of them a label:
// the lit ground disc (magenta under him, gold under you), the camera (pushed in
// on the podium for the call, pulled back to the pack for the answer), the lit
// lane, and the sound.
//
// Coordinates: the web build's (x, y, z) is (x, y, -z) here.

using System.Collections.Generic;
using Bonanza.Core;
using UnityEngine;

namespace Bonanza
{
    public sealed class DrumlineDashGame : Minigame
    {
        public override string Id => "drumline-dash";
        public override string Title => "DRUMLINE DASH";
        public override string Blurb => "Hear it. Hit it back.";
        public override double SecondsPerBeat => 60 / DrumChart.Bpm;
        public override string Controls => "The drum major plays a rhythm. Play it back in the next bar.\nSPACE, click or tap. Extra presses stumble your marcher.";

        static readonly float[] LaneZ = { -2.0f, -0.55f, 0.9f, 2.35f };
        static readonly Vector3 MajorPos = new Vector3(7.7f, 0, 2.5f);
        static readonly uint[] RacerHex = { 0xffc32b, 0x35c8f0, 0x9ee87a, 0x6f7d96 };   // you (sunburst), piccolo, tenor, crash
        static readonly float[] RacerH = { 1.0f, 0.8f, 0.95f, 1.2f };
        static readonly float[] RacerW = { 0.95f, 0.75f, 1.2f, 0.8f };
        static readonly Color CallHue = Hex(0xff5fd0);   // magenta: his turn
        static readonly Color RespHue = Hex(0xffb03a);   // gold: your turn
        static readonly Color Dark = Hex(0x2a1030);
        static readonly float[] TomHz = { 252, 214, 180, 152, 128 };

        struct Framing { public Vector3 Target; public float Distance, Height, Yaw, Fov; }
        static readonly Framing CamResp = new Framing { Target = new Vector3(0.6f, 1.5f, -0.1f), Distance = 11.4f, Height = 3.9f, Yaw = 0.30f, Fov = 50 };
        static readonly Framing CamCall = new Framing { Target = new Vector3(6.6f, 2.2f, 1.7f), Distance = 8.6f, Height = 3.0f, Yaw = 0.52f, Fov = 46 };

        DrumlineRound _round;
        readonly Callouts _say = new Callouts();
        double _now, _lastBeat = -99;
        bool _playing, _finaleAnnounced, _endBanner;

        sealed class Marcher
        {
            public Transform Root, Body;
            public Material Mat;
            public Color Color;
            public float X, Hop, Stagger;
        }
        readonly Marcher[] _m = new Marcher[4];
        Transform _major, _gate, _cam;
        readonly Material[] _heads = new Material[5];
        readonly Transform[] _headT = new Transform[5];
        readonly float[] _headLight = new float[5];
        Material _discMajor, _discYou, _laneYou;
        Transform _discYouT;
        readonly List<Transform> _stripes = new List<Transform>();
        float _scroll, _speed = 4.4f, _speedTarget = 4.4f, _callGlow, _majorStrike;
        Vector3 _camPos, _camTarget;
        float _camFov = 50;

        sealed class Cue { public double Time; public int Kind, I; public DrumPhrase P; }   // 0 call note, 1 handoff hat, 2 count
        readonly List<Cue> _cues = new List<Cue>();
        int _cueAt;
        readonly List<Cue> _lights = new List<Cue>();
        int _lightAt;

        // ------------------------------------------------------------ contract

        public override double MusicBeatAt(double time) => _round.Map.BeatAt(time);
        public override double MusicTimeAt(double beat) => _round.Map.TimeAt(beat);

        public override void Load()
        {
            _round = new DrumlineRound(0xd12b, Save.OffsetMs, Host.Lineup);
            _round.Judged += OnJudged;
            _round.Fumbled += OnFumble;
            _round.CallStarted += OnCallStart;
            _round.RespStarted += OnRespStart;
            _round.PhraseClosed += OnPhraseClosed;

            _cam = Camera.main.transform;
            PlaceCamera(CamResp, 1, true);

            // Field and lanes.
            Prim(PrimitiveType.Plane, new Vector3(2, 0, 0), new Vector3(4.2f, 1, 1.6f), new Color(0.24f, 0.4f, 0.26f));
            for (int i = 0; i < 4; i++)
            {
                var m = i == 0 ? (_laneYou = Mat(new Color(0.3f, 0.3f, 0.34f))) : Mat(new Color(0.3f, 0.3f, 0.34f));
                Prim(PrimitiveType.Cube, new Vector3(0, 0.01f, LaneZ[i]), new Vector3(30, 0.02f, 1.3f), m);
            }
            var stripe = Mat(new Color(0.92f, 0.92f, 0.95f));
            for (int k = 0; k < 12; k++) _stripes.Add(Prim(PrimitiveType.Cube, Vector3.zero, new Vector3(0.12f, 0.03f, 5.8f), stripe).transform);

            // Marchers: YOU nearest and biggest.
            for (int i = 0; i < _round.Racers.Count; i++)
            {
                int lane = _round.Racers[i].Index;
                var mc = _m[i] = new Marcher { Color = Hex(RacerHex[lane]) };
                mc.Root = new GameObject(_round.Racers[i].Name).transform;
                mc.Root.SetParent(transform, false);
                mc.Root.localPosition = new Vector3(0, 0, LaneZ[lane]);
                float s = i == 0 ? 1.45f : 1.34f;
                mc.Mat = Mat(mc.Color);
                float h = RacerH[lane] * s, w = RacerW[lane] * s * 0.5f;
                mc.Body = Prim(PrimitiveType.Capsule, new Vector3(0, h * 0.5f, 0), new Vector3(w, h * 0.5f, w), mc.Mat, mc.Root).transform;
                Prim(PrimitiveType.Sphere, new Vector3(0, h + 0.25f * s, 0), Vector3.one * 0.5f * s, Color.Lerp(mc.Color, Color.white, 0.4f), mc.Root);
                if (i == 0) Prim(PrimitiveType.Cylinder, new Vector3(0.45f, h * 0.45f, -0.3f), new Vector3(0.5f, 0.12f, 0.5f), Color.white, mc.Root);   // the snare
            }

            // The drum major on his podium, the rack in front of him.
            Prim(PrimitiveType.Cylinder, MajorPos + new Vector3(0, 0.57f, 0), new Vector3(2.2f, 0.57f, 2.2f), new Color(0.45f, 0.4f, 0.52f));
            _major = new GameObject("Major").transform;
            _major.SetParent(transform, false);
            _major.localPosition = MajorPos + new Vector3(0, 1.14f, 0);
            Prim(PrimitiveType.Capsule, new Vector3(0, 1.0f, 0), new Vector3(0.6f, 1.0f, 0.6f), Hex(0xc77dff), _major);
            Prim(PrimitiveType.Sphere, new Vector3(0, 2.3f, 0), Vector3.one * 0.62f, Hex(0xe6c3ff), _major);
            Prim(PrimitiveType.Cylinder, new Vector3(0, 2.85f, 0), new Vector3(0.45f, 0.35f, 0.45f), Hex(0x1b1030), _major);   // shako
            for (int k = 0; k < 5; k++)
            {
                float a = Mathf.Lerp(-0.9f, 0.9f, k / 4f);
                var p = MajorPos + new Vector3(-Mathf.Cos(a) * 2.0f - 0.2f, 1.95f, Mathf.Sin(a) * 2.0f);
                _heads[k] = Mat(Dark);
                _headT[k] = Prim(PrimitiveType.Cylinder, p, new Vector3(0.62f, 0.08f, 0.62f), _heads[k]).transform;
            }

            // Turn discs (the web build's light beams, on the ground).
            _discMajor = Mat(Dark);
            Prim(PrimitiveType.Cylinder, MajorPos + new Vector3(0, 1.16f, 0), new Vector3(2.5f, 0.01f, 2.5f), _discMajor);
            _discYou = Mat(Dark);
            _discYouT = Prim(PrimitiveType.Cylinder, new Vector3(0, 0.03f, LaneZ[0]), new Vector3(2.2f, 0.01f, 1.3f), _discYou).transform;

            // Finish gate, parked off screen until the last phrase.
            _gate = new GameObject("Finish").transform;
            _gate.SetParent(transform, false);
            var gm = Mat(Color.white);
            Prim(PrimitiveType.Cube, new Vector3(0, 2.7f, -3.2f), new Vector3(0.3f, 5.4f, 0.3f), gm, _gate);
            Prim(PrimitiveType.Cube, new Vector3(0, 2.7f, 3.4f), new Vector3(0.3f, 5.4f, 0.3f), gm, _gate);
            Prim(PrimitiveType.Cube, new Vector3(0, 5.3f, 0.1f), new Vector3(0.4f, 0.6f, 6.9f), RespHue, _gate);
            _gate.gameObject.SetActive(false);

            Prewarm();
        }

        public override void Begin(double now)
        {
            _round.Start(now);
            _now = now;
            _playing = true;
            var map = _round.Map;
            // Every call scheduled up front at audio times; a sound that says
            // "now" is already late.
            foreach (var p in _round.Phrases)
            {
                for (int i = 0; i < p.CallBeats.Count; i++)
                {
                    var c = new Cue { Time = map.TimeAt(p.CallBeats[i]), Kind = 0, I = i, P = p };
                    _cues.Add(c);
                    _lights.Add(c);
                }
                // The handoff: a hat on the last beat of the call says "it comes to you next".
                _cues.Add(new Cue { Time = map.TimeAt(p.RespBeat - 1), Kind = 1 });
            }
            // Count-in over the first four lead beats; the first call fills the rest.
            for (int b = -DrumChart.LeadBeats; b < -4; b++) _cues.Add(new Cue { Time = map.TimeAt(b), Kind = 2, I = ((b % 4) + 4) % 4 });
            _cues.Sort((a, b) => a.Time.CompareTo(b.Time));
            _lights.Sort((a, b) => a.Time.CompareTo(b.Time));
            _say.Banner(Title, 1.5f, Blurb);
        }

        public override void Press(string action, double time)
        {
            if (!_playing || action != "a") return;
            if (!_round.Press(time) && _round.Mode == DrumMode.Lead) Host.Play(TickClip, 0.12f);
        }

        public override void Tick(double now, float dt)
        {
            _now = now;
            double beat = _round.BeatAt(now);
            for (int b = (int)System.Math.Floor(_lastBeat) + 1; b <= System.Math.Floor(beat); b++)
                if (b >= -DrumChart.LeadBeats && b < -4) _say.Count((-4 - b).ToString());
            _lastBeat = beat;

            _round.Update(now);
            PumpCues(now);
            while (_lightAt < _lights.Count && now >= _lights[_lightAt].Time - 0.07)
            {
                var ev = _lights[_lightAt++];
                _headLight[ev.I % 5] = 1;
                _callGlow = 1;
                _majorStrike = 1;
            }
        }

        public override Outcome Result()
        {
            var o = _round.Result();
            if (o != null)
            {
                _playing = false;
                if (!_endBanner) EndBanner();
            }
            return o;
        }

        public override IEnumerable<KeyValuePair<string, double>> TestChart()
        {
            foreach (var p in _round.Phrases)
                foreach (var b in p.NoteBeats) yield return new KeyValuePair<string, double>("a", _round.Map.TimeAt(b));
        }

        // ------------------------------------------------------------ round events

        void OnCallStart(DrumPhrase p)
        {
            _speedTarget = 4.0f;
            Host.MusicDuck(p.Finale ? DuckFinale : DuckCall);   // web: the call sits in a hole in the mix
            Host.Play(Crash, p.Finale ? 0.3f : 0.16f, 1, 0.35f);
            if (p.Finale && !_finaleAnnounced)
            {
                _finaleAnnounced = true;
                _say.Banner("FINALE", 1.5f, "two bars - give it back whole", CallHue);
            }
        }

        // web drumlineDash: music volume 0.72 normally, 0.34 under a call, 0.30 under the finale call.
        const float DuckCall = 0.34f / 0.72f, DuckFinale = 0.30f / 0.72f;

        void OnRespStart(DrumPhrase p)
        {
            _speedTarget = 5.6f;
            Host.MusicDuck(1);
        }

        void OnJudged(Note note, Verdict v, double errMs)
        {
            var p = (DrumPhrase)note.Data;
            var y = _m[0];
            MusicJudged(v != Verdict.Miss);
            var pos = new Vector3(y.X + 0.25f, 2.6f, LaneZ[0] - 0.25f);
            _say.Verdict(v, pos, p.Finale ? 1.2f : 1);
            double at = System.Math.Max(note.Time + errMs / 1000, _now + 0.004);
            if (v != Verdict.Miss)
            {
                Host.PlayAt(SnareNoise, at, v == Verdict.Perfect ? 0.5f : 0.4f, 1, -0.18f);
                Host.PlayAt(SnareBody, at, 0.25f, 1, -0.18f);
                y.Hop = v == Verdict.Perfect ? 1.1f : 0.8f;
            }
            else
            {
                Host.PlayAt(MissTone, _now + 0.004, 0.14f, 1, -0.2f);
                y.Stagger = 0.5f;
            }
        }

        void OnFumble()
        {
            var y = _m[0];
            y.Stagger = 1;
            Host.Play(Rim, 0.3f, 1, -0.3f);
            Host.Play(Thud, 0.18f, 1, -0.3f);
            _say.Say("FUMBLE", new Vector3(y.X, 2.2f, LaneZ[0]), new Color(0.6f, 0.55f, 0.7f), 0.5f, 0.7f);
        }

        void OnPhraseClosed(DrumPhrase p, double q)
        {
            if (q >= 0.92) _say.Say(p.Finale ? "NAILED IT!" : "CLEAN!", new Vector3(_m[0].X, 3.2f, LaneZ[0]), RespHue, 0.8f);
            for (int i = 1; i < _round.Racers.Count; i++)
                if (_round.Racers[i].Stumbled)
                {
                    _m[i].Stagger = 1;
                    Host.Play(Tones.Blip(96, 0.3f, 10, Wave.Sine, 0.7f), 0.2f, 1, (i - 2) * 0.3f);
                }
        }

        void EndBanner()
        {
            _endBanner = true;
            int place = _round.You.Place;
            _say.Banner(place == 1 ? "WINNER!" : place + new[] { "st", "nd", "rd", "th" }[place - 1] + " PLACE", 2.4f,
                $"{_round.Perfect} perfect - best streak {_round.MaxCombo}", place == 1 ? RespHue : Color.white);
            for (int k = 0; k < 3; k++) Host.PlayAt(Tones.Blip(Tones.Midi(72 + k * 4), 0.35f, 5, Wave.Square), _now + 0.02 + k * 0.11, 0.12f);
        }

        // ------------------------------------------------------------ sound (placeholder synth)

        static AudioClip TomClip(int k) => Tones.Blip(TomHz[k], 0.26f, 14, Wave.Sine, 0.75f);
        static AudioClip Rim => Tones.Blip(1900, 0.03f, 90, Wave.Noise);
        static AudioClip Hat => Tones.Blip(8000, 0.08f, 40, Wave.Noise);
        static AudioClip Crash => Tones.Blip(6000, 0.9f, 4, Wave.Noise);
        static AudioClip SnareNoise => Tones.Blip(3000, 0.12f, 28, Wave.Noise);
        static AudioClip SnareBody => Tones.Blip(190, 0.08f, 40, Wave.Triangle, 0.8f);
        static AudioClip Thud => Tones.Blip(110, 0.07f, 40, Wave.Sine, 0.7f);
        static AudioClip MissTone => Tones.Blip(140, 0.2f, 12, Wave.Square, 0.7f);
        static AudioClip TickClip => Tones.Blip(2400, 0.02f, 120);

        /// Built before the round: synthesising mid-round stalls a frame.
        void Prewarm()
        {
            for (int k = 0; k < 5; k++) TomClip(k);
            _ = Rim; _ = Hat; _ = Crash; _ = SnareNoise; _ = SnareBody; _ = Thud; _ = MissTone; _ = TickClip;
            Tones.Blip(96, 0.3f, 10, Wave.Sine, 0.7f);
            for (int k = 0; k < 3; k++) Tones.Blip(Tones.Midi(72 + k * 4), 0.35f, 5, Wave.Square);
        }

        void PumpCues(double now)
        {
            double horizon = now + 0.16;
            while (_cueAt < _cues.Count && _cues[_cueAt].Time <= horizon)
            {
                var c = _cues[_cueAt++];
                double at = System.Math.Max(c.Time, now + 0.003);
                if (c.Kind == 0)
                {
                    // His voice: tuned toms + rim, panned right. Nothing else uses it.
                    int k = c.I % TomHz.Length;
                    float big = c.P.Finale ? 1.25f : 1;
                    Host.PlayAt(TomClip(k), at, 0.55f * big, 1, 0.24f + k * 0.05f);
                    Host.PlayAt(Rim, at, 0.22f * big, 1, 0.34f);
                    if (c.I == 0) Host.PlayAt(Hat, at, 0.12f, 1, 0.4f);
                }
                else if (c.Kind == 1) Host.PlayAt(Hat, at, 0.14f, 1, 0.1f);
                else Host.CountTick(at, c.I);
            }
        }

        // ------------------------------------------------------------ stage

        void PlaceCamera(Framing f, float k, bool snap)
        {
            var target = f.Target;
            var pos = target + new Vector3(Mathf.Sin(f.Yaw) * f.Distance, f.Height, -Mathf.Cos(f.Yaw) * f.Distance);
            _camTarget = snap ? target : Vector3.Lerp(_camTarget, target, k);
            _camPos = snap ? pos : Vector3.Lerp(_camPos, pos, k);
            _camFov = snap ? f.Fov : Mathf.Lerp(_camFov, f.Fov, k);
            _cam.position = _camPos;
            _cam.LookAt(_camTarget);
            Camera.main.fieldOfView = _camFov;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            _say.Update(dt);
            if (_round == null) return;
            double beat = _playing ? _round.BeatAt(_now) : -DrumChart.LeadBeats;
            var mode = _round.Mode;
            bool onCall = mode == DrumMode.Call;
            float bob = Mathf.Abs(Mathf.Sin(Phase(beat) * Mathf.PI));

            // Marchers: step on the beat, lean ahead, hop on a hit, stagger on a fumble.
            for (int i = 0; i < _round.Racers.Count; i++)
            {
                var m = _m[i];
                var r = _round.Racers[i];
                m.X = Damp(m.X, (float)r.XTarget, 2.6f, dt);
                m.Hop = Damp(m.Hop, 0, 9, dt);
                m.Stagger = Damp(m.Stagger, 0, 5, dt);
                m.Root.localPosition = new Vector3(m.X, bob * 0.08f + m.Hop * 0.25f, LaneZ[r.Index]);
                m.Root.localRotation = Quaternion.Euler(m.Stagger * 18 * Mathf.Sin(Time.time * 30), 0, -Mathf.Clamp(m.X, -5, 5) * 1.0f - 6);
            }
            _majorStrike = Damp(_majorStrike, 0, 10, dt);
            _major.localScale = new Vector3(1, 1 - _majorStrike * 0.06f, 1);

            // Rack: the light ramps (~90 ms) and lingers.
            for (int k = 0; k < 5; k++)
            {
                _headLight[k] = Damp(_headLight[k], 0, 3.2f, dt);
                _heads[k].SetColor("_BaseColor", Color.Lerp(Dark, CallHue, _headLight[k]));
                _headT[k].localScale = new Vector3(0.62f, 0.08f + _headLight[k] * 0.05f, 0.62f);
            }

            // The field moves; faster on your bar.
            _speed = Damp(_speed, _speedTarget, 2.2f, dt);
            _scroll += _speed * dt;
            const float span = 30f;
            for (int k = 0; k < _stripes.Count; k++)
            {
                float u = (k * span / _stripes.Count + _scroll) / span;
                u -= Mathf.Floor(u);
                _stripes[k].localPosition = new Vector3(span / 2 - u * span, 0.03f, 0.2f);
            }

            // Whose turn: one disc lit, and the camera.
            _callGlow = Damp(_callGlow, onCall ? 0.55f : 0, 6, dt);
            _discMajor.SetColor("_BaseColor", Color.Lerp(Dark, CallHue, onCall ? 1 : 0.06f));
            _discYou.SetColor("_BaseColor", Color.Lerp(Dark, RespHue, mode == DrumMode.Resp ? 1 : 0.05f));
            _laneYou.SetColor("_BaseColor", mode == DrumMode.Resp ? Color.Lerp(new Color(0.3f, 0.3f, 0.34f), RespHue, 0.45f) : new Color(0.3f, 0.3f, 0.34f));
            _discYouT.localPosition = new Vector3(_m[0].X, 0.03f, LaneZ[0]);

            var want = onCall ? CamCall : CamResp;
            if (!onCall) want.Target = new Vector3(0.6f + _m[0].X * 0.24f, 1.5f, -0.1f);
            PlaceCamera(want, 1 - Mathf.Exp(-3.6f * dt), false);

            // Finish gate slides in over the last 12 beats.
            var last = _round.Phrases[_round.Phrases.Count - 1];
            double toEnd = last.EndBeat - beat;
            if (_playing && toEnd < 12)
            {
                _gate.gameObject.SetActive(true);
                float t = Mathf.Clamp01((float)(12 - toEnd) / 12);
                float e = 1 - Mathf.Pow(1 - t, 3);
                _gate.localPosition = new Vector3(2.5f + 52 * (1 - e), 0, 0);
            }
        }

        // ------------------------------------------------------------ HUD

        public override void DrawHud(float s)
        {
            if (_round == null) return;
            Hud.Text(new Rect(16 * s, 12 * s, 600 * s, 200 * s),
                $"SCORE  {System.Math.Round(_round.Score01 * 1000)}\nCOMBO  {_round.Combo}\nACC  {_round.HitQuality * 100:0.0}%", Hud.Left, Color.white);

            // Standings, every frame: the twist is that you can SEE yourself losing.
            int n = _round.Racers.Count;
            float rowH = 30 * s, w = 280 * s, x = 16 * s, y0 = Screen.height - 16 * s - n * (rowH + 6 * s);
            int total = _round.Phrases.Count + 1;
            for (int i = 0; i < n; i++)
            {
                var r = _round.Racers[i];
                var m = _m[i];
                float y = y0 + (r.Place - 1) * (rowH + 6 * s);
                Hud.Box(new Rect(x, y, w, rowH), i == 0 ? new Color(1, 0.69f, 0.23f, 0.25f) : new Color(0.05f, 0.02f, 0.09f, 0.45f));
                Hud.Text(new Rect(x + 6 * s, y, 24 * s, rowH), r.Place.ToString(), Hud.Left, Color.white);
                Hud.Box(new Rect(x + 30 * s, y + 5 * s, 9 * s, rowH - 10 * s), m.Color);
                Hud.Text(new Rect(x + 46 * s, y, 110 * s, rowH), r.Name, Hud.Small, Color.white, TextAnchor.MiddleLeft);
                float bw = 110 * s, bx = x + w - bw - 10 * s;
                Hud.Box(new Rect(bx, y + rowH / 2 - 4 * s, bw, 8 * s), new Color(1, 1, 1, 0.16f));
                Hud.Box(new Rect(bx, y + rowH / 2 - 4 * s, bw * Mathf.Clamp01((float)(r.Progress / total)), 8 * s), m.Color);
            }
            _say.Draw();
        }
    }
}
