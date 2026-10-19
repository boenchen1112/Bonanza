// Chomp Chorus on the shared look kit: four singers on pedestals, one of them yours.
// Rules live in Bonanza.Core (ChompRound); this is stage, telegraph, sound and HUD only.
// Web source: games/chompChorus/index.js + props.js. The set and the singers are in ChompChorusSet.cs.
//
// The twist is AUDIBLE: every bar each live voice sustains its own chord tone,
// panned to its own place; a voice that misses drops out, leaving a hole at a
// specific pitch in a specific ear, until it lands a note again.
//
// Telegraph, per lane (web props.js): a breath ring converging on the head, an air pipe filling beside the
// pedestal, the singer inflating as it gulps (0..1 across the note's lead), the key glyph on the player's
// pedestal, and a bridge linking the pedestals of lanes that fire together. The player's lane has a white
// ring, the glyph and the YOU tag.

using System.Collections.Generic;
using Bonanza.Core;
using Bonanza.Look;
using UnityEngine;

namespace Bonanza
{
    public sealed partial class ChompChorusGame : Minigame
    {
        public override string Id => "chomp-chorus";
        public override string Title => "CHOMP CHORUS";
        public override string Blurb => "Four singers, one of them is you.";
        public override double SecondsPerBeat => 60 / ChompChart.Bpm;
        public override string Controls => "When your singer (ringed) gulps on the offbeat, press ON the beat.\nAny arrow, WASD, SPACE, click or tap. Miss and you will hear the hole in the chord.";
        protected override bool UseLook => true;
        public override bool UseCanvasHud => true;

        const int N = 4;
        static readonly uint[] LaneHex = { 0x35c8f0, 0xff5d3a, 0x9ee87a, 0xffc32b };   // lagoon, ember, lime, sunburst
        /// Every singer's eye line is parked near this height so the row reads as a line (web HEAD_LINE).
        const float HeadLine = 1.6f;
        static readonly Color CountColor = new Color(1f, 0.914f, 0.659f);   // web #ffe9a8

        ChompRound _round;
        double _now;
        bool _playing;

        sealed class Lane
        {
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
        float _time;

        // ------------------------------------------------------------ contract

        public override double MusicBeatAt(double time) => _round.Map.BeatAt(time);
        public override double MusicTimeAt(double beat) => _round.Map.TimeAt(beat);

        public override void Load()
        {
            // In a party the lineup's CPUs sing the other voices; free play is lane 0 and 0.8 CPUs.
            _round = ChompRound.ForLineup(Host.Lineup, 0xc40f, Save.OffsetMs);
#if UNITY_EDITOR
            // Editor-only check hook: BONANZA_CHOMP_LANE=n sings lane n (the shell always seats the human at P1, lane 0).
            if (int.TryParse(System.Environment.GetEnvironmentVariable("BONANZA_CHOMP_LANE"), out int devLane) && devLane > 0 && devLane < N)
                _round = new ChompRound(devLane, null, 0xc40f, Save.OffsetMs);
#endif
            _round.LaneJudged += OnJudged;
            for (int i = 0; i < N; i++) _lanes[i] = new Lane();

            // web: rig.frame({ target: [0, 1.05, 0], distance: 6.9, height: 1.15, fov: 48 }), reframed (see UpdateCamera), snapped.
            Look.Rig.Frame(CamTarget, CamDist, CamHeight, 0, CamFov, 2.6f, immediate: true);
            Look.Rig.SetPushGain(0.75f);
            Look.ShadowFocus(new Vector3(0, 0, 0.2f), 5.6f);   // the four singers and their pads
            BuildStage();
            for (int i = 0; i < N; i++) Cast(_singers[i].Root.gameObject);   // their shadows fall on the pedestal caps

            var fx = Look.Fx;
            fx.SetGroundY(0);
            fx.SetConfettiLanding(true);   // paper settles flat on the boards instead of slicing through them edge-on
            fx.SetFocus(new Vector3(0, 1.5f, 0));
            fx.Prewarm();   // every FX material and the callout font now: a first-use shader link mid-round would push a press out of its window
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

            // HUD (web ui.hud + _mountVoiceDots): score block, the four voice dots, the title.
            Ui.ShowScoreboard();
            var dots = new Color[N];
            for (int i = 0; i < N; i++) dots[i] = Hex(LaneHex[i]);
            Ui.Voices(dots, _round.Human);
            Ui.Banner(Title, "You sing the ringed voice. Any arrow or Space.", null, 1.8f);
        }

