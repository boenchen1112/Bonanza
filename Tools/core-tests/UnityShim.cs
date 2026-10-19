// Minimal stand-in so the pure-C# tests compile and run outside Unity (see README.md).
using System;
using System.IO;

namespace UnityEngine
{
    public static class Application
    {
        // <repo>/Assets, found by walking up from the test binary until Assets/Game/Core exists.
        public static string dataPath
        {
            get
            {
                var d = new DirectoryInfo(AppContext.BaseDirectory);
                while (d != null && !Directory.Exists(Path.Combine(d.FullName, "Assets", "Game", "Core"))) d = d.Parent;
                if (d == null) throw new DirectoryNotFoundException("run from inside the Bonanza repo");
                return Path.Combine(d.FullName, "Assets");
            }
        }
    }
}
