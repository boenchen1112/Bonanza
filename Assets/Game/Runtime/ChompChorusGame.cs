// Chomp Chorus in grey boxes: four singers on pedestals, one of them yours.
// Rules live in Bonanza.Core (ChompRound); this is stage, telegraph, sound
// and HUD only. Web source: games/chompChorus/index.js + props.js.
//
// The twist is AUDIBLE: every bar each live voice sustains its own chord tone,
// panned to its own place; a voice that misses drops out, leaving a hole at a
// specific pitch in a specific ear, until it lands a note again.
//
// Telegraph, per lane: the singer's head inflates as it gulps (0..1 across the
// note's lead), a meter beside the pedestal fills, and the breath ball above
// the head grows. The human's lane has a white ring and the label "YOU".

using System.Collections.Generic;
using Bonanza.Core;
using UnityEngine;

namespace Bonanza
{
    public sealed class ChompChorusGame : Minigame
    {
        public override string Id => "chomp-chorus";
        public override string Title => "CHOMP CHORUS";
        public override string Blurb => "Four singers, one of them is you.";
        public override double SecondsPerBeat => 60 / ChompChart.Bpm;
        public override string Controls => "When your singer (ringed) gulps on the offbeat, press ON the beat.\nAny arrow, WASD, SPACE, click or tap. Miss and you will hear the hole in the chord.";

        const int N = 4;
        static readonly uint[] LaneHex = { 0x35c8f0, 0xff5d3a, 0x9ee87a, 0xffc32b };   // lagoon, ember, lime, sunburst
        static readonly float[] BuildW = { 0.95f, 1.25f, 0.75f, 0.6f };               // round, wide, tall, small
        static readonly float[] BuildH = { 0.95f, 0.7f, 1.35f, 0.65f };
        const float HeadLine = 1.6f;

        ChompRound _round;
        readonly Callouts _say = new Callouts();
        double _now;
        bool _playing;

        sealed class Lane
        {
            public Transform Body, Head, Breath, Meter;
            public Material BodyMat, HeadMat;
            public Color Color;
            public float HeadY, PedTop;
            public float Sing, Flinch, AliveT = 1, Gulp, Armed;
            public int Cursor;
            public readonly List<Tele> List = new List<Tele>();
        }

        sealed class Tele { public double Beat, Lead; public bool Demo; public Note Note; public CpuNote Cpu; public bool Fired; }
        sealed class Cue { public double Beat, Time; public int Kind, Lane; public bool Big; }   // 0 gulp, 1 bed, 2 finish

        readonly Lane[] _lanes = new Lane[N];
        readonly List<Cue> _cues = new List<Cue>();
        int _cueAt;
        bool _finaleBanner, _finaleDone;
        /// Frames in the finale's hold window, and those with a key held (web finale.frames / heldFrames).
        public int FinaleFrames { get; private set; }
        public int FinaleHeldFrames { get; private set; }
        public double FinaleSustain => FinaleFrames > 0 ? (double)FinaleHeldFrames / FinaleFrames : 0;
        double _lastBeat = -99;

        // ------------------------------------------------------------ contract

        public override double MusicBeatAt(double time) => _round.Map.BeatAt(time);
        public override double MusicTimeAt(double beat) => _round.Map.TimeAt(beat);

