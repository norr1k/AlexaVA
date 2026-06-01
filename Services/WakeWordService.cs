using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using NAudio.Wave;
using NanoWakeWord;

namespace Alexa.Services;

public sealed class WakeWordService : IDisposable
{
    private const string WakeWordModel = "alexa_v0.1";
    private const float WakeWordThreshold = 0.7f;
    private const int FrameLength = 512;

    private readonly object _syncRoot = new();
    private readonly short[] _frame = new short[FrameLength];
    private WakeWordRuntime? _runtime;
    private WaveInEvent? _waveIn;
    private string? _inputDeviceName;
    private int _frameOffset;
    private bool _isDisposed;
    private bool _isSuspended;
    private bool _isListening;

    public event Action? WakeWordDetected;

    public void Configure(AppSettings settings)
    {
        lock (_syncRoot)
        {
            _inputDeviceName = settings.SelectedInputDevice;
        }

        RestartIfAllowed();
    }

    public void SetSuspended(bool isSuspended)
    {
        lock (_syncRoot)
        {
            if (_isDisposed || _isSuspended == isSuspended)
                return;

            _isSuspended = isSuspended;
        }

        if (isSuspended)
            StopListening();
        else
            RestartIfAllowed();
    }

    public void Dispose()
    {
        lock (_syncRoot)
        {
            _isDisposed = true;
        }

        StopListening();
    }

    private void RestartIfAllowed()
    {
        lock (_syncRoot)
        {
            if (_isDisposed || _isSuspended)
                return;
        }

        StopListening();
        StartListening();
    }

    private void StartListening()
    {
        lock (_syncRoot)
        {
            if (_isDisposed || _isSuspended || _isListening)
                return;

            try
            {
                _frameOffset = 0;
                _runtime = new WakeWordRuntime(new WakeWordRuntimeConfig
                {
                    WakeWords =
                    [
                        new WakeWordConfig
                        {
                            Model = WakeWordModel,
                            Threshold = WakeWordThreshold
                        }
                    ]
                });

                _waveIn = new WaveInEvent
                {
                    DeviceNumber = FindInputDeviceNumber(_inputDeviceName),
                    WaveFormat = AudioDeviceService.RecordingFormat,
                    BufferMilliseconds = 32
                };

                _waveIn.DataAvailable += OnDataAvailable;
                _waveIn.RecordingStopped += OnRecordingStopped;
                _waveIn.StartRecording();
                _isListening = true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Wake-word listener failed to start: {ex}");
                CleanupListener();
            }
        }
    }

    private void StopListening()
    {
        WaveInEvent? waveIn;

        lock (_syncRoot)
        {
            waveIn = _waveIn;
        }

        if (waveIn is not null)
        {
            try
            {
                waveIn.StopRecording();
            }
            catch
            {
                CleanupListener();
            }
        }
        else
        {
            CleanupListener();
        }
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        for (var i = 0; i + 1 < e.BytesRecorded; i += 2)
        {
            WakeWordRuntime? runtime;

            lock (_syncRoot)
            {
                if (_isDisposed || _isSuspended || _runtime is null)
                    return;

                _frame[_frameOffset++] = BitConverter.ToInt16(e.Buffer, i);

                if (_frameOffset < FrameLength)
                    continue;

                _frameOffset = 0;
                runtime = _runtime;
            }

            if (runtime.Process(_frame) >= 0)
            {
                SuspendAfterDetection();
                return;
            }
        }
    }

    private void SuspendAfterDetection()
    {
        lock (_syncRoot)
        {
            if (_isDisposed || _isSuspended)
                return;

            _isSuspended = true;
        }

        Task.Run(() =>
        {
            StopListening();
            WakeWordDetected?.Invoke();
        });
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        if (e.Exception is not null)
            Debug.WriteLine($"Wake-word listener stopped with error: {e.Exception}");

        CleanupListener();
    }

    private void CleanupListener()
    {
        lock (_syncRoot)
        {
            if (_waveIn is not null)
            {
                _waveIn.DataAvailable -= OnDataAvailable;
                _waveIn.RecordingStopped -= OnRecordingStopped;
                _waveIn.Dispose();
                _waveIn = null;
            }

            _runtime?.Dispose();
            _runtime = null;
            _frameOffset = 0;
            _isListening = false;
        }
    }

    private static int FindInputDeviceNumber(string? deviceName)
    {
        if (string.IsNullOrWhiteSpace(deviceName))
            return 0;

        return Enumerable
            .Range(0, WaveInEvent.DeviceCount)
            .FirstOrDefault(index => WaveInEvent.GetCapabilities(index).ProductName == deviceName);
    }
}
