// Finale Fever, the tempo game, in grey boxes on the shared look kit: tap on every beat of a hymn while its
// tempo moves under you (gentle ramps, then ups and downs, then jumps and held notes), and in places with the
// conductor's baton hidden, keep the beat by ear. The rules live in Bonanza.Core (TempoRound, TempoCurve); this is
// the stage, the sound and the callouts. No rule logic lives here.
//
// The music is a rendered clip per excerpt (Tools/hymns/render-hymns.mjs: the hymn's melody laid on the excerpt's
// tempo curve, the very curve the round judges against), decoded in Load() and played through Host.PlayClip like
// Swing Kings' hymns. A build without the private hymn data faults cleanly ("hymn data missing").
//
// HOOKS for the visual work (all read-only, valid after Begin):
//   Round         the TempoRound (Slots, Hearts, BiasText(), ...)         ExcerptIndex   0..2, 3 after the last
//   Slot          the excerpt on screen (its Curve, Beats, Level, Name)   Beat           curve beats since the excerpt's first beat (fractional; negative in the count-in)
//   CurveBpm      the tempo as heard now                                  PulseHidden    true while the stroke that lands next has no baton: draw no beat cue at all
//   Hearts        hearts left (0 = SURVIVE)                               LastCall / LastVerdict / LastErrMs / TapSerial  the last tap (TapSerial counts up on every judged tap)
// The stage reads them in Update(); nothing here depends on art.

using System.Collections.Generic;
using System.Linq;
using Bonanza.Core;
using Bonanza.HudKit;
using Bonanza.Look;
using UnityEngine;

namespace Bonanza
{
    public sealed class FinaleFeverGame : Minigame
    {
        public override string Id => "finale-fever";
        public override string Title => "FINALE FEVER";
        public override string Blurb => "Stay on the beat while the tempo moves.";
        public override bool OwnsMusic => true;   // the hymns' tempo clips (HymnData), through Host.PlayClip
        protected override bool UseLook => true;
        public override bool UseCanvasHud => true;
        public override string Controls => "A hymn plays and the tempo moves: speeding up, slowing down, jumping, holding a note.\nSPACE / click / tap on EVERY beat. Each tap says EARLY, LATE or ON.\nSometimes the baton disappears: keep the beat by ear.\nThree hearts: a line you mostly miss costs one, a clean line wins one back.";

        // ------------------------------------------------------------ constants a human tunes by ear
        const double TapGain = 0.12;          // the tap tick's gain
        const double PreRoll = 0.5;           // the clip is handed to the music deck this long before its first beat
        const int LeadBeats = 4;              // count-in beats before the first excerpt (the later ones get TempoRound.GapBeats)
        const double ResultHold = 1.4;        // seconds the last callout stays up before the results card

        static Vector3 W(float x, float y, float z) => new Vector3(x, y, -z);   // the web's z, flipped
        static readonly Vector3 PodiumPos = W(0, 0, 1.6f);
        static readonly Vector3 PlayerPos = W(2.9f, 0, -0.4f);
        static readonly Vector3 RingPos = W(0, 0.03f, 0.1f);
        static readonly Color Gold = Hex(0xffd93d), Red = Hex(0xff5d73), Lime = Hex(0x9ee87a), Sky = Hex(0x6fd0ff), Orange = Hex(0xff8a3c);

        TempoRound _round;
        HymnData _data;
        readonly AudioClip[] _clips = new AudioClip[TempoRound.ExcerptCount];
        IReadOnlyList<int> _ids;
        bool _playing;
        double _now, _doneAt = -1;
        int _musicSlot = -1, _stoppedSlot = -1;
        int _tickCursor;
        readonly List<CountTick> _ticks = new List<CountTick>();
        int _hiddenSeen = -1;
        bool _surviveShown;

        struct CountTick { public double Time; public int Accent; public bool Say; public string Text; }

        // ------------------------------------------------------------ hooks