        public override void Load()
        {
            // In a party the lineup's CPUs sing the other voices; free play is lane 0 and 0.8 CPUs.
            _round = ChompRound.ForLineup(Host.Lineup, 0xc40f, Save.OffsetMs);
            _round.LaneJudged += OnJudged;

            var cam = Camera.main;
            cam.transform.position = new Vector3(0, 2.2f, -6.9f);
            cam.transform.LookAt(new Vector3(0, 1.05f, 0));
            cam.fieldOfView = 48;

            Prim(PrimitiveType.Plane, new Vector3(0, 0, 2), new Vector3(2.4f, 1, 1.2f), new Color(0.26f, 0.22f, 0.36f));
            for (int i = 0; i < N; i++)
            {
                var l = _lanes[i] = new Lane { Color = Hex(LaneHex[i]) };
                float x = ChompChart.X[i];
                float bodyH = BuildH[i] * 1.2f;
                // Pedestals put four very different builds' HEADS on one line;
                // the tall one stands a head above it (up is up).
                l.PedTop = Mathf.Clamp(HeadLine - bodyH - 0.3f, 0.1f, 1.05f);
                l.HeadY = l.PedTop + bodyH + 0.3f;
                Prim(PrimitiveType.Cylinder, new Vector3(x, l.PedTop / 2, 0), new Vector3(1.1f, l.PedTop / 2, 1.1f), new Color(0.55f, 0.52f, 0.6f));
                if (i == _round.Human)
                    Prim(PrimitiveType.Cylinder, new Vector3(x, 0.01f, 0), new Vector3(1.6f, 0.01f, 1.6f), Color.white);
                l.BodyMat = Mat(l.Color);
                l.HeadMat = Mat(Color.Lerp(l.Color, Color.white, 0.35f));
                l.Body = Prim(PrimitiveType.Capsule, new Vector3(x, l.PedTop + bodyH / 2, 0), new Vector3(BuildW[i] * 0.6f, bodyH / 2, BuildW[i] * 0.6f), l.BodyMat).transform;
                l.Head = Prim(PrimitiveType.Sphere, new Vector3(x, l.HeadY, 0), Vector3.one * 0.55f * Mathf.Max(0.8f, BuildW[i]), l.HeadMat).transform;
                l.Breath = Prim(PrimitiveType.Sphere, new Vector3(x, l.HeadY + 0.7f, 0), Vector3.zero, Mat(new Color(1, 1, 1, 1))).transform;
                l.Meter = Prim(PrimitiveType.Cube, new Vector3(x + 0.75f, 0, -0.2f), new Vector3(0.12f, 0, 0.12f), l.BodyMat).transform;
            }
            Prewarm();
        }

        public override void Begin(double now)
        {
            _round.Start(now);
            _now = now;
            _playing = true;
            var map = _round.Map;

            // Per-lane lookahead: "what is the next thing this lane has to do".
            foreach (var n in _round.Judge.Notes)
            {
                var c = (ChompNote)n.Data;
                _lanes[c.Lane].List.Add(new Tele { Beat = c.Beat, Lead = c.LeadOrDefault, Note = n });
            }
            foreach (var c in _round.CpuNotes) _lanes[c.Src.Lane].List.Add(new Tele { Beat = c.Src.Beat, Lead = c.Src.LeadOrDefault, Cpu = c });
            // Lead-in demonstration: a cascade, one lane per beat.
            for (int i = 0; i < N; i++) _lanes[i].List.Add(new Tele { Beat = -ChompChart.LeadBeats + 1 + i, Lead = 1, Demo = true });
            foreach (var l in _lanes) l.List.Sort((a, b) => a.Beat.CompareTo(b.Beat));

            // Audio cues: a flat sorted list drained by a cursor.
            var all = new List<ChompNote>(_round.Chart);
            var lastGulp = new double[] { -99, -99, -99, -99 };
            foreach (var n in all)
            {
                double g = n.Beat - n.LeadOrDefault * 0.94;
                if (g - lastGulp[n.Lane] > 0.45) { _cues.Add(new Cue { Beat = g, Kind = 0, Lane = n.Lane, Big = n.LeadOrDefault > 1 }); lastGulp[n.Lane] = g; }
            }
            for (int i = 0; i < N; i++) _cues.Add(new Cue { Beat = -ChompChart.LeadBeats + i + 0.1, Kind = 0, Lane = i });
            for (int bar = -2; bar * 4 <= ChompChart.FinaleEnd; bar++) _cues.Add(new Cue { Beat = bar * 4, Kind = 1 });
            _cues.Add(new Cue { Beat = ChompChart.FinaleEnd, Kind = 2 });
            _cues.Sort((a, b) => a.Beat.CompareTo(b.Beat));
            foreach (var c in _cues) c.Time = map.TimeAt(c.Beat);

            _say.Banner(Title, 1.8f, "You sing the ringed voice. Any arrow or Space.");
        }

