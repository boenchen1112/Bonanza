// Bounce Brigade (the game id and name stay): hear a hymn's last lines, find the downbeat, conduct it.
// The jumper hops on every beat by itself. Answer with the arrow keys, one per beat, starting on the beat
// you think is the DOWNBEAT with the first key of the meter's pattern: DOWN starts 4 boxes (down left right
// up), LEFT starts 3 (left right up), RIGHT starts 2 (right up). Each following on-beat key fills the next
// box; a wrong key or a missed beat empties them. Once the boxes are full every beat lights one box (box 1 on
// the beat you claimed), looping with the music. BACKSPACE (b: X, K) clears; ENTER (a: Space, Z, J) submits a
// full answer and locks it. Right = the hymn's pattern AND box 1 on a true downbeat. The steps under the
// jumper (heavier = lower: 4 beats 1 3 2 3, 3 beats 1 3 3, 2 beats 1 3) hide under cloud that lifts at the
// halfway point of the excerpt or once everyone has submitted; a submission after that ranks below all earlier
// ones and scores a quarter.
//
// The rules live in Bonanza.Core (Meter, MeterAnswer, HymnRound, ...); this is stage, sound and HUD.
// The grid is built from the hymn's MIDI + tempo map (HymnData) and the audio was rendered from the same
// pair (Tools/hymns), so the beats you hear are the beats judged. A build without the private hymn data
// (a fresh clone) fails Load() with "hymn data missing" and the host returns to the title.
//
// Hooks for the visual side: Round / CurrentSlot / Shown / BeatIndex / BeatPhase / BarPosition / BeatsPerBar /
// StepHeight (the shown beat's, Shown.Beats[i].StepHeight for any beat) / Revealed / Answer (the player's boxes:
// Count, Filled, KeyAt(i), Full, Locked, SubmitTime, StartBeat) / LitBox (the box that flickers now, -1 if none),
// and the tile colours by pattern key (KeyColor).
//
// Time: everything is game time (the audio clock); presses arrive in game time.

using System.Collections.Generic;
using System.Linq;
using Bonanza.Core;
using UnityEngine;

namespace Bonanza
{
    public sealed class HymnMeterGame : Minigame
    {
        public override string Id => "bounce-brigade";
        public override string Title => "BOUNCE BRIGADE";
        public override string Blurb => "Find the downbeat. Conduct the hymn.";
        public override string Controls => "Six hymn excerpts. Listen: where does the bar start?\nOn the DOWNBEAT press the first key of the pattern, then one key per beat:\n4 beats: DOWN LEFT RIGHT UP   -   3 beats: LEFT RIGHT UP   -   2 beats: RIGHT UP\nA wrong key or a missed beat empties the boxes. BACKSPACE clears them.\nWhen they are full press ENTER to lock your answer. The earlier you are right, the more you score.";
        public override bool OwnsMusic => true;

        const double PreRoll = 0.5;      // the clip starts this long before the first beat (the hymn leading in)
        const double ResultHold = 1.8;   // the last answer stays on screen this long before the results card
        const float TileStep = 1.5f;

        // ------------------------------------------------------------ hooks for the visual side

        public HymnRound Round => _round;
        /// The excerpt being played (or about to be), null before Begin and after the last.
        public HymnSlot CurrentSlot => _round != null && _round.Current >= 0 && _round.Current < _round.Slots.Count ? _round.Slots[_round.Current] : null;
        /// The excerpt whose floor is on screen: the one being played, and after its window the one just played until
        /// the next begins (so its answer and reveal stay visible through the gap).
        public HymnSlot Shown => _round != null && _layoutSlot >= 0 && _layoutSlot < _round.Slots.Count ? _round.Slots[_layoutSlot] : null;
        /// Index into Shown.Beats of the latest beat that has sounded, -1 before the first.
        public int BeatIndex { get; private set; } = -1;
        /// 0..1 from that beat to the next.
        public float BeatPhase { get; private set; }
        public int BarPosition => BeatIndex >= 0 && Shown != null ? Shown.Beats[BeatIndex].BarPosition : -1;
        public int BeatsPerBar => BeatIndex >= 0 && Shown != null ? Shown.Beats[BeatIndex].BeatsPerBar : 0;
        /// The cloud is off the shown excerpt's floor.
        public bool Revealed => Shown != null && Shown.Revealed;
        /// Step height of the latest beat that has sounded, 1 = lowest = heaviest (4 beats 1 3 2 3, 3 beats 1 3 3, 2 beats 1 3), 0 before the first.
        public int StepHeight => BeatIndex >= 0 && Shown != null ? Shown.Beats[BeatIndex].StepHeight : 0;
        /// The player's boxes on the shown excerpt (Count, Filled, KeyAt, Full, Locked, SubmitTime, StartBeat), null before the first.
        public MeterAnswer Answer => Shown?.Answer;
        /// The box that lights at the current time once the boxes are full (box 1 on the beat claimed as the downbeat), -1 if none.
        public int LitBox => Answer != null ? Answer.LitBox(_now) : -1;

