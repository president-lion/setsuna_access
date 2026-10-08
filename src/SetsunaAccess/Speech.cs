using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace SetsunaAccess
{
    /// <summary>
    /// Speech output via Prism (https://github.com/ethindp/prism), which routes to whatever
    /// screen reader is running - NVDA, JAWS, OneCore, SAPI. x86 build, out of UserLibs.
    /// Every spoken line is also written to speech.log, so behaviour can be checked without audio.
    /// </summary>
    internal static class Speech
    {
        private const string Lib = "prism";
        private const CallingConvention C = CallingConvention.Cdecl;

        // Must match PrismConfig in prism.h exactly: it is returned by value from
        // prism_config_init, so a size mismatch corrupts the stack.
        [StructLayout(LayoutKind.Sequential)]
        private struct PrismConfig
        {
            public byte Version;
            public IntPtr Registry;
            public IntPtr AvailabilityCallback;
            public IntPtr AvailabilityUserdata;
            public uint PollIntervalMs;
            public uint DebounceSamples;
            public uint BackoffMaxMs;
            [MarshalAs(UnmanagedType.U1)] public bool AutoPowerManage;
            public IntPtr AvailabilityBaselineCallback;
        }

        [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr LoadLibraryW(string path);

        [DllImport(Lib, CallingConvention = C)] private static extern PrismConfig prism_config_init();
        [DllImport(Lib, CallingConvention = C)] private static extern IntPtr prism_init(ref PrismConfig cfg);
        [DllImport(Lib, CallingConvention = C)] private static extern void prism_shutdown(IntPtr ctx);
        [DllImport(Lib, CallingConvention = C)] private static extern UIntPtr prism_registry_count(IntPtr ctx);
        [DllImport(Lib, CallingConvention = C)] private static extern ulong prism_registry_id_at(IntPtr ctx, UIntPtr index);
        [DllImport(Lib, CallingConvention = C)] private static extern IntPtr prism_registry_name(IntPtr ctx, ulong id);
        [DllImport(Lib, CallingConvention = C)] private static extern IntPtr prism_registry_acquire(IntPtr ctx, ulong id);
        [DllImport(Lib, CallingConvention = C)] private static extern IntPtr prism_registry_acquire_best(IntPtr ctx);
        [DllImport(Lib, CallingConvention = C)] private static extern IntPtr prism_backend_name(IntPtr backend);
        [DllImport(Lib, CallingConvention = C)] private static extern int prism_backend_initialize(IntPtr backend);
        [DllImport(Lib, CallingConvention = C)] private static extern int prism_backend_speak(IntPtr backend, byte[] text, [MarshalAs(UnmanagedType.U1)] bool interrupt);
        [DllImport(Lib, CallingConvention = C)] private static extern int prism_backend_output(IntPtr backend, byte[] text, [MarshalAs(UnmanagedType.U1)] bool interrupt);
        [DllImport(Lib, CallingConvention = C)] private static extern int prism_backend_stop(IntPtr backend);
        [DllImport(Lib, CallingConvention = C)] private static extern ulong prism_backend_get_features(IntPtr backend);

        private const int PrismOk = 0;
        private const int PrismAlreadyInitialized = 15;
        private const ulong FeatureOutput = 1UL << 2;
        private const ulong FeatureStop = 1UL << 3;

        private static IntPtr _ctx, _backend;
        private static bool _useOutput, _canStop;
        private static readonly RepeatFilter _repeats = new RepeatFilter(TimeSpan.FromMilliseconds(400));

        public static string BackendName { get; private set; } = "<none>";
        public static bool Ready { get { return _backend != IntPtr.Zero; } }

        /// <summary>The last line spoken, for the repeat key.</summary>
        public static string Last { get; private set; } = "";

        /// <summary>
        /// Loads prism.dll by full path, so the later DllImport("prism") binds to the module
        /// already in the process instead of searching the DLL path.
        /// </summary>
        public static void Preload(string userLibsDir)
        {
            var dll = Path.Combine(userLibsDir, "prism.dll");
            if (!File.Exists(dll)) { Log.Warn("Speech", "missing " + dll); return; }
            if (LoadLibraryW(dll) == IntPtr.Zero)
                Log.Error("Speech", "LoadLibrary failed, error " + Marshal.GetLastWin32Error());
        }

        public static bool Init(string requestedBackend)
        {
            try
            {
                var cfg = prism_config_init();
                _ctx = prism_init(ref cfg);
                if (_ctx == IntPtr.Zero) { Log.Error("Speech", "prism_init failed"); return false; }

                ulong wanted = 0;
                var count = (int)prism_registry_count(_ctx).ToUInt32();
                for (var i = 0; i < count; i++)
                {
                    var id = prism_registry_id_at(_ctx, new UIntPtr((uint)i));
                    var nm = Utf8(prism_registry_name(_ctx, id));
                    Log.Debug("Speech", "backend [" + i + "] " + nm);
                    if (!string.Equals(requestedBackend, "auto", StringComparison.OrdinalIgnoreCase)
                        && string.Equals(nm, requestedBackend, StringComparison.OrdinalIgnoreCase))
                        wanted = id;
                }

                _backend = wanted != 0 ? prism_registry_acquire(_ctx, wanted) : prism_registry_acquire_best(_ctx);
                if (_backend == IntPtr.Zero)
                {
                    Log.Warn("Speech", "no usable speech backend (is a screen reader running?)");
                    return false;
                }

                var rc = prism_backend_initialize(_backend);
                if (rc != PrismOk && rc != PrismAlreadyInitialized)
                    Log.Warn("Speech", "backend_initialize returned " + rc + " (continuing)");

                var features = prism_backend_get_features(_backend);
                _useOutput = (features & FeatureOutput) != 0;
                _canStop = (features & FeatureStop) != 0;
                BackendName = Utf8(prism_backend_name(_backend));
                Log.Info("Speech", "backend: " + BackendName);
                return true;
            }
            catch (Exception ex)
            {
                Log.Error("Speech", "Prism unavailable, running silently: " + ex);
                _backend = IntPtr.Zero;
                return false;
            }
        }

        public static void Say(string text, bool interrupt = true)
        {
            if (text == null || text.Trim().Length == 0) return;
            if (!_repeats.Allow(text, DateTime.UtcNow)) return;

            Last = text;
            Log.Append("speech.log", (interrupt ? "! " : "+ ") + text);
            if (_backend == IntPtr.Zero) return;
            try
            {
                var buf = Encoding.UTF8.GetBytes(text + "\0");
                var rc = _useOutput ? prism_backend_output(_backend, buf, interrupt)
                                    : prism_backend_speak(_backend, buf, interrupt);
                if (rc != PrismOk) Log.Debug("Speech", "speak rc=" + rc);
            }
            catch (Exception ex) { Log.Once("Speech.Say", ex); }
        }

        /// <summary>Speak even if it repeats the last line (typed letters: the l's in "hello").</summary>
        public static void SayAlways(string text, bool interrupt = true)
        {
            _repeats.Reset();
            Say(text, interrupt);
        }

        /// <summary>Speaks the last line again, bypassing the repeat filter.</summary>
        public static void Repeat()
        {
            _repeats.Reset();
            Say(Last);
        }

        public static void Stop()
        {
            if (_backend == IntPtr.Zero || !_canStop) return;
            try { prism_backend_stop(_backend); } catch { }
        }

        public static void Shutdown()
        {
            // acquire_* hands back a registry-owned backend, so only the context is released.
            try { if (_ctx != IntPtr.Zero) prism_shutdown(_ctx); } catch { }
            _ctx = _backend = IntPtr.Zero;
        }

        private static string Utf8(IntPtr p)
        {
            if (p == IntPtr.Zero) return "";
            var len = 0;
            while (Marshal.ReadByte(p, len) != 0) len++;
            var bytes = new byte[len];
            Marshal.Copy(p, bytes, 0, len);
            return Encoding.UTF8.GetString(bytes);
        }
    }
}
