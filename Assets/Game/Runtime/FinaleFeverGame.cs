// Finale Fever in grey boxes: the maestro on a podium throws orbs along a
// lifted arc into your strike zone; you answer his phrase one bar later while
// the tempo climbs 150 -> 187.5. Rules live in Bonanza.Core (FeverRound);
// this is stage, orbs, camera, sound and HUD only. Web source:
// games/finaleFever/index.js + set.js.
//
// The telegraph shortens by section (4, 3, 2 beats of orb flight) and is gone
// for the solo: no orbs, the maestro goes dark, only the music says when.
// Chords are two orbs that split in the air onto the left and right pips.
//
// Coordinates: the web build's (x, y, z) is (x, y, -z) here.

using System.Collections.Generic;
using Bonanza.Core;
using UnityEngine;

namespace Bonanza
{
    public sealed class FinaleFeverGame : Minigame
    {
        public override string Id => "finale-fever";
        public override string Title => "FINALE FEVER";
        public override string Blurb => "It only gets faster.";
        /// The ramp's tempo where the round was paused.
        public override double SecondsPerBeat => 60 / _round.Map.BpmAt(_round.BeatAt(_now));
        public override string Controls => "The maestro plays a phrase; answer it in the next bar as the orbs land.\nSPACE / tap for single orbs. Chords: LEFT + RIGHT (or tap twice).\nThe last four bars have no orbs: play the hook from memory.";

        static Vector3 W(float x, float y, float z) => new Vector3(x, y, -z);
        static readonly Vector3 BossPos = W(-3.7f, 0.92f, -1.25f);
        static readonly Vector3 PlayerPos = W(3.15f, 0, 0.35f);
        static readonly Vector3 Launch = W(-2.75f, 2.65f, -0.95f);
        static readonly Vector3 Strike = W(1.72f, 1.28f, 0.55f);
        static readonly Color Gold = Hex(0xffd93d), Pink = Hex(0xff5fd0), Red = Hex(0xff5d73), Lime = Hex(0x9ee87a);
        static readonly Color LaneL = Hex(0x35c8f0), LaneR = Hex(0xff8a3c);

        FeverRound _round;
        readonly Callouts _say = new Callouts();
        double _now, _lastBeat = -99;
        bool _playing;
        string _section = "teach";

        Transform _boss, _player, _baton, _ring, _cam;
        Material _bossMat, _ringMat, _floorMat;
        readonly Transform[] _pips = new Transform[3];
        float _bossLunge, _playerLean, _ringPulse, _camPunch, _heartPop;
        Vector3 _camPos, _camTarget;
        float _camFov = 52;

        sealed class Orb { public Transform T; public Material M; public Note Note; public FeverNote N; public double From, To; public bool On; }
        readonly List<Orb> _orbs = new List<Orb>();
        int _orbCursor, _cueA, _cueV;

        // ------------------------------------------------------------ contract

        public override double MusicBeatAt(double time) => _round.Map.BeatAt(time);
        public override double MusicTimeAt(double beat) => _round.Map.TimeAt(beat);

