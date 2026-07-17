using System.Globalization;
using System.IO;
using System.Text;
using Swinger.Input;
using UnityEngine;

namespace Swinger.Presentation
{
    // Build Plan v3 Phase 4 prep: logs real Joy-Con samples through the
    // Unity input path to a CSV, same (timestamp, magnitude) shape as the
    // Python captures (src/swings_fresh.csv, src/swings_counted.csv), so
    // Phase 4's real-hardware validation has data to replay-test against.
    // Attach next to a JoyConUdpReceiver; enabled while capturing.
    public sealed class CaptureLogger : MonoBehaviour
    {
        [SerializeField] private JoyConUdpReceiver inputSource;
        [SerializeField] private string fileName = "unity_capture.csv";

        private StreamWriter _writer;
        private int _sampleCount;

        private void OnEnable()
        {
            string path = Path.Combine(Application.persistentDataPath, fileName);
            _writer = new StreamWriter(path, append: false) { AutoFlush = false };
            _writer.WriteLine("timestamp,gx,gy,gz,magnitude");
            _sampleCount = 0;
            Debug.Log($"[CaptureLogger] writing to {path}");
        }

        private void Update()
        {
            if (inputSource == null) return;
            while (inputSource.TryDequeue(out var sample))
            {
                _writer.WriteLine(string.Join(",",
                    sample.timestamp.ToString("R", CultureInfo.InvariantCulture),
                    sample.gx.ToString("R", CultureInfo.InvariantCulture),
                    sample.gy.ToString("R", CultureInfo.InvariantCulture),
                    sample.gz.ToString("R", CultureInfo.InvariantCulture),
                    sample.magnitude.ToString("R", CultureInfo.InvariantCulture)));
                _sampleCount++;
            }
        }

        private void OnDisable()
        {
            _writer?.Flush();
            _writer?.Dispose();
            _writer = null;
            Debug.Log($"[CaptureLogger] wrote {_sampleCount} samples");
        }
    }
}
