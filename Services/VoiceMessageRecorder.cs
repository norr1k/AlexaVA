using System;
using System.Linq;
using System.Threading.Tasks;
using NAudio.Wave;

namespace Alexa.Services;

/// <summary>
/// Управляет записью одного голосового сообщения с ручным стартом и остановкой
/// </summary>
public sealed class VoiceMessageRecorder : IDisposable
{
    private readonly TaskCompletionSource _recordingStopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly WaveInEvent _waveIn;
    private readonly WaveFileWriter _writer;
    private readonly double _sensitivity;
    private readonly double _recordingVolume;
    private bool _isDisposed;

    #region Initialization

    /// <summary>
    /// Создает рекордер для записи WAV-файла с выбранного устройства ввода.
    /// </summary>
    public VoiceMessageRecorder(string filePath, string? inputDeviceName, double sensitivity, double recordingVolume)
    {
        _sensitivity = sensitivity;
        _recordingVolume = recordingVolume;

        _waveIn = new WaveInEvent
        {
            DeviceNumber = FindInputDeviceNumber(inputDeviceName),
            WaveFormat = AudioDeviceService.RecordingFormat,
            BufferMilliseconds = 50
        };
        _writer = new WaveFileWriter(filePath, AudioDeviceService.RecordingFormat);

        _waveIn.DataAvailable += OnDataAvailable;
        _waveIn.RecordingStopped += OnRecordingStopped;
    }

    #endregion

    #region Recording control

    /// <summary>
    /// Запускает запись голосового сообщения
    /// </summary>
    public void Start()
    {
        _waveIn.StartRecording();
    }

    /// <summary>
    /// Останавливает запись 
    /// </summary>
    public async Task StopAsync()
    {
        if (_isDisposed)
            return;

        _waveIn.StopRecording();
        await _recordingStopped.Task;
    }

    /// <summary>
    /// Освобождает объекты и отписывает обработчики 
    /// </summary>
    public void Dispose()
    {
        if (_isDisposed)
            return;

        _isDisposed = true;
        _waveIn.DataAvailable -= OnDataAvailable;
        _waveIn.RecordingStopped -= OnRecordingStopped;
        _writer.Dispose();
        _waveIn.Dispose();
    }

    #endregion

    #region Event handlers

    /// <summary>
    /// Получает очередной PCM-буфер от микрофона, применяет чувствительность и пишет его в .wav
    /// </summary>
    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        var buffer = ApplyRecordingGain(e.Buffer, e.BytesRecorded, _sensitivity, _recordingVolume);
        _writer.Write(buffer, 0, buffer.Length);
        _writer.Flush();
    }

    /// <summary>
    /// Завершает Task ожидания остановки записи и пробрасывает ошибку драйвера, если такая была
    /// </summary>
    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        if (e.Exception is not null)
            _recordingStopped.TrySetException(e.Exception);
        else
            _recordingStopped.TrySetResult();
    }

    #endregion

    #region Helpers

    /// <summary>
    /// Находит устройство ввода по имени или возвращает устройство по умолчанию
    /// </summary>
    private static int FindInputDeviceNumber(string? deviceName)
    {
        if (string.IsNullOrWhiteSpace(deviceName))
            return 0;

        for (var i = 0; i < WaveInEvent.DeviceCount; i++)
        {
            if (WaveInEvent.GetCapabilities(i).ProductName == deviceName)
                return i;
        }

        return 0;
    }

    /// <summary>
    /// Применяет чувствительность к PCM-буферу
    /// </summary>
    private static byte[] ApplyRecordingGain(byte[] source, int bytesRecorded, double sensitivity, double recordingVolume)
    {
        var multiplier = Math.Clamp(sensitivity, 0, 100) / 50.0 * (Math.Clamp(recordingVolume, 0, 100) / 100.0);
        if (Math.Abs(multiplier - 1.0) < 0.001)
            return source.Take(bytesRecorded).ToArray();

        var buffer = new byte[bytesRecorded];
        for (var i = 0; i + 1 < bytesRecorded; i += 2)
        {
            var sample = BitConverter.ToInt16(source, i);
            var adjusted = Math.Clamp(sample * multiplier, short.MinValue, short.MaxValue);
            var bytes = BitConverter.GetBytes((short)adjusted);
            buffer[i] = bytes[0];
            buffer[i + 1] = bytes[1];
        }

        return buffer;
    }

    #endregion
}
