using System;
using System.Collections.Generic;
using System.IO;
using MelonLoader;

namespace SetsunaAccess
{
    /// <summary>
    /// Thin wrapper over MelonLoader's logger, plus the files the mod keeps under
    /// UserData\SetsunaAccess: speech.log (every spoken line) and unhandled.log.
    /// </summary>
    internal static class Log
    {
        private static MelonLogger.Instance _ml;
        private static readonly HashSet<string> _onceKeys = new HashSet<string>();
        private static readonly object _fileLock = new object();

        public static string Dir { get; private set; }

        public static void Init(MelonLogger.Instance logger)
        {
            _ml = logger;
            Dir = Path.Combine(MelonUtils.UserDataDirectory, "SetsunaAccess");
            Directory.CreateDirectory(Dir);
            // Each session starts fresh; the previous one is kept as .old for comparison.
            foreach (var name in new[] { "speech.log", "unhandled.log" })
            {
                var p = Path.Combine(Dir, name);
                try { if (File.Exists(p)) File.Copy(p, p + ".old", true); File.WriteAllText(p, ""); } catch { }
            }
        }

        public static void Info(string area, string msg) { _ml?.Msg($"[{area}] {msg}"); }
        public static void Warn(string area, string msg) { _ml?.Warning($"[{area}] {msg}"); }
        public static void Error(string area, string msg) { _ml?.Error($"[{area}] {msg}"); }
        public static void Debug(string area, string msg) { _ml?.Msg($"[{area}] {msg}"); }

        /// <summary>Logs an exception once per key, so a failing per-frame hook cannot flood the log.</summary>
        public static void Once(string key, Exception ex)
        {
            lock (_onceKeys)
                if (!_onceKeys.Add(key)) return;
            _ml?.Error($"[{key}] {ex}");
        }

        public static void Append(string file, string line)
        {
            if (Dir == null) return;
            lock (_fileLock)
            {
                try { File.AppendAllText(Path.Combine(Dir, file), $"{DateTime.Now:HH:mm:ss.fff} {line}\n"); }
                catch { }
            }
        }
    }
}
