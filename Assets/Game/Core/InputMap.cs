// The input vocabulary, device-free (web core/input.js KEY_MAP, PAD_MAP and pollGamepads).
// Every device maps into the same actions: a, b, left, down, up, right, pause. Keys are
// named by their web KeyboardEvent.code so the table reads the same as the prototype's;
// the Runtime turns a code into an Input System path.

using System;
using System.Collections.Generic;

namespace Bonanza.Core
{
    public static class InputMap
    {
        public const string A = "a", B = "b", Left = "left", Down = "down", Up = "up", Right = "right", Pause = "pause";

        /// Every action a minigame can be handed (pause is the host's).
        public static readonly string[] GameActions = { A, B, Left, Down, Up, Right };

        /// Web KEY_MAP, in the order it is declared.
        public static readonly KeyValuePair<string, string>[] Keys =
        {
            new KeyValuePair<string, string>("Space", A), new KeyValuePair<string, string>("KeyZ", A),
            new KeyValuePair<string, string>("KeyJ", A), new KeyValuePair<string, string>("Enter", A),
            new KeyValuePair<string, string>("KeyX", B), new KeyValuePair<string, string>("KeyK", B),
            new KeyValuePair<string, string>("ArrowLeft", Left), new KeyValuePair<string, string>("KeyA", Left),
            new KeyValuePair<string, string>("ArrowRight", Right), new KeyValuePair<string, string>("KeyD", Right),
            new KeyValuePair<string, string>("ArrowUp", Up), new KeyValuePair<string, string>("KeyW", Up),
            new KeyValuePair<string, string>("ArrowDown", Down), new KeyValuePair<string, string>("KeyS", Down),
            new KeyValuePair<string, string>("Escape", Pause), new KeyValuePair<string, string>("KeyP", Pause),
        };

        /// Web PAD_MAP: standard-mapping button index -> action.
        public static readonly KeyValuePair<int, string>[] PadButtons =
        {
            new KeyValuePair<int, string>(0, A), new KeyValuePair<int, string>(1, B),
            new KeyValuePair<int, string>(2, B), new KeyValuePair<int, string>(3, A),
            new KeyValuePair<int, string>(12, Up), new KeyValuePair<int, string>(13, Down),
            new KeyValuePair<int, string>(14, Left), new KeyValuePair<int, string>(15, Right),
            new KeyValuePair<int, string>(9, Pause),
        };

        public const float StickDeadzone = 0.5f;

        /// The action for a web key code, or null.
        public static string ForKey(string code)
        {
            foreach (var kv in Keys) if (kv.Key == code) return kv.Value;
            return null;
        }

        /// The action for a standard-mapping gamepad button, or null.
        public static string ForPadButton(int index)
        {
            foreach (var kv in PadButtons) if (kv.Key == index) return kv.Value;
            return null;
        }

        /// The back action of every menu: b or pause.
        public static bool IsBack(string action) => action == B || action == Pause;

        /// Finale Fever has two lanes: up folds into left, down and b into right (web finaleFever input()).
        public static string FinaleLane(string action)
        {
            if (action == Up) return Left;
            if (action == B || action == Down) return Right;
            return action;
        }
    }

    /// The left stick as a d-pad (web pollGamepads): a direction is on past +-0.5 on its axis.
    /// Axes follow the web: y grows downward, so up is y < -0.5.
    public sealed class StickDpad
    {
        bool _left, _right, _up, _down;

        /// Feed one stick sample; `emit(action, down)` is called for every direction that changed.
        public void Update(float ax, float ay, Action<string, bool> emit)
        {
            Step(ref _left, ax < -InputMap.StickDeadzone, InputMap.Left, emit);
            Step(ref _right, ax > InputMap.StickDeadzone, InputMap.Right, emit);
            Step(ref _up, ay < -InputMap.StickDeadzone, InputMap.Up, emit);
            Step(ref _down, ay > InputMap.StickDeadzone, InputMap.Down, emit);
        }

        static void Step(ref bool state, bool on, string action, Action<string, bool> emit)
        {
            if (on == state) return;
            state = on;
            emit(action, on);
        }

        public void Reset() { _left = _right = _up = _down = false; }
    }

    /// The actions currently down (web Input._held). Down and Up report whether anything changed,
    /// so a repeated key-down or a release of something not held is swallowed.
    public sealed class HeldActions
    {
        readonly HashSet<string> _held = new HashSet<string>();

        public bool IsHeld(string action) => _held.Contains(action);
        public int Count => _held.Count;

        public bool Down(string action) => _held.Add(action);
        public bool Up(string action) => _held.Remove(action);

        /// Window blur: everything held, in a stable order, then cleared.
        public List<string> ReleaseAll()
        {
            var all = new List<string>(_held);
            all.Sort(StringComparer.Ordinal);
            _held.Clear();
            return all;
        }
    }
}
