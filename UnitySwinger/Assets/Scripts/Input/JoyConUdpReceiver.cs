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

        private readonly ConcurrentQueue<GyroSample> _queue = new ConcurrentQueue<GyroSample>();
        private UdpClient _client;
        private Thread _thread;
        private volatile bool _running;
        private Stopwatch _clock;

        // Duplicate-read skip mirrors joycon_stream.py's stream() (F3/A5) --
        // guards against a future direct-HID input path reusing this same
        // queue and re-polling a cached reading faster than the device
        // actually updates.
        private double? _lastGx, _lastGy, _lastGz;

        public int PendingCount => _queue.Count;

        public bool TryDequeue(out GyroSample sample) => _queue.TryDequeue(out sample);

        private void Awake()
        {
            _clock = Stopwatch.StartNew();
        }

        private void OnEnable()
        {
            _client = new UdpClient(port);
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
                catch (SocketException)
                {
                    break; // socket closed on OnDisable
                }

                double now = _clock.Elapsed.TotalSeconds;
                WirePacket packet;
                try
                {
                    string json = Encoding.UTF8.GetString(data);
                    packet = JsonUtility.FromJson<WirePacket>(json);
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[JoyConUdpReceiver] malformed packet: {e.Message}");
                    continue;
                }

                if (_lastGx == packet.gx && _lastGy == packet.gy && _lastGz == packet.gz)
                    continue;
                _lastGx = packet.gx;
                _lastGy = packet.gy;
                _lastGz = packet.gz;

                _queue.Enqueue(new GyroSample(now, packet.gx, packet.gy, packet.gz));
            }
        }

        private void Update()
        {
            // Drain on the main thread; logging only, no game logic yet --
            // this satisfies Phase 1's exit criterion (visible magnitude
            // spikes on a deliberate swing) before any detector porting.
            while (_queue.TryDequeue(out var sample))
            {
                if (logSpikes && sample.magnitude >= spikeLogThreshold)
                {
                    Debug.Log($"[JoyConUdpReceiver] spike mag={sample.magnitude:F1} t={sample.timestamp:F3}");
                }
            }
        }
    }
}
