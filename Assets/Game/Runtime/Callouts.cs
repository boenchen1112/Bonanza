// Placeholder feedback text shared by the greybox games: world-anchored
// verdict words that float up and fade, a centred banner, and the count-in
// number. Driven by frame time (it is presentation, never judgement).

using System.Collections.Generic;
using UnityEngine;

namespace RhythmGame
{
    public sealed class Callouts
    {
        sealed class Item { public string Text; public Vector3 World; public float Life, Age, Size; public Color Color; }

        readonly List<Item> _items = new List<Item>();
        string _banner, _sub, _count;
        float _bannerLife, _bannerAge, _countLife;
        Color _bannerColor = Hud.Gold;

        public static Color VerdictColor(Core.Verdict v) => v switch
        {
            Core.Verdict.Perfect => new Color(1f, 0.87f, 0.35f),
            Core.Verdict.Great => new Color(0.45f, 0.95f, 0.6f),
            Core.Verdict.Good => new Color(0.5f, 0.8f, 1f),
            _ => new Color(1f, 0.5f, 0.5f),
        };

        public static string VerdictWord(Core.Verdict v) => v switch
        {
            Core.Verdict.Perfect => "PERFECT",
            Core.Verdict.Great => "GREAT",
            Core.Verdict.Good => "GOOD",
            _ => "MISS",
        };

        /// A word at a world position. size: 1 = Mid style, smaller for CPU/dense.
        public void Say(string text, Vector3 world, Color color, float life = 0.7f, float size = 1)
            => _items.Add(new Item { Text = text, World = world, Color = color, Life = life, Size = size });

        public void Verdict(Core.Verdict v, Vector3 world, float size = 1) => Say(VerdictWord(v), world, VerdictColor(v), 0.6f, size);

        public void Banner(string text, float life = 1.2f, string sub = null, Color? color = null)
        {
            _banner = text; _sub = sub; _bannerLife = life; _bannerAge = 0; _bannerColor = color ?? Hud.Gold;
        }

        public void Count(string text) { _count = text; _countLife = 0.45f; }

        public void Update(float dt)
        {
            for (int i = _items.Count - 1; i >= 0; i--)
            {
                _items[i].Age += dt;
                if (_items[i].Age >= _items[i].Life) _items.RemoveAt(i);
            }
            _bannerAge += dt;
            _countLife -= dt;
        }

        public void Draw()
        {
            var cam = Camera.main;
            foreach (var c in _items)
            {
                float a = Mathf.Clamp01((c.Life - c.Age) / 0.2f);
                var style = c.Size >= 1 ? Hud.Mid : Hud.Small;
                Hud.AtWorld(cam, c.World + Vector3.up * (c.Age * 0.8f), c.Text, style, new Color(c.Color.r, c.Color.g, c.Color.b, a));
            }
            if (_banner != null && _bannerAge < _bannerLife)
            {
                float a = Mathf.Clamp01((_bannerLife - _bannerAge) / 0.25f);
                Hud.Center(0.26f, _banner, Hud.Big, new Color(_bannerColor.r, _bannerColor.g, _bannerColor.b, a), 120);
                if (_sub != null) Hud.Center(0.38f, _sub, Hud.Mid, new Color(1, 1, 1, a), 60);
            }
            if (_countLife > 0 && _count != null) Hud.Center(0.12f, _count, Hud.Big, Hud.Gold);
        }
    }
}