        public override void Load()
        {
            _round = new FeverRound(Save.OffsetMs);
            _round.Judged += OnJudged;
            _round.HeartLost += OnHeartLost;
            _round.HeartGained += OnHeartGained;
            _round.Ghost += () => { Host.PlaySfx("tick", _now, gain: 0.12f); _playerLean = -0.4f; };

            _cam = Camera.main.transform;

            _floorMat = Mat(new Color(0.2f, 0.12f, 0.24f));
            Prim(PrimitiveType.Plane, new Vector3(0, 0, 0), new Vector3(3, 1, 2), _floorMat);

            // The maestro: tall, on a podium, with a cape and a baton.
            Prim(PrimitiveType.Cylinder, new Vector3(BossPos.x, BossPos.y / 2, BossPos.z), new Vector3(2.6f, BossPos.y / 2, 2.6f), Hex(0x4a0c26));
            _boss = new GameObject("Maestro").transform;
            _boss.SetParent(transform, false);
            _boss.localPosition = BossPos;
            _bossMat = Mat(Hex(0x3d4bff));
            Prim(PrimitiveType.Capsule, new Vector3(0, 1.35f, 0), new Vector3(0.62f, 1.35f, 0.62f), _bossMat, _boss);
            Prim(PrimitiveType.Sphere, new Vector3(0, 2.95f, 0), Vector3.one * 0.66f, Hex(0xc9d0ff), _boss);
            Prim(PrimitiveType.Cylinder, new Vector3(0, 1.1f, 0.25f), new Vector3(1.0f, 0.9f, 0.3f), Hex(0x2a1060), _boss);   // cape
            _baton = new GameObject("Baton").transform;
            _baton.SetParent(_boss, false);
            _baton.localPosition = new Vector3(0.55f, 2.0f, -0.2f);
            Prim(PrimitiveType.Cube, new Vector3(0.35f, 0.35f, 0), new Vector3(0.06f, 0.9f, 0.06f), Color.white, _baton).transform.localRotation = Quaternion.Euler(0, 0, -45);

            // You.
            _player = new GameObject("Player").transform;
            _player.SetParent(transform, false);
            _player.localPosition = PlayerPos;
            Prim(PrimitiveType.Capsule, new Vector3(0, 0.75f, 0), new Vector3(0.7f, 0.75f, 0.7f), Hex(0xff5d3a), _player);
            Prim(PrimitiveType.Sphere, new Vector3(0, 1.75f, 0), Vector3.one * 0.62f, Hex(0xffc2a8), _player);

            // The strike zone and its three pips (left, centre, right).
            _ringMat = Mat(new Color(0.35f, 0.3f, 0.4f));
            _ring = Prim(PrimitiveType.Cylinder, Strike, new Vector3(1.7f, 0.02f, 1.7f), _ringMat).transform;
            _ring.localRotation = Quaternion.Euler(90, 0, 0);
            for (int l = -1; l <= 1; l++)
                _pips[l + 1] = Prim(PrimitiveType.Sphere, Strike + new Vector3(l * 1.34f, 0, -0.05f), Vector3.one * 0.28f,
                    l < 0 ? LaneL : l > 0 ? LaneR : Gold).transform;

            PlaceCamera(0, false, 1, true);
        }

        public override void Begin(double now)
        {
            _round.Start(now);
            _now = now;
            _playing = true;
            _say.Banner(Title, 1.5f, Blurb);
        }

        public override void Press(string action, double time)
        {
            if (!_playing) return;
            _round.Press(action, time);
        }

        public override void Tick(double now, float dt)
        {
            _now = now;
            double beat = _round.BeatAt(now);
            for (int b = (int)System.Math.Floor(_lastBeat) + 1; b <= System.Math.Floor(beat); b++)
            {
                string s = CountIn.TextFor(b, FeverChart.LeadBeats);
                if (s != null) _say.Count(s);
                if (b < 0 && b >= -FeverChart.LeadBeats) Host.CountTick(_round.Map.TimeAt(b), CountIn.Accent(b));
            }
            _lastBeat = beat;

            _round.Update(now);
            var section = FeverChart.SectionAt(beat);
            if (section != _section) OnSection(section);
            FireCues(now, beat);
            LaunchOrbs(beat);
        }

        public override Outcome Result()
        {
            var o = _round.Result();
            if (o != null) _playing = false;
            return o;
        }

        public override IEnumerable<KeyValuePair<string, double>> TestChart()
        {
            foreach (var n in _round.Live) yield return new KeyValuePair<string, double>(n.Action, n.Time);
        }

        // ------------------------------------------------------------ cues

        /// Boss audio a lookahead early (at its exact ramped time), and his arm on the beat.
        void FireCues(double now, double beat)
        {
            var cues = _round.Cues;
            double horizon = _round.BeatAt(now + 0.14);
            while (_cueA < cues.Count && cues[_cueA].Beat <= horizon)
            {
                var c = cues[_cueA++];
                double at = System.Math.Max(_round.Map.TimeAt(c.Beat), now + 0.004);
                // web fireAudioCues: the key is read at dispatch (scaleInfo() now), not at the cue's own beat.
                if (c.Type == "call") BossVoice(c, at, DegAt(beat));
                else Host.PlayVoice(VoiceCatalog.FinaleRim, at, c.Accent ? 1 : VoiceMix.FinaleRimPlain);
            }
            while (_cueV < cues.Count && cues[_cueV].Beat <= beat)
            {
                var c = cues[_cueV++];
                if (c.Type != "call") continue;
                _bossLunge = c.Accent ? (c.Kind == "swing" ? 1 : 0.55f) : Mathf.Max(_bossLunge, 0.22f);
            }
        }

