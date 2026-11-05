using System;
using KeyScope;

internal static class NativeEngineSmokeTests
{
    private static int Main()
    {
        const int sampleRate = 48000;
        const int blockSize = 16384;
        float[] block = new float[blockSize];
        double[] frequencies = { 130.81, 261.63, 329.63, 392.00 };

        for (int i = 0; i < block.Length; i++)
        {
            double value = 0;
            for (int note = 0; note < frequencies.Length; note++)
                value += Math.Sin(2.0 * Math.PI * frequencies[note] * i / sampleRate);
            block[i] = (float)(value * 0.15);
        }

        try
        {
            using (NativeKeyFinderDetector detector = new NativeKeyFinderDetector())
            {
                detector.Start(sampleRate);
                for (int i = 0; i < 65; i++) detector.AddAudio(block);
                KeyEstimate result = detector.GetEstimate();
                if (result.State != EstimateState.Key || result.Key != "C major")
                {
                    Console.Error.WriteLine("Native key engine expected C major but returned " + result.Key + ".");
                    return 1;
                }
            }
            Console.WriteLine("Native whole-track key-engine smoke test passed.");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine("Native key-engine smoke test failed: " + exception.Message);
            return 2;
        }
    }
}
