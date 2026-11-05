using System;
using System.Threading;
using KeyScope;

internal static class NativeLiveCaptureProbe
{
    private static int Main()
    {
        int updates = 0;
        string lastStatus = "no update";
        using (AudioLoopbackListener listener = new AudioLoopbackListener())
        {
            listener.AnalysisUpdated += delegate(KeyEstimate estimate, double? bpm)
            {
                updates++;
                lastStatus = estimate.State == EstimateState.Key ? estimate.Key : estimate.Detail;
            };
            listener.Failed += delegate(string error) { lastStatus = "FAIL: " + error; };
            listener.Start();
            if (!listener.IsRunning)
            {
                Console.Error.WriteLine("Could not start the native live capture path.");
                return 1;
            }

            Thread.Sleep(22000);
            listener.Stop();
        }

        Console.WriteLine("Native live capture updates: " + updates + "; last status: " + lastStatus);
        return updates > 0 ? 0 : 2;
    }
}