        public TempoRound Round => _round;
        public int ExcerptIndex { get; private set; }
        public TempoSlot Slot => _round != null && ExcerptIndex < _round.Slots.Count ? _round.Slots[ExcerptIndex] : null;
        /// Curve beats since the excerpt's first beat, fractional (negative through the count-in).
        public double Beat { get; private set; }
        public double CurveBpm { get; private set; }
        /// True while the stroke that lands next is hidden: the stage must show no beat cue (no baton, no pulse).
        public bool PulseHidden { get; private set; }
        public int Hearts => _round != null ? _round.Hearts : 3;
        public TapCall? LastCall { get; private set; }
        public Verdict LastVerdict { get; private set; }
        public double LastErrMs { get; private set; }
        public int TapSerial { get; private set; }

        /// The pause menu counts back in at the beat now playing.
        public override double SecondsPerBeat
        {
            get
            {
                var s = Slot;
                if (s == null || s.Curve == null) return 0.5;
                int i = Mathf.Clamp((int)System.Math.Floor(System.Math.Max(0, Beat)), 0, s.Curve.BeatCount - 1);
                return System.Math.Max(0.25, System.Math.Min(0.5, s.Curve.LengthOf(i)));   // never slower than half a second: the count back in stays short
            }
        }

        // The SFX chord lookup (Host.PlaySfx) wants a beat for the clock: beats counted through the round's excerpts.
        public override double MusicBeatAt(double time)
        {
            if (_round == null || _round.Slots.Count == 0) return time * 2;
            double beats = 0;
            foreach (var s in _round.Slots)
            {
                if (time < s.EndTime || s == _round.Slots[_round.Slots.Count - 1]) return beats + s.Curve.BeatAt(time - s.StartTime);
                beats += s.Curve.BeatCount;
            }
            return beats;
        }

        public override double MusicTimeAt(double beat)
        {
            if (_round == null || _round.Slots.Count == 0) return beat / 2;
            foreach (var s in _round.Slots)
            {
                if (beat < s.Curve.BeatCount || s == _round.Slots[_round.Slots.Count - 1]) return s.StartTime + s.Curve.TimeAt(beat);
                beat -= s.Curve.BeatCount;
            }
            return 0;
        }

        public override bool AudioReady
        {
            get
            {
                foreach (var c in _clips)
                {
                    if (c == null) continue;
                    if (c.loadState == AudioDataLoadState.Loading || c.loadState == AudioDataLoadState.Unloaded) return false;
                }
                return true;
            }
        }

        // ------------------------------------------------------------ contract

        public override void Load()
        {
            LoadHymns();   // first: a build without the private hymns fails here, before the stage is built
            BuildStage();
            Look.Fx.SetGroundY(0);
            Look.Fx.SetFocus(W(0, 1.5f, 0.5f));
            Look.Fx.Prewarm();   // touch every FX material now: a first-use shader link mid-round would push a tap out of its window
            _round.Tapped += OnTapped;
            _round.Missed += OnMissed;
            _round.HeartLost += OnHeartLost;
            _round.HeartGained += OnHeartGained;
            _round.ExcerptStarted += OnExcerptStarted;
        }

        /// The tempo pool, the three excerpts and their clips (decoded now, never mid-round).
        void LoadHymns()
        {
            _data = HymnData.Load();   // throws "hymn data missing: ..." for the host's fault path
            if (_data.Problems.Count > 0) Debug.LogWarning("[ERROR] hymn data: " + string.Join("; ", _data.Problems));
            if (_data.TempoPool.Count == 0)
                throw new System.InvalidOperationException("hymn data missing: no tempo clips (" + string.Join("; ", _data.TempoProblems) + "); run Tools/hymns/prepare-data.sh");
            if (_data.TempoProblems.Count > 0) Debug.LogWarning("[ERROR] hymn data: " + string.Join("; ", _data.TempoProblems));
            List<double> skills = null;
            if (Host.Lineup != null)
            {
                skills = Host.Lineup.Where(p => p.IsCpu).Select(p => p.CpuSkill).ToList();
                var human = Host.Lineup.FirstOrDefault(p => !p.IsCpu);
                var ids = new List<int> { human != null ? human.Id : 0 };
                ids.AddRange(Host.Lineup.Where(p => p.IsCpu).Select(p => p.Id));
                _ids = ids;
            }
            // A new set of hymns each time: the game's seed, moved by how often it has been played.
            uint seed = unchecked((uint)(Rng.Next() * 4294967295.0) + (uint)Save.Get(Id).Plays * 0x9e3779b1u);
            _round = new TempoRound(_data.TempoPool, seed, skills);
            for (int k = 0; k < TempoRound.ExcerptCount; k++)
            {
                string name = TempoRound.ClipName(_round.Playlist[k], TempoRound.SlotLevels[k]);
                _clips[k] = HymnData.Clip(name);
                if (_clips[k] == null) throw new System.InvalidOperationException("hymn data missing: clip " + name);
                _clips[k].LoadAudioData();
            }
        }

