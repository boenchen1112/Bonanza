// Palettes: the colour half of the house style (web render/look/palette.js).
// One named palette per minigame plus the shell. A palette is a complete
// description of a lit world; PaletteState cross-fades every channel so a
// scene change is a lighting change, not a cut. Colours are authored as sRGB
// hex, held as gamma Colors, and mixed in linear space like the web does.
// Directions are the web's three.js space (z toward the camera); LookRig
// flips z for Unity.

using System;
using System.Collections.Generic;
using UnityEngine;

namespace Bonanza.Look
{
    public enum PC
    {
        SkyTop, SkyMid, SkyBot, Sun, Ground, GroundAlt, GroundRim, Band, BandAlt,
        Accent, Accent2, Accent3, Key, FillSky, FillGround, Rim, Fog,
        Dirt, Metal, Batter, Plate,   // stand-in props, so the day/night swap carries them
        Perfect, Great, Good, Miss,   // verdict colours: re-tinted per palette, audited like the accents
    }

    public enum PF
    {
        SunSize, SunStrength, KeyIntensity, FillIntensity, RimStrength, RimPower,
        FogNear, FogFar, Exposure, Bloom, BloomThreshold, Saturation, Vignette,
        FogHeight, FogHeightY,   // extra fog that rises with height and distance (0 = off: plain depth fog)
    }

    public enum PD { SunDir, KeyDir, RimDir }

    public sealed class Palette
    {
        public readonly Color[] C = new Color[Enum.GetValues(typeof(PC)).Length];
        public readonly float[] F = new float[Enum.GetValues(typeof(PF)).Length];
        public readonly Vector3[] D = new Vector3[Enum.GetValues(typeof(PD)).Length];
        public Color[] Crowd = new Color[0];

        public Palette Clone()
        {
            var p = new Palette();
            Array.Copy(C, p.C, C.Length);
            Array.Copy(F, p.F, F.Length);
            Array.Copy(D, p.D, D.Length);
            p.Crowd = (Color[])Crowd.Clone();
            return p;
        }

        public Color this[PC k] { get => C[(int)k]; set => C[(int)k] = value; }
        public float this[PF k] { get => F[(int)k]; set => F[(int)k] = value; }
        public Vector3 this[PD k] { get => D[(int)k]; set => D[(int)k] = value; }

        public static Color Hex(uint rgb) =>
            new Color(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f);

        public Palette Set(PC k, uint hex) { C[(int)k] = Hex(hex); return this; }
        public Palette Set(PF k, float v) { F[(int)k] = v; return this; }
        public Palette Set(PD k, float x, float y, float z) { D[(int)k] = new Vector3(x, y, z); return this; }
        public Palette SetCrowd(params uint[] hex)
        {
            Crowd = new Color[hex.Length];
            for (int i = 0; i < hex.Length; i++) Crowd[i] = Hex(hex[i]);
            return this;
        }
    }

    public static class Palettes
    {
        /// Gameplay colours must differ from their backdrop by at least this much relative luminance.
        public const float MinLumaDelta = 0.17f;

        static readonly Dictionary<string, Palette> _all = new Dictionary<string, Palette>();
        static Palette _base;

        public static Palette Get(string name)
        {
            Init();
            return name != null && _all.TryGetValue(name, out var p) ? p : _all["default"];
        }

        static Palette Make(Palette from, Action<Palette> f)
        {
            var p = from.Clone();
            f(p);
            Audit(p);
            return p;
        }

