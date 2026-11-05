using System;
using System.Collections.Generic;

namespace KeyScope
{
    public enum EstimateState
    {
        Key,
        Silence,
        Uncertain
    }

    public sealed class KeyEstimate
    {
        public EstimateState State { get; private set; }
        public string Key { get; private set; }
        public string Mode { get; private set; }
        public int Confidence { get; private set; }
        public string Detail { get; private set; }
        public double BestCorrelation { get; private set; }
        public double Margin { get; private set; }

        private KeyEstimate(EstimateState state, string key, string mode, int confidence,
            string detail, double bestCorrelation, double margin)
        {
            State = state;
            Key = key;
            Mode = mode;
            Confidence = confidence;
            Detail = detail;
            BestCorrelation = bestCorrelation;
            Margin = margin;
        }

        public static KeyEstimate Detected(string key, string mode, int confidence,
            double bestCorrelation, double margin)
        {
            return new KeyEstimate(EstimateState.Key, key, mode, confidence,
                "Stable song/section estimate from accumulated tonal audio", bestCorrelation, margin);
        }

        public static KeyEstimate NoSignal()
        {
            return new KeyEstimate(EstimateState.Silence, "—", "", 0,
                "No audible signal — play audio on the default Windows output.", 0, 0);
        }

        public static KeyEstimate NotCertain(string detail, double bestCorrelation, double margin)
        {
            return new KeyEstimate(EstimateState.Uncertain, "—", "", 0,
                detail, bestCorrelation, margin);
        }

        public static KeyEstimate Held(KeyEstimate previous, string detail)
        {
            return new KeyEstimate(EstimateState.Key, previous.Key, previous.Mode, previous.Confidence,
                detail, previous.BestCorrelation, previous.Margin);
        }
    }

    internal sealed class TonalFrame
    {
        public bool HasSignal;
        public bool IsTonal;
        public double[] Chroma;
        public double Rms;
        public string Detail;
    }

    /// <summary>
    /// Estimates a song/section-level key. A chord normally lasts a fraction of a second, while a key is
    /// supported by many seconds of note and chord evidence, so the display deliberately has a longer memory.
    /// </summary>
    public sealed class StableKeyDetector
    {
        // The capture analyser runs every 0.6 seconds. A 30-second memory resists chord-by-chord flipping.
        private const int HistoryLength = 50;
        private const int MinimumEvidenceWindows = 24;
        private const int SwitchAgreementWindows = 8;
        private const int HoldThroughUncertainWindows = 10;
        private readonly Queue<double[]> _history = new Queue<double[]>();
        private KeyEstimate _displayed;
        private string _challengerKey;
        private int _challengerWindows;
        private int _uncertainWindows;

        public KeyEstimate Analyze(float[] samples, int sampleRate)
        {
            TonalFrame frame = KeyAnalyzer.CreateFrame(samples, sampleRate);
            if (!frame.HasSignal)
            {
                Reset();
                return KeyEstimate.NoSignal();
            }

            if (!frame.IsTonal)
            {
                return StabilizeUncertain(KeyEstimate.NotCertain(frame.Detail, 0, 0));
            }

            _history.Enqueue(frame.Chroma);
            while (_history.Count > HistoryLength)
            {
                _history.Dequeue();
            }

            if (_history.Count < MinimumEvidenceWindows)
            {
                return KeyEstimate.NotCertain(
                    "Collecting song/section context for a reliable key (" + _history.Count + "/" +
                    MinimumEvidenceWindows + ")…", 0, 0);
            }

            return StabilizeKey(KeyAnalyzer.Estimate(AverageHistory()));
        }

        public void Reset()
        {
            _history.Clear();
            _displayed = null;
            _challengerKey = null;
            _challengerWindows = 0;
            _uncertainWindows = 0;
        }

        private KeyEstimate StabilizeKey(KeyEstimate proposed)
        {
            if (proposed.State != EstimateState.Key)
            {
                return StabilizeUncertain(proposed);
            }

            _uncertainWindows = 0;
            if (_displayed == null)
            {
                _displayed = proposed;
                ClearChallenger();
                return proposed;
            }

            if (proposed.Key == _displayed.Key)
            {
                _displayed = proposed;
                ClearChallenger();
                return proposed;
            }

            // A global profile can still drift while a chorus or bridge has strong local harmony. Require
            // several consecutive global estimates before changing an already displayed song/section key.
            CountChallenger(proposed.Key);
            if (_challengerWindows >= SwitchAgreementWindows &&
                proposed.Confidence >= Math.Max(38, _displayed.Confidence - 5))
            {
                _displayed = proposed;
                ClearChallenger();
                return proposed;
            }

            return KeyEstimate.Held(_displayed,
                "Holding the song/section key while checking whether the new harmony persists.");
        }