        public override void Begin(double now)
        {
            _round.Start(now);
            double lead = LeadBeats * _round.Slots[0].Curve.LengthOf(0);
            _round.Start(now + lead);   // the first excerpt waits for its count-in
            _now = now;
            _playing = true;
            BuildTicks();
            Ui.ShowScoreboard();
            Ui.Outs(0);
            Ui.Hint("<color=#ffe58a>SPACE</color> / click / tap on <color=#ffe58a>EVERY BEAT</color> · the tempo will move");
            Ui.Banner(Title, "tap every beat", Gold, 1.4f);
        }

        public override void Press(string action, double time)
        {
            if (!_playing || action != "a") return;
            time += Save.OffsetMs / 1000.0;
            Host.PlaySfx("tick", time, gain: (float)TapGain);
            _round.Press(action, time);
            _playerHop = 1;
        }

        public override void Tick(double now, float dt)
        {
            _now = now;
            _round.Update(now);
            DriveMusic(now);
            DriveTicks(now);
            Follow(now);
            UpdateHud();
        }

        public override Outcome Result()
        {
            if (!_round.Done) return null;
            if (_doneAt < 0) { _doneAt = _now; Host.PlaySfx(_round.Survived ? "impact" : "fanfare", _now); }
            if (_now - _doneAt < ResultHold) return null;
            _playing = false;
            return _round.Result(_ids);
        }

        public override IEnumerable<KeyValuePair<string, double>> TestChart()
        {
            foreach (var p in _round.PerfectPresses()) yield return new KeyValuePair<string, double>(p.Action, p.Time);
        }

        // ------------------------------------------------------------ the round's clock, sound

        /// The count-in ticks: LeadBeats before the first excerpt, GapBeats before each later one, at that excerpt's
        /// own opening tempo (so the new tempo is heard before the first tap).
        void BuildTicks()
        {
            _ticks.Clear();
            _tickCursor = 0;
            foreach (var s in _round.Slots)
            {
                int n = s.Index == 0 ? LeadBeats : TempoRound.GapBeats;
                double len = s.Curve.LengthOf(0);
                for (int j = n; j >= 1; j--)
                    _ticks.Add(new CountTick { Time = s.StartTime - j * len, Accent = n - j == 0 ? 0 : 1, Say = s.Index == 0, Text = s.Index == 0 ? j.ToString() : null });
            }
        }

        void DriveTicks(double now)
        {
            while (_tickCursor < _ticks.Count && _ticks[_tickCursor].Time <= now + 0.15)
            {
                var t = _ticks[_tickCursor++];
                Host.CountTick(t.Time, t.Accent);
                if (t.Say && t.Time > now - 0.2) Ui.Countdown(t.Text);
            }
        }

