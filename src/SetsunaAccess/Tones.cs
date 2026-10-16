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
    /// played with winmm PlaySound. Pan is baked into the stereo samples. The beacon, Momentum and
    /// wall bump cues use the user's recordings in mod/sounds when they load.
    /// </summary>
    internal static class Tones
    {
        [DllImport("winmm.dll", SetLastError = true)]
        private static extern bool PlaySound(IntPtr sound, IntPtr hmod, uint flags);

        private const uint SND_ASYNC = 0x1, SND_NODEFAULT = 0x2, SND_MEMORY = 0x4;
        private const int Rate = 44100;

        // PlaySound reads the buffer while it plays, so it must stay allocated until the next call.
        private static IntPtr _playing = IntPtr.Zero;

        // The user's recorded cues (mod/sounds, embedded in the DLL), as mono samples at 44.1 kHz.
        // Null when missing or unreadable; the generated beep is used instead.
        private static float[] _beaconWav, _momentumWav, _wallWav;
        private static bool _loaded;

        public static void Momentum()
        {
            Load();
            if (_momentumWav != null) PlaySamples(_momentumWav, 1f, 0f, 1f);
            else Play(1320f, 0.09f, 0f, 0.5f);
        }

        /// <summary>Low thud: walking into something.</summary>
        public static void Bump()
        {
            Load();
            if (_wallWav != null) PlaySamples(_wallWav, 1f, 0f, 1f);
            else Play(160f, 0.06f, 0f, 0.5f);
        }

        /// <summary>Walking beacon: pan -1..1 (left..right), pitch multiplier.</summary>
        public static void Beacon(float pan, float pitch, bool noRoute = false)
        {
            // Without a walkable route the beep drops lower, so a straight-line guess sounds different.
            Load();
            if (_beaconWav != null) PlaySamples(_beaconWav, (noRoute ? 0.67f : 1f) * pitch, pan, 0.8f);
            else Play((noRoute ? 440f : 660f) * pitch, 0.07f, pan, 0.35f);
        }

        private static void Load()
        {
            if (_loaded) return;
            _loaded = true;
            _beaconWav = ReadWav("sounds.beacon.wav");
            _momentumWav = ReadWav("sounds.momentum.wav");
            _wallWav = ReadWav("sounds.wall.wav");
        }

        /// <summary>16-bit PCM WAV resource, mixed down to mono and resampled to 44.1 kHz.</summary>
        private static float[] ReadWav(string resource)
        {
            try
            {
                using (var st = typeof(Tones).Assembly.GetManifestResourceStream(resource))
                {
                    if (st == null) return null;
                    var r = new BinaryReader(st);
                    if (new string(r.ReadChars(4)) != "RIFF") return null;
                    r.ReadInt32();
                    if (new string(r.ReadChars(4)) != "WAVE") return null;
                    int channels = 0, rate = 0, bits = 0;
                    while (st.Position + 8 <= st.Length)
                    {
                        var id = new string(r.ReadChars(4));
                        var size = r.ReadInt32();
                        if (id == "fmt ")
                        {
                            var fmt = r.ReadBytes(size);
                            channels = BitConverter.ToInt16(fmt, 2);
                            rate = BitConverter.ToInt32(fmt, 4);
                            bits = BitConverter.ToInt16(fmt, 14);
                        }
                        else if (id == "data")
                        {
                            if (bits != 16 || channels < 1 || rate <= 0) return null;
                            var data = r.ReadBytes(size);
                            var frames = data.Length / (2 * channels);
                            var mono = new float[frames];
                            for (var i = 0; i < frames; i++)
                            {
                                float sum = 0f;
                                for (var c = 0; c < channels; c++) sum += BitConverter.ToInt16(data, (i * channels + c) * 2);
                                mono[i] = sum / channels / short.MaxValue;
                            }
                            return rate == Rate ? mono : Resample(mono, (float)rate / Rate);
                        }
                        else r.ReadBytes(size + (size & 1));
                    }
                }
            }
            catch (Exception ex) { Log.Once("Tones.ReadWav", ex); }
            return null;
        }

        /// <summary>Reads the source at <paramref name="step"/> samples per output sample (above 1 = higher pitch).</summary>
        private static float[] Resample(float[] src, float step)
        {
            var n = (int)(src.Length / step);
            var dst = new float[n];
            for (var i = 0; i < n; i++)
            {
                var pos = i * step;
                var a = (int)pos;
                var b = Math.Min(a + 1, src.Length - 1);
                dst[i] = Mathf.Lerp(src[a], src[b], pos - a);
            }
            return dst;
        }

        private static void PlaySamples(float[] mono, float pitch, float pan, float volume)
        {
            var samples = Mathf.Approximately(pitch, 1f) ? mono : Resample(mono, pitch);
            var angle = (Mathf.Clamp(pan, -1f, 1f) + 1f) * Mathf.PI / 4f;
            float left = Mathf.Cos(angle) * volume, right = Mathf.Sin(angle) * volume;
            var w = Header(samples.Length, out var ms);
            foreach (var s in samples)
            {
                w.Write((short)(Mathf.Clamp(s * left, -1f, 1f) * short.MaxValue));
                w.Write((short)(Mathf.Clamp(s * right, -1f, 1f) * short.MaxValue));
            }
            w.Flush();
            Send(ms.ToArray());
        }

        private static void Play(float hz, float seconds, float pan, float volume)
        {
            Send(Make(hz, seconds, pan, volume));
        }

        private static void Send(byte[] wav)
        {
            try
            {
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

            var w = Header(n, out var ms);
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

        /// <summary>A 44.1 kHz 16-bit stereo WAV header for <paramref name="n"/> frames; the samples follow.</summary>
        private static BinaryWriter Header(int n, out MemoryStream ms)
        {
            ms = new MemoryStream(44 + n * 4);
            var w = new BinaryWriter(ms);
            w.Write(new[] { (byte)'R', (byte)'I', (byte)'F', (byte)'F' });
            w.Write(36 + n * 4);
            w.Write(new[] { (byte)'W', (byte)'A', (byte)'V', (byte)'E', (byte)'f', (byte)'m', (byte)'t', (byte)' ' });
            w.Write(16); w.Write((short)1); w.Write((short)2); w.Write(Rate); w.Write(Rate * 4); w.Write((short)4); w.Write((short)16);
            w.Write(new[] { (byte)'d', (byte)'a', (byte)'t', (byte)'a' });
            w.Write(n * 4);
            return w;
        }
    }
}
