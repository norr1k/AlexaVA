using System;
using System.Threading;
using System.Threading.Tasks;

namespace Alexa.Services;

/// <summary>
/// Фоново проверяет доступность сервера и выполняет переподключение с экспоненциальной задержкой.
/// </summary>
public sealed class ServerConnectionService : IDisposable
{
    private static readonly TimeSpan ConnectedCheckInterval = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan InitialReconnectDelay = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan MaxReconnectDelay = TimeSpan.FromSeconds(30);

    private CancellationTokenSource? _connectionCts;
    private ServerConnectionState? _lastPublishedState;
    private string? _lastPublishedText;

    public event Action<ServerConnectionSnapshot>? StateChanged;

    /// <summary>
    /// Запускает новый цикл проверки сервера, отменяя предыдущий.
    /// </summary>
    public void Start(AppSettings settings, string authorizationToken)
    {
        Stop();

        _lastPublishedState = null;
        _lastPublishedText = null;
        _connectionCts = new CancellationTokenSource();
        _ = RunConnectionLoopAsync(settings, authorizationToken, _connectionCts.Token);
    }

    /// <summary>
    /// Останавливает текущий цикл проверки сервера.
    /// </summary>
    public void Stop()
    {
        _connectionCts?.Cancel();
        _connectionCts?.Dispose();
        _connectionCts = null;
    }

    /// <summary>
    /// Останавливает сервис и освобождает токен отмены.
    /// </summary>
    public void Dispose()
    {
        Stop();
    }

    private async Task RunConnectionLoopAsync(
        AppSettings settings,
        string authorizationToken,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(settings.ServerAddress))
        {
            Publish(ServerConnectionState.NotConfigured, "Сервер не настроен", false, null);
            return;
        }

        var reconnectDelay = InitialReconnectDelay;

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                using var apiClient = new AlexaApiClient(
                    settings.ServerAddress,
                    settings.ServerPort,
                    authorizationToken);

                await apiClient.CheckHealthAsync(cancellationToken);
                reconnectDelay = InitialReconnectDelay;
                Publish(ServerConnectionState.Connected, "Сервер подключен", true, null);
                await Task.Delay(ConnectedCheckInterval, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch
            {
                Publish(
                    ServerConnectionState.Reconnecting,
                    $"Нет связи с сервером. Повтор через {reconnectDelay.TotalSeconds:0} с",
                    false,
                    reconnectDelay);

                try
                {
                    await Task.Delay(reconnectDelay, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    return;
                }

                reconnectDelay = TimeSpan.FromSeconds(Math.Min(
                    reconnectDelay.TotalSeconds * 2,
                    MaxReconnectDelay.TotalSeconds));
            }
        }
    }

    private void Publish(
        ServerConnectionState state,
        string text,
        bool isConnected,
        TimeSpan? reconnectDelay)
    {
        StateChanged?.Invoke(new ServerConnectionSnapshot(state, text, isConnected, reconnectDelay));
        if (_lastPublishedState != state || _lastPublishedText != text)
        {
            AppLogger.Info($"Server connection update: {state}; {text}");
            _lastPublishedState = state;
            _lastPublishedText = text;
        }
    }
}

/// <summary>
/// Состояние фонового подключения к серверу.
/// </summary>
public enum ServerConnectionState
{
    NotConfigured,
    Connected,
    Reconnecting
}

/// <summary>
/// Снимок состояния подключения, передаваемый в UI и tray.
/// </summary>
public sealed record ServerConnectionSnapshot(
    ServerConnectionState State,
    string Text,
    bool IsConnected,
    TimeSpan? ReconnectDelay);