        /// Each excerpt's clip is handed to the deck PreRoll before its first beat: beat 0 sounds at the slot's start
        /// (the file holds `lead` seconds of silence and the graph's own offset before it), and it stops after the last beat.
        void DriveMusic(double now)
        {
            int k = _musicSlot + 1;
            if (k < _round.Slots.Count && now >= _round.Slots[k].StartTime - PreRoll)
            {
                var s = _round.Slots[k];
                Host.PlayClip(_clips[k], s.StartTime, _data.BeatOffset + _data.TempoLead(s.Clip));
                _musicSlot = k;
            }
            if (_musicSlot >= 0 && _stoppedSlot < _musicSlot && now > _round.Slots[_musicSlot].EndTime + 0.05)
            {
                _stoppedSlot = _musicSlot;
                Host.MusicStop(0.4f);
            }
        }

        /// Which excerpt is on screen, where its curve is, and whether the baton shows.
        void Follow(double now)
        {
            if (_round.Slots.Count == 0) return;
            int k = 0;
            while (k < _round.Slots.Count - 1 && now >= _round.Slots[k].EndTime) k++;
            ExcerptIndex = k;
            var s = _round.Slots[k];
            Beat = s.Curve.BeatAt(now - s.StartTime);
            CurveBpm = s.Curve.BpmAt(Beat);
            int landing = (int)System.Math.Floor(Beat) + 1;   // the stroke now in the air lands on this beat
            PulseHidden = landing >= 0 && landing < s.Beats.Count && s.Beats[landing].Hidden && now >= s.StartTime - 1e-9;
            if (PulseHidden && _hiddenSeen != k * 1000 + landing && (landing == 0 || !s.Beats[landing - 1].Hidden))
            {
                _hiddenSeen = k * 1000 + landing;
                Ui.Popup("BY EAR", Sky, 0.5f, 0.2f, 0.9f, 1.4f);
            }
        }

        // ------------------------------------------------------------ round events

        void OnExcerptStarted(TempoSlot slot)
        {
            Ui.Banner($"{slot.Index + 1} / {TempoRound.ExcerptCount}", slot.Level.ToString().ToUpperInvariant(), Gold, 1.3f);
        }

        void OnTapped(TempoBeat b)
        {
            LastCall = b.Call;
            LastVerdict = b.Verdict;
            LastErrMs = b.ErrMs;
            TapSerial++;
            MusicJudged(true);
            string word = b.Call == TapCall.On ? "ON" : b.Call == TapCall.Early ? "EARLY" : "LATE";
            var col = b.Call == TapCall.On ? Lime : b.Call == TapCall.Early ? Sky : Orange;
            Ui.Popup(word, col, 0.5f, 0.62f, b.Call == TapCall.On ? 1.05f : 0.9f, 0.55f);
            if (b.Call == TapCall.On) Look.Fx.Ring(RingPos, Gold);
            _ringPulse = b.Call == TapCall.On ? 1 : 0.45f;
        }

        void OnMissed(TempoBeat b)
        {
            LastCall = null;
            LastVerdict = Verdict.Miss;
            TapSerial++;
            MusicJudged(false);
            Ui.Popup("MISS", Red, 0.5f, 0.62f, 0.9f, 0.55f);
            Host.PlaySfx("miss", _now);
        }

        void OnHeartLost(int left)
        {
            Ui.Outs(3 - left);
            Host.PlaySfx(left == 0 ? "impact" : "miss", _now);
            if (_round.Survived && left == 0 && !_surviveShown)
            {
                _surviveShown = true;
                Ui.Banner("SURVIVE!", "keep the beat", Red, 1.6f);
            }
        }

        void OnHeartGained(int left)
        {
            Ui.Outs(3 - left);
            Ui.Popup("HEART +1", Lime, 0.5f, 0.3f, 0.9f, 0.9f);
            Host.PlaySfx("coin", _now);
        }

        void UpdateHud()
        {
            Ui.Score(Mathf.RoundToInt((float)(_round.Accuracy * 1000)));
            Ui.Combo(_round.Combo);
            Ui.Accuracy((float)_round.Accuracy);
            if (ExcerptIndex > 0 || Beat >= 16) Ui.HideHint();
        }

        // ------------------------------------------------------------ stage (greybox: a podium, a baton, a ring, you)

        Transform _baton, _batonPivot, _player, _ring;
        Material _ringMat, _batonMat;
        float _ringPulse, _playerHop;