        static void Init()
        {
            if (_base != null) return;
            _base = new Palette()
                .Set(PC.SkyTop, 0x1a1140).Set(PC.SkyMid, 0x3b2470).Set(PC.SkyBot, 0xff8f5e)
                .Set(PD.SunDir, -0.25f, 0.12f, -1).Set(PC.Sun, 0xffd6a0)
                .Set(PF.SunSize, 0.22f).Set(PF.SunStrength, 0.75f)
                .Set(PC.Ground, 0x2c2154).Set(PC.GroundAlt, 0x3a2c6b).Set(PC.GroundRim, 0xffd93d)
                .Set(PC.Band, 0x4a3382).Set(PC.BandAlt, 0x6a45a8)
                .Set(PC.Accent, 0xffd93d).Set(PC.Accent2, 0x4dd6ff).Set(PC.Accent3, 0xff5d73)
                .SetCrowd(0xffd93d, 0x4dd6ff, 0xff5d73, 0x9ee87a, 0xffffff)
                .Set(PC.Key, 0xfff2dc).Set(PD.KeyDir, -0.55f, 0.78f, 0.42f).Set(PF.KeyIntensity, 2.5f)
                .Set(PC.FillSky, 0x9fb8ff).Set(PC.FillGround, 0x3b1f52).Set(PF.FillIntensity, 1.15f)
                .Set(PC.Rim, 0x8fe8ff).Set(PD.RimDir, 0.62f, 0.35f, -0.7f).Set(PF.RimStrength, 0.85f).Set(PF.RimPower, 2.6f)
                .Set(PC.Fog, 0x3b2470).Set(PF.FogNear, 16).Set(PF.FogFar, 62)
                .Set(PF.Exposure, 1.05f).Set(PF.Bloom, 0.85f).Set(PF.BloomThreshold, 0.72f)
                .Set(PF.Saturation, 1.08f).Set(PF.Vignette, 0.42f)
                .Set(PC.Dirt, 0xc08a5a).Set(PC.Metal, 0x73737f).Set(PC.Batter, 0x8c8c99).Set(PC.Plate, 0xffffff)
                .Set(PC.Perfect, Feel.Perfect).Set(PC.Great, Feel.Great).Set(PC.Good, Feel.Good).Set(PC.Miss, Feel.Miss);

            _all["default"] = Make(_base, p => { });

            // Shell: dusk fairground. Warm horizon, cool zenith, gold furniture.
            _all["shell"] = Make(_base, p => p
                .Set(PC.SkyTop, 0x140b38).Set(PC.SkyMid, 0x3d1f6e).Set(PC.SkyBot, 0xff7a4d)
                .Set(PC.Sun, 0xffc06a).Set(PD.SunDir, 0, -0.05f, -1).Set(PF.SunStrength, 0.9f).Set(PF.SunSize, 0.3f)
                .Set(PC.Ground, 0x241a4e).Set(PC.GroundAlt, 0x33265f).Set(PC.GroundRim, 0xffd93d)
                .Set(PC.Band, 0x4a2a80).Set(PC.BandAlt, 0x7a3fb0)
                .Set(PC.Accent, 0xffd93d).Set(PC.Accent2, 0x59e2ff).Set(PC.Accent3, 0xff6bd6)
                .Set(PC.Rim, 0xff8fd8).Set(PD.RimDir, 0.5f, 0.3f, -0.8f).Set(PF.RimStrength, 0.9f)
                .Set(PC.FillSky, 0x8ea6ff).Set(PC.FillGround, 0x4a2050)
                .Set(PC.Fog, 0x3a1f68).Set(PF.FogNear, 18).Set(PF.FogFar, 70).Set(PF.Bloom, 0.95f));

            // Swing Kings: floodlit dusk ballpark. Turf green, sodium-lamp key.
            _all["swing-kings"] = Make(_base, p => p
                .Set(PC.SkyTop, 0x08163f).Set(PC.SkyMid, 0x1d3f86).Set(PC.SkyBot, 0xff9a5c)
                .Set(PC.Sun, 0xffcf8a).Set(PD.SunDir, 0.35f, -0.02f, -1).Set(PF.SunStrength, 0.85f).Set(PF.SunSize, 0.26f)
                .Set(PC.Ground, 0x1c6b45).Set(PC.GroundAlt, 0x238055).Set(PC.GroundRim, 0xfff0a8)
                .Set(PC.Band, 0x14336e).Set(PC.BandAlt, 0x2a5aa8)
                .Set(PC.Accent, 0xffd93d).Set(PC.Accent2, 0x5ce1ff).Set(PC.Accent3, 0xff6b5d)
                .SetCrowd(0xffd93d, 0x5ce1ff, 0xff6b5d, 0xffffff, 0x9ee87a)
                .Set(PC.Key, 0xfff6e0).Set(PD.KeyDir, -0.5f, 0.82f, 0.35f).Set(PF.KeyIntensity, 2.7f)
                .Set(PC.FillSky, 0x9dc6ff).Set(PC.FillGround, 0x123f2c).Set(PF.FillIntensity, 1.2f)
                .Set(PC.Rim, 0xffe9a8).Set(PD.RimDir, 0.6f, 0.3f, -0.75f).Set(PF.RimStrength, 0.95f)
                .Set(PC.Fog, 0x6a5a8e).Set(PF.FogNear, 12).Set(PF.FogFar, 60)   // sky-tinted dusk haze: the far bowl takes ~25%, the near stands none
                .Set(PF.FogHeight, 0.4f).Set(PF.FogHeightY, 1.6f)   // and it rises: upper rows and the skyline sink into it
                .Set(PF.Bloom, 0.8f).Set(PF.BloomThreshold, 1.35f).Set(PF.Exposure, 1.08f));

            // Late innings: the sun is gone and the stadium lights carry the frame.
            _all["swing-kings-night"] = Make(_base, p => p
                .Set(PC.SkyTop, 0x030820).Set(PC.SkyMid, 0x0a1a48).Set(PC.SkyBot, 0x3a2a70)
                .Set(PC.Sun, 0xb8d4ff).Set(PD.SunDir, 0.35f, -0.08f, -1).Set(PF.SunStrength, 0.14f).Set(PF.SunSize, 0.18f)
                .Set(PC.Ground, 0x0b3d33).Set(PC.GroundAlt, 0x0f4a3d).Set(PC.GroundRim, 0xd8f0ff)   // dark blue-green turf: the floods' pools carry the light
                .Set(PC.Band, 0x0e2458).Set(PC.BandAlt, 0x2346a0)
                .Set(PC.Accent, 0xffe066).Set(PC.Accent2, 0x7fe7ff).Set(PC.Accent3, 0xff5d7a)
                .SetCrowd(0xffe066, 0x7fe7ff, 0xff5d7a, 0xffffff, 0xb28cff)
                .Set(PC.Key, 0xfff0d6).Set(PD.KeyDir, -0.35f, 0.9f, 0.3f).Set(PF.KeyIntensity, 2.5f)
                .Set(PC.FillSky, 0x2c4490).Set(PC.FillGround, 0x081f18).Set(PF.FillIntensity, 0.2f)   // (web 0.72) the floods, not the sky, light the night
                .Set(PC.Rim, 0xbfe6ff).Set(PD.RimDir, 0.6f, 0.35f, -0.75f).Set(PF.RimStrength, 1.45f)   // backlit crowd and set edges
                .Set(PC.Fog, 0x0e1a4a).Set(PF.FogNear, 13).Set(PF.FogFar, 58).Set(PF.FogHeight, 0.5f).Set(PF.FogHeightY, 1.4f)   // deep-blue night air
                .Set(PF.Bloom, 0.9f).Set(PF.BloomThreshold, 1.5f).Set(PF.Exposure, 1.0f).Set(PF.Saturation, 1.2f).Set(PF.Vignette, 0.6f)
                .Set(PC.Metal, 0x585a72).Set(PC.Batter, 0x6c7090));   // Dirt and Plate stay as in the base: the grade alone darkens them at night

            // Drumline Dash: hot night parade. Magenta and ember.
            _all["drumline-dash"] = Make(_base, p => p
                .Set(PC.SkyTop, 0x1e0630).Set(PC.SkyMid, 0x6a1055).Set(PC.SkyBot, 0xff5a3c)
                .Set(PC.Sun, 0xff9a4a).Set(PD.SunDir, -0.4f, -0.06f, -1).Set(PF.SunStrength, 0.95f).Set(PF.SunSize, 0.28f)
                .Set(PC.Ground, 0x2c0c3e).Set(PC.GroundAlt, 0x3d1252).Set(PC.GroundRim, 0xffb03a)
                .Set(PC.Band, 0x5c1160).Set(PC.BandAlt, 0x94207a)
                .Set(PC.Accent, 0xffb03a).Set(PC.Accent2, 0xff5fd0).Set(PC.Accent3, 0x5ce1ff)
                .SetCrowd(0xffb03a, 0xff5fd0, 0x5ce1ff, 0xffffff, 0xffd93d)
                .Set(PC.Key, 0xffe7cf).Set(PD.KeyDir, -0.6f, 0.75f, 0.4f).Set(PF.KeyIntensity, 2.6f)
                .Set(PC.FillSky, 0xff9ad8).Set(PC.FillGround, 0x3a0d40).Set(PF.FillIntensity, 1.1f)
                .Set(PC.Rim, 0xff6bd6).Set(PF.RimStrength, 1.0f)
                .Set(PC.Fog, 0x5a1050).Set(PF.FogNear, 15).Set(PF.FogFar, 60)
                .Set(PF.Bloom, 1.0f).Set(PF.BloomThreshold, 1.3f));

            // Bounce Brigade: bright poolside afternoon, cool and clean.
            _all["bounce-brigade"] = Make(_base, p => p
                .Set(PC.SkyTop, 0x042a52).Set(PC.SkyMid, 0x0d6f9e).Set(PC.SkyBot, 0x5fe0d0)
                .Set(PC.Sun, 0xbdfff2).Set(PD.SunDir, 0.2f, 0.05f, -1).Set(PF.SunStrength, 0.7f).Set(PF.SunSize, 0.24f)
                .Set(PC.Ground, 0x0b4a63).Set(PC.GroundAlt, 0x0f5f7c).Set(PC.GroundRim, 0x9ee87a)
                .Set(PC.Band, 0x0d5f8a).Set(PC.BandAlt, 0x1a86b8)
                .Set(PC.Accent, 0x9ee87a).Set(PC.Accent2, 0xffd93d).Set(PC.Accent3, 0xff7ad0)
                .SetCrowd(0x9ee87a, 0xffd93d, 0xff7ad0, 0xffffff, 0x7ef7ff)
                .Set(PC.Key, 0xf2fbff).Set(PD.KeyDir, -0.45f, 0.8f, 0.4f).Set(PF.KeyIntensity, 2.6f)
                .Set(PC.FillSky, 0x9ef0ff).Set(PC.FillGround, 0x0a3d52).Set(PF.FillIntensity, 1.25f)
                .Set(PC.Rim, 0x7ef7ff).Set(PF.RimStrength, 0.85f)
                .Set(PC.Fog, 0x0d6489).Set(PF.FogNear, 20).Set(PF.FogFar, 76)
                .Set(PF.Bloom, 0.75f).Set(PF.BloomThreshold, 1.3f).Set(PF.Saturation, 1.1f));

            // Chomp Chorus: purple midnight kitchen, acid-lime highlights.
            // Bounce Brigade (the hymn meter) climbs a stair of columns above a sea of cloud, one sky per level:
            // Easy at dawn, Medium in full day, Hard at vespers. Clouds are the ground: Ground is their shadow tone
            // (the shadow tint is derived from it), the fill from below is the light bounced off the cloud sea.
            _all["bounce-brigade-easy"] = Make(_base, p => p
                .Set(PC.SkyTop, 0x3a5cc6).Set(PC.SkyMid, 0x8ba2ee).Set(PC.SkyBot, 0xffc49a)
                .Set(PC.Sun, 0xfff0cc).Set(PD.SunDir, 0.34f, 0.13f, -1).Set(PF.SunStrength, 0.9f).Set(PF.SunSize, 0.26f)
                .Set(PC.Ground, 0x8a7fb8).Set(PC.GroundAlt, 0xb3a6d6).Set(PC.GroundRim, 0xffe3a8)
                .Set(PC.Band, 0x6a78c8).Set(PC.BandAlt, 0x9aa6e6)
                .Set(PC.Accent, 0xffc23d).Set(PC.Accent2, 0x2f9bff).Set(PC.Accent3, 0xff4f9a)
                .SetCrowd(0xffffff, 0xfff1e0, 0xf0f0ff)
                .Set(PC.Key, 0xfff0dc).Set(PD.KeyDir, -0.5f, 0.72f, 0.48f).Set(PF.KeyIntensity, 2.45f)
                .Set(PC.FillSky, 0x7f84f2).Set(PC.FillGround, 0xffc6b4).Set(PF.FillIntensity, 1.3f)
                .Set(PC.Rim, 0xffdcae).Set(PD.RimDir, 0.55f, 0.35f, -0.75f).Set(PF.RimStrength, 1.25f)
                .Set(PC.Fog, 0xf4c8b4).Set(PF.FogNear, 30).Set(PF.FogFar, 170)
                .Set(PF.Bloom, 0.55f).Set(PF.BloomThreshold, 1.45f).Set(PF.Exposure, 1.0f).Set(PF.Saturation, 1.12f).Set(PF.Vignette, 0.3f));
            _all["bounce-brigade-medium"] = Make(_base, p => p
                .Set(PC.SkyTop, 0x1a4fd4).Set(PC.SkyMid, 0x4a92f2).Set(PC.SkyBot, 0xbcdfff)
                .Set(PC.Sun, 0xfffbec).Set(PD.SunDir, 0.28f, 0.3f, -1).Set(PF.SunStrength, 0.85f).Set(PF.SunSize, 0.22f)
                .Set(PC.Ground, 0x6f8fc8).Set(PC.GroundAlt, 0x9fbbe8).Set(PC.GroundRim, 0xfff2b0)
                .Set(PC.Band, 0x3f74c8).Set(PC.BandAlt, 0x76a6ea)
                .Set(PC.Accent, 0xffc23d).Set(PC.Accent2, 0x2f9bff).Set(PC.Accent3, 0xff4f9a)
                .SetCrowd(0xffffff, 0xf4f8ff, 0xfffaf0)
                .Set(PC.Key, 0xfffaf0).Set(PD.KeyDir, -0.42f, 0.8f, 0.42f).Set(PF.KeyIntensity, 2.9f)
                .Set(PC.FillSky, 0x6d8eff).Set(PC.FillGround, 0xe8f0ff).Set(PF.FillIntensity, 1.35f)
                .Set(PC.Rim, 0xffffff).Set(PD.RimDir, 0.5f, 0.4f, -0.75f).Set(PF.RimStrength, 1.1f)
                .Set(PC.Fog, 0xc0deff).Set(PF.FogNear, 30).Set(PF.FogFar, 170)
                .Set(PF.Bloom, 0.5f).Set(PF.BloomThreshold, 1.5f).Set(PF.Exposure, 1.04f).Set(PF.Saturation, 1.14f).Set(PF.Vignette, 0.28f));
            _all["bounce-brigade-hard"] = Make(_base, p => p
                .Set(PC.SkyTop, 0x1a1552).Set(PC.SkyMid, 0x51399a).Set(PC.SkyBot, 0xff9472)
                .Set(PC.Sun, 0xffc68a).Set(PD.SunDir, 0.3f, 0.08f, -1).Set(PF.SunStrength, 1.0f).Set(PF.SunSize, 0.3f)
                .Set(PC.Ground, 0x5a3f86).Set(PC.GroundAlt, 0x7a5aa6).Set(PC.GroundRim, 0xffc68a)
                .Set(PC.Band, 0x45307e).Set(PC.BandAlt, 0x7048a8)
                .Set(PC.Accent, 0xffc23d).Set(PC.Accent2, 0x2f9bff).Set(PC.Accent3, 0xff4f9a)
                .SetCrowd(0xfff0f0, 0xffe6d0, 0xf0e6ff)
                .Set(PC.Key, 0xffd9b4).Set(PD.KeyDir, -0.55f, 0.62f, 0.5f).Set(PF.KeyIntensity, 2.3f)
                .Set(PC.FillSky, 0x7a70e0).Set(PC.FillGround, 0xc48ab0).Set(PF.FillIntensity, 1.25f)
                .Set(PC.Rim, 0xffa86e).Set(PD.RimDir, 0.6f, 0.3f, -0.75f).Set(PF.RimStrength, 1.25f)
                .Set(PC.Fog, 0xc98ea2).Set(PF.FogNear, 28).Set(PF.FogFar, 160)
                .Set(PF.Bloom, 0.6f).Set(PF.BloomThreshold, 1.4f).Set(PF.Exposure, 1.02f).Set(PF.Saturation, 1.1f).Set(PF.Vignette, 0.36f));

            _all["chomp-chorus"] = Make(_base, p => p
                .Set(PC.SkyTop, 0x110226).Set(PC.SkyMid, 0x3f0e5e).Set(PC.SkyBot, 0xb03bb0)
                .Set(PC.Sun, 0xff9ae0).Set(PD.SunDir, 0.1f, -0.08f, -1).Set(PF.SunStrength, 0.8f).Set(PF.SunSize, 0.3f)
                .Set(PC.Ground, 0x341457).Set(PC.GroundAlt, 0x3d1660).Set(PC.GroundRim, 0x9ee87a)
                .Set(PC.Band, 0x45126a).Set(PC.BandAlt, 0x6d1f9c)
                .Set(PC.Accent, 0x9ee87a).Set(PC.Accent2, 0xff5d73).Set(PC.Accent3, 0x6fd0ff)
                .SetCrowd(0x9ee87a, 0xff5d73, 0x6fd0ff, 0xffd93d, 0xffffff)
                .Set(PC.Key, 0xf0e2ff).Set(PD.KeyDir, -0.5f, 0.78f, 0.38f).Set(PF.KeyIntensity, 2.4f)
                .Set(PC.FillSky, 0xb98cff).Set(PC.FillGround, 0x2a0b40).Set(PF.FillIntensity, 1.05f)
                .Set(PC.Rim, 0x9ee87a).Set(PF.RimStrength, 1.0f).Set(PF.RimPower, 2.2f)
                .Set(PC.Fog, 0x3a0d58).Set(PF.FogNear, 14).Set(PF.FogFar, 58)
                .Set(PF.Bloom, 1.0f).Set(PF.BloomThreshold, 1.3f));   // beams and rings glow without washing the faces out

            // Chomp Chorus' last bar: "ALL FOUR!" warms the house up, a pinker horizon, a hotter key, more bloom.
            _all["chomp-chorus-finale"] = Make(_all["chomp-chorus"], p => p
                .Set(PC.SkyMid, 0x5e1470).Set(PC.SkyBot, 0xff6fae).Set(PC.Sun, 0xffd0f0).Set(PF.SunStrength, 1.05f)
                .Set(PC.Key, 0xfff0f6).Set(PF.KeyIntensity, 2.75f).Set(PC.Rim, 0xffd93d)
                .Set(PC.Fog, 0x4a1068).Set(PF.Bloom, 1.2f).Set(PF.Exposure, 1.06f));

            // Finale Fever: crimson and gold, fireworks over a dark house.
            _all["finale-fever"] = Make(_base, p => p
                .Set(PC.SkyTop, 0x1c0418).Set(PC.SkyMid, 0x74102c).Set(PC.SkyBot, 0xff7a2f)
                .Set(PC.Sun, 0xffb45a).Set(PD.SunDir, 0, 0, -1).Set(PF.SunStrength, 1.0f).Set(PF.SunSize, 0.34f)
                .Set(PC.Ground, 0x2c0820).Set(PC.GroundAlt, 0x3f0c2c).Set(PC.GroundRim, 0xffd93d)
                .Set(PC.Band, 0x5e0d28).Set(PC.BandAlt, 0x9c1a38)
                .Set(PC.Accent, 0xffd93d).Set(PC.Accent2, 0xff8a3c).Set(PC.Accent3, 0x6fd0ff)
                .SetCrowd(0xffd93d, 0xff8a3c, 0x6fd0ff, 0xffffff, 0xff5d73)
                .Set(PC.Key, 0xfff0d8).Set(PD.KeyDir, -0.55f, 0.76f, 0.42f).Set(PF.KeyIntensity, 2.7f)
                .Set(PC.FillSky, 0xffa9a0).Set(PC.FillGround, 0x360a24).Set(PF.FillIntensity, 1.1f)
                .Set(PC.Rim, 0xffb45a).Set(PF.RimStrength, 1.05f)
                .Set(PC.Fog, 0x63102a).Set(PF.FogNear, 14).Set(PF.FogFar, 64)
                .Set(PF.Bloom, 1.1f).Set(PF.BloomThreshold, 1.35f).Set(PF.Exposure, 1.1f));
        }