        public override void Press(string action, double time)
        {
            if (!_playing) return;
            if (action != "a" && ChompChart.LaneOf(action) < 0) return;
            if (!_round.Press(time))
            {
                // A press that claimed no note: a tick, and the singer flinches.
                _lanes[_round.Human].Flinch = 1;
                Host.Play(TickClip, 0.15f);
            }
        }

        public override void Tick(double now, float dt)
        {
            _now = now;
            double beat = _round.BeatAt(now);
            for (int b = (int)System.Math.Floor(_lastBeat) + 1; b <= System.Math.Floor(beat); b++)
            {
                string s = Core.CountIn.TextFor(b, ChompChart.LeadBeats);
                if (s != null) _say.Count(s);
                if (b < 0 && b >= -ChompChart.LeadBeats) Host.CountTick(_round.Map.TimeAt(b), Core.CountIn.Accent(b));
            }
            _lastBeat = beat;

            _round.Update(now);
            PumpCues(now);
            Telegraph(dt, beat);
            Finale(beat);
        }

        public override Outcome Result()
        {
            var o = _round.Result();
            if (o != null) _playing = false;
            return o;
        }

        public override IEnumerable<KeyValuePair<string, double>> TestChart()
        {
            foreach (var n in _round.Judge.Notes) yield return new KeyValuePair<string, double>(_round.HumanAction, n.Time);
        }

        // ------------------------------------------------------------ verdicts

        void OnJudged(int i, Verdict v, double errMs, ChompNote note, bool cpu)
        {
            var l = _lanes[i];
            bool hit = v != Verdict.Miss;
            if (!cpu) MusicJudged(hit);
            if (!hit && !cpu && Host.Autoplay)
                Debug.Log($"[MISS] game={Id} beat={note.Beat} now={_round.BeatAt(_now):F3} dt={Time.unscaledDeltaTime:F3}");
            bool wasAlive = l.AliveT > 0.5f;
            double at = System.Math.Max(_round.Map.TimeAt(note.Beat) + errMs / 1000, _now + 0.004);
            if (hit)
            {
                l.Sing = 1;
                Sing(i, v, at, note.Finale);
                if (!wasAlive && !cpu) _say.Say("BACK!", new Vector3(ChompChart.X[i], l.HeadY + 0.9f, 0), l.Color, 0.6f);
            }
            else
            {
                l.Sing = 0;
                if (wasAlive) Sour(i, at);
            }
            _say.Verdict(v, new Vector3(ChompChart.X[i], l.HeadY + 0.6f, 0), cpu ? 0.5f : 1);
        }

        // ------------------------------------------------------------ sound (placeholder synth)

        float MidiFor(int lane, double beat)
        {
            // Harmony of the track (audio/music/chomp-chorus.js); the loop starts with the transport.
            int bar = (int)System.Math.Floor((beat + ChompChart.LeadBeats) / 4);
            var c = Harmony.ChompChorus.ChordAtBar(bar);
            return c[System.Math.Min(lane, c.Length - 1)] + 24;
        }

        static float Spb => (float)(60 / ChompChart.Bpm);
        AudioClip SingClip(int lane, double beat, bool finale) => Tones.Blip(Tones.Midi(MidiFor(lane, beat)), finale ? Spb * 4 : Spb * 0.85f, finale ? 0.8f : 3f, Wave.Triangle);
        AudioClip SourClip(int lane, double beat) => Tones.Blip(Tones.Midi(MidiFor(lane, beat) - 12), 0.3f, 8, Wave.Square, 0.45f);
        static AudioClip GulpClip(int lane, bool big) => Tones.Blip(150 + lane * 26, Spb * (big ? 3.4f : 0.62f), big ? 1.5f : 6f, Wave.Sine, big ? 4.4f : 3f);
        AudioClip BedClip(int lane, double beat) => Tones.Blip(Tones.Midi(MidiFor(lane, beat)), Spb * 3.9f, 0.9f, Wave.Triangle, 1, 0.8f);
        static AudioClip TickClip => Tones.Blip(2400, 0.02f, 120);
        static AudioClip FinishClip => Tones.Blip(Tones.Midi(81), 0.6f, 3, Wave.Square);

