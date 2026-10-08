using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace SetsunaAccess
{
    /// <summary>
    /// Where the game's CPU goes, for perf.log. The frame cap leaves the main thread asleep ~90% of each frame,
    /// yet Task Manager still showed SETSUNA.exe near 90%, so some other thread is busy. Every 10 s this reads
    /// the process's CPU time and each thread's (Toolhelp snapshot + GetThreadTimes) and names the busiest
    /// threads by the module their start address is in (graphics driver, CRI audio, Mono, Unity...).
    /// </summary>
    internal static class CpuProbe
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct THREADENTRY32
        {
            public uint dwSize, cntUsage, th32ThreadID, th32OwnerProcessID;
            public int tpBasePri, tpDeltaPri;
            public uint dwFlags;
        }

        [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint pid);
        [DllImport("kernel32.dll")] private static extern bool Thread32First(IntPtr snap, ref THREADENTRY32 te);
        [DllImport("kernel32.dll")] private static extern bool Thread32Next(IntPtr snap, ref THREADENTRY32 te);
        [DllImport("kernel32.dll")] private static extern IntPtr OpenThread(uint access, bool inherit, uint id);
        [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr h);
        [DllImport("kernel32.dll")] private static extern bool GetThreadTimes(IntPtr h, out long create, out long exit, out long kernel, out long user);
        [DllImport("kernel32.dll")] private static extern bool GetProcessTimes(IntPtr h, out long create, out long exit, out long kernel, out long user);
        [DllImport("kernel32.dll")] private static extern IntPtr GetCurrentProcess();
        [DllImport("kernel32.dll")] private static extern uint GetCurrentProcessId();
        [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
        [DllImport("kernel32.dll")] private static extern bool GetModuleHandleEx(uint flags, IntPtr addr, out IntPtr module);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern uint GetModuleFileName(IntPtr module, StringBuilder name, int size);
        [DllImport("ntdll.dll")] private static extern int NtQueryInformationThread(IntPtr h, int cls, out IntPtr info, int size, IntPtr ret);

        private const uint SnapThread = 4, QueryInfo = 0x40, QueryLimited = 0x800;

        private static uint _mainThread;
        private static long _lastProcess, _lastWall;
        private static readonly Dictionary<uint, long> _lastThread = new Dictionary<uint, long>();
        private static readonly Dictionary<uint, string> _module = new Dictionary<uint, string>();

        /// <summary>Call once from the main thread so it can be told apart.</summary>
        public static void MarkMain() { if (_mainThread == 0) _mainThread = GetCurrentThreadId(); }

        /// <summary>A perf.log line: process CPU and the busiest threads since the last call.</summary>
        public static string Sample()
        {
            try
            {
                var wall = DateTime.UtcNow.Ticks;
                long c, e, k, u;
                GetProcessTimes(GetCurrentProcess(), out c, out e, out k, out u);
                var proc = k + u;
                var cores = Math.Max(1, Environment.ProcessorCount);
                var threads = new List<KeyValuePair<uint, long>>();
                var snap = CreateToolhelp32Snapshot(SnapThread, 0);
                var pid = GetCurrentProcessId();
                if (snap != IntPtr.Zero && snap != new IntPtr(-1))
                {
                    var te = new THREADENTRY32 { dwSize = (uint)Marshal.SizeOf(typeof(THREADENTRY32)) };
                    for (var ok = Thread32First(snap, ref te); ok; ok = Thread32Next(snap, ref te))
                    {
                        if (te.th32OwnerProcessID != pid) continue;
                        var h = OpenThread(QueryInfo | QueryLimited, false, te.th32ThreadID);
                        if (h == IntPtr.Zero) continue;
                        if (GetThreadTimes(h, out c, out e, out k, out u))
                        {
                            long last;
                            var used = k + u;
                            var delta = _lastThread.TryGetValue(te.th32ThreadID, out last) ? used - last : 0;
                            _lastThread[te.th32ThreadID] = used;
                            if (delta > 0) threads.Add(new KeyValuePair<uint, long>(te.th32ThreadID, delta));
                            if (!_module.ContainsKey(te.th32ThreadID)) _module[te.th32ThreadID] = StartModule(h);
                        }
                        CloseHandle(h);
                    }
                    CloseHandle(snap);
                }
                var span = wall - _lastWall;
                var first = _lastWall == 0;
                var procDelta = proc - _lastProcess;
                _lastWall = wall;
                _lastProcess = proc;
                if (first || span <= 0) return null;

                threads.Sort((a, b) => b.Value.CompareTo(a.Value));
                var sb = new StringBuilder();
                sb.Append("cpu ").Append(Pct(procDelta, span * cores)).Append("% of the machine (")
                  .Append(Pct(procDelta, span)).Append("% of one core); busiest threads:");
                for (var i = 0; i < threads.Count && i < 5; i++)
                {
                    var id = threads[i].Key;
                    sb.Append(i == 0 ? " " : ", ").Append(id == _mainThread ? "main" : _module[id] + " #" + id)
                      .Append(' ').Append(Pct(threads[i].Value, span)).Append('%');
                }
                sb.Append("; ").Append(threads.Count).Append(" threads used CPU");
                return sb.ToString();
            }
            catch (Exception ex) { Log.Once("CpuProbe", ex); return null; }
        }

        private static string Pct(long part, long whole) { return (100.0 * part / whole).ToString("0"); }

        /// <summary>The DLL the thread started in (its Win32 start address), or "?".</summary>
        private static string StartModule(IntPtr thread)
        {
            IntPtr start, module;
            if (NtQueryInformationThread(thread, 9, out start, IntPtr.Size, IntPtr.Zero) != 0 || start == IntPtr.Zero) return "?";
            if (!GetModuleHandleEx(4 | 2, start, out module)) return "?"; // FROM_ADDRESS | UNCHANGED_REFCOUNT
            var name = new StringBuilder(260);
            GetModuleFileName(module, name, name.Capacity);
            var s = name.ToString();
            var slash = s.LastIndexOfAny(new[] { '\\', '/' });
            return slash >= 0 ? s.Substring(slash + 1) : s;
        }
    }
}
