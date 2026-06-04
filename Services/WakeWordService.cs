using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NanoWakeWord;

namespace Alexa.Services;

/// <summary>
/// Слушает микрофон через WASAPI и запускает событие при распознавании wake-word.
/// </summary>
public sealed class WakeWordService : IDisposable
{
    private const string WakeWordModel = "alexa_v0.1";
    private const float WakeWordThreshold = 0.5f;
    private const int FrameLength = 512;
    private const int WakeWordSampleRate = 16000;

    private readonly object _syncRoot = new();
    private readonly short[] _frame = new short[FrameLength];
    private WakeWordRuntime? _runtime;
    private WasapiCapture? _capture;
    private string? _inputDeviceName;
    private int _frameOffset;
    private double _resampleAccumulator;
    private bool _isDisposed;
    private bool _isSuspended;
    private bool _isListening;
    private bool _notifyAfterStop;

    public event Action? WakeWordDetected;

    /// <summary>
    /// Применяет настройки устройства ввода и перезапускает прослушивание при необходимости.
    /// </summary>
    public void Configure(AppSettings settings)
    {
        lock (_syncRoot)
        {
            _inputDeviceName = settings.SelectedInputDevice;
        }

        RestartIfAllowed();
    }

    /// <summary>
    /// Приостанавливает или возобновляет прослушивание wake-word.
    /// </summary>
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

    /// <summary>
    /// Останавливает прослушивание и освобождает аудио-ресурсы.
    /// </summary>
    public void Dispose()
    {
        lock (_syncRoot)
        {
            _isDisposed = true;
            _notifyAfterStop = false;
        }

        StopListening();
    }

    /// <summary>
    /// Перезапускает listener, если сервис не выключен и не находится в паузе.
    /// </summary>
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

    /// <summary>
    /// Создает NanoWakeWord runtime и начинает захват аудио с выбранного микрофона.
    /// </summary>
    private void StartListening()
    {
        lock (_syncRoot)
        {
            if (_isDisposed || _isSuspended || _isListening)
                return;

            try
            {
                _frameOffset = 0;
                _resampleAccumulator = 0;
                _notifyAfterStop = false;
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

                var inputDevice = FindInputDevice(_inputDeviceName);
                _capture = new WasapiCapture(inputDevice);
                _capture.DataAvailable += OnDataAvailable;
                _capture.RecordingStopped += OnRecordingStopped;
                _capture.StartRecording();
                _isListening = true;
                AppLogger.Info($"Wake-word listener started. Model='{WakeWordModel}'; Threshold={WakeWordThreshold}; InputDevice='{inputDevice.FriendlyName}'");
            }
            catch (Exception ex)
            {
                AppLogger.Error(ex, "Wake-word listener failed to start");
                Debug.WriteLine($"Wake-word listener failed to start: {ex}");
                CleanupListener();
            }
        }
    }

    /// <summary>
    /// Останавливает текущий WASAPI capture; уведомление о wake-word отправляется после полной остановки.
    /// </summary>
    private void StopListening()
    {
        WasapiCapture? capture;

        lock (_syncRoot)
        {
            capture = _capture;
        }

        if (capture is null)
        {
            NotifyIfNeeded(CleanupListener());
            return;
        }

        try
        {
            capture.StopRecording();
        }
        catch (Exception ex)
        {
            AppLogger.Error(ex, "Wake-word listener failed to stop");
            Debug.WriteLine($"Wake-word listener failed to stop: {ex}");
            NotifyIfNeeded(CleanupListener());
        }
    }

    /// <summary>
    /// Получает очередной буфер WASAPI и передает его в обработку wake-word.
    /// </summary>
    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        WasapiCapture? capture;

        lock (_syncRoot)
        {
            if (_isDisposed || _isSuspended || _runtime is null)
                return;

            capture = _capture;
        }

        if (capture is null)
            return;

