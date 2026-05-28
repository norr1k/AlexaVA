using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Alexa.ViewModel;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Alexa;

/// <summary>
/// Окно настроек приложения. Логика настроек находится в SettingsViewModel.
/// </summary>
public partial class SettingsWindow : Window
{
    public static event Action<bool>? HotkeyCaptureChanged;

    private readonly HashSet<Key> _pressedHotkeyKeys = new();
    private HotkeyCaptureTarget _captureTarget = HotkeyCaptureTarget.None;

    /// <summary>
    /// Инициализирует XAML-разметку окна настроек.
    /// </summary>
    public SettingsWindow()
    {
        InitializeComponent();
        ConfigureHotkeyTextBox(ShowWindowHotkeyTextBox);
        ConfigureHotkeyTextBox(VoiceRecordHotkeyTextBox);
    }

    private void ConfigureHotkeyTextBox(TextBox textBox)
    {
        textBox.GotFocus += HotkeyTextBox_OnGotFocus;
        textBox.LostFocus += HotkeyTextBox_OnLostFocus;
        textBox.AddHandler(KeyDownEvent, HotkeyTextBox_OnKeyDown, RoutingStrategies.Tunnel);
        textBox.AddHandler(KeyUpEvent, HotkeyTextBox_OnKeyUp, RoutingStrategies.Tunnel);
        textBox.AddHandler(TextInputEvent, HotkeyTextBox_OnTextInput, RoutingStrategies.Tunnel);
        textBox.AddHandler(PointerPressedEvent, HotkeyTextBox_OnPointerPressed, RoutingStrategies.Tunnel);
    }

    private void HotkeyTextBox_OnGotFocus(object? sender, GotFocusEventArgs e)
    {
        BeginHotkeyCapture(sender as TextBox);
    }

    private void HotkeyTextBox_OnLostFocus(object? sender, RoutedEventArgs e)
    {
        EndHotkeyCapture();
    }

    private void HotkeyTextBox_OnTextInput(object? sender, TextInputEventArgs e)
    {
        e.Handled = true;
    }