        private KeyEstimate StabilizeUncertain(KeyEstimate proposed)
        {
            ClearChallenger();
            _uncertainWindows++;
            if (_displayed != null && _uncertainWindows <= HoldThroughUncertainWindows)
            {
                return KeyEstimate.Held(_displayed,
                    "Holding the song/section key through a brief ambiguous or non-tonal passage.");
            }

            if (_uncertainWindows > HoldThroughUncertainWindows)
            {
                _displayed = null;
                _history.Clear();
            }
            return proposed;
        }

        private void CountChallenger(string key)
        {
            if (_challengerKey == key)
            {
                _challengerWindows++;
            }
            else
            {
                _challengerKey = key;
                _challengerWindows = 1;
            }
        }

        private void ClearChallenger()
        {
            _challengerKey = null;
            _challengerWindows = 0;
        }

        private double[] AverageHistory()
        {
            double[] result = new double[12];
            int count = _history.Count;
            int index = 0;

            foreach (double[] chroma in _history)
            {
                // Let recent evidence matter without turning this back into a chord detector.
                double weight = 0.85 + (0.15 * (index + 1) / count);
                for (int pc = 0; pc < 12; pc++)
                {
                    result[pc] += chroma[pc] * weight;
                }
                index++;
            }

            Normalize(result);
            return result;
        }

        private static void Normalize(double[] values)
        {
            double sum = 0;
            for (int i = 0; i < values.Length; i++) sum += values[i];
            if (sum <= 0) return;
            for (int i = 0; i < values.Length; i++) values[i] /= sum;
        }
    }

    /// <summary>
    /// A compact chroma / key-profile analyser. It estimates musical key from the current mix; it does not
    /// claim ground truth for ambiguous harmony, percussion, atonal music, or a short single-note passage.
    /// </summary>
    public static class KeyAnalyzer
    {
        private const int FftSize = 16384;
        private static readonly string[] PitchClasses =
        {
            "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B"
        };

        // Krumhansl-Schmuckler key profiles, indexed from C upward.
        private static readonly double[] MajorProfile =
        {
            6.35, 2.23, 3.48, 2.33, 4.38, 4.09, 2.52, 5.19, 2.39, 3.66, 2.29, 2.88
        };

        private static readonly double[] MinorProfile =
        {
            6.33, 2.68, 3.52, 5.38, 2.60, 3.53, 2.54, 4.75, 3.98, 2.69, 3.34, 3.17
        };

        // Aarden-Essen profiles complement Krumhansl-Schmuckler. Averaging both avoids letting any one
        // hand-tuned template dominate a genre, instrument mix, or voicing.
        private static readonly double[] AardenEssenMajorProfile =
        {
            17.766, 0.145, 14.926, 0.160, 19.805, 11.358, 0.291, 22.062, 0.146, 8.155, 0.232, 4.951
        };

        private static readonly double[] AardenEssenMinorProfile =
        {
            18.264, 0.738, 14.049, 16.859, 0.702, 14.436, 0.582, 18.616, 4.566, 1.931, 7.376, 1.756
        };

        internal static TonalFrame CreateFrame(float[] samples, int sampleRate)
        {
            TonalFrame frame = new TonalFrame();
            if (samples == null || samples.Length < 2048 || sampleRate <= 0)
            {
                frame.Detail = "Waiting for enough audio…";
                return frame;
            }

            double sumSquares = 0;
            for (int i = 0; i < samples.Length; i++)
            {
                double sample = samples[i];
                sumSquares += sample * sample;
            }
            frame.Rms = Math.Sqrt(sumSquares / samples.Length);

            // Loopback audio below this level is generally silence or interface noise.
            if (frame.Rms < 0.0025)
            {
                frame.Detail = "No audible signal";
                return frame;
            }
            frame.HasSignal = true;

            double[] real = new double[FftSize];
            double[] imaginary = new double[FftSize];
            int inputStart = Math.Max(0, samples.Length - FftSize);
            int copyLength = Math.Min(FftSize, samples.Length);
            for (int i = 0; i < copyLength; i++)
            {
                // Hann window limits spectral leakage between musical notes.
                double window = 0.5 - (0.5 * Math.Cos((2.0 * Math.PI * i) / (copyLength - 1)));
                real[i] = samples[inputStart + i] * window;
            }

            FastFourierTransform(real, imaginary);
            double[] chroma = BuildHarmonicChroma(real, imaginary, sampleRate);

            Normalize(chroma);
            double spread = ChromaSpread(chroma);
            Candidate best = FindBestCandidate(chroma);

            // Noise and percussion tend to produce a nearly flat pitch-class histogram and a weak profile fit.
            if (spread < 0.060 || best.Correlation < 0.12 || best.Margin < 0.012)
            {
                frame.Detail = "Audio is present, but no stable tonal centre is clear.";
                return frame;
            }

            frame.IsTonal = true;
            frame.Chroma = chroma;
            frame.Detail = "Tonal audio found";
            return frame;
        }

