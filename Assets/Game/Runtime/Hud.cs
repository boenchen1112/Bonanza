// IMGUI text helpers shared by the host and every game. Placeholder HUD until
// the UI look is designed. The WebGL built-in font lacks symbols like ●○:
// keep every string ASCII.

using UnityEngine;

namespace RhythmGame
{
    public static class Hud
    {
        public static GUIStyle Big, Mid, Small, Left;
        public static float S = 1;

        /// Call at the top of every OnGUI.
        public static void Begin()
        {
            if (Big == null)
            {
                Big = new GUIStyle(GUI.skin.label) { fontSize = 64, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = false };
                Mid = new GUIStyle(Big) { fontSize = 30 };
                Small = new GUIStyle(Big) { fontSize = 20, fontStyle = FontStyle.Normal };
                Left = new GUIStyle(Small) { alignment = TextAnchor.UpperLeft };
            }
            S = Screen.height / 720f;
            Big.fontSize = (int)(64 * S); Mid.fontSize = (int)(30 * S); Small.fontSize = (int)(20 * S); Left.fontSize = (int)(20 * S);
        }

        public static readonly Color Gold = new Color(1f, 0.9f, 0.54f);

        public static void Text(Rect r, string text, GUIStyle style, Color color, TextAnchor? anchor = null)
        {
            var old = style.alignment;
            if (anchor.HasValue) style.alignment = anchor.Value;
            style.normal.textColor = new Color(0, 0, 0, color.a * 0.7f);
            GUI.Label(new Rect(r.x + 2, r.y + 2, r.width, r.height), text, style);
            style.normal.textColor = color;
            GUI.Label(r, text, style);
            style.alignment = old;
        }

        /// Centered across the screen at fraction `y` of its height.
        public static void Center(float y, string text, GUIStyle style, Color color, float h = 100)
            => Text(new Rect(0, Screen.height * y, Screen.width, h * S), text, style, color);

        public static void Dim(float alpha)
        {
            GUI.color = new Color(0, 0, 0, alpha);
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = Color.white;
        }

        public static void Box(Rect r, Color c)
        {
            GUI.color = c;
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = Color.white;
        }

        /// A world-anchored label; false if behind the camera.
        public static bool AtWorld(Camera cam, Vector3 world, string text, GUIStyle style, Color color, float w = 600, float h = 100)
        {
            var sp = cam.WorldToScreenPoint(world);
            if (sp.z < 0) return false;
            Text(new Rect(sp.x - w * 0.5f * S, Screen.height - sp.y - h * 0.5f * S, w * S, h * S), text, style, color);
            return true;
        }
    }
}
