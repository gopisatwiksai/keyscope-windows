using System;
using System.Collections.Generic;
using System.Threading;
using CSCore;
using CSCore.SoundIn;

namespace KeyScope
{
    /// <summary>
    /// Captures the default Windows playback endpoint with WASAPI loopback. It hears what is sent to
    /// speakers/headphones (Spotify, browsers, VLC, Ableton, etc.), not the microphone.
    /// </summary>
    public sealed class AudioLoopbackListener : IDisposable
    {
        private const int RingCapacity = 65536;
        private readonly object _sampleLock = new object();
        private readonly object _pendingAudioLock = new object();
        private readonly float[] _ringBuffer = new float[RingCapacity];
        private readonly Queue<CapturedAudioBlock> _pendingAudio = new Queue<CapturedAudioBlock>();
        private readonly NativeKeyFinderDetector _keyDetector = new NativeKeyFinderDetector();
        private readonly BpmTracker _bpmTracker = new BpmTracker();
        private WasapiLoopbackCapture _capture;
        private Timer _analysisTimer;
        private int _sampleRate;
        private int _channels;
        private int _bytesPerSample;
        private int _blockAlign;
        private int _writePosition;
        private int _bufferedSamples;
        private int _analyzing;
        private double _silentSeconds;
        private volatile bool _running;

        public event Action<KeyEstimate, double?> AnalysisUpdated;
        public event Action<string> Failed;

        public bool IsRunning { get { return _running; } }
        public int SampleRate { get { return _sampleRate; } }

        public void Start()
        {
            if (_running) return;

            try
            {
                _bpmTracker.Reset();
                lock (_sampleLock)
                {
                    _writePosition = 0;
                    _bufferedSamples = 0;
                }
                lock (_pendingAudioLock) _pendingAudio.Clear();
                _silentSeconds = 0;

                // A float format lets WASAPI choose the closest device-supported format when necessary.
                WaveFormat requestedFormat = new WaveFormat(48000, 32, 2, AudioEncoding.IeeeFloat);
                _capture = new WasapiLoopbackCapture(25, requestedFormat);
                _capture.DataAvailable += CaptureDataAvailable;
                _capture.Initialize();

                _sampleRate = _capture.WaveFormat.SampleRate;
                _channels = _capture.WaveFormat.Channels;
                _bytesPerSample = _capture.WaveFormat.BytesPerSample;
                _blockAlign = _capture.WaveFormat.BlockAlign;
                if (_sampleRate <= 0 || _channels <= 0 || _bytesPerSample <= 0 || _blockAlign <= 0)
                {
                    throw new InvalidOperationException("Windows returned an unsupported playback format.");
                }

                _keyDetector.Start(_sampleRate);

                _running = true;
                _capture.Start();
                _analysisTimer = new Timer(AnalyzeLatest, null, 700, 600);
            }
            catch (Exception exception)
            {
                CleanUpCapture();
                ReportFailure("Could not start Windows loopback capture: " + exception.Message);
            }
        }

        public void Stop()
        {
            if (!_running && _capture == null) return;
            _running = false;
            Timer timer = _analysisTimer;
            _analysisTimer = null;
            if (timer != null) timer.Dispose();
            CleanUpCapture();
            _keyDetector.Dispose();
            _bpmTracker.Reset();
            lock (_pendingAudioLock) _pendingAudio.Clear();
            _silentSeconds = 0;
        }

