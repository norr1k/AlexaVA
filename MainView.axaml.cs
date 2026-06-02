using System;
using System.Threading.Tasks;
using Alexa.Models;
using Alexa.ViewModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Alexa;

/// <summary>
/// Главное окно чата. Отвечает за window chrome, открытие настроек и поведение скрытия в трей
/// </summary>
public partial class MainView : Window
{
    private SettingsWindow? _settingsWindow;
    private bool _isApplicationExitRequested;

    public event Action<bool>? VoiceRecordingStateChanged;

    #region Initialization

    /// <summary>
    /// Инициализирует XAML-разметку главного окна
    /// </summary>
    public MainView()
    {
        InitializeComponent();

        if (DataContext is MainViewModel viewModel)
            viewModel.VoiceRecordingStateChanged += OnVoiceRecordingStateChanged;
    }

    #endregion

    #region Window chrome handlers

    /// <summary>
    /// Запускает перетаскивание окна за кастомную верхнюю панель
    /// </summary>
    private void HeaderGrid_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        BeginMoveDrag(e);
    }

    /// <summary>
    /// Скрывает приложение в трей вместо полного закрытия процесса
    /// </summary>
    private void CloseButton_OnClick(object? sender, RoutedEventArgs e)
    {
        Hide();
    }

    /// <summary>
    /// Сворачивает окно в панель задач
    /// </summary>
    private void Button_OnClick(object? sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    /// <summary>
    /// Переключает воспроизведение голосового сообщения при клике по пузырю сообщения.
    /// </summary>
    private async void ChatMessage_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control { DataContext: ChatMessage message } ||
            DataContext is not MainViewModel viewModel)
        {
            return;
        }

        e.Handled = true;
        await viewModel.ToggleMessageAudioPlaybackAsync(message);
    }

    /// <summary>
    /// При нажатии Enter без модификаторов отправляет сообщение в чат 
    /// </summary>
    private void MessageTextBox_OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || e.KeyModifiers != KeyModifiers.None)
        {
            return;
        }

        if (sender is TextBox textBox && DataContext is MainViewModel viewModel)
        {
            e.Handled = true;
            viewModel.Message = textBox.Text ?? string.Empty;

            if (viewModel.SendMessageCommand.CanExecute(null))
                viewModel.SendMessageCommand.Execute(null);
        }
    }

    #endregion

    #region Settings

    /// <summary>
    /// Открывает окно настроек по нажатию кнопки в заголовке
    /// </summary>
    private void SettingsButton_OnClick(object? sender, RoutedEventArgs e)
    {
        ShowSettingsWindow();
    }

    /// <summary>
    /// Показывает окно настроек, переиспользуя уже открытый экземпляр
    /// </summary>
    public void ShowSettingsWindow()
    {
        if (_settingsWindow is { IsVisible: true })
        {
            _settingsWindow.Activate();
            return;
        }

        _settingsWindow = new SettingsWindow();
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;

        // Если чат видим, настройки открываются как дочернее окно. Иначе открываются отдельно из трея
        if (IsVisible)
            _settingsWindow.Show(this);
        else
            _settingsWindow.Show();
    }

    #endregion

    #region Tray-visible window control

    /// <summary>
    /// Показывает и активирует окно чата из системного трея
    /// </summary>
    public void ShowChatWindow()
    {
        Show();
        WindowState = WindowState.Normal;
        Topmost = true;
        Topmost = false;
        Activate();
    }

    /// <summary>
    /// Переключает запись голосового сообщения из глобальной горячей клавиши.
    /// </summary>
    public async Task ToggleVoiceRecordingFromHotkeyAsync()
    {
        if (DataContext is MainViewModel viewModel)
            await viewModel.ToggleVoiceRecordingFromHotkeyAsync();
    }

    /// <summary>
    /// Запускает запись голосового сообщения после срабатывания wake-word.
    /// </summary>
    public async Task<bool> StartVoiceRecordingFromWakeWordAsync()
    {
        if (DataContext is MainViewModel viewModel)
            return await viewModel.StartVoiceRecordingFromWakeWordAsync();

        return false;
    }

    /// <summary>
    /// Обновляет текст состояния подключения к серверу в верхней панели окна.
    /// </summary>
    public void SetServerConnectionStatus(string statusText)
    {
        if (DataContext is MainViewModel viewModel)
            viewModel.ServerConnectionStatusText = statusText;
    }

    /// <summary>
    /// Выполняет полный выход: чистит временные аудиофайлы и закрывает окно
    /// </summary>
    public async Task ExitApplicationAsync()
    {
        _isApplicationExitRequested = true;

        if (DataContext is MainViewModel viewModel)
            await viewModel.CleanupSessionAudioFilesAsync();

        _settingsWindow?.Close();
        Close();
    }

    /// <summary>
    /// Перехватывает системное закрытие окна: обычное закрытие скрывает окно, реальный выход разрешен только из трея
    /// </summary>
    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (_isApplicationExitRequested)
        {
            base.OnClosing(e);
            return;
        }

        e.Cancel = true;
        Hide();
    }

    /// <summary>
    /// Передает наружу изменение состояния записи для сервисов приложения.
    /// </summary>
    private void OnVoiceRecordingStateChanged(bool isVoiceRecording)
    {
        VoiceRecordingStateChanged?.Invoke(isVoiceRecording);
    }

    #endregion
}