        ProcessCaptureBuffer(e.Buffer, e.BytesRecorded, capture.WaveFormat);
    }

    /// <summary>
    /// Преобразует входной буфер в mono samples и понижает частоту до 16 kHz для NanoWakeWord.
    /// </summary>
    private void ProcessCaptureBuffer(byte[] buffer, int bytesRecorded, WaveFormat waveFormat)
    {
        var bytesPerSample = waveFormat.BitsPerSample / 8;
        if (bytesPerSample <= 0 || waveFormat.Channels <= 0 || waveFormat.SampleRate <= 0)
            return;

        var blockAlign = bytesPerSample * waveFormat.Channels;
        var sampleFrames = bytesRecorded / blockAlign;

        for (var sampleFrame = 0; sampleFrame < sampleFrames; sampleFrame++)
        {
            var monoSample = ReadMonoSample(buffer, sampleFrame * blockAlign, waveFormat, bytesPerSample);

            _resampleAccumulator += WakeWordSampleRate;
            if (_resampleAccumulator < waveFormat.SampleRate)
                continue;

            _resampleAccumulator -= waveFormat.SampleRate;
            if (ProcessWakeWordSample((short)Math.Clamp(monoSample * short.MaxValue, short.MinValue, short.MaxValue)))
                return;
        }
    }

    /// <summary>
    /// Накапливает фрейм фиксированной длины и отправляет его в NanoWakeWord runtime.
    /// </summary>
    private bool ProcessWakeWordSample(short sample)
    {
        WakeWordRuntime? runtime;

        lock (_syncRoot)
        {
            if (_isDisposed || _isSuspended || _runtime is null)
                return true;

            _frame[_frameOffset++] = sample;

            if (_frameOffset < FrameLength)
                return false;

            _frameOffset = 0;
            runtime = _runtime;
        }

        if (runtime.Process(_frame) < 0)
            return false;

        AppLogger.Info("Wake-word detected by runtime");
        SuspendAfterDetection();
        return true;
    }

    /// <summary>
    /// Ставит listener на паузу после детекта и инициирует остановку захвата микрофона.
    /// </summary>
    private void SuspendAfterDetection()
    {
        lock (_syncRoot)
        {
            if (_isDisposed || _isSuspended)
                return;

            _isSuspended = true;
            _notifyAfterStop = true;
        }

        Task.Run(StopListening);
    }

    /// <summary>
    /// Завершает cleanup после остановки WASAPI capture.
    /// </summary>
    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        if (e.Exception is not null)
        {
            AppLogger.Error(e.Exception, "Wake-word listener stopped with error");
            Debug.WriteLine($"Wake-word listener stopped with error: {e.Exception}");
        }

        NotifyIfNeeded(CleanupListener());
    }

    /// <summary>
    /// Отписывает обработчики, освобождает capture/runtime и возвращает признак необходимости уведомить о детекте.
    /// </summary>
    private bool CleanupListener()
    {
        lock (_syncRoot)
        {
            if (_capture is not null)
            {
                _capture.DataAvailable -= OnDataAvailable;
                _capture.RecordingStopped -= OnRecordingStopped;
                _capture.Dispose();
                _capture = null;
            }

            _runtime?.Dispose();
            _runtime = null;
            _frameOffset = 0;
            _resampleAccumulator = 0;
            _isListening = false;
            AppLogger.Info("Wake-word listener resources cleaned up");

            var shouldNotify = _notifyAfterStop && !_isDisposed;
            _notifyAfterStop = false;
            return shouldNotify;
        }
    }

    /// <summary>
    /// Отправляет событие распознавания wake-word, если оно было отложено до остановки capture.
    /// </summary>
    private void NotifyIfNeeded(bool shouldNotify)
    {
        if (shouldNotify)
            WakeWordDetected?.Invoke();
    }

    /// <summary>
    /// Усредняет каналы входного аудио в один mono sample.
    /// </summary>
    private static float ReadMonoSample(byte[] buffer, int offset, WaveFormat waveFormat, int bytesPerSample)
    {
        var sample = 0f;

        for (var channel = 0; channel < waveFormat.Channels; channel++)
            sample += ReadSample(buffer, offset + channel * bytesPerSample, waveFormat);

        return sample / waveFormat.Channels;
    }

    /// <summary>
    /// Читает один sample из PCM или float WASAPI-буфера и нормализует его в диапазон -1..1.
    /// </summary>
    private static float ReadSample(byte[] buffer, int offset, WaveFormat waveFormat)
    {
        return waveFormat.Encoding switch
        {
            WaveFormatEncoding.IeeeFloat when waveFormat.BitsPerSample == 32 => BitConverter.ToSingle(buffer, offset),
            WaveFormatEncoding.Pcm when waveFormat.BitsPerSample == 16 => BitConverter.ToInt16(buffer, offset) / 32768f,
            WaveFormatEncoding.Pcm when waveFormat.BitsPerSample == 24 => ReadPcm24(buffer, offset) / 8388608f,
            WaveFormatEncoding.Pcm when waveFormat.BitsPerSample == 32 => BitConverter.ToInt32(buffer, offset) / 2147483648f,
            _ => 0f
        };
    }

    /// <summary>
    /// Читает 24-bit PCM sample с расширением знака до Int32.
    /// </summary>
    private static int ReadPcm24(byte[] buffer, int offset)
    {
        var value = buffer[offset] | (buffer[offset + 1] << 8) | (buffer[offset + 2] << 16);
        return (value & 0x800000) != 0 ? value | unchecked((int)0xFF000000) : value;
    }

    /// <summary>
    /// Находит WASAPI-устройство ввода по имени или возвращает системный микрофон по умолчанию.
    /// </summary>
    private static MMDevice FindInputDevice(string? deviceName)
    {
        var enumerator = new MMDeviceEnumerator();
        var devices = enumerator
            .EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active)
            .ToArray();

        if (!string.IsNullOrWhiteSpace(deviceName))
        {
            var device = devices.FirstOrDefault(endpoint =>
                string.Equals(endpoint.FriendlyName, deviceName, StringComparison.OrdinalIgnoreCase) ||
                endpoint.FriendlyName.Contains(deviceName, StringComparison.OrdinalIgnoreCase) ||
                deviceName.Contains(endpoint.FriendlyName, StringComparison.OrdinalIgnoreCase));

            if (device is not null)
                return device;
        }

        return enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Multimedia);
    }
}