        private void CaptureDataAvailable(object sender, DataAvailableEventArgs eventArgs)
        {
            if (!_running || eventArgs.ByteCount <= 0) return;
            try
            {
                int frames = eventArgs.ByteCount / _blockAlign;
                if (frames <= 0) return;

                double sumSquares = 0;
                float[] monoSamples = new float[frames];
                int position = eventArgs.Offset;
                for (int frame = 0; frame < frames; frame++)
                {
                    double mono = 0;
                    for (int channel = 0; channel < _channels; channel++)
                    {
                        mono += ReadSample(eventArgs.Data, position + channel * _bytesPerSample, eventArgs.Format);
                    }
                    float sample = (float)(mono / _channels);
                    sumSquares += sample * sample;
                    monoSamples[frame] = sample;
                    position += _blockAlign;
                }
                double rms = Math.Sqrt(sumSquares / frames);
                double seconds = (double)frames / _sampleRate;
                _bpmTracker.AddBlockRms(rms, seconds);
                lock (_pendingAudioLock)
                {
                    _pendingAudio.Enqueue(new CapturedAudioBlock(monoSamples, rms, seconds));
                }
            }
            catch (Exception exception)
            {
                // The capture callback must stay alive; report a decoding problem and stop cleanly.
                _running = false;
                ReportFailure("Audio capture stopped: " + exception.Message);
            }
        }

        private static float ReadSample(byte[] data, int offset, WaveFormat format)
        {
            int bits = format.BitsPerSample;
            AudioEncoding encoding = format.WaveFormatTag;

            if (bits == 32 && encoding != AudioEncoding.Pcm)
            {
                return BitConverter.ToSingle(data, offset);
            }
            if (bits == 64 && encoding != AudioEncoding.Pcm)
            {
                return (float)BitConverter.ToDouble(data, offset);
            }
            if (bits == 16)
            {
                short value = (short)(data[offset] | (data[offset + 1] << 8));
                return value / 32768f;
            }
            if (bits == 24)
            {
                int value = data[offset] | (data[offset + 1] << 8) | (data[offset + 2] << 16);
                if ((value & 0x800000) != 0) value |= unchecked((int)0xFF000000);
                return value / 8388608f;
            }
            if (bits == 32)
            {
                int value = BitConverter.ToInt32(data, offset);
                return value / 2147483648f;
            }
            throw new NotSupportedException("Unsupported sample size: " + bits + " bits.");
        }

        private void AddToRingBuffer(float sample)
        {
            lock (_sampleLock)
            {
                _ringBuffer[_writePosition] = sample;
                _writePosition = (_writePosition + 1) % RingCapacity;
                if (_bufferedSamples < RingCapacity) _bufferedSamples++;
            }
        }

        private void AnalyzeLatest(object state)
        {
            if (!_running || Interlocked.Exchange(ref _analyzing, 1) != 0) return;
            try
            {
                CapturedAudioBlock[] blocks = TakePendingAudio();
                if (blocks.Length == 0) return;

                for (int index = 0; index < blocks.Length; index++)
                {
                    CapturedAudioBlock block = blocks[index];
                    if (block.Rms < 0.0025)
                    {
                        _silentSeconds += block.Seconds;
                        continue;
                    }
                    _silentSeconds = 0;
                    _keyDetector.AddAudio(block.Samples);
                }

                KeyEstimate estimate;
                if (_silentSeconds >= 2.5)
                {
                    _keyDetector.Reset();
                    _bpmTracker.Reset();
                    estimate = KeyEstimate.NoSignal();
                }
                else
                {
                    estimate = _keyDetector.GetEstimate();
                }
                double? bpm = _bpmTracker.TryEstimate();
                Action<KeyEstimate, double?> handler = AnalysisUpdated;
                if (handler != null && _running) handler(estimate, bpm);
            }
            catch (Exception exception)
            {
                ReportFailure("Analysis stopped: " + exception.Message);
            }
            finally
            {
                Interlocked.Exchange(ref _analyzing, 0);
            }
        }

        private float[] GetLatestSamples(int desiredLength)
        {
            lock (_sampleLock)
            {
                int count = Math.Min(desiredLength, _bufferedSamples);
                if (count <= 0) return null;
                float[] result = new float[count];
                int start = _writePosition - count;
                if (start < 0) start += RingCapacity;
                for (int i = 0; i < count; i++)
                {
                    result[i] = _ringBuffer[(start + i) % RingCapacity];
                }
                return result;
            }
        }

        private CapturedAudioBlock[] TakePendingAudio()
        {
            lock (_pendingAudioLock)
            {
                CapturedAudioBlock[] result = _pendingAudio.ToArray();
                _pendingAudio.Clear();
                return result;
            }
        }