        public override void Press(string action, double time)
        {
            if (!_playing) return;
            if (action != "a" && ChompChart.LaneOf(action) < 0) return;
            if (!_round.Press(time))
            {
                // A press that claimed no note: a tick, and the singer flinches.
                _lanes[_round.Human].Flinch = 1;
                Host.PlaySfx("tick", time, gain: 0.12f);
            }
        }

        public override void Tick(double now, float dt)
        {
            _now = now;
            double beat = _round.BeatAt(now);
            for (int b = (int)System.Math.Floor(_lastBeat) + 1; b <= System.Math.Floor(beat); b++)
            {
                string s = Core.CountIn.TextFor(b, ChompChart.LeadBeats);
                if (s != null) Ui.Banner(s, null, CountColor, 0.52f);   // web countIn: a centred banner
                if (b < 0 && b >= -ChompChart.LeadBeats) Host.CountTick(_round.Map.TimeAt(b), Core.CountIn.Accent(b));
            }
            _lastBeat = beat;

            _round.Update(now);
            PumpCues(now);
            Telegraph(dt, beat);
            Finale(beat);

            // The look: beat flash on the audio clock, the set pulses on every whole beat.
            Look.BeatPulse = beat < 0 ? 0 : Mathf.Exp(-5f * Phase(beat));
            Look.TickBeat(beat);
            UpdateCrowd(beat);
            UpdateCamera(beat, dt);

            Ui.Score(Mathf.RoundToInt((float)(1000 * _round.Points / System.Math.Max(1, _round.MaxPoints))));
            Ui.Combo(_round.Judge.Stats.Combo);
            Ui.Accuracy((float)_round.Judge.Accuracy);
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

        double _lastVerdictAt = -99;
        bool _finaleArmed;
        int _curtainBeat = int.MinValue;

        void OnJudged(int i, Verdict v, double errMs, ChompNote note, bool cpu)
        {
            var l = _lanes[i];
            bool hit = v != Verdict.Miss;
            if (!cpu) MusicJudged(hit);
            if (!hit && !cpu && Host.Autoplay)
                Debug.Log($"[MISS] game={Id} beat={note.Beat} now={_round.BeatAt(_now):F3} dt={Time.unscaledDeltaTime:F3}");
            bool wasAlive = l.AliveT > 0.5f;
            double at = System.Math.Max(_round.Map.TimeAt(note.Beat) + errMs / 1000, _now + 0.004);
            // Web: CPU voices judge in the same frame as the player's; only the player's note sounds its verdict,
            // and camera, flash and the world callout belong to the player's note, or a chord reads as one white flash.
            bool dense = cpu || _now - _lastVerdictAt < 0.055;
            if (!cpu) _lastVerdictAt = _now;
            if (!dense) Host.PlaySfx(SfxTheory.VerdictSfx(v), at);
            var fx = Look.Fx;
            var head = new Vector3(ChompChart.X[i], _headY[i], 0);
            var laneC = Hex(LaneHex[i]);
            if (hit)
            {
                l.Sing = 1;
                Sing(i, v, at, note.Finale);
                if (!wasAlive)
                {
                    // A voice coming back is a moment: it is the thing the player was trying to fix.
                    fx.Flare(head + Vector3.up * 0.5f, laneC, 0.9f, 0.3f, 0.35f, 2.6f);
                    Halo(i, laneC, 1.25f);
                    if (!cpu) fx.PopWord("BACK!", head + new Vector3(0, 0.95f, -0.2f), laneC, 0.45f, 0.7f, 0.8f);
                }
            }
            else
            {
                l.Sing = 0;
                if (wasAlive) Sour(i, at);
                // Grey smoke where a bright note should have been.
                fx.Burst(head + new Vector3(0, 0, -0.1f), 10, Hex(0x8d7fa6), 2.6f, 0.12f, 0.55f);
            }

            if (hit)
            {
                Halo(i, laneC, 1);
                // A chord of three or more voices landing together is a stage moment.
                if (!note.Finale)
                {
                    if (note.Beat != _chordBeat) { _chordBeat = note.Beat; _chordHits = 0; }
                    if (++_chordHits == 3) ChordMoment();
                }
            }

            int combo = cpu ? 0 : _round.Judge.Stats.Combo;
            var o = VerdictOpts.Default;
            o.NoLoops = true;   // no thin loops over the faces: the halo on the cap and the breath ring carry the hit
            o.Dir = new Vector3(ChompChart.X[i] * 0.09f, 1, -0.15f);
            o.Combo = combo;
            o.Scale = (note.Finale ? 1.35f : cpu ? 0.7f : dense ? 0.82f : 1) * 0.8f;   // the singer stands in the burst: keep its rings off the neighbours
            o.Stage = !dense; o.Text = !dense;
            o.GroundY = _pedTop[i];
            fx.PlayVerdict(v, new Vector3(ChompChart.X[i], _headY[i] + 0.12f, -0.05f), o);
            if (cpu) return;

            var f = Feel.ForCombo(v, combo);
            Look.Hitstop(note.Finale ? Feel.FinaleHitstop : Feel.HitstopFor(f.Hitstop, _round.Judge.Notes, _round.Map.TimeAt(note.Beat)));
            var crowd = _env?.Crowd;
            if (hit)
            {
                _youPulse = 1;
                _camPush = v == Verdict.Perfect ? 1 : 0.5f;
                if (Feel.IsMilestone(combo)) crowd?.Hype(0.6f + Feel.ComboTier(combo) * 0.08f);
            }
            else crowd?.Deflate(0.8f);
        }

        // ------------------------------------------------------------ sound (baked web voices, VoiceCatalog)

        /// The chord degree the track sits on at a game beat (web _chordAt: the loop's chord for that bar). Lane i
        /// sings chord tone i two octaves up; every (lane, chord) voice is a baked clip.
        static int DegAt(double beat) => Harmony.ChompChorus.ChordDegAt((int)System.Math.Floor((beat + ChompChart.LeadBeats) / 4));

        /// The singer: a detuned lead held 0.85 beats (the finale: four beats), a bell on top for a perfect and on the finale.
        void Sing(int lane, Verdict v, double at, bool finale)
        {
            int deg = DegAt(_round.BeatAt(at));
            if (finale)
            {
                Host.PlayVoice(VoiceCatalog.ChompFinaleSing(lane, deg), at);
                Host.PlayVoice(VoiceCatalog.ChompBell(lane, deg), at);
                return;
            }
            Host.PlayVoice(VoiceCatalog.ChompSing(lane, deg), at,
                v == Verdict.Perfect ? VoiceMix.ChompSingPerfect : v == Verdict.Great ? 1 : VoiceMix.ChompSingGood);
            if (v == Verdict.Perfect) Host.PlayVoice(VoiceCatalog.ChompBell(lane, deg), at);
        }

        /// The hole: a lane that just died gets a deflating raspberry in its ear.
        void Sour(int lane, double at) => Host.PlayVoice(VoiceCatalog.ChompSour(lane, DegAt(_round.BeatAt(at))), at);

        void Gulp(int lane, double at, bool big) => Host.PlayVoice(VoiceCatalog.ChompGulp(lane, big), at);

        /// One bar of sustained four-part harmony, minus every voice that is out.
        void Bed(double at, double beat)
        {
            int deg = DegAt(beat);
            for (int i = 0; i < N; i++)
                if (_round.Alive[i]) Host.PlayVoice(VoiceCatalog.ChompBed(i, deg), at);
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
                else Host.PlaySfx("fanfare", at);
            }
        }