        /// <summary>
        /// Builds a harmonic pitch-class profile (HPCP) from spectral peaks. In contrast to simply folding
        /// every FFT bin into twelve notes, this rewards a plausible fundamental when its harmonics agree.
        /// That substantially reduces the tendency for a bright guitar, vocal, or cymbal-heavy mix to look
        /// like whichever chord is loudest in one small window.
        /// </summary>
        private static double[] BuildHarmonicChroma(double[] real, double[] imaginary, int sampleRate)
        {
            int spectrumLast = Math.Min(FftSize / 2 - 1, (int)Math.Floor(6000.0 * FftSize / sampleRate));
            int lowestBin = Math.Max(2, (int)Math.Ceiling(55.0 * FftSize / sampleRate));
            int highestFundamentalBin = Math.Min(
                (int)Math.Floor(1400.0 * FftSize / sampleRate), spectrumLast - 1);
            double[] magnitude = new double[spectrumLast + 1];
            double largestPeak = 0;

            for (int bin = lowestBin - 1; bin <= spectrumLast; bin++)
            {
                double value = Math.Sqrt((real[bin] * real[bin]) + (imaginary[bin] * imaginary[bin]));
                magnitude[bin] = value;
                if (value > largestPeak) largestPeak = value;
            }

            double[] chroma = new double[12];
            if (largestPeak <= 0) return chroma;

            for (int bin = lowestBin; bin <= highestFundamentalBin; bin++)
            {
                double centre = magnitude[bin];
                // Work with local peaks rather than every spectral bin. It makes the pitch representation
                // far less sensitive to broadband noise and non-pitched transients.
                if (centre < magnitude[bin - 1] || centre < magnitude[bin + 1]) continue;

                double harmonicEnergy = 0;
                for (int harmonic = 1; harmonic <= 6; harmonic++)
                {
                    int harmonicBin = bin * harmonic;
                    if (harmonicBin > spectrumLast) break;
                    harmonicEnergy += magnitude[harmonicBin] / Math.Pow(harmonic, 0.70);
                }

                if (centre < largestPeak * 0.003 && harmonicEnergy < largestPeak * 0.012) continue;

                // Sub-bin interpolation improves note-class assignment when A is not exactly 440 Hz or the
                // FFT centre falls between two pitches.
                double denominator = magnitude[bin - 1] - 2.0 * centre + magnitude[bin + 1];
                double offset = Math.Abs(denominator) < 0.000000001 ? 0 :
                    0.5 * (magnitude[bin - 1] - magnitude[bin + 1]) / denominator;
                offset = Clamp(offset, -0.5, 0.5);
                double frequency = (bin + offset) * sampleRate / FftSize;
                if (frequency < 55.0 || frequency > 1400.0) continue;

                double midi = 69.0 + 12.0 * Log2(frequency / 440.0);
                int pitchClass = PositiveMod((int)Math.Round(midi), 12);
                chroma[pitchClass] += Math.Pow(harmonicEnergy, 0.62);
            }

            Normalize(chroma);
            return chroma;
        }

        internal static KeyEstimate Estimate(double[] chroma)
        {
            Candidate best = FindBestCandidate(chroma);
            double spread = ChromaSpread(chroma);
            int confidence = (int)Math.Round(Clamp(
                (0.45 * best.Correlation + 1.70 * best.Margin + 0.50 * spread) * 100.0,
                0, 100));

            if (best.Correlation < 0.16 || best.Margin < 0.012 || confidence < 28)
            {
                return KeyEstimate.NotCertain("Listening for a clearer, stable tonal centre…",
                    best.Correlation, best.Margin);
            }

            string mode = best.IsMinor ? "Minor" : "Major";
            return KeyEstimate.Detected(PitchClasses[best.Tonic] + " " + mode.ToLowerInvariant(),
                mode, confidence, best.Correlation, best.Margin);
        }

