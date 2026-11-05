using System;
using System.Runtime.InteropServices;

namespace KeyScope
{
    /// <summary>
    /// Managed wrapper around libKeyFinder's progressive whole-track chromagram analysis. Unlike the first
    /// prototype, this does not classify the current chord-sized FFT window as a musical key.
    /// </summary>
    internal sealed class NativeKeyFinderDetector : IDisposable
    {
        private const int SilenceCode = 24;
        private const int MinimumEvidenceSeconds = 18;
        private static readonly string[] KeyNames =
        {
            "A major", "A minor", "Bb major", "Bb minor", "B major", "B minor",
            "C major", "C minor", "Db major", "Db minor", "D major", "D minor",
            "Eb major", "Eb minor", "E major", "E minor", "F major", "F minor",
            "Gb major", "Gb minor", "G major", "G minor", "Ab major", "Ab minor"
        };

        private IntPtr _handle;
        private int _sampleRate;
        private long _samplesFed;
        private KeyEstimate _displayed;
        private string _challenger;
        private int _challengerCount;

        [DllImport("keyfinder_bridge.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr KF_Create(uint sampleRate);

        [DllImport("keyfinder_bridge.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern void KF_Destroy(IntPtr handle);

        [DllImport("keyfinder_bridge.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int KF_AddMonoFloat(IntPtr handle, float[] samples, int count);

        [DllImport("keyfinder_bridge.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int KF_GetKey(IntPtr handle);

        public void Start(int sampleRate)
        {
            DisposeHandle();
            _sampleRate = sampleRate;
            _samplesFed = 0;
            _displayed = null;
            _challenger = null;
            _challengerCount = 0;
            _handle = KF_Create((uint)sampleRate);
            if (_handle == IntPtr.Zero)
            {
                throw new InvalidOperationException("The embedded key-analysis engine could not start.");
            }
        }

        public void AddAudio(float[] monoSamples)
        {
            if (_handle == IntPtr.Zero || monoSamples == null || monoSamples.Length == 0) return;
            int result = KF_AddMonoFloat(_handle, monoSamples, monoSamples.Length);
            if (result != 0)
            {
                throw new InvalidOperationException("The key-analysis engine rejected captured audio (" + result + ").");
            }
            _samplesFed += monoSamples.Length;
        }

        public KeyEstimate GetEstimate()
        {
            int seconds = _sampleRate == 0 ? 0 : (int)(_samplesFed / _sampleRate);
            if (seconds < MinimumEvidenceSeconds)
            {
                return KeyEstimate.NotCertain(
                    "Whole-track key analysis is collecting tonal evidence (" + seconds + "/" +
                    MinimumEvidenceSeconds + " seconds)…", 0, 0);
            }

            int nativeKey = KF_GetKey(_handle);
            if (nativeKey == SilenceCode)
            {
                return KeyEstimate.NotCertain("The captured audio has no clear tonal material yet.", 0, 0);
            }
            if (nativeKey < 0 || nativeKey >= KeyNames.Length)
            {
                return KeyEstimate.NotCertain("The key-analysis engine needs more clean tonal audio.", 0, 0);
            }

            string key = KeyNames[nativeKey];
            string mode = key.EndsWith("minor", StringComparison.Ordinal) ? "Minor" : "Major";
            // This is evidence duration, not an accuracy score. The UI labels it accordingly.
            KeyEstimate proposed = KeyEstimate.Detected(key, mode, Math.Min(99, seconds), 0, 0);

            if (_displayed == null || _displayed.Key == proposed.Key)
            {
                _displayed = proposed;
                _challenger = null;
                _challengerCount = 0;
                return proposed;
            }

            if (_challenger == proposed.Key) _challengerCount++;
            else
            {
                _challenger = proposed.Key;
                _challengerCount = 1;
            }

            if (_challengerCount >= 8)
            {
                _displayed = proposed;
                _challenger = null;
                _challengerCount = 0;
                return proposed;
            }

            return KeyEstimate.Held(_displayed,
                "Whole-track analysis is checking whether a new section has a sustained different key.");
        }

        public void Reset()
        {
            if (_sampleRate > 0) Start(_sampleRate);
        }

        private void DisposeHandle()
        {
            if (_handle == IntPtr.Zero) return;
            KF_Destroy(_handle);
            _handle = IntPtr.Zero;
        }

        public void Dispose()
        {
            DisposeHandle();
        }
    }
}