        // ------------------------------------------------------------ telegraph

        void Telegraph(float dt, double beat)
        {
            double minBeat = double.PositiveInfinity;
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
                            if (!e.Fired) DemoSing(i, e);
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
                    if (d <= e.Lead && d > -0.4)
                    {
                        armed = 1; g = Mathf.Clamp01((float)(1 - d / e.Lead));
                        if (e.Beat < minBeat) minBeat = e.Beat;
                    }
                }
                l.Armed = Damp(l.Armed, armed, 22, dt);
                l.Gulp = g;
            }
            // The pending chord: the lanes whose next note is the earliest armed one (web _telegraph chordFlag).
            _chordN = 0;
            for (int i = 0; i < N; i++)
            {
                var l = _lanes[i];
                bool on = l.Armed > 0.01f && l.Cursor < l.List.Count && System.Math.Abs(l.List[l.Cursor].Beat - minBeat) < 1e-6;
                _chordFlag[i] = on ? 1 : 0;
                _chordN += _chordFlag[i];
            }
        }

        /// Lead-in demo: an unscored gulp-and-sing so the mechanic is shown, not told.
        void DemoSing(int lane, Tele e)
        {
            e.Fired = true;
            _lanes[lane].Sing = 1;
            Sing(lane, Verdict.Great, System.Math.Max(_round.Map.TimeAt(e.Beat), _now + 0.004), false);
            Halo(lane, Hex(LaneHex[lane]), 1);
        }

