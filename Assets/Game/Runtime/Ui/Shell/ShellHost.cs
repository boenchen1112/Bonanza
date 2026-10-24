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

        internal void SetActive(bool on) { if (Root) Root.gameObject.SetActive(on); }
    }

    /// What the pointer is doing over a screen's buttons.
    public struct ShellInput
    {
        public int Hover;        // button index under the pointer, -1 none
        public bool Pressed;     // pointer button held over `Hover`
        public bool Reduced;     // Settings.ReduceMotion
    }

    public sealed class ShellHost : MonoBehaviour
    {
        public const float EnterSeconds = 0.55f, ExitSeconds = 0.25f, ReducedSeconds = 0.15f;

        public ShellBackdrop Backdrop { get; private set; }
        public TitleScreen Title { get; private set; }
        public RosterScreen Roster { get; private set; }
        public PartyHubScreen Hub { get; private set; }
        public PartyFinalScreen Final { get; private set; }
        public ShellScreen Current { get; private set; }

        RectTransform _root;
        Canvas _canvas;
        ShellScreen _leaving;
        bool _exiting;
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
            Title = new TitleScreen();
            Title.BuildInto(_root, "Title");
            Roster = new RosterScreen();
            Roster.BuildInto(_root, "Roster");
            Hub = new PartyHubScreen();
            Hub.BuildInto(_root, "PartyHub");
            Final = new PartyFinalScreen();
            Final.BuildInto(_root, "PartyFinal");
        }

        void Update()
        {
            bool red = Settings.ReduceMotion;
            float dt = Time.unscaledDeltaTime;
            Step(Current, !_exiting, dt, red);
            if (_leaving != null)
            {
                Step(_leaving, false, dt, red);
                if (_leaving.Shown <= 0.001f) { _leaving.SetActive(false); _leaving = null; }
            }
            var inp = ReadInput(Current, red);
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
            if (Current != null && Current != s) { _leaving = Current; }
            Current = s; _exiting = false;
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
            if (s == null || i < 0 || i >= s.Buttons.Count || !s.Buttons[i]) return Rect.zero;
            s.Buttons[i].GetWorldCorners(_corners);
            return new Rect(_corners[0].x, Screen.height - _corners[1].y, _corners[2].x - _corners[0].x, _corners[1].y - _corners[0].y);
        }

        public int ButtonCount => Current != null ? Current.Buttons.Count : 0;
        /// The current screen is fully in and takes clicks.
        public bool Interactive => Current != null && !_exiting && Current.Shown > 0.6f;
    }
}