        // ------------------------------------------------------------ colour maths

        static float Lin(float v) => v <= 0.04045f ? v / 12.92f : Mathf.Pow((v + 0.055f) / 1.055f, 2.4f);

        /// WCAG relative luminance of an sRGB colour.
        public static float Luminance(Color c) => 0.2126f * Lin(c.r) + 0.7152f * Lin(c.g) + 0.0722f * Lin(c.b);
        public static float LumaDelta(Color a, Color b) => Mathf.Abs(Luminance(a) - Luminance(b));

        static Color FromHsl(float h, float s, float l)
        {
            float a = s * Mathf.Min(l, 1 - l);
            float F(float n) { float k = (n + h * 12) % 12; return l - a * Mathf.Max(-1, Mathf.Min(Mathf.Min(k - 3, 9 - k), 1)); }
            return new Color(F(0), F(8), F(4));
        }

        static void ToHsl(Color c, out float h, out float s, out float l)
        {
            float max = Mathf.Max(c.r, c.g, c.b), min = Mathf.Min(c.r, c.g, c.b);
            l = (max + min) / 2; h = 0; s = 0;
            float d = max - min;
            if (d < 1e-6f) return;
            s = l > 0.5f ? d / (2 - max - min) : d / (max + min);
            if (max == c.r) h = ((c.g - c.b) / d + (c.g < c.b ? 6 : 0)) / 6;
            else if (max == c.g) h = ((c.b - c.r) / d + 2) / 6;
            else h = ((c.r - c.g) / d + 4) / 6;
        }