    private void HotkeyTextBox_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is TextBox textBox)
        {
            textBox.Focus();
            BeginHotkeyCapture(textBox);
        }

        e.Handled = true;
    }

    private void HotkeyTextBox_OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (_captureTarget == HotkeyCaptureTarget.None)
            BeginHotkeyCapture(sender as TextBox);

        e.Handled = true;

        AddModifierKeys(e.KeyModifiers);
        var key = NormalizeKey(e.Key);
        if (!IsIgnoredKey(key))
            _pressedHotkeyKeys.Add(key);

        UpdateCapturedHotkey();
    }

    private void HotkeyTextBox_OnKeyUp(object? sender, KeyEventArgs e)
    {
        e.Handled = true;

        var key = NormalizeKey(e.Key);
        _pressedHotkeyKeys.Remove(key);

        if (!HasModifier(e.KeyModifiers, KeyModifiers.Control))
        {
            _pressedHotkeyKeys.Remove(Key.LeftCtrl);
            _pressedHotkeyKeys.Remove(Key.RightCtrl);
        }

        if (!HasModifier(e.KeyModifiers, KeyModifiers.Shift))
        {
            _pressedHotkeyKeys.Remove(Key.LeftShift);
            _pressedHotkeyKeys.Remove(Key.RightShift);
        }

        if (!HasModifier(e.KeyModifiers, KeyModifiers.Alt))
        {
            _pressedHotkeyKeys.Remove(Key.LeftAlt);
            _pressedHotkeyKeys.Remove(Key.RightAlt);
        }

        if (!HasModifier(e.KeyModifiers, KeyModifiers.Meta))
        {
            _pressedHotkeyKeys.Remove(Key.LWin);
            _pressedHotkeyKeys.Remove(Key.RWin);
        }

        if (_pressedHotkeyKeys.Count == 0)
            EndHotkeyCapture();
    }

    private void BeginHotkeyCapture(TextBox? textBox)
    {
        _pressedHotkeyKeys.Clear();
        _captureTarget = textBox?.Name switch
        {
            "ShowWindowHotkeyTextBox" => HotkeyCaptureTarget.ShowWindow,
            "VoiceRecordHotkeyTextBox" => HotkeyCaptureTarget.VoiceRecord,
            _ => HotkeyCaptureTarget.None
        };

        if (_captureTarget != HotkeyCaptureTarget.None)
            HotkeyCaptureChanged?.Invoke(true);
    }

    private void EndHotkeyCapture()
    {
        _pressedHotkeyKeys.Clear();
        if (_captureTarget != HotkeyCaptureTarget.None)
            HotkeyCaptureChanged?.Invoke(false);

        _captureTarget = HotkeyCaptureTarget.None;
    }

    private void UpdateCapturedHotkey()
    {
        if (DataContext is not SettingsViewModel viewModel || _captureTarget == HotkeyCaptureTarget.None)
            return;

        var hotkeyText = FormatHotkey(_pressedHotkeyKeys);
        if (string.IsNullOrWhiteSpace(hotkeyText))
            return;

        if (_captureTarget == HotkeyCaptureTarget.ShowWindow)
            viewModel.ShowWindowHotkey = hotkeyText;
        else if (_captureTarget == HotkeyCaptureTarget.VoiceRecord)
            viewModel.VoiceRecordHotkey = hotkeyText;
    }

    private void AddModifierKeys(KeyModifiers modifiers)
    {
        if (modifiers.HasFlag(KeyModifiers.Control))
            _pressedHotkeyKeys.Add(Key.LeftCtrl);
        if (modifiers.HasFlag(KeyModifiers.Shift))
            _pressedHotkeyKeys.Add(Key.LeftShift);
        if (modifiers.HasFlag(KeyModifiers.Alt))
            _pressedHotkeyKeys.Add(Key.LeftAlt);
        if (modifiers.HasFlag(KeyModifiers.Meta))
            _pressedHotkeyKeys.Add(Key.LWin);
    }

    private static string FormatHotkey(IReadOnlyCollection<Key> keys)
    {
        var parts = new List<string>();

        if (keys.Contains(Key.LeftCtrl) || keys.Contains(Key.RightCtrl))
            parts.Add("Ctrl");
        if (keys.Contains(Key.LeftShift) || keys.Contains(Key.RightShift))
            parts.Add("Shift");
        if (keys.Contains(Key.LeftAlt) || keys.Contains(Key.RightAlt))
            parts.Add("Alt");
        if (keys.Contains(Key.LWin) || keys.Contains(Key.RWin))
            parts.Add("Win");

        parts.AddRange(keys
            .Where(key => !IsModifierKey(key))
            .OrderBy(key => key.ToString())
            .Select(FormatKey));

        return string.Join("+", parts);
    }

    private static string FormatKey(Key key)
    {
        return key switch
        {
            Key.D0 => "0",
            Key.D1 => "1",
            Key.D2 => "2",
            Key.D3 => "3",
            Key.D4 => "4",
            Key.D5 => "5",
            Key.D6 => "6",
            Key.D7 => "7",
            Key.D8 => "8",
            Key.D9 => "9",
            _ => key.ToString()
        };
    }

    private static Key NormalizeKey(Key key)
    {
        return key == Key.System ? Key.LeftAlt : key;
    }

    private static bool IsModifierKey(Key key)
    {
        return key is Key.LeftCtrl or Key.RightCtrl or
            Key.LeftShift or Key.RightShift or
            Key.LeftAlt or Key.RightAlt or
            Key.LWin or Key.RWin;
    }

    private static bool IsIgnoredKey(Key key)
    {
        return key is Key.None or Key.System;
    }

    private static bool HasModifier(KeyModifiers modifiers, KeyModifiers modifier)
    {
        return modifiers.HasFlag(modifier);
    }

    private enum HotkeyCaptureTarget
    {
        None,
        ShowWindow,
        VoiceRecord
    }

    protected override void OnClosed(EventArgs e)
    {
        EndHotkeyCapture();
        base.OnClosed(e);
    }
}
