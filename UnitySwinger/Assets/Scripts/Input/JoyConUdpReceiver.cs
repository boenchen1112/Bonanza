using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Swinger.Input
{
    // Build Plan v3 Phase 1, input fallback option 2: receives the gyro
    // stream from src/joycon_udp_bridge.py instead of talking to the Joy-Con
    // HID device directly. This decouples input bring-up from evaluating
    // stale community Joy-Con libraries.
    //
    // Reads on a dedicated background thread, not Update() -- Update() at
    // 60fps under-samples the device's ~66Hz native rate (the same class of
    // bug as F3 in the Python bug audit). Each sample is timestamped on
    // arrival using the Stopwatch clock, which is this project's canonical
    // clock domain per Phase 3's "one clock domain" rule -- the `t` field in
    // the JSON packet is the Python process's own perf_counter clock and is
    // NOT comparable to this clock; it is logged but not used for judging.
    [Serializable]
    internal struct WirePacket
    {
        public double t;
        public float gx;
        public float gy;
        public float gz;
    }

    public readonly struct GyroSample
    {
        public readonly double timestamp; // Stopwatch seconds, canonical clock
        public readonly double gx, gy, gz;
        public readonly double magnitude;

        public GyroSample(double timestamp, double gx, double gy, double gz)
        {
            this.timestamp = timestamp;
            this.gx = gx;
            this.gy = gy;
            this.gz = gz;
            magnitude = Math.Sqrt(gx * gx + gy * gy + gz * gz);
        }
    }

    public class JoyConUdpReceiver : MonoBehaviour
    {
        [SerializeField] private int port = 9999;
        [SerializeField] private bool logSpikes = true;
        [SerializeField] private double spikeLogThreshold = 15000.0; // matches Python idle~8-27 vs swing~20k-30k

        // J2 Unity-side equivalent (Bug_Audit_2026-07-28.md /
        // Swinger_Build_Plan_v4.md Phase A.3): without a socket receive
        // timeout, _client.Receive() blocks forever if the Python bridge
        // dies or the UDP stream goes quiet -- the receive thread hangs
        // with no on-screen indication, and any caller-side wait loop
        // (e.g. RunCalibrationFlow's practice-measure loop) would simply
        // stall waiting for samples that never arrive. ReceiveTimeoutMs
        // bounds how long a single Receive() call blocks so the loop can
        // keep checking `_running` and the caller can observe staleness via
        // TimeSinceLastSampleS / IsSignalLost.
        private const int ReceiveTimeoutMs = 250;
        // How long without a sample before the input is considered lost --
        // several multiples of the device's native ~66Hz inter-sample gap
        // (~15ms) so ordinary jitter never false-positives.
        public const double SignalLostThresholdS = 1.0;

        private readonly ConcurrentQueue<GyroSample> _queue = new ConcurrentQueue<GyroSample>();
        private UdpClient _client;
        private Thread _thread;
        private volatile bool _running;
        private Stopwatch _clock;
        // Ticks (not a bare double) + Interlocked, so cross-thread reads
        // from Update() can't tear: `double` isn't a valid `volatile` type
        // in C#, and this field is written on the receive thread but read
        // on the main thread every frame via IsSignalLost.
        private long _lastSampleAtTicks = long.MinValue;

        // Duplicate-read skip mirrors joycon_stream.py's stream() (F3/A5) --
        // guards against a future direct-HID input path reusing this same
        // queue and re-polling a cached reading faster than the device
        // actually updates.
        private double? _lastGx, _lastGy, _lastGz;

        public int PendingCount => _queue.Count;

        public bool TryDequeue(out GyroSample sample) => _queue.TryDequeue(out sample);

        // Seconds since the last genuinely received UDP packet (not the
        // canonical clock's "now" minus a queued sample's timestamp --
        // this reflects socket activity even while the queue is being
        // drained). double.PositiveInfinity before the first packet ever
        // arrives.
        public double TimeSinceLastSampleS
        {
            get
            {
                long last = System.Threading.Interlocked.Read(ref _lastSampleAtTicks);
                if (last == long.MinValue) return double.PositiveInfinity;
                return (_clock.ElapsedTicks - last) / (double)Stopwatch.Frequency;
            }
        }

        public bool IsSignalLost => TimeSinceLastSampleS >= SignalLostThresholdS;

        // Build Plan v3 Phase 3's "one clock domain" rule: this component
        // previously always started its own private Stopwatch, which would
        // NOT be the same clock (or the same zero-point) as the one driving
        // the beat schedule/judge -- call this before the object's OnEnable
        // (e.g. from another component's Awake, relying on Unity calling all
        // Awakes before any OnEnable for objects already active at scene
        // load) to make gyro-sample timestamps and beat-schedule timestamps
        // directly comparable. If never called, falls back to a private
        // Stopwatch (preserves the Phase 1 input-only scene's behavior).
        public void UseClock(Stopwatch clock)
        {
            _clock = clock;
        }

        private void OnEnable()
        {
            if (_clock == null)
            {
                _clock = Stopwatch.StartNew();
            }
            _client = new UdpClient(port);
            _client.Client.ReceiveTimeout = ReceiveTimeoutMs;
            _running = true;
            _thread = new Thread(ReceiveLoop) { IsBackground = true, Name = "JoyConUdpReceiver" };
            _thread.Start();
            Debug.Log($"[JoyConUdpReceiver] listening on UDP :{port}");
        }

        private void OnDisable()
        {
            _running = false;
            _client?.Close();
            _thread?.Join(500);
        }

        private void ReceiveLoop()
        {
            var remote = new IPEndPoint(IPAddress.Any, port);
            while (_running)
            {
                byte[] data;
                try
                {
                    data = _client.Receive(ref remote);
                }
                catch (SocketException se) when (se.SocketErrorCode == SocketError.TimedOut)
                {
                    // ReceiveTimeoutMs elapsed with nothing arriving -- not
                    // an error, just the periodic check-in that lets this
                    // loop re-test `_running` instead of blocking forever
                    // (J2 Unity-side equivalent). TimeSinceLastSampleS/
                    // IsSignalLost reflect the gap to callers; loop back.
                    continue;
                }
                catch (SocketException)
                {
                    break; // socket closed on OnDisable
                }

                double now = _clock.Elapsed.TotalSeconds;
                System.Threading.Interlocked.Exchange(ref _lastSampleAtTicks, _clock.ElapsedTicks);
                WirePacket packet;
                string json = Encoding.UTF8.GetString(data);
                try
                {
                    packet = JsonUtility.FromJson<WirePacket>(json);
                }
                catch (Exception e)
                {
                    string hex = BitConverter.ToString(data);
                    Debug.LogWarning($"[JoyConUdpReceiver] malformed packet: {e.Message} len={data.Length} from={remote} hex={hex}");
                    continue;
                }

                if (_lastGx == packet.gx && _lastGy == packet.gy && _lastGz == packet.gz)
                    continue;
                _lastGx = packet.gx;
                _lastGy = packet.gy;
                _lastGz = packet.gz;

                var sample = new GyroSample(now, packet.gx, packet.gy, packet.gz);
                _queue.Enqueue(sample);

                if (logSpikes && sample.magnitude >= spikeLogThreshold)
                {
                    Debug.Log($"[JoyConUdpReceiver] spike mag={sample.magnitude:F1} t={sample.timestamp:F3}");
                }
            }
        }
    }
}