        /// Push fg away from bg in luminance until they are min apart, keeping hue.
        public static Color EnsureContrast(Color fg, Color bg, float min = MinLumaDelta)
        {
            if (LumaDelta(fg, bg) >= min) return fg;
            ToHsl(fg, out float h, out float s, out float l);
            bool up = Luminance(fg) >= Luminance(bg);
            Color o = fg;
            for (int i = 1; i <= 14; i++)
            {
                float step = i * 0.045f;
                float nl = Mathf.Clamp01(up ? l + step : l - step);
                float ns = Mathf.Clamp01(s + (up ? step * 0.35f : step * 0.15f));
                o = FromHsl(h, ns, nl);
                if (LumaDelta(o, bg) >= min) return o;
                if ((up && nl >= 1) || (!up && nl <= 0)) break;
            }
            for (int i = 1; i <= 14; i++)
            {
                float step = i * 0.05f;
                float nl = Mathf.Clamp01(up ? l - step : l + step);
                o = FromHsl(h, Mathf.Clamp01(s + step * 0.2f), nl);
                if (LumaDelta(o, bg) >= min) return o;
            }
            return o;
        }

        /// Gameplay colours must read against the sky and the ground behind them.
        static void Audit(Palette p)
        {
            var backs = new[] { p[PC.SkyMid], p[PC.SkyTop], p[PC.Ground], p[PC.Band] };
            foreach (var k in new[] { PC.Accent, PC.Accent2, PC.Accent3, PC.GroundRim, PC.Perfect, PC.Great, PC.Good, PC.Miss })
            {
                var v = p[k];
                foreach (var bg in backs) v = EnsureContrast(v, bg);
                p[k] = v;
            }
        }
    }