        /// A floor tile's colour for a pattern key (down, left, right, up).
        public static Color KeyColor(string key) => key == "down" ? Hex(0xffd34d) : key == "left" ? Hex(0x4cc9f0) : key == "right" ? Hex(0xf15bb5) : Hex(0x8ee05a);

        // ------------------------------------------------------------ state

        HymnData _data;
        HymnRound _round;
        readonly AudioClip[] _clips = new AudioClip[HymnRound.ExcerptCount];
        IReadOnlyList<int> _ids;
        readonly Callouts _say = new Callouts();
        int _musicSlot = -1, _stoppedSlot = -1, _layoutSlot = -1;
        double _now, _doneAt = -1;
        bool _playing;
        readonly float[] _flash = new float[4];       // down left right up: how lit the key box is
        static readonly string[] KeyOrder = { "down", "left", "right", "up" };
        string _lastLine = "";
        float _lastLineLife;

        static readonly Color Hidden = new Color(0.42f, 0.45f, 0.58f);
        sealed class Tile { public Transform Root, Cloud; public Material Mat; public float Pop; public float CloudK = 1; public Color Key; }
        readonly List<Tile> _tiles = new List<Tile>();
        Transform _jumper, _cam;
        Material _cloudMat;
        float _camX;

        public override double SecondsPerBeat
        {
            get
            {
                var s = CurrentSlot ?? (_round != null && _round.Slots.Count > 0 ? _round.Slots[0] : null);
                // the pause menu counts back in at this tempo: a hymn's own beat when quick, never slower than half a second
                return s != null && s.Beats.Count > 1 ? System.Math.Max(0.25, System.Math.Min(0.5, s.Beats[1].Time - s.Beats[0].Time)) : 0.5;
            }
        }

        // The SFX chord lookup (Host.PlaySfx) wants a beat for the clock; the hymns have no fixed one.
        public override double MusicBeatAt(double time) => time * 2;
        public override double MusicTimeAt(double beat) => beat / 2;

        // ------------------------------------------------------------ contract

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

        public override void Load()
        {
            _data = HymnData.Load();   // throws "hymn data missing: ..." for the host's fault path
            if (_data.Problems.Count > 0) Debug.LogWarning("[ERROR] hymn data: " + string.Join("; ", _data.Problems));

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
            _round = new HymnRound(_data.Pool, seed, skills);
            _round.ExcerptStarted += OnStarted;
            _round.ExcerptRevealed += OnRevealed;
            _round.ExcerptJudged += OnJudged;

            // Prewarm: every clip of the round is decoded before the rules card lets go (Load, never mid-round).
            for (int k = 0; k < HymnRound.ExcerptCount; k++)
            {
                var cand = _data.Pool.First(c => c.Name == _round.Playlist[k]);
                string name = cand.ClipFor(HymnLadder.SlotLevels[k]);
                _clips[k] = HymnData.Clip(name);
                if (_clips[k] == null) throw new System.InvalidOperationException("hymn data missing: clip " + name);
                _clips[k].LoadAudioData();
            }

            BuildStage();
        }

        public override void Begin(double now)
        {
            _round.Start(now + 2.0);   // a short silence before the first hymn leads in
            _now = now;
            _playing = true;
            _say.Banner(Title, 1.4f, Blurb);
        }

        public override void Press(string action, double time)
        {
            if (!_playing || _round.Done) return;
            double t = time + Save.OffsetMs / 1000.0;
            if (!_round.Press(action, t)) return;   // not an arrow, clear or submit: nothing to answer with
            int ki = System.Array.IndexOf(KeyOrder, action);
            if (ki < 0) return;   // clear and submit make no tick
            _flash[ki] = 1;
            Host.PlaySfx("tick", time, gain: 0.12f);
        }