        void LaunchOrbs(double beat)
        {
            var live = _round.Live;
            while (_orbCursor < live.Count)
            {
                var n = (FeverNote)live[_orbCursor].Data;
                if (n.Telegraph <= 0) { _orbCursor++; continue; }
                if (n.Beat - n.Telegraph > beat) break;
                var note = live[_orbCursor++];
                if (note.Judged) continue;
                var o = _orbs.Find(x => !x.On);
                if (o == null)
                {
                    o = new Orb { M = Mat(Color.white) };
                    o.T = Prim(PrimitiveType.Sphere, Launch, Vector3.one * 0.4f, o.M).transform;
                    _orbs.Add(o);
                }
                o.On = true; o.Note = note; o.N = n; o.From = n.Beat - n.Telegraph; o.To = n.Beat;
                o.M.SetColor("_BaseColor", n.Lane < 0 ? LaneL : n.Lane > 0 ? LaneR : n.Kind == "swing" ? Pink : Gold);
                o.T.localScale = Vector3.one * (n.Kind == "swing" ? 0.62f : 0.4f);
                o.T.gameObject.SetActive(true);
            }
        }

        /// A throw, not a lerp: quadratic bezier with the control point lifted.
        static Vector3 OrbPos(float u, int lane)
        {
            Vector3 a = Launch, b = Strike + new Vector3(lane * 1.34f, 0, 0);
            var c = new Vector3((Launch.x + Strike.x) * 0.5f + lane * 0.9f, Mathf.Max(Launch.y, Strike.y) + 1.55f, (Launch.z + Strike.z) * 0.5f + 0.2f);
            float it = 1 - u;
            return it * it * a + 2 * it * u * c + u * u * b;
        }

        void KillOrbs() { foreach (var o in _orbs) { o.On = false; o.T.gameObject.SetActive(false); } }

        // ------------------------------------------------------------ round events

        void OnSection(string section)
        {
            _section = section;
            if (section == "play") _say.Banner("DUEL", 1.2f, "Answer every phrase.", Gold);
            else if (section == "escalate") _say.Banner("FEVER", 1.4f, "Less warning. More notes.", Hex(0xff8a3c));
            else if (section == "solo")
            {
                _say.Banner("SOLO", 1.8f, "No signals. Just the music.", Pink);
                Host.PlaySfx("swoosh", _now);
                KillOrbs();   // nothing on screen may tell you when
            }
        }

        void OnJudged(Note note, FeverNote n, Verdict v, double errMs)
        {
            var orb = _orbs.Find(o => o.On && o.Note == note);
            if (orb != null) { orb.On = false; orb.T.gameObject.SetActive(false); }
            var pos = Strike + new Vector3(n.Lane * 1.34f, 0.5f, 0);
            MusicJudged(v != Verdict.Miss);
            if (n.Kind == "finale")
            {
                bool hit = v != Verdict.Miss;
                _say.Banner(hit ? "FINALE!" : "SO CLOSE!", 2.2f, hit ? "You took the last note." : "The maestro takes the last note.", hit ? Gold : Red);
                _say.Verdict(hit ? Verdict.Perfect : Verdict.Miss, pos, 1.4f);
                _camPunch = 1.6f;
                _ringPulse = 2;
                Host.PlaySfx(hit ? "fanfare" : "impact", _now);   // web finaleHit: the cadence on the last note, a thud when the maestro takes it
                return;
            }
            _say.Verdict(v, pos, n.Kind == "swing" ? 1.3f : 1);
            Host.PlaySfx(SfxTheory.VerdictSfx(v), _now);
            // A milestone hit climbs the scale (web: sfx('combo', min(9, combo / 10)) on perfect and great).
            if (v >= Verdict.Great && Bonanza.Look.Feel.IsMilestone(_round.Combo)) Host.PlaySfx("combo", _now, System.Math.Min(9, _round.Combo / 10));
            if (v != Verdict.Miss)
            {
                // The player's answer: bright, high, in the chord the track sits on; panned to their side.
                int deg = DegAt(_round.BeatAt(_now)), i = _round.Combo;
                Host.PlayVoice(VoiceCatalog.FinaleAnswer(deg, i, n.Kind == "solo"), _now + 0.004);
                if (n.Kind == "swing") Host.PlayVoice(VoiceCatalog.FinalePluck(deg, i), _now + 0.004);
                _ringPulse = Mathf.Max(_ringPulse, n.Kind == "swing" ? 1.4f : 0.9f);
                _playerLean = 1;
            }
            else
            {
                _camPunch = Mathf.Max(_camPunch, 0.3f);
                _ringPulse = Mathf.Max(_ringPulse, 0.25f);
            }
        }