    /// A palette being used: holds the live values everything reads each frame
    /// and cross-fades them on a change (web PaletteState).
    public sealed class PaletteState
    {
        public string Name { get; private set; }
        public readonly Palette Live;
        Palette _from, _to;
        float _t = 1, _dur = 0.001f;

        public bool Transitioning => _t < 1;
        public Palette Target => _to;

        public PaletteState(string name)
        {
            _to = _from = Palettes.Get(name);
            Name = name;
            Live = _to.Clone();
        }

        public void Set(string name, float dur = 0.55f)
        {
            var p = Palettes.Get(name);
            if (p == _to && _t >= 1) { Name = name; return; }
            _from = Live.Clone();
            _to = p;
            Name = name;
            _t = 0;
            _dur = Mathf.Max(0.001f, dur);
            if (dur <= 0.001f) Apply(1);
        }

        /// Returns true while a fade is running.
        public bool Update(float dt)
        {
            if (_t >= 1) return false;
            _t = Mathf.Clamp01(_t + dt / _dur);
            float k = _t * _t * _t * (_t * (_t * 6 - 15) + 10);   // smootherstep
            Apply(k);
            return true;
        }

        void Apply(float k)
        {
            for (int i = 0; i < Live.C.Length; i++)
                Live.C[i] = Color.Lerp(_from.C[i].linear, _to.C[i].linear, k).gamma;
            for (int i = 0; i < Live.F.Length; i++) Live.F[i] = Mathf.Lerp(_from.F[i], _to.F[i], k);
            for (int i = 0; i < Live.D.Length; i++) Live.D[i] = Vector3.Lerp(_from.D[i], _to.D[i], k).normalized;
            Live.Crowd = _to.Crowd;
        }
    }
}