        private static Candidate FindBestCandidate(double[] chroma)
        {
            Candidate best = new Candidate();
            Candidate second = new Candidate();
            best.Correlation = double.NegativeInfinity;
            second.Correlation = double.NegativeInfinity;

            for (int tonic = 0; tonic < 12; tonic++)
            {
                Candidate major = new Candidate();
                major.Tonic = tonic;
                major.IsMinor = false;
                major.Correlation = 0.50 * Correlation(chroma, MajorProfile, tonic) +
                    0.50 * Correlation(chroma, AardenEssenMajorProfile, tonic);
                PlaceCandidate(major, ref best, ref second);

                Candidate minor = new Candidate();
                minor.Tonic = tonic;
                minor.IsMinor = true;
                minor.Correlation = 0.50 * Correlation(chroma, MinorProfile, tonic) +
                    0.50 * Correlation(chroma, AardenEssenMinorProfile, tonic);
                PlaceCandidate(minor, ref best, ref second);
            }

            best.Margin = best.Correlation - second.Correlation;
            return best;
        }

        private static void PlaceCandidate(Candidate candidate, ref Candidate best, ref Candidate second)
        {
            if (candidate.Correlation > best.Correlation)
            {
                second = best;
                best = candidate;
            }
            else if (candidate.Correlation > second.Correlation)
            {
                second = candidate;
            }
        }

        // Pearson correlation makes a flat chroma vector score near zero instead of favouring a profile by chance.
        private static double Correlation(double[] chroma, double[] profile, int tonic)
        {
            const double mean = 1.0 / 12.0;
            double profileMean = 0;
            for (int i = 0; i < 12; i++) profileMean += profile[i];
            profileMean /= 12.0;

            double numerator = 0;
            double left = 0;
            double right = 0;
            for (int pc = 0; pc < 12; pc++)
            {
                double x = chroma[pc] - mean;
                double y = profile[PositiveMod(pc - tonic, 12)] - profileMean;
                numerator += x * y;
                left += x * x;
                right += y * y;
            }
            if (left < 0.000000001 || right < 0.000000001) return 0;
            return numerator / Math.Sqrt(left * right);
        }

        private static double ChromaSpread(double[] chroma)
        {
            const double mean = 1.0 / 12.0;
            double squares = 0;
            for (int i = 0; i < chroma.Length; i++)
            {
                double delta = chroma[i] - mean;
                squares += delta * delta;
            }
            return Math.Sqrt(squares / chroma.Length) * 12.0;
        }

        private static void Normalize(double[] values)
        {
            double sum = 0;
            for (int i = 0; i < values.Length; i++) sum += values[i];
            if (sum <= 0) return;
            for (int i = 0; i < values.Length; i++) values[i] /= sum;
        }

        private static void FastFourierTransform(double[] real, double[] imaginary)
        {
            int n = real.Length;
            for (int i = 1, j = 0; i < n; i++)
            {
                int bit = n >> 1;
                for (; (j & bit) != 0; bit >>= 1) j ^= bit;
                j ^= bit;
                if (i < j)
                {
                    double temp = real[i]; real[i] = real[j]; real[j] = temp;
                    temp = imaginary[i]; imaginary[i] = imaginary[j]; imaginary[j] = temp;
                }
            }

            for (int length = 2; length <= n; length <<= 1)
            {
                double angle = -2.0 * Math.PI / length;
                double stepReal = Math.Cos(angle);
                double stepImaginary = Math.Sin(angle);
                for (int start = 0; start < n; start += length)
                {
                    double currentReal = 1;
                    double currentImaginary = 0;
                    int half = length >> 1;
                    for (int k = 0; k < half; k++)
                    {
                        int even = start + k;
                        int odd = even + half;
                        double oddReal = real[odd] * currentReal - imaginary[odd] * currentImaginary;
                        double oddImaginary = real[odd] * currentImaginary + imaginary[odd] * currentReal;
                        double evenReal = real[even];
                        double evenImaginary = imaginary[even];
                        real[even] = evenReal + oddReal;
                        imaginary[even] = evenImaginary + oddImaginary;
                        real[odd] = evenReal - oddReal;
                        imaginary[odd] = evenImaginary - oddImaginary;

                        double nextReal = currentReal * stepReal - currentImaginary * stepImaginary;
                        currentImaginary = currentReal * stepImaginary + currentImaginary * stepReal;
                        currentReal = nextReal;
                    }
                }
            }
        }

        private static int PositiveMod(int value, int modulus)
        {
            int result = value % modulus;
            return result < 0 ? result + modulus : result;
        }

        private static double Log2(double value)
        {
            return Math.Log(value) / Math.Log(2.0);
        }

        private static double Clamp(double value, double min, double max)
        {
            return Math.Max(min, Math.Min(max, value));
        }

        private struct Candidate
        {
            public int Tonic;
            public bool IsMinor;
            public double Correlation;
            public double Margin;
        }
    }
}