        void BuildStage()
        {
            Look.Rig.Frame(W(0.4f, 1.6f, 0.8f), 9.2f, 2.4f, 0.0f, 45, 3f, immediate: true);
            Prim(PrimitiveType.Plane, new Vector3(0, 0, 0), new Vector3(3, 1, 2), Hex(0x2c0820));
            Prim(PrimitiveType.Cylinder, new Vector3(PodiumPos.x, 0.3f, PodiumPos.z), new Vector3(2.4f, 0.3f, 2.4f), Hex(0x5e0d28));
            var maestro = new GameObject("Maestro").transform;
            maestro.SetParent(transform, false);
            maestro.localPosition = PodiumPos + new Vector3(0, 0.6f, 0);
            Prim(PrimitiveType.Capsule, new Vector3(0, 1.0f, 0), new Vector3(0.7f, 1.0f, 0.7f), Hex(0x3d4bff), maestro);
            Prim(PrimitiveType.Sphere, new Vector3(0, 2.3f, 0), Vector3.one * 0.62f, Hex(0xc9d0ff), maestro);
            _batonPivot = new GameObject("BatonPivot").transform;
            _batonPivot.SetParent(maestro, false);
            _batonPivot.localPosition = new Vector3(0.55f, 1.55f, -0.35f);
            _batonMat = Mat(Color.white, lift: 0.8f);
            _baton = Prim(PrimitiveType.Cube, new Vector3(0, 0.55f, 0), new Vector3(0.07f, 1.1f, 0.07f), _batonMat, _batonPivot).transform;
            _ringMat = Mat(new Color(0.4f, 0.3f, 0.35f), lift: 0.2f);
            _ring = Prim(PrimitiveType.Cylinder, RingPos, new Vector3(1.9f, 0.02f, 1.9f), _ringMat).transform;
            _player = new GameObject("Player").transform;
            _player.SetParent(transform, false);
            _player.localPosition = PlayerPos;
            Prim(PrimitiveType.Capsule, new Vector3(0, 0.75f, 0), new Vector3(0.7f, 0.75f, 0.7f), Hex(0xff5d3a), _player);
            Prim(PrimitiveType.Sphere, new Vector3(0, 1.75f, 0), Vector3.one * 0.62f, Hex(0xffc2a8), _player);
        }

        void Update()
        {
            float dt = LookRig.VisualDt(Time.deltaTime);
            if (_round == null || _round.Slots.Count == 0 || _baton == null) return;
            bool show = _playing && !PulseHidden;
            float p = (float)(Beat - System.Math.Floor(Beat));            // 0 just after a beat .. 1 at the next
            // the baton: struck low on the beat, thrown up, and falling back to land on the next one; gone when hidden
            float lift = Mathf.Sin(Mathf.PI * Mathf.Pow(p, 0.7f));
            _baton.gameObject.SetActive(show);
            _batonPivot.localRotation = Quaternion.Euler(0, 0, Mathf.Lerp(-8, 62, lift));
            // The beat the stage keeps (the camera nudge, the surfaces' lift) stops with the baton: a hidden stretch has no cue at all.
            Look.BeatPulse = show ? Mathf.Pow(1 - p, 3) * 0.6f : 0;
            if (show && Beat >= 0) Look.TickBeat(Beat);
            _ringPulse = Damp(_ringPulse, 0, 7, dt);
            float rs = 1 + _ringPulse * 0.3f + (show ? Mathf.Pow(1 - p, 3) * 0.08f : 0);
            _ring.localScale = new Vector3(1.9f * rs, 0.02f, 1.9f * rs);
            _ringMat.SetColor("_BaseColor", Color.Lerp(new Color(0.4f, 0.3f, 0.35f), Gold, Mathf.Clamp01(_ringPulse)));
            _playerHop = Damp(_playerHop, 0, 9, dt);
            _player.localPosition = PlayerPos + new Vector3(0, _playerHop * 0.18f, 0);
        }

        // The HUD is on the canvas (UseCanvasHud): no DrawHud.
    }
}
