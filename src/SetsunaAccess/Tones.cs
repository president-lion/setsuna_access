using System;
using System.IO;
using System.Runtime.InteropServices;
using UnityEngine;

namespace SetsunaAccess
{
    /// <summary>
    /// Short generated beeps for cues too quick for speech (the Momentum timing window) and the
    /// walking beacon. The game ships with Unity audio disabled (AudioManager.m_DisableAudio) and
    /// plays everything through CRI, so these go straight to Windows: a WAV built in memory and
    /// played with winmm PlaySound. Pan is baked into the stereo samples.
    /// </summary>
    internal static class Tones
    {
        [DllImport("winmm.dll", SetLastError = true)]
        private static extern bool PlaySound(IntPtr sound, IntPtr hmod, uint flags);

        private const uint SND_ASYNC = 0x1, SND_NODEFAULT = 0x2, SND_MEMORY = 0x4;
        private const int Rate = 44100;

        // PlaySound reads the buffer while it plays, so it must stay allocated until the next call.
        private static IntPtr _playing = IntPtr.Zero;

        public static void Momentum() { Play(1320f, 0.09f, 0f, 0.5f); }

        /// <summary>Low thud: walking into something.</summary>
        public static void Bump() { Play(160f, 0.06f, 0f, 0.5f); }

        /// <summary>Walking beacon: pan -1..1 (left..right), pitch multiplier.</summary>
        public static void Beacon(float pan, float pitch, bool noRoute = false)
        {
            // Without a walkable route the beep drops lower, so a straight-line guess sounds different.
            Play((noRoute ? 440f : 660f) * pitch, 0.07f, pan, 0.35f);
        }

        private static void Play(float hz, float seconds, float pan, float volume)
        {
            try
            {
                var wav = Make(hz, seconds, pan, volume);
                var mem = Marshal.AllocHGlobal(wav.Length);
                Marshal.Copy(wav, 0, mem, wav.Length);
                PlaySound(mem, IntPtr.Zero, SND_MEMORY | SND_ASYNC | SND_NODEFAULT);
                // The new call has stopped the previous sound, so its buffer can go.
                if (_playing != IntPtr.Zero) Marshal.FreeHGlobal(_playing);
                _playing = mem;
            }
            catch (Exception ex) { Log.Once("Tones", ex); }
        }

        private static byte[] Make(float hz, float seconds, float pan, float volume)
        {
            var n = (int)(Rate * seconds);
            // Equal-power pan.
            var angle = (Mathf.Clamp(pan, -1f, 1f) + 1f) * Mathf.PI / 4f;
            float left = Mathf.Cos(angle) * volume, right = Mathf.Sin(angle) * volume;

            var ms = new MemoryStream(44 + n * 4);
            var w = new BinaryWriter(ms);
            w.Write(new[] { (byte)'R', (byte)'I', (byte)'F', (byte)'F' });
            w.Write(36 + n * 4);
            w.Write(new[] { (byte)'W', (byte)'A', (byte)'V', (byte)'E', (byte)'f', (byte)'m', (byte)'t', (byte)' ' });
            w.Write(16); w.Write((short)1); w.Write((short)2); w.Write(Rate); w.Write(Rate * 4); w.Write((short)4); w.Write((short)16);
            w.Write(new[] { (byte)'d', (byte)'a', (byte)'t', (byte)'a' });
            w.Write(n * 4);
            var fade = Rate * 0.005f; // 5 ms in and out, no clicks
            for (var i = 0; i < n; i++)
            {
                var env = Mathf.Min(1f, Mathf.Min(i, n - i) / fade);
                var s = Mathf.Sin(2f * Mathf.PI * hz * i / Rate) * env;
                w.Write((short)(s * left * short.MaxValue));
                w.Write((short)(s * right * short.MaxValue));
            }
            w.Flush();
            return ms.ToArray();
        }
    }
}
