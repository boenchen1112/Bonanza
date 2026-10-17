// The conducting trace: the ribbon the bat tip leaves behind for the WHOLE windup (web swingKings/trace.js,
// data half; the mesh lives in Runtime). It keeps the entire path from the moment of the press, thinning the
// samples when the buffer fills, so the ribbon grows from a stub into a long curve as the hold gets longer.
// Colour and width track POWER, not time: cold and thin is a bunt, hot and wide is a home run.

using System;

namespace Bonanza.Core
{
    public sealed class ConductTrace
    {
        public const int MaxSamples = 96;
        public const double BaseRate = 1.0 / 45;     // seconds between samples before decimation
        public const double BaseWidth = 0.07;
        public const double FadePerSecond = 2.6;     // the ribbon dissolves after release

        public readonly float[] X = new float[MaxSamples], Y = new float[MaxSamples], Z = new float[MaxSamples];
        int _n;
        double _acc, _rate = BaseRate;

        public int Count => _n;
        public bool Live { get; private set; }
        /// 1 while alive, drains after release.
        public double Fade { get; private set; }
        public double Power { get; private set; }
        public double Width = BaseWidth;
        public bool Visible => Fade > 0 && _n >= 2 || Live;

        // Cold -> warm -> white-hot, matching the tier thresholds (web RAMP).
        static readonly double[] RampT = { 0.00, 0.28, 0.55, 0.72, 1.00 };
        static readonly uint[] RampC = { 0x2fb8ff, 0x5ce1ff, 0xffd93d, 0xff9a3a, 0xfff3cf };

        /// The ribbon colour at power t (0..1) as r,g,b in 0..1.
        public static void RampColor(double t, out float r, out float g, out float b)
        {
            t = Math.Max(0, Math.Min(1, t));
            for (int i = 1; i < RampT.Length; i++)
            {
                if (t <= RampT[i] || i == RampT.Length - 1)
                {
                    double u = RampT[i] == RampT[i - 1] ? 0 : (t - RampT[i - 1]) / (RampT[i] - RampT[i - 1]);
                    u = Math.Max(0, Math.Min(1, u));
                    uint a = RampC[i - 1], c = RampC[i];
                    r = Lerp((int)((a >> 16) & 255), (int)((c >> 16) & 255), u);
                    g = Lerp((int)((a >> 8) & 255), (int)((c >> 8) & 255), u);
                    b = Lerp((int)(a & 255), (int)(c & 255), u);
                    return;
                }
            }
            r = g = b = 1;
        }

        static float Lerp(int a, int b, double u) => (float)(a + (b - a) * u) / 255f;

        /// Half-width of the ribbon at power/fade/sample position u (0 tail .. 1 head): the tail is thin, the head full.
        public double HalfWidth(double u) => Width * (0.55 + Power * 1.25) * (0.55 + Fade * 0.45) * (0.20 + 0.80 * u * u);

        void Decimate()
        {
            int w = 0;
            for (int i = 0; i < _n; i += 2) { X[w] = X[i]; Y[w] = Y[i]; Z[w] = Z[i]; w++; }
            _n = w;
            _rate *= 2;
        }

        public void Begin(float x, float y, float z)
        {
            _n = 0; _acc = 0; _rate = BaseRate;
            Live = true; Fade = 1; Power = 0;
            X[_n] = x; Y[_n] = y; Z[_n] = z; _n++;
        }

        /// Feed the head position; `power` is the current power 0..1.
        public void Push(double dt, float x, float y, float z, double power)
        {
            if (!Live) return;
            Power = power;
            _acc += dt;
            if (_acc >= _rate)
            {
                _acc = 0;
                if (_n >= MaxSamples) Decimate();
                X[_n] = x; Y[_n] = y; Z[_n] = z; _n++;
            }
            else if (_n > 0)
            {
                // keep the head glued to the object between samples
                X[_n - 1] = x; Y[_n - 1] = y; Z[_n - 1] = z;
            }
        }

        /// Stop feeding; the ribbon holds its shape and dissolves.
        public void Release() { Live = false; }

        /// Colour the ribbon for the power it was released at (the last pushed one).
        public void SetPower(double p) { Power = p; }

        public void Clear() { Live = false; Fade = 0; _n = 0; }

        public void Update(double dt)
        {
            if (!Live && Fade > 0) Fade = Math.Max(0, Fade - dt * FadePerSecond);
        }
    }
}
