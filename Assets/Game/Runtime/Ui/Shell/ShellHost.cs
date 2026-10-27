// The uGUI shell: one canvas (1280x720 reference, like the game HUD) that holds the current ShellScreen over the
// ShellBackdrop. The GameHost keeps all logic, input and focus; this layer is a view. Screens publish their buttons'
// rectangles so the host can lay invisible IMGUI click targets over them (the ResultsCard pattern), and read back the
// hover and press state of the pointer.
//
//   shell.Show(shell.Title, "title");   // enter transition, camera moves to the pose
//   shell.Hide();                        // exit transition
//   shell.SetPose("hub");                // backdrop only

using System.Collections.Generic;
using Bonanza.HudKit;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Bonanza.Shell
{
    /// A screen of the shell: built once, shown with an eased 0..1 `Shown` the host animates.
    public abstract class ShellScreen
    {
        protected RectTransform Root;
        protected CanvasGroup Group;
        /// 0 hidden .. 1 fully in. Eased by the host (enter 0.55 s, exit 0.25 s; 0.15 s under reduced motion).
        public float Shown { get; internal set; }
        /// Seconds since the screen began entering.
        public float Age { get; internal set; }
        public bool Built => Root != null;
        public bool Visible => Root != null && Root.gameObject.activeSelf;
        /// The clickable buttons, in focus order. Rects are read each frame for the host's click targets.
        public readonly List<RectTransform> Buttons = new List<RectTransform>();
        int _pressButton = -1;
        /// Which pill holds the gold focus after a press moved it (0 the main action, 1 the back pill), or -1 for the default.
        public int PressFocus = -1;
        internal void NotePress(int i) { _pressButton = i; }
        /// The button the pointer went down on this frame (focus follows the press, the click then fires), or -1; read once.
        public int TakePressButton() { int c = _pressButton; _pressButton = -1; return c; }

        internal void BuildInto(RectTransform parent, string name)
        {
            Root = UiKit.Rect(name, parent); UiKit.Stretch(Root);
            Group = Root.gameObject.AddComponent<CanvasGroup>();
            Group.interactable = false; Group.blocksRaycasts = false;
            Build(Root);
            Root.gameObject.SetActive(false);
        }

        protected abstract void Build(RectTransform root);
        /// Per-frame: position and tint everything from `Shown`, `Age` and the screen's own state.
        public abstract void Tick(float dt, ShellInput input);

        /// False while something the screen shows is still being prepared (the host holds the entrance, up to a short wait).
        public virtual bool Ready => true;

        internal void SetActive(bool on) { if (Root) Root.gameObject.SetActive(on); }
    }

    /// What the pointer is doing over a screen's buttons.
    public struct ShellInput
    {
        public int Hover;        // button index under the pointer, -1 none
        public bool Pressed;     // pointer button held over `Hover`
        public bool Reduced;     // Settings.ReduceMotion
        public Vector2 Pointer;  // pointer in screen px, y up
        public bool Held;        // pointer button down anywhere (a slider drag keeps going off its track)
        public bool Fresh;       // the pointer button went down this frame
    }

    public sealed class ShellHost : MonoBehaviour
    {
        public const float EnterSeconds = 0.55f, ExitSeconds = 0.25f, ReducedSeconds = 0.15f;

        public ShellBackdrop Backdrop { get; private set; }
        public TitleScreen Title { get; private set; }
        public RosterScreen Roster { get; private set; }
        public PartyHubScreen Hub { get; private set; }
        public PartyFinalScreen Final { get; private set; }
        public FreePlayScreen FreePlay { get; private set; }
        public OptionsScreen Options { get; private set; }
        public ShellScreen Current { get; private set; }

        RectTransform _root;
        Canvas _canvas;
        ShellScreen _leaving;
        bool _exiting;
        Image _veil, _curtain;
        TMPro.TextMeshProUGUI _curtainText;
        float _veilT = 1, _readyWait, _coverT;
        bool _cover;
        /// Screen-to-screen dip: a dark wash that peaks mid-way, so the swap and the backdrop's pose move hide behind it.
        public const float VeilSeconds = 0.25f, VeilPeak = 0.5f;
        static readonly Vector3[] _corners = new Vector3[4];

        public static ShellHost Create(Transform parent)
        {
            var go = new GameObject("ShellUi", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var host = go.AddComponent<ShellHost>();
            host.Init();
            return host;
        }

        void Init()
        {
            _canvas = gameObject.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 10;   // under the game HUD (20)
            var sc = gameObject.AddComponent<CanvasScaler>();
            sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            sc.referenceResolution = new Vector2(1280, 720);
            sc.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            // Height-led: 4:3 shows 960 reference px across, 21:9 shows 1680; screens keep their core inside 900.
            sc.matchWidthOrHeight = 1;
            _root = (RectTransform)transform;
            var cr = UiKit.Rect("Curtain", _root); UiKit.Stretch(cr);
            _curtain = cr.gameObject.AddComponent<Image>();
            _curtain.color = new Color(UiKit.Ink.r, UiKit.Ink.g, UiKit.Ink.b, 1);
            _curtain.raycastTarget = false;
            _curtain.enabled = false;
            _curtainText = UiKit.Text(cr, "Line", TextStyle.Display, 30f, new Color(1, 0.93f, 0.7f, 0.5f), TMPro.TextAlignmentOptions.Center);
            UiKit.Stretch(_curtainText.rectTransform); _curtainText.text = "";
            Title = new TitleScreen();
            Title.BuildInto(_root, "Title");
            Roster = new RosterScreen();
            Roster.BuildInto(_root, "Roster");
            Hub = new PartyHubScreen();
            Hub.BuildInto(_root, "PartyHub");
            Final = new PartyFinalScreen();
            Final.BuildInto(_root, "PartyFinal");
            FreePlay = new FreePlayScreen();
            FreePlay.BuildInto(_root, "FreePlay");
            Options = new OptionsScreen();
            Options.BuildInto(_root, "Options");
            var vr = UiKit.Rect("Veil", _root); UiKit.Stretch(vr);
            _veil = vr.gameObject.AddComponent<Image>();
            _veil.color = new Color(UiKit.Ink.r, UiKit.Ink.g, UiKit.Ink.b, 0);
            _veil.raycastTarget = false;   // never eats a press
            _veil.enabled = false;
        }

        void TickVeil(float dt, bool reduced)
        {
            if (_veilT >= 1) return;
            if (reduced) { _veilT = 1; _veil.enabled = false; return; }
            _veilT = Mathf.Min(1, _veilT + dt / VeilSeconds);
            if (_veilT >= 1) { _veil.enabled = false; return; }
            var c = _veil.color; c.a = VeilPeak * Mathf.Sin(_veilT * Mathf.PI); _veil.color = c;
        }

        /// Solid wash behind the screens for the gap between a game closing and the next screen drawing:
        /// no frame is ever the bare 3D default sky. It lifts once the current screen is half in.
        public void Cover(string line = null)
        {
            _curtainText.text = line ?? "";
            _curtain.transform.SetAsLastSibling();   // above every screen: nothing (a title, a winner) shows through it before it lifts
            _cover = true; _coverT = 0;
            _curtain.enabled = true;
            var c = _curtain.color; c.a = 1; _curtain.color = c;
            var tc = _curtainText.color; tc.a = CurtainTextAlpha; _curtainText.color = tc;
        }

        const float CurtainTextAlpha = 0.5f, CurtainTextFade = 0.2f, CurtainFade = 0.3f;
        /// The cover is still up (or still fading): a screen that stages a reveal (the party's wrap-up) waits for it.
        public bool Covered => _curtain.enabled;

        public void Uncover() { _cover = false; }

        void TickCurtain(float dt)
        {
            if (!_curtain.enabled) return;
            _coverT += dt;
            bool lift = !_cover || _coverT > 4f || (Current != null && !_exiting && Current.Shown >= 0.3f && (Backdrop == null || Backdrop.Live));
            var c = _curtain.color;
            if (!lift) { c.a = 1; _curtain.color = c; return; }
            _cover = false;
            // the line leaves first on a solid cover, then the cover lifts: nothing behind it (a title, a winner) shows
            // through the words, at any screen size or frame rate
            var tc = _curtainText.color;
            if (tc.a > 0.001f && !string.IsNullOrEmpty(_curtainText.text))
            {
                tc.a = Mathf.MoveTowards(tc.a, 0, dt * CurtainTextAlpha / CurtainTextFade); _curtainText.color = tc;
                c.a = 1; _curtain.color = c;
                return;
            }
            tc.a = 0; _curtainText.color = tc;
            c.a = Mathf.MoveTowards(c.a, 0, dt / CurtainFade);
            _curtain.color = c;
            if (c.a <= 0.001f) _curtain.enabled = false;
        }

        void Update()
        {
            bool red = Settings.ReduceMotion;
            float dt = Time.unscaledDeltaTime;
            TickVeil(dt, red);
            TickCurtain(dt);
            // a screen enters once the one before it is nearly gone (no double exposure) and its own content is ready (no pop-in)
            bool hold = false;
            if (Current != null && !_exiting && Current.Shown < 1f)
            {
                if (_leaving != null && !red && _leaving.Shown > 0.2f) hold = true;
                else if (!Current.Ready && _readyWait < 1.5f) { hold = true; _readyWait += dt; }
            }
            if (!hold) Step(Current, !_exiting, dt, red);
            else if (Current != null && !_exiting) Current.Age = 0;
            if (Current == Final && !_exiting && Covered) Final.Age = 0;   // the wrap-up's reveal starts once the cover has fully lifted
            if (_leaving != null)
            {
                Step(_leaving, false, dt, red);
                if (_leaving.Shown <= 0.001f) { _leaving.SetActive(false); _leaving = null; }
            }
            var inp = ReadInput(Current, red);
            if (Current != null && inp.Fresh && inp.Hover >= 0) Current.NotePress(inp.Hover);
            if (Current != null && Current.Visible) Current.Tick(dt, inp);
            if (_leaving != null) _leaving.Tick(dt, default);
        }

        static void Step(ShellScreen s, bool entering, float dt, bool reduced)
        {
            if (s == null) return;
            float dur = reduced ? ReducedSeconds : entering ? EnterSeconds : ExitSeconds;
            s.Shown = Mathf.MoveTowards(s.Shown, entering ? 1 : 0, dt / dur);
            if (entering) s.Age += dt;
            if (!entering && s.Shown <= 0.001f) s.SetActive(false);
        }

        /// Show a screen (the old one eases out) and move the backdrop's camera to `pose`.
        public void Show(ShellScreen s, string pose)
        {
            if (Backdrop != null && pose != null) Backdrop.SetPose(pose);
            if (s == Current && !_exiting) return;
            if (Current != null && Current != s)
            {
                _leaving = Current;
                if (s != null && !Settings.ReduceMotion) { _veilT = 0; _veil.enabled = true; _veil.transform.SetAsLastSibling(); }
            }
            Current = s; _exiting = false; _readyWait = 0;
            if (s != null) { s.SetActive(true); if (s.Shown <= 0.001f) s.Age = 0; }
        }

        public void Hide()
        {
            if (Current == null || _exiting) return;
            _exiting = true;
        }

        /// Immediately gone (a game is taking over).
        public void HideNow()
        {
            if (Current != null) { Current.Shown = 0; Current.SetActive(false); }
            if (_leaving != null) { _leaving.Shown = 0; _leaving.SetActive(false); _leaving = null; }
            _exiting = false; Current = null;
        }

        public void AttachBackdrop(ShellBackdrop b) { Backdrop = b; }

        // ---------------------------------------------------------------- pointer

        ShellInput ReadInput(ShellScreen s, bool reduced)
        {
            var inp = new ShellInput { Hover = -1, Reduced = reduced };
            if (s == null || _exiting || s.Shown < 0.6f) return inp;
            var ptr = Pointer.current;
            if (ptr == null) return inp;
            var p = ptr.position.ReadValue();
            inp.Pointer = p; inp.Held = ptr.press.isPressed; inp.Fresh = ptr.press.wasPressedThisFrame;
            for (int i = 0; i < s.Buttons.Count; i++)
            {
                var r = ButtonScreen(s, i);
                // r is IMGUI space (y down); the pointer is y up
                if (r.width > 0 && r.Contains(new Vector2(p.x, Screen.height - p.y))) { inp.Hover = i; break; }
            }
            inp.Pressed = inp.Hover >= 0 && ptr.press.isPressed;
            return inp;
        }

        /// Button i of the current screen in IMGUI coordinates (y down), for the host's invisible click targets.
        public Rect ButtonScreen(int i) => ButtonScreen(Current, i);

        static Rect ButtonScreen(ShellScreen s, int i)
        {
            if (s == null || i < 0 || i >= s.Buttons.Count || !s.Buttons[i] || !s.Buttons[i].gameObject.activeInHierarchy) return Rect.zero;
            s.Buttons[i].GetWorldCorners(_corners);
            return new Rect(_corners[0].x, Screen.height - _corners[1].y, _corners[2].x - _corners[0].x, _corners[1].y - _corners[0].y);
        }

        public int ButtonCount => Current != null ? Current.Buttons.Count : 0;
        /// The current screen is fully in and takes clicks.
        public bool Interactive => Current != null && !_exiting && Current.Shown > 0.6f;
    }
}