        /// A clean, thick ring of light washing out across the singer's cap (lies flat, depth-tested: the singer stands
        /// in it), the hit's halo instead of a loop over the face.
        void Halo(int lane, Color c, float size)
        {
            Look.Fx.Ring(new Vector3(ChompChart.X[lane], _pedTop[lane] + 0.06f, 0), c, 0.42f * size, 0.7f, 1.0f + 0.5f * size, 0.22f, 0.04f, 0, 0.9f, Vector3.up);
        }

        double _chordBeat = double.NaN;
        int _chordHits;

        /// Three or four voices on one beat: paper from the stands, a punch-in, the house jumps, the rig sweeps.
        /// No screen flash (the beams already peak here): total light stays capped.
        void ChordMoment()
        {
            _chordFx = 1;
            bool calm = Settings.ReduceMotion;
            var fx = Look.Fx;
            int n = calm ? 14 : 40;
            fx.Confetti(new Vector3(-7.5f, 5.5f, 15), n, null, 6.5f, 1.2f, 0.34f, 2.0f);
            fx.Confetti(new Vector3(7.5f, 5.5f, 15), n, null, 6.5f, 1.2f, 0.34f, 2.0f);
            fx.Confetti(new Vector3(0, 6.5f, 17), n, null, 6.5f, 1.2f, 0.34f, 2.0f);
            Look.Rig.PunchZoom(1.05f);
            Look.Rig.Shake(0.05f, Vector3.up);
            _env?.Crowd?.Cheer(1.1f);
            _env?.Spotlights?.Chase(6f, 0.07f);
        }

