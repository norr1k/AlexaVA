using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace Alexa.Services;

/// <summary>
/// Работа с аудиоустройствами и тестовой записью микрофона
/// </summary>
public static class AudioDeviceService
{
    public const int SampleRate = 16000;
    public const int BitsPerSample = 16;
    public const int Channels = 1;

    /// <summary>
    /// Формат записи
    /// </summary>
    public static readonly WaveFormat RecordingFormat = new(SampleRate, BitsPerSample, Channels);

    #region Device discovery

    /// <summary>
    /// Возвращает список доступных устройств ввода звука
    /// </summary>
    public static IReadOnlyList<string> GetInputDevices()
    {
        var devices = new List<string>();

        for (var i = 0; i < WaveInEvent.DeviceCount; i++)
            devices.Add(WaveInEvent.GetCapabilities(i).ProductName);

        return devices.Count > 0 ? devices : ["Системное устройство ввода"];
    }

    /// <summary>
    /// Возвращает список доступных устройств вывода звука
    /// </summary>
    public static IReadOnlyList<string> GetOutputDevices()
    {
        var devices = new List<string>();

        using var enumerator = new MMDeviceEnumerator();
        devices.AddRange(enumerator
            .EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active)
            .Select(device => device.FriendlyName));

        return devices.Count > 0 ? devices : ["Системное устройство вывода"];
    }

    #endregion

    #region Microphone test

    /// <summary>
    /// 3-х секундный тест микрофона
    /// </summary>
    public static async Task RecordAndPlayEchoAsync(
        string filePath,
        string? inputDeviceName,
        string? outputDeviceName,
        double sensitivity,
        double recordingVolume,
        double playbackVolume,
        CancellationToken cancellationToken = default)
    {
        await RecordAsync(filePath, inputDeviceName, sensitivity, recordingVolume, TimeSpan.FromSeconds(3), cancellationToken);
        await PlayAsync(filePath, outputDeviceName, playbackVolume, cancellationToken);
    }

    public static async Task PlayFileAsync(
        string filePath,
        string? outputDeviceName,
        double playbackVolume,
        CancellationToken cancellationToken = default)
    {
        await PlayAsync(filePath, outputDeviceName, playbackVolume, cancellationToken);
    }

    /// <summary>
    /// Записывает WAV-файл указанной длительности с выбранного устройства ввода
    /// </summary>
    private static async Task RecordAsync(
        string filePath,
        string? inputDeviceName,
        double sensitivity,
        double recordingVolume,
        TimeSpan duration,
        CancellationToken cancellationToken)
    {
        var recordingFinished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        using var waveIn = new WaveInEvent
        {
            DeviceNumber = FindInputDeviceNumber(inputDeviceName),
            WaveFormat = RecordingFormat,
            BufferMilliseconds = 50
        };
        await using var writer = new WaveFileWriter(filePath, RecordingFormat);

        waveIn.DataAvailable += (_, e) =>
        {
            // NAudio отдает PCM-буфер, поэтому чувствительность применяем до записи в WAV
            var buffer = ApplyRecordingGain(e.Buffer, e.BytesRecorded, sensitivity, recordingVolume);
            writer.Write(buffer, 0, buffer.Length);
            writer.Flush();
        };
        waveIn.RecordingStopped += (_, e) =>
        {
            if (e.Exception is not null)
                recordingFinished.TrySetException(e.Exception);
            else
                recordingFinished.TrySetResult();
        };

        using var registration = cancellationToken.Register(() =>
        {
            waveIn.StopRecording();
            recordingFinished.TrySetCanceled(cancellationToken);
        });

        waveIn.StartRecording();

        try
        {
            await Task.Delay(duration, cancellationToken);
        }
        finally
        {
            waveIn.StopRecording();
        }
        
        await recordingFinished.Task;
    }

    /// <summary>
    /// Воспроизводит WAV-файл
    /// </summary>
    private static async Task PlayAsync(
        string filePath,
        string? outputDeviceName,
        double playbackVolume,
        CancellationToken cancellationToken)
    {
        var playbackFinished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        using var reader = new AudioFileReader(filePath);
        reader.Volume = (float)(Math.Clamp(playbackVolume, 0, 100) / 100.0);
        using var outputDevice = FindOutputDevice(outputDeviceName);
        using var waveOut = new WasapiOut(outputDevice, AudioClientShareMode.Shared, false, 100);

        waveOut.PlaybackStopped += (_, e) =>
        {
            if (e.Exception is not null)
                playbackFinished.TrySetException(e.Exception);
            else
                playbackFinished.TrySetResult();
        };

        using var registration = cancellationToken.Register(() =>
        {
            waveOut.Stop();
            playbackFinished.TrySetCanceled(cancellationToken);
        });

        waveOut.Init(reader);
        waveOut.Play();

        // Ждем фактического окончания воспроизведения, чтобы вызывающий код мог безопасно удалить файл.
        await playbackFinished.Task;
    }

    #endregion

    #region Device selection helpers

    /// <summary>
    /// Находит индекс устройства ввода по сохраненному имени или возвращает системное устройство по умолчанию
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
    /// Находит устройство вывода CoreAudio по имени или возвращает мультимедийное устройство по умолчанию
    /// </summary>
    private static MMDevice FindOutputDevice(string? deviceName)
    {
        var enumerator = new MMDeviceEnumerator();

        if (!string.IsNullOrWhiteSpace(deviceName))
        {
            var device = enumerator
                .EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active)
                .FirstOrDefault(endpoint => endpoint.FriendlyName == deviceName);

            if (device is not null)
                return device;
        }

        return enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
    }

    #endregion

    #region PCM processing

    /// <summary>
    /// Применяет чувствительность записи к 16-битному PCM-буферу
    /// </summary>
    private static byte[] ApplyRecordingGain(byte[] source, int bytesRecorded, double sensitivity, double recordingVolume)
    {
        // 50% является нейтральным уровнем. Ниже сигнал ослабляется, выше усиливается
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