        private void CleanUpCapture()
        {
            WasapiLoopbackCapture capture = _capture;
            _capture = null;
            if (capture == null) return;
            try { capture.DataAvailable -= CaptureDataAvailable; } catch { }
            try { capture.Stop(); } catch { }
            try { capture.Dispose(); } catch { }
        }

        private void ReportFailure(string message)
        {
            Action<string> handler = Failed;
            if (handler != null) handler(message);
        }

        public void Dispose()
        {
            Stop();
        }

        private struct CapturedAudioBlock
        {
            public CapturedAudioBlock(float[] samples, double rms, double seconds)
            {
                Samples = samples;
                Rms = rms;
                Seconds = seconds;
            }

            public readonly float[] Samples;
            public readonly double Rms;
            public readonly double Seconds;
        }
    }

    /// <summary>
    /// Deliberately conservative, rough tempo estimator based on an energy envelope. It stays blank until
    /// several seconds of rhythmic audio are available, rather than inventing a number for silence or ambience.
    /// </summary>
    internal sealed class BpmTracker
    {
        private const double EnvelopeRate = 50.0;
        private const double EnvelopePeriod = 1.0 / EnvelopeRate;
        private readonly object _lock = new object();
        private readonly List<double> _envelope = new List<double>();
        private double _energySum;
        private double _accumulatedSeconds;

        public void AddBlockRms(double rms, double seconds)
        {
            if (seconds <= 0) return;
            lock (_lock)
            {
                double remaining = seconds;
                double power = rms * rms;
                while (remaining > 0)
                {
                    double needed = EnvelopePeriod - _accumulatedSeconds;
                    double used = Math.Min(remaining, needed);
                    _energySum += power * used;
                    _accumulatedSeconds += used;
                    remaining -= used;
                    if (_accumulatedSeconds >= EnvelopePeriod - 0.0000001)
                    {
                        _envelope.Add(Math.Sqrt(_energySum / _accumulatedSeconds));
                        if (_envelope.Count > 900) _envelope.RemoveAt(0);
                        _energySum = 0;
                        _accumulatedSeconds = 0;
                    }
                }
            }
        }

        public double? TryEstimate()
        {
            double[] source;
            lock (_lock)
            {
                if (_envelope.Count < 350) return null; // about seven seconds
                int count = Math.Min(_envelope.Count, 800);
                source = _envelope.GetRange(_envelope.Count - count, count).ToArray();
            }

            double[] onset = new double[source.Length];
            for (int i = 0; i < source.Length; i++)
            {
                int from = Math.Max(0, i - 25);
                int to = Math.Min(source.Length - 1, i + 25);
                double average = 0;
                for (int j = from; j <= to; j++) average += source[j];
                average /= (to - from + 1);
                onset[i] = Math.Max(0, source[i] - average);
            }

            int minLag = (int)Math.Round(EnvelopeRate * 60.0 / 200.0);
            int maxLag = (int)Math.Round(EnvelopeRate * 60.0 / 60.0);
            double bestCorrelation = double.NegativeInfinity;
            int bestLag = 0;
            for (int lag = minLag; lag <= maxLag; lag++)
            {
                double numerator = 0;
                double left = 0;
                double right = 0;
                for (int i = lag; i < onset.Length; i++)
                {
                    numerator += onset[i] * onset[i - lag];
                    left += onset[i] * onset[i];
                    right += onset[i - lag] * onset[i - lag];
                }
                if (left <= 0.000000001 || right <= 0.000000001) continue;
                double correlation = numerator / Math.Sqrt(left * right);
                if (correlation > bestCorrelation)
                {
                    bestCorrelation = correlation;
                    bestLag = lag;
                }
            }

            if (bestLag == 0 || bestCorrelation < 0.20) return null;
            return 60.0 * EnvelopeRate / bestLag;
        }

        public void Reset()
        {
            lock (_lock)
            {
                _envelope.Clear();
                _energySum = 0;
                _accumulatedSeconds = 0;
            }
        }
    }
}
