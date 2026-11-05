using System;
using System.Threading;
using CSCore;
using CSCore.SoundIn;

internal static class LoopbackProbe
{
    private static int Main()
    {
        int callbacks = 0;
        int bytes = 0;
        try
        {
            WaveFormat requested = new WaveFormat(48000, 32, 2, AudioEncoding.IeeeFloat);
            using (WasapiLoopbackCapture capture = new WasapiLoopbackCapture(25, requested))
            {
                capture.DataAvailable += delegate(object sender, DataAvailableEventArgs args)
                {
                    callbacks++;
                    bytes += args.ByteCount;
                };
                capture.Initialize();
                Console.WriteLine("Format: {0} Hz, {1} channel(s), {2}-bit {3}",
                    capture.WaveFormat.SampleRate, capture.WaveFormat.Channels,
                    capture.WaveFormat.BitsPerSample, capture.WaveFormat.WaveFormatTag);
                capture.Start();
                Thread.Sleep(2000);
                capture.Stop();
            }
            Console.WriteLine("WASAPI loopback callbacks: {0}; bytes captured: {1}", callbacks, bytes);
            return callbacks > 0 && bytes > 0 ? 0 : 2;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine("Loopback probe failed: " + exception.Message);
            return 1;
        }
    }
}