        /// The scripted last bar.
        void Finale(double beat)
        {
            if (!_finaleBanner && beat >= ChompChart.FinaleBeat - 3.2)
            {
                _finaleBanner = true;
                Ui.Banner("ALL FOUR!", "hold it!", CountColor, 2.4f, plate: true, y: 0.16f);   // over the house, clear of the four faces
                Host.PlaySfx("swoosh", _now);
                Look.SetPalette("chomp-chorus-finale", 1.6f);
                _env?.Crowd?.SetEnergy(0.95f);
            }
            // Once the late half of the claim window is out, a finale that was hit at all lands with an impact (web _finale).
            if (!_finaleArmed && beat >= ChompChart.FinaleBeat + 0.45 && beat <= ChompChart.FinaleEnd + 0.5)
            {
                _finaleArmed = true;
                int h = _round.FinaleHits;
                if (h > 0)
                {
                    Host.PlaySfx("impact", _now);
                    var rig = Look.Rig;
                    rig.PunchZoom(1.14f);
                    rig.Shake(Feel.FinaleShake * (h / 4f), Vector3.up);
                    rig.DoFlash(Feel.FinaleFlash * (h / 4f), Hex(0xfff6d8));
                    Look.Fx.Confetti(new Vector3(0, 2.2f, 0), 120, null, 8.5f, 1.2f);
                    _env?.Crowd?.Cheer(1.2f);
                }
            }
            if (!_finaleDone && beat >= ChompChart.FinaleEnd)
            {
                _finaleDone = true;
                Host.MusicStop(1.1f);   // web chompChorus: the track fades out under the last note
                int h = _round.FinaleHits;
                Ui.Banner(h == N ? "FULL CHORD!" : h >= 2 ? "NICE CHORUS" : "ROUGH NIGHT", null, CountColor, 1.8f, plate: true);
                if (h == N) { Look.Fx.Confetti(new Vector3(0, 2.6f, 0), 160, null, 9, 1.3f); _env?.Crowd?.Cheer(1.4f); }
                else if (h < 2) _env?.Crowd?.Deflate(0.7f);
            }
        }

        /// The crowd's energy climbs with the round and the player's streak; the finale holds it high and waves.
        void UpdateCrowd(double beat)
        {
            var crowd = _env?.Crowd;
            if (crowd == null) return;
            float prog = Mathf.Clamp01((float)(beat / ChompChart.FinaleBeat));
            float streak = Mathf.Clamp01(_round.Judge.Stats.Combo / 24f) * 0.2f;
            float alive = 0; for (int i = 0; i < N; i++) alive += _round.Alive[i] ? 0.25f : 0;
            if (beat < ChompChart.FinaleBeat - 3.2) crowd.SetEnergy(Mathf.Clamp01(0.3f + prog * 0.45f + streak - (1 - alive) * 0.2f));
            int whole = (int)System.Math.Floor(beat);
            if (beat >= ChompChart.FinaleBeat && whole != _curtainBeat)
            {
                _curtainBeat = whole;
                if (whole % 2 == 0) crowd.Wave(0.8f, 1.8f);
                // The held chord rains confetti on every beat of the last bar (while anyone landed it).
                if (beat < ChompChart.FinaleEnd + 1 && _round.FinaleHits > 0 && !Settings.ReduceMotion)
                    Look.Fx.Confetti(new Vector3(((whole * 7) % 5 - 2) * 1.4f, 4.2f, 0.4f), 26 + 8 * _round.FinaleHits, null, 3.5f, 0.3f);
            }
        }

        // ------------------------------------------------------------ presentation (runs on through results)

        void Update()
        {
            if (_round == null || _singers[0] == null) return;
            float dt = LookRig.VisualDt(Time.deltaTime);
            _time += dt;
            if (!_playing) Look.BeatPulse = 0;
            double beat = _playing ? _round.BeatAt(_now) : ChompChart.EndBeat;
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
                // Sustain: hold the beams and the voices up through the last bar.
                if (finaleHold && _round.Alive[i]) l.Sing = Mathf.Max(l.Sing, 0.55f + 0.45f * Mathf.Abs(Mathf.Sin((float)beat * Mathf.PI)));
                l.Sing = Damp(l.Sing, 0, 4.2f, dt);
                l.Flinch = Damp(l.Flinch, 0, 9, dt);
                l.AliveT = Damp(l.AliveT, _round.Alive[i] ? 1 : 0, 7, dt);
                UpdateSinger(i, dt, _time, beat, finaleHold);
                Ui.Voice(i, l.AliveT, Mathf.Clamp01(l.Sing));
            }
            UpdateProps(dt, beat, _time, finaleHold);
        }
    }
}