        /// Synthesise every clip the round can ask for before it starts: built
        /// lazily, a 1.7 s finale clip stalls a frame long enough to push a
        /// press outside the claim window.
        void Prewarm()
        {
            for (int bar = -ChompChart.LeadBeats / 4 - 1; bar * 4 <= ChompChart.EndBeat + 4; bar++)
                for (int i = 0; i < N; i++)
                {
                    double beat = bar * 4;
                    SingClip(i, beat, false); SingClip(i, beat, true); SourClip(i, beat); BedClip(i, beat);
                }
            for (int i = 0; i < N; i++) { GulpClip(i, false); GulpClip(i, true); }
            _ = TickClip; _ = FinishClip;
        }

        void Sing(int lane, Verdict v, double at, bool finale)
        {
            float gain = finale ? 0.34f : v == Verdict.Perfect ? 0.3f : v == Verdict.Great ? 0.25f : 0.18f;
            Host.PlayAt(SingClip(lane, _round.BeatAt(at), finale), at, gain);
        }

        void Sour(int lane, double at) => Host.PlayAt(SourClip(lane, _round.BeatAt(at)), at, 0.12f);

        void Gulp(int lane, double at, bool big) => Host.PlayAt(GulpClip(lane, big), at, big ? 0.12f : 0.08f);

        void Bed(double at, double beat)
        {
            for (int i = 0; i < N; i++)
                if (_round.Alive[i]) Host.PlayAt(BedClip(i, beat), at, 0.07f);
        }

        void PumpCues(double now)
        {
            double horizon = now + 0.16;
            while (_cueAt < _cues.Count && _cues[_cueAt].Time <= horizon)
            {
                var c = _cues[_cueAt++];
                double at = System.Math.Max(c.Time, now + 0.003);
                if (c.Kind == 0) Gulp(c.Lane, at, c.Big);
                else if (c.Kind == 1) Bed(at, c.Beat);
                else Host.PlayAt(FinishClip, at, 0.12f);
            }
        }

        // ------------------------------------------------------------ telegraph + stage

        void Telegraph(float dt, double beat)
        {
            for (int i = 0; i < N; i++)
            {
                var l = _lanes[i];
                int c = l.Cursor;
                while (c < l.List.Count)
                {
                    var e = l.List[c];
                    if (e.Demo)
                    {
                        if (beat >= e.Beat)
                        {
                            if (!e.Fired) { e.Fired = true; l.Sing = 1; Sing(i, Verdict.Great, System.Math.Max(_round.Map.TimeAt(e.Beat), _now + 0.004), false); }
                            c++; continue;
                        }
                        break;
                    }
                    bool judged = e.Note != null ? e.Note.Judged : e.Cpu.Judged;
                    if (judged || e.Beat < beat - 0.7) { c++; continue; }
                    break;
                }
                l.Cursor = c;

                float armed = 0, g = 0;
                if (c < l.List.Count)
                {
                    var e = l.List[c];
                    double d = e.Beat - beat;
                    if (d <= e.Lead && d > -0.4) { armed = 1; g = Mathf.Clamp01((float)(1 - d / e.Lead)); }
                }
                l.Armed = Damp(l.Armed, armed, 22, dt);
                l.Gulp = g;
            }
        }

        void Finale(double beat)
        {
            if (!_finaleBanner && beat >= ChompChart.FinaleBeat - 3.2)
            {
                _finaleBanner = true;
                _say.Banner("ALL FOUR!", 1.3f, "big note - hold it");
            }
            if (!_finaleDone && beat >= ChompChart.FinaleEnd)
            {
                _finaleDone = true;
                Host.MusicStop(1.1f);   // web chompChorus: the track fades out under the last note
                int h = _round.FinaleHits;
                _say.Banner(h == N ? "FULL CHORD!" : h >= 2 ? "NICE CHORUS" : "ROUGH NIGHT", 1.8f);
            }
        }