        void OnHeartLost(int left)
        {
            _heartPop = 1;
            if (_round.Survived && left == 0 && !_surviveShown)
            {
                _surviveShown = true;
                _say.Banner("SURVIVE!", 1.9f, "Nobody leaves the finale.", Red);
            }
            Host.PlaySfx("miss", _now);
            if (left == 0) Host.PlaySfx("impact", _now);   // web loseHeart: the last heart goes with a thud
        }
        bool _surviveShown;

        void OnHeartGained(int left)
        {
            _heartPop = 1;
            _say.Say("NICE!", Strike + new Vector3(0, 1.5f, 0), Lime, 0.8f);
            Host.PlaySfx("coin", _now);
        }

        // ------------------------------------------------------------ sound (baked web voices, VoiceCatalog)

        /// The chord degree the track sits on at a game beat (web scaleInfo().chordDeg). The web reads it when it
        /// plays a voice, so a cue dispatched just before a bar line still sounds on the bar that is ending.
        static int DegAt(double beat) => Harmony.FinaleFever.ChordDegAt((int)System.Math.Floor((beat + FeverChart.LeadBeats) / 4));

        /// The boss sounds like a boss: low brass or an orchestra hit over a taiko, two octaves under the player's
        /// bells, panned hard to his side (web bossVoice). The chord is the track's, unshifted, as the web reads it.
        void BossVoice(FeverCue c, double at, int deg)
        {
            if (c.Kind == "swing") Host.PlayVoice(VoiceCatalog.FinaleSwing(deg), at);
            else if (c.Kind == "chord") Host.PlayVoice(VoiceCatalog.FinaleChord(deg, c.Tone), at);
            else Host.PlayVoice(VoiceCatalog.FinaleCall(deg, c.Tone, c.Accent), at);
        }

        // ------------------------------------------------------------ stage

        void PlaceCamera(float heat, bool solo, float k, bool snap)
        {
            Vector3 target; float d, h, yaw, fov;
            if (solo) { target = W(PlayerPos.x - 1.35f, 1.5f, 0.35f); d = 7.0f - _camPunch * 0.35f; h = 2.05f; yaw = -0.24f; fov = 49; }
            else { target = W(heat * 0.5f, 1.55f + heat * 0.06f, -0.4f); d = 10.3f - heat * 1.35f - _camPunch * 0.25f; h = 2.65f - heat * 0.4f; yaw = 0.02f + heat * 0.09f; fov = 52 - heat * 1.5f; }
            var pos = target + new Vector3(Mathf.Sin(yaw) * d, h, -Mathf.Cos(yaw) * d);
            _camTarget = snap ? target : Vector3.Lerp(_camTarget, target, k);
            _camPos = snap ? pos : Vector3.Lerp(_camPos, pos, k);
            _camFov = snap ? fov : Mathf.Lerp(_camFov, fov, k);
            _cam.position = _camPos;
            _cam.LookAt(_camTarget);
            Camera.main.fieldOfView = _camFov;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            _say.Update(dt);
            if (_round == null) return;
            double beat = _playing ? _round.BeatAt(_now) : -FeverChart.LeadBeats;
            bool solo = FeverChart.SectionAt(beat) == "solo";
            float heat = Mathf.Clamp01((float)FeverChart.ProgressAt(beat) * 1.05f);
            float hop = Phase(beat); hop = 4 * hop * (1 - hop);

            // Orbs in flight.
            foreach (var o in _orbs)
            {
                if (!o.On) continue;
                float u = Mathf.Clamp01((float)((beat - o.From) / System.Math.Max(1e-3, o.To - o.From)));
                o.T.localPosition = OrbPos(u, o.N.Lane);
                if (beat > o.To + 0.6) { o.On = false; o.T.gameObject.SetActive(false); }
            }

            // The maestro leans in as the tempo climbs, rears back for the solo, and goes dark.
            _bossLunge = Damp(_bossLunge, 0, 6, dt);
            float leanZ = solo ? -0.55f : _bossLunge * 0.55f + heat * 0.35f;
            _boss.localPosition = Vector3.Lerp(_boss.localPosition, BossPos + new Vector3(0, solo ? 0 : _bossLunge * 0.05f, -leanZ), 1 - Mathf.Exp(-4.5f * dt));
            _boss.localRotation = Quaternion.Euler(0, 0, solo ? 0 : -_bossLunge * 10);
            _baton.localRotation = Quaternion.Euler(0, 0, Mathf.Lerp(0, solo ? 55 : -60, solo ? 1 : _bossLunge));
            var bossCol = Hex(0x3d4bff);
            _bossMat.SetColor("_BaseColor", solo ? bossCol * 0.25f : _round.Hearts == 0 ? Color.Lerp(bossCol, Red, 0.5f) : bossCol);

            // You brace on your bar, bob on the beat.
            int bar = (int)System.Math.Floor(beat / 4);
            bool answering = solo || (beat >= 0 && bar % 2 == 1);
            _playerLean = Damp(_playerLean, 0, 4.5f, dt);
            _player.localPosition = PlayerPos + new Vector3(-(answering ? 0.24f : 0) - _playerLean * 0.12f, 0, answering ? -hop * 0.05f : 0);

            // Strike zone breathes on the beat; hits make it pulse.
            _ringPulse = Mathf.Max(0, _ringPulse - dt * 6.5f);
            float rs = 1 + hop * 0.055f + _ringPulse * 0.3f;
            _ring.localScale = new Vector3(1.7f * rs, 0.02f, 1.7f * rs);
            _ringMat.SetColor("_BaseColor", Color.Lerp(new Color(0.35f, 0.3f, 0.4f), solo ? Pink : Gold, Mathf.Clamp01(_ringPulse + (answering ? 0.25f : 0))));
            _floorMat.SetColor("_BaseColor", _round.Hearts == 0 && !solo ? new Color(0.36f, 0.08f, 0.12f) : Color.Lerp(new Color(0.2f, 0.12f, 0.24f), new Color(0.32f, 0.12f, 0.2f), heat));

            _camPunch = Damp(_camPunch, 0, 3.5f, dt);
            _heartPop = Damp(_heartPop, 0, 6, dt);
            PlaceCamera(heat, solo, 1 - Mathf.Exp(-(solo ? 2.2f : 2.6f) * dt), false);
        }