        public override void Tick(double now, float dt)
        {
            _now = now;
            _round.Update(now);
            DriveMusic(now);
            UpdateBeat(now);
        }

        public override Outcome Result()
        {
            if (_round == null || !_round.Done) return null;
            if (_doneAt < 0) _doneAt = _now;
            if (_now - _doneAt < ResultHold) return null;
            _playing = false;
            return _round.Result(_ids);
        }

        public override IEnumerable<KeyValuePair<string, double>> TestChart()
        {
            foreach (var p in _round.PerfectPresses()) yield return new KeyValuePair<string, double>(p.Action, p.Time);
        }

        protected override void OnDestroy()
        {
            foreach (var c in _clips) if (c != null) c.UnloadAudioData();
            base.OnDestroy();
        }

        // ------------------------------------------------------------ music

        void DriveMusic(double now)
        {
            if (_round.Slots.Count == 0) return;
            int k = _musicSlot + 1;
            if (k < _round.Slots.Count && now >= _round.Slots[k].StartTime - PreRoll)
            {
                // File time 0 sounds at the slot's Shift; the deck starts in the middle if that is already past.
                Host.PlayClip(_clips[k], _round.Slots[k].Shift, _data.BeatOffset);
                _musicSlot = k;
            }
            if (_musicSlot >= 0 && _stoppedSlot < _musicSlot && now > _round.Slots[_musicSlot].WindowEnd + 0.05)
            {
                _stoppedSlot = _musicSlot;
                Host.MusicStop(0.4f);
            }
        }

        // ------------------------------------------------------------ round events

        void OnStarted(HymnSlot slot)
        {
            _lastLine = "";
            BuildLayout(slot);
            _say.Banner($"{slot.Index + 1} / {HymnRound.ExcerptCount}", 1.3f, slot.Level.ToString().ToUpperInvariant());
        }

        void OnRevealed(HymnSlot slot)
        {
            if (slot.Index == _layoutSlot) foreach (var t in _tiles) t.Pop = 0.6f;
        }

        void OnJudged(HymnSlot slot, HymnJudgement j)
        {
            var me = j.Results[0];
            if (me.Correct)
                _lastLine = "RIGHT" + (me.PostReveal ? " - AFTER THE REVEAL" : j.Winner == 0 ? " - FIRST!" : j.Winner > 0 ? " - a rival was first" : "");
            else
            {
                _lastLine = (me.Submitted ? "WRONG" : "NO ANSWER") + (j.Winner > 0 ? " - a rival got it" : "");
                Host.PlaySfx("miss", _now + 0.01);
            }
            _lastLineLife = 3f;
            MusicJudged(me.Correct);
        }

        // ------------------------------------------------------------ stage

        void BuildStage()
        {
            _cam = Camera.main.transform;
            Prim(PrimitiveType.Plane, new Vector3(0, -0.05f, 0), new Vector3(60, 1, 3), new Color(0.2f, 0.22f, 0.3f));
            _jumper = new GameObject("Jumper").transform;
            _jumper.SetParent(transform, false);
            Prim(PrimitiveType.Capsule, new Vector3(0, 0.6f, 0), new Vector3(0.7f, 0.6f, 0.7f), Hex(0xfff4d6), _jumper);
            Prim(PrimitiveType.Sphere, new Vector3(0.15f, 1.0f, -0.26f), Vector3.one * 0.26f, Color.black, _jumper);
            _cloudMat = Mat(new Color(0.96f, 0.97f, 1f));
            for (int i = 0; i < 52; i++) AddTile();
            PlaceCamera(0, true);
        }

        void AddTile()
        {
            var t = new Tile { Mat = Mat(Color.gray) };
            t.Root = new GameObject("Tile " + _tiles.Count).transform;
            t.Root.SetParent(transform, false);
            Prim(PrimitiveType.Cube, Vector3.zero, new Vector3(1.3f, 0.2f, 1.3f), t.Mat, t.Root);
            t.Cloud = Prim(PrimitiveType.Sphere, new Vector3(0, 0.25f, 0), new Vector3(1.9f, 0.9f, 1.7f), _cloudMat, t.Root).transform;
            t.Root.gameObject.SetActive(false);
            _tiles.Add(t);
        }