        void Update()
        {
            float dt = Time.deltaTime;
            _say.Update(dt);
            if (_round == null) return;
            double beat = _playing ? _round.BeatAt(_now) : 0;
            bool finaleHold = beat >= ChompChart.FinaleBeat + 0.45 && beat <= ChompChart.FinaleEnd + 0.5;
            if (_playing && finaleHold)
            {
                // Web chompChorus finale stats: frames the player really held a key through the last bar.
                FinaleFrames++;
                bool held = Host.IsHeld("a");
                foreach (var a in ChompChart.Actions) held |= Host.IsHeld(a);
                if (held) FinaleHeldFrames++;
            }
            for (int i = 0; i < N; i++)
            {
                var l = _lanes[i];
                if (finaleHold && _round.Alive[i]) l.Sing = Mathf.Max(l.Sing, 0.55f + 0.45f * Mathf.Abs(Mathf.Sin((float)beat * Mathf.PI)));
                l.Sing = Damp(l.Sing, 0, 4.2f, dt);
                l.Flinch = Damp(l.Flinch, 0, 9, dt);
                l.AliveT = Damp(l.AliveT, _round.Alive[i] ? 1 : 0, 7, dt);

                float puff = l.Gulp * l.Armed;
                float wob = l.Flinch * Mathf.Sin(Time.time * 46) * 0.06f;
                float baseS = 0.55f * Mathf.Max(0.8f, BuildW[i]);
                l.Head.localScale = new Vector3(baseS * (1 + puff * 0.3f + wob + l.Sing * 0.12f), baseS * (1 + puff * 0.24f - wob + l.Sing * 0.2f), baseS * (1 + puff * 0.3f));
                l.Breath.localScale = Vector3.one * (0.35f * puff);
                l.Meter.localScale = new Vector3(0.12f, Mathf.Max(0.001f, puff * 1.2f), 0.12f);
                l.Meter.localPosition = new Vector3(ChompChart.X[i] + 0.75f, puff * 0.6f, -0.2f);
                // A dead voice literally dims.
                var col = Color.Lerp(new Color(0.35f, 0.33f, 0.4f), l.Color, l.AliveT);
                l.BodyMat.SetColor("_BaseColor", col);
                l.HeadMat.SetColor("_BaseColor", Color.Lerp(col, Color.white, 0.35f + l.Sing * 0.4f));
            }
        }

        // ------------------------------------------------------------ HUD

        public override void DrawHud(float s)
        {
            if (_round == null) return;
            var st = _round.Judge.Stats;
            double score = 1000 * _round.Points / System.Math.Max(1, _round.MaxPoints);
            Hud.Text(new Rect(16 * s, 12 * s, 600 * s, 200 * s),
                $"SCORE  {System.Math.Round(score)}\nCOMBO  {st.Combo}\nACC  {_round.Judge.Accuracy * 100:0.0}%", Hud.Left, Color.white);

            // Voice dots: the twist is learned by ear, but the same fact is shown quietly.
            float d = 26 * s, gap = 18 * s, x0 = Screen.width / 2f - (N * d + (N - 1) * gap) / 2;
            for (int i = 0; i < N; i++)
            {
                var l = _lanes[i];
                var r = new Rect(x0 + i * (d + gap), Screen.height - 50 * s, d, d);
                if (i == _round.Human) Hud.Box(new Rect(r.x - 4 * s, r.y - 4 * s, r.width + 8 * s, r.height + 8 * s), Color.white);
                Hud.Box(r, new Color(l.Color.r, l.Color.g, l.Color.b, 0.18f + 0.82f * l.AliveT));
            }
            var cam = Camera.main;
            Hud.AtWorld(cam, new Vector3(ChompChart.X[_round.Human], -0.15f, -0.5f), "YOU", Hud.Small, Color.white, 200, 40);
            _say.Draw();
        }
    }
}