        // ------------------------------------------------------------ HUD

        public override void DrawHud(float s)
        {
            if (_round == null) return;
            Hud.Text(new Rect(16 * s, 12 * s, 600 * s, 200 * s),
                $"SCORE  {System.Math.Round(_round.Accuracy * 1000)}\nCOMBO  {_round.Combo}\nACC  {_round.Accuracy * 100:0.0}%", Hud.Left, Color.white);

            // Hearts, tempo, whose turn: top centre.
            float hs = 26 * s, gap = 10 * s, x0 = Screen.width / 2f - (3 * hs + 2 * gap) / 2, y = 14 * s;
            for (int i = 0; i < 3; i++)
            {
                bool on = i < _round.Hearts;
                float grow = on && i == _round.Hearts - 1 ? _heartPop * 6 * s : 0;
                Hud.Box(new Rect(x0 + i * (hs + gap) - grow / 2, y - grow / 2, hs + grow, hs + grow), on ? Red : new Color(0.24f, 0.1f, 0.2f));
            }
            double beat = _playing ? _round.BeatAt(_now) : -FeverChart.LeadBeats;
            Hud.Text(new Rect(0, y + hs + 4 * s, Screen.width, 30 * s), $"{System.Math.Round(_round.Map.BpmAt(beat))} BPM", Hud.Mid, Gold, TextAnchor.UpperCenter);
            string turn; Color tc;
            bool solo = FeverChart.SectionAt(beat) == "solo";
            if (_round.Hearts == 0 && !solo) { turn = "SURVIVE"; tc = Phase(beat * 2) < 0.5f ? Red : new Color(Red.r, Red.g, Red.b, 0.3f); }
            else if (beat < 0) { turn = "GET READY"; tc = Hex(0xffb45a); }
            else if (solo) { turn = "YOUR SOLO"; tc = Pink; }
            else if (((int)System.Math.Floor(beat / 4)) % 2 == 0) { turn = "MAESTRO"; tc = Hex(0xffb45a); }
            else { turn = "YOUR TURN"; tc = Lime; }
            Hud.Text(new Rect(0, y + hs + 34 * s, Screen.width, 30 * s), turn, Hud.Mid, tc, TextAnchor.UpperCenter);
            _say.Draw();
        }
    }
}