        void BuildLayout(HymnSlot slot)
        {
            while (_tiles.Count < slot.Beats.Count) AddTile();
            for (int i = 0; i < _tiles.Count; i++)
            {
                var t = _tiles[i];
                bool on = i < slot.Beats.Count;
                t.Root.gameObject.SetActive(on);
                if (!on) continue;
                var b = slot.Beats[i];
                t.Root.localPosition = new Vector3(i * TileStep, 0, 0);
                t.Mat.SetColor("_BaseColor", Hidden);   // the colour is the answer: it only shows once the cloud is gone
                t.Key = KeyColor(Meter.KeyFor(b.BeatsPerBar, b.BarPosition));
                t.CloudK = 1;
                t.Pop = 0;
                t.Cloud.localScale = new Vector3(1.9f, 0.9f, 1.7f);
            }
            _layoutSlot = slot.Index;
            _camX = -2;
        }

        void UpdateBeat(double now)
        {
            var slot = Shown;
            int idx = -1;
            float phase = 0;
            if (slot != null && slot.Beats.Count > 0)
            {
                var beats = slot.Beats;
                for (int i = beats.Count - 1; i >= 0; i--) if (beats[i].Time <= now) { idx = i; break; }
                if (idx >= 0)
                {
                    double next = idx + 1 < beats.Count ? beats[idx + 1].Time : beats[idx].Time + (idx > 0 ? beats[idx].Time - beats[idx - 1].Time : 0.5);
                    phase = (float)Mathf.Clamp01((float)((now - beats[idx].Time) / System.Math.Max(1e-3, next - beats[idx].Time)));
                }
            }
            if (idx != BeatIndex && idx >= 0 && idx < _tiles.Count) _tiles[idx].Pop = 1;
            BeatIndex = idx;
            BeatPhase = phase;
        }

        void PlaceCamera(float x, bool snap)
        {
            _camX = snap ? x : _camX;
            _cam.position = new Vector3(_camX + 2.5f, 4.4f, -9.5f);
            _cam.LookAt(new Vector3(_camX + 2.5f, 0.6f, 0));
            Camera.main.fieldOfView = 48;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            _say.Update(dt);
            if (_round == null || _cam == null) return;
            if (_lastLineLife > 0) _lastLineLife -= dt;
            for (int i = 0; i < 4; i++) _flash[i] = Damp(_flash[i], 0, 9, dt);

            var slot = Shown;
            bool revealed = slot != null && slot.Revealed;
            foreach (var t in _tiles)
            {
                if (!t.Root.gameObject.activeSelf) continue;
                t.CloudK = Damp(t.CloudK, revealed ? 0 : 1, 7, dt);
                t.Mat.SetColor("_BaseColor", Color.Lerp(Hidden, t.Key, 1 - t.CloudK));
                t.Cloud.localScale = new Vector3(1.9f, 0.9f, 1.7f) * Mathf.Max(0.0001f, t.CloudK);
                t.Cloud.gameObject.SetActive(t.CloudK > 0.02f);
                t.Pop = Damp(t.Pop, 0, 10, dt);
                t.Root.localScale = new Vector3(1, 1 - t.Pop * 0.35f, 1);
            }

            // The jumper hops one tile per beat, ahead of the first beat it waits left of the floor.
            float f = -1.5f;
            if (slot != null && slot.Beats.Count > 0)
            {
                if (BeatIndex < 0) f = -1.5f + Mathf.Clamp01(1 - (float)((slot.Beats[0].Time - _now) / 1.5)) * 0.5f;
                else f = BeatIndex + BeatPhase;
            }
            float arc = BeatIndex >= 0 ? 4 * BeatPhase * (1 - BeatPhase) * 0.8f : 0;
            _jumper.localPosition = new Vector3(f * TileStep, 0.1f + arc, 0);
            _camX = Damp(_camX, f * TileStep - 1f, 5, dt);
            PlaceCamera(_camX, false);
        }

        // ------------------------------------------------------------ HUD

