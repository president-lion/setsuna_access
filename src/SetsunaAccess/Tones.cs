using UnityEngine;

namespace SetsunaAccess
{
    /// <summary>
    /// Short generated beeps for cues too quick for speech (the Momentum timing window).
    /// The game plays its sound through CRI, so Unity audio may have no listener: one is added
    /// to the mod's own object when the scene has none.
    /// </summary>
    internal static class Tones
    {
        private static AudioSource _src, _beaconSrc;
        private static AudioClip _momentum, _beacon;

        public static void Momentum() { Play(ref _momentum, 1320f, 0.09f); }

        /// <summary>Walking beacon: pan -1..1 (left..right), pitch multiplier.</summary>
        public static void Beacon(float pan, float pitch)
        {
            Ensure();
            if (_beacon == null) _beacon = Make(660f, 0.06f);
            _beaconSrc.panStereo = pan;
            _beaconSrc.pitch = pitch;
            _beaconSrc.PlayOneShot(_beacon);
        }

        private static void Play(ref AudioClip clip, float hz, float seconds)
        {
            Ensure();
            if (clip == null) clip = Make(hz, seconds);
            _src.PlayOneShot(clip);
        }

        private static void Ensure()
        {
            if (_src == null)
            {
                var go = new GameObject("SetsunaAccessTones");
                Object.DontDestroyOnLoad(go);
                _src = go.AddComponent<AudioSource>();
                _src.volume = 0.6f;
                _beaconSrc = go.AddComponent<AudioSource>();
                _beaconSrc.volume = 0.4f;
            }
            if (Object.FindObjectOfType<AudioListener>() == null) _src.gameObject.AddComponent<AudioListener>();
        }

        private static AudioClip Make(float hz, float seconds)
        {
            const int rate = 44100;
            var n = (int)(rate * seconds);
            var data = new float[n];
            for (var i = 0; i < n; i++)
            {
                var env = Mathf.Min(1f, Mathf.Min(i, n - i) / (rate * 0.005f)); // 5 ms fade in/out
                data[i] = Mathf.Sin(2f * Mathf.PI * hz * i / rate) * 0.5f * env;
            }
            var clip = AudioClip.Create("tone" + hz, n, 1, rate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
