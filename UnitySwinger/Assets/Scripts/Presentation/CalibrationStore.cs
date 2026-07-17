using System.Collections.Generic;
using System.IO;
using Swinger.Logic;
using UnityEngine;

namespace Swinger.Presentation
{
    // File-I/O half of calibration.py's persistence, deliberately kept out
    // of the UnityEngine-free Logic assembly (Build Plan v3 Phase 2/3 split).
    // Persists to Application.persistentDataPath/calibration.json, same
    // shape as the Python file (offset_s, sharpness_seed) -- but NEVER seeded
    // from the Python file itself: it measures a different stack's latency.
    // A fresh Unity install starts from Calibration.DefaultOffsetS until the
    // player calibrates through Settings.
    public static class CalibrationStore
    {
        private static string PathOnDisk => Path.Combine(Application.persistentDataPath, "calibration.json");

        [System.Serializable]
        private class Payload
        {
            public double offset_s;
            public List<double> sharpness_seed = new List<double>();
        }

        public static (double offsetS, List<double> sharpnessSeed, bool wasLoaded) Load()
        {
            string path = PathOnDisk;
            if (File.Exists(path))
            {
                try
                {
                    string json = File.ReadAllText(path);
                    var payload = JsonUtility.FromJson<Payload>(json);
                    if (payload != null)
                    {
                        return (payload.offset_s, payload.sharpness_seed ?? new List<double>(), true);
                    }
                }
                catch (System.Exception e)
                {
                    Debug.LogWarning($"[CalibrationStore] failed to read {path}: {e.Message}");
                }
            }
            return (Calibration.DefaultOffsetS, new List<double>(), false);
        }

        public static void Save(double offsetS, List<double> sharpnessSeed)
        {
            var payload = new Payload { offset_s = offsetS, sharpness_seed = sharpnessSeed ?? new List<double>() };
            string json = JsonUtility.ToJson(payload, prettyPrint: true);
            File.WriteAllText(PathOnDisk, json);
        }
    }
}