        public override void DrawHud(float s)
        {
            if (_round == null) return;
            var slot = Shown;
            var white = Color.white;
            if (slot != null)
            {
                Hud.Text(new Rect(16 * s, 12 * s, 700 * s, 120 * s),
                    $"EXCERPT {slot.Index + 1}/{HymnRound.ExcerptCount}  {slot.Level.ToString().ToUpperInvariant()}", Hud.Left, white);
                string state = _now < slot.StartTime ? "LISTEN..." : slot.Revealed ? "REVEALED" : _now > slot.WindowEnd ? "TIME" : "CONDUCT";
                Hud.Center(0.06f, state, Hud.Mid, state == "REVEALED" ? Hud.Gold : white, 60);
                string pos = slot.Revealed && BarPosition >= 0 ? $"BAR POSITION {BarPosition + 1} of {BeatsPerBar}" : "BAR POSITION ?";
                Hud.Center(0.13f, pos, Hud.Small, white, 40);
                Hud.Center(0.18f, AnswerLine(slot), Hud.Small, white, 40);
            }
            // the answer boxes: filled with the key, the lit one is the beat's position in the answer
            var ans = slot?.Answer;
            if (ans != null)
            {
                int boxes = ans.Count > 0 ? ans.Count : 4;
                int lit = ans.LitBox(_now);
                float aw = 96 * s, ah = 64 * s, ag = 12 * s, ax0 = Screen.width / 2f - (boxes * aw + (boxes - 1) * ag) / 2f, ay = Screen.height - 240 * s;
                for (int i = 0; i < boxes; i++)
                {
                    var r = new Rect(ax0 + i * (aw + ag), ay, aw, ah);
                    string key = ans.KeyAt(i);
                    if (key == null) { Hud.Box(r, new Color(1, 1, 1, 0.12f)); continue; }
                    var c = KeyColor(key);
                    Hud.Box(r, new Color(c.r, c.g, c.b, i == lit ? 1f : 0.45f));
                    Hud.Text(r, key.ToUpperInvariant(), Hud.Small, i == lit ? Hud.Gold : white);
                }
            }
            // the four keys
            float bw = 110 * s, bh = 56 * s, gap = 10 * s, x0 = Screen.width / 2f - (4 * bw + 3 * gap) / 2f, y = Screen.height - 150 * s;
            for (int i = 0; i < 4; i++)
            {
                var r = new Rect(x0 + i * (bw + gap), y, bw, bh);
                var c = KeyColor(KeyOrder[i]);
                Hud.Box(r, new Color(c.r, c.g, c.b, 0.2f + 0.6f * _flash[i]));
                Hud.Text(r, KeyOrder[i].ToUpperInvariant(), Hud.Small, white);
            }
            // the six excerpts
            float sw = 70 * s, sh = 30 * s, sx = Screen.width / 2f - (6 * sw + 5 * 8 * s) / 2f, sy = Screen.height - 70 * s;
            for (int i = 0; i < HymnRound.ExcerptCount; i++)
            {
                var r = new Rect(sx + i * (sw + 8 * s), sy, sw, sh);
                var sl = i < _round.Slots.Count ? _round.Slots[i] : null;
                var j = sl?.Judgement;
                Color c = j == null ? new Color(1, 1, 1, i == _round.Current ? 0.35f : 0.12f) : j.Results[0].Correct ? new Color(0.35f, 0.85f, 0.45f, 0.8f) : new Color(0.9f, 0.35f, 0.35f, 0.8f);
                Hud.Box(r, c);
                Hud.Text(r, j == null ? (i + 1).ToString() : j.Winner == 0 ? "WON" : j.Results[0].Correct ? "OK" : "-", Hud.Small, white);
            }
            if (_lastLineLife > 0) Hud.Center(0.24f, _lastLine, Hud.Mid, Hud.Gold, 60);
            _say.Draw();
        }

        // One line under the state: where the player's answer stands.
        string AnswerLine(HymnSlot slot)
        {
            var a = slot.Answer;
            if (a.Locked) return slot.Revealed && a.SubmitTime > _round.RevealAt(slot) ? "LOCKED (AFTER THE REVEAL)" : "LOCKED";
            if (a.Full) return "ENTER TO LOCK   BACKSPACE TO CLEAR";
            if (a.Filled > 0) return $"BOX {a.Filled} of {a.Count}";
            return "PRESS THE FIRST KEY ON THE DOWNBEAT";
        }
    }
}
