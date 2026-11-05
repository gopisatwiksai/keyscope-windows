using System;
using KeyScope;

internal static class SmokeTests
{
    private static int Main()
    {
        try
        {
            TestSilenceIsNotKey();
            TestCMajorTriad();
            TestAMinorTriad();
            TestChordProgressionUsesGlobalKey();
            TestKeyHysteresis();
            TestRhythmicEnvelope();
            Console.WriteLine("All detector smoke tests passed.");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine("Smoke test failed: " + exception.Message);
            return 1;
        }
    }

    private static void TestSilenceIsNotKey()
    {
        StableKeyDetector detector = new StableKeyDetector();
        KeyEstimate result = detector.Analyze(new float[16384], 48000);
        Require(result.State == EstimateState.Silence, "silence must not receive a key");
    }

    private static void TestCMajorTriad()
    {
        KeyEstimate result = AnalyzeChord(new double[] { 130.81, 261.63, 329.63, 392.00 });
        Require(result.State == EstimateState.Key, "C major triad should produce an estimate");
        Require(result.Key == "C major", "expected C major, received " + result.Key);
    }

    private static void TestAMinorTriad()
    {
        KeyEstimate result = AnalyzeChord(new double[] { 110.00, 220.00, 261.63, 329.63 });
        Require(result.State == EstimateState.Key, "A minor triad should produce an estimate");
        Require(result.Key == "A minor", "expected A minor, received " + result.Key);
    }

    private static KeyEstimate AnalyzeChord(double[] frequencies)
    {
        float[] samples = GenerateChord(frequencies);

        StableKeyDetector detector = new StableKeyDetector();
        KeyEstimate result = null;
        // KeyScope deliberately waits for song/section context rather than treating one chord as a key.
        for (int pass = 0; pass < 30; pass++) result = detector.Analyze(samples, 48000);
        return result;
    }

    private static void TestKeyHysteresis()
    {
        StableKeyDetector detector = new StableKeyDetector();
        float[] cMajor = GenerateChord(new double[] { 130.81, 261.63, 329.63, 392.00 });
        float[] dMajor = GenerateChord(new double[] { 146.83, 293.66, 369.99, 440.00 });
        KeyEstimate result = null;

        for (int pass = 0; pass < 30; pass++) result = detector.Analyze(cMajor, 48000);
        Require(result.Key == "C major", "stable detector should settle on C major");

        // A few different windows are intentionally held rather than immediately flipping the shown key.
        for (int pass = 0; pass < 3; pass++) result = detector.Analyze(dMajor, 48000);
        Require(result.Key == "C major", "brief key change should not immediately replace the displayed key");

        for (int pass = 0; pass < 65; pass++) result = detector.Analyze(dMajor, 48000);
        Require(result.Key == "D major", "sustained new harmony should eventually replace the displayed key");
    }

    private static void TestChordProgressionUsesGlobalKey()
    {
        StableKeyDetector detector = new StableKeyDetector();
        float[] cMajor = GenerateChord(new double[] { 130.81, 261.63, 329.63, 392.00 });
        float[] fMajor = GenerateChord(new double[] { 174.61, 349.23, 440.00, 523.25 });
        float[] gMajor = GenerateChord(new double[] { 196.00, 392.00, 493.88, 587.33 });
        KeyEstimate result = null;

        // C-F-G-C uses three different chords but has an unambiguous C-major pitch collection.
        for (int cycle = 0; cycle < 8; cycle++)
        {
            result = detector.Analyze(cMajor, 48000);
            result = detector.Analyze(fMajor, 48000);
            result = detector.Analyze(gMajor, 48000);
            result = detector.Analyze(cMajor, 48000);
        }
        Require(result.State == EstimateState.Key, "a tonal progression should receive a global-key estimate");
        Require(result.Key == "C major", "C-F-G-C progression should resolve to C major, received " + result.Key);
    }

    private static float[] GenerateChord(double[] frequencies)
    {
        const int sampleRate = 48000;
        const int length = 16384;
        float[] samples = new float[length];
        for (int i = 0; i < samples.Length; i++)
        {
            double sample = 0;
            for (int note = 0; note < frequencies.Length; note++)
            {
                sample += Math.Sin(2.0 * Math.PI * frequencies[note] * i / sampleRate);
            }
            samples[i] = (float)(sample / frequencies.Length * 0.75);
        }
        return samples;
    }

    private static void TestRhythmicEnvelope()
    {
        BpmTracker tracker = new BpmTracker();
        // 120 BPM = one onset every 25 frames at the tracker’s 50 Hz envelope rate.
        for (int frame = 0; frame < 650; frame++)
        {
            double rms = (frame % 25) < 3 ? 0.8 : 0.08;
            tracker.AddBlockRms(rms, 0.02);
        }
        double? bpm = tracker.TryEstimate();
        Require(bpm.HasValue, "regular rhythmic audio should produce a BPM estimate");
        Require(Math.Abs(bpm.Value - 120.0) <= 5.0, "expected ~120 BPM, received " + bpm.Value);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
