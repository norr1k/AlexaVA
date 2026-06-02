using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Alexa.Services;

/// <summary>
/// Действия, которые могут запускаться глобальными горячими клавишами.
/// </summary>
public enum GlobalHotkeyAction
{
    ShowWindow,
    ToggleVoiceRecording
}

/// <summary>
/// Регистрирует глобальные хоткеи через low-level keyboard hook Windows.
/// </summary>
public sealed class GlobalHotkeyService : IDisposable
{
    private const int WhKeyboardLl = 13;
    private const int WmKeyDown = 0x0100;
    private const int WmSysKeyDown = 0x0104;
    private const int WmKeyUp = 0x0101;
    private const int WmSysKeyUp = 0x0105;

    private const int VkControl = 0x11;
    private const int VkShift = 0x10;
    private const int VkMenu = 0x12;
    private const int VkLWin = 0x5B;
    private const int VkRWin = 0x5C;

    private readonly LowLevelKeyboardProc _keyboardProc;
    private readonly List<RegisteredHotkey> _hotkeys = new();
    private readonly HashSet<int> _pressedKeys = new();
    private bool _isSuspended;
    private nint _hookHandle;

    public event Action<GlobalHotkeyAction>? HotkeyPressed;

    /// <summary>
    /// Создает сервис и сохраняет делегат callback, чтобы его не собрал GC.
    /// </summary>
    public GlobalHotkeyService()
    {
        _keyboardProc = KeyboardHookCallback;
    }

    /// <summary>
    /// Применяет сохраненные настройки хоткеев и запускает или останавливает hook.
    /// </summary>
    public void Configure(AppSettings settings)
    {
        _hotkeys.Clear();
        _pressedKeys.Clear();

        if (!OperatingSystem.IsWindows() || !settings.HotkeysEnabled)
        {
            Stop();
            return;
        }

        if (settings.ShowWindowHotkeyEnabled)
            AddHotkey(settings.ShowWindowHotkey, GlobalHotkeyAction.ShowWindow);

        if (settings.VoiceRecordHotkeyEnabled)
            AddHotkey(settings.VoiceRecordHotkey, GlobalHotkeyAction.ToggleVoiceRecording);

        if (_hotkeys.Count == 0)
            Stop();
        else
            Start();
    }

    /// <summary>
    /// Освобождает установленный keyboard hook.
    /// </summary>
    public void Dispose()
    {
        Stop();
    }

    /// <summary>
    /// Временно отключает обработку хоткеев без снятия hook.
    /// </summary>
    public void SetSuspended(bool isSuspended)
    {
        _isSuspended = isSuspended;
        if (isSuspended)
            _pressedKeys.Clear();
    }

    /// <summary>
    /// Добавляет хоткей в список отслеживаемых комбинаций, если строка корректно распарсилась.
    /// </summary>
    private void AddHotkey(string hotkeyText, GlobalHotkeyAction action)
    {
        if (Hotkey.TryParse(hotkeyText, out var hotkey))
            _hotkeys.Add(new RegisteredHotkey(hotkey, action));
    }

    /// <summary>
    /// Устанавливает системный low-level keyboard hook.
    /// </summary>
    private void Start()
    {
        if (_hookHandle != 0)
            return;

        using var process = Process.GetCurrentProcess();
        using var module = process.MainModule;
        var moduleHandle = module is null ? 0 : GetModuleHandle(module.ModuleName);
        _hookHandle = SetWindowsHookEx(WhKeyboardLl, _keyboardProc, moduleHandle, 0);
    }

    /// <summary>
    /// Снимает системный keyboard hook, если он был установлен.
    /// </summary>
    private void Stop()
    {
        if (_hookHandle == 0)
            return;

        UnhookWindowsHookEx(_hookHandle);
        _hookHandle = 0;
    }

    /// <summary>
    /// Обрабатывает нажатия и отпускания клавиш, сравнивая их с зарегистрированными хоткеями.
    /// </summary>
    private nint KeyboardHookCallback(int nCode, nint wParam, nint lParam)
    {
        if (nCode < 0)
            return CallNextHookEx(_hookHandle, nCode, wParam, lParam);

        if (_isSuspended)
            return CallNextHookEx(_hookHandle, nCode, wParam, lParam);

        var message = wParam.ToInt32();
        var keyInfo = Marshal.PtrToStructure<KbdLlHookStruct>(lParam);
        var vkCode = (int)keyInfo.VkCode;

        if (message is WmKeyUp or WmSysKeyUp)
        {
            _pressedKeys.Remove(vkCode);
            return CallNextHookEx(_hookHandle, nCode, wParam, lParam);
        }

        if (message is WmKeyDown or WmSysKeyDown)
        {
            if (!_pressedKeys.Add(vkCode))
                return CallNextHookEx(_hookHandle, nCode, wParam, lParam);

            var currentModifiers = GetCurrentModifiers();
            foreach (var registeredHotkey in _hotkeys)
            {
                if (registeredHotkey.Hotkey.Matches(vkCode, currentModifiers))
                {
                    HotkeyPressed?.Invoke(registeredHotkey.Action);
                    break;
                }
            }
        }

        return CallNextHookEx(_hookHandle, nCode, wParam, lParam);
    }

    /// <summary>
    /// Возвращает текущий набор нажатых модификаторов по состоянию клавиатуры Windows.
    /// </summary>
    private static HotkeyModifiers GetCurrentModifiers()
    {
        var modifiers = HotkeyModifiers.None;

        if (IsKeyDown(VkControl))
            modifiers |= HotkeyModifiers.Control;
        if (IsKeyDown(VkShift))
            modifiers |= HotkeyModifiers.Shift;
        if (IsKeyDown(VkMenu))
            modifiers |= HotkeyModifiers.Alt;
        if (IsKeyDown(VkLWin) || IsKeyDown(VkRWin))
            modifiers |= HotkeyModifiers.Win;

        return modifiers;
    }

    /// <summary>
    /// Проверяет, удерживается ли виртуальная клавиша.
    /// </summary>
    private static bool IsKeyDown(int virtualKey)
    {
        return (GetAsyncKeyState(virtualKey) & 0x8000) != 0;
    }

    /// <summary>
    /// Связка распарсенного хоткея и действия приложения.
    /// </summary>
    private sealed record RegisteredHotkey(Hotkey Hotkey, GlobalHotkeyAction Action);

    /// <summary>
    /// Представляет одну комбинацию горячих клавиш.
    /// </summary>
    private readonly record struct Hotkey(int VirtualKey, HotkeyModifiers Modifiers)
    {
        /// <summary>
        /// Проверяет, совпадает ли текущее нажатие с хоткеем.
        /// </summary>
        public bool Matches(int virtualKey, HotkeyModifiers modifiers)
        {
            return VirtualKey == virtualKey && Modifiers == modifiers;
        }

        /// <summary>
        /// Преобразует строку вида Ctrl+Shift+A в внутреннее представление хоткея.
        /// </summary>
        public static bool TryParse(string text, out Hotkey hotkey)
        {
            hotkey = default;

            if (string.IsNullOrWhiteSpace(text))
                return false;

            var modifiers = HotkeyModifiers.None;
            int? virtualKey = null;

            foreach (var rawPart in text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var part = rawPart.ToUpperInvariant();
                switch (part)
                {
                    case "CTRL":
                    case "CONTROL":
                        modifiers |= HotkeyModifiers.Control;
                        break;
                    case "SHIFT":
                        modifiers |= HotkeyModifiers.Shift;
                        break;
                    case "ALT":
                        modifiers |= HotkeyModifiers.Alt;
                        break;
                    case "WIN":
                    case "WINDOWS":
                        modifiers |= HotkeyModifiers.Win;
                        break;
                    default:
                        if (!TryParseVirtualKey(part, out var parsedVirtualKey))
                            return false;

                        virtualKey = parsedVirtualKey;
                        break;
                }
            }

            if (virtualKey is null)
                return false;

            hotkey = new Hotkey(virtualKey.Value, modifiers);
            return true;
        }

        /// <summary>
        /// Преобразует текст клавиши в virtual-key код Windows.
        /// </summary>
        private static bool TryParseVirtualKey(string text, out int virtualKey)
        {
            virtualKey = 0;

            if (NamedVirtualKeys.TryGetValue(text, out virtualKey))
                return true;

            if (text.Length == 1 && text[0] is >= 'A' and <= 'Z')
            {
                virtualKey = text[0];
                return true;
            }

            if (text.Length == 1 && text[0] is >= '0' and <= '9')
            {
                virtualKey = text[0];
                return true;
            }

            if (text.StartsWith('F') &&
                int.TryParse(text[1..], out var functionKeyNumber) &&
                functionKeyNumber is >= 1 and <= 24)
            {
                virtualKey = 0x70 + functionKeyNumber - 1;
                return true;
            }

            return false;
        }

        private static readonly IReadOnlyDictionary<string, int> NamedVirtualKeys = new Dictionary<string, int>
        {
            ["BACK"] = 0x08,
            ["BACKSPACE"] = 0x08,
            ["TAB"] = 0x09,
            ["CLEAR"] = 0x0C,
            ["ENTER"] = 0x0D,
            ["RETURN"] = 0x0D,
            ["ESC"] = 0x1B,
            ["ESCAPE"] = 0x1B,
            ["SPACE"] = 0x20,
            ["PAGEUP"] = 0x21,
            ["PRIOR"] = 0x21,
            ["PAGEDOWN"] = 0x22,
            ["NEXT"] = 0x22,
            ["END"] = 0x23,
            ["HOME"] = 0x24,
            ["LEFT"] = 0x25,
            ["UP"] = 0x26,
            ["RIGHT"] = 0x27,
            ["DOWN"] = 0x28,
            ["INSERT"] = 0x2D,
            ["DELETE"] = 0x2E,
            ["NUMPAD0"] = 0x60,
            ["NUMPAD1"] = 0x61,
            ["NUMPAD2"] = 0x62,
            ["NUMPAD3"] = 0x63,
            ["NUMPAD4"] = 0x64,
            ["NUMPAD5"] = 0x65,
            ["NUMPAD6"] = 0x66,
            ["NUMPAD7"] = 0x67,
            ["NUMPAD8"] = 0x68,
            ["NUMPAD9"] = 0x69,
            ["MULTIPLY"] = 0x6A,
            ["ADD"] = 0x6B,
            ["SUBTRACT"] = 0x6D,
            ["DECIMAL"] = 0x6E,
            ["DIVIDE"] = 0x6F,
            ["OEM1"] = 0xBA,
            ["OEMPLUS"] = 0xBB,
            ["OEMCOMMA"] = 0xBC,
            ["OEMMINUS"] = 0xBD,
            ["OEMPERIOD"] = 0xBE,
            ["OEM2"] = 0xBF,
            ["OEM3"] = 0xC0,
            ["OEM4"] = 0xDB,
            ["OEM5"] = 0xDC,
            ["OEM6"] = 0xDD,
            ["OEM7"] = 0xDE,
            ["OEM8"] = 0xDF,
            ["PAUSE"] = 0x13,
            ["CAPSLOCK"] = 0x14,
            ["CAPITAL"] = 0x14,
            ["PRINTSCREEN"] = 0x2C,
            ["SNAPSHOT"] = 0x2C,
            ["SCROLL"] = 0x91,
            ["SCROLLLOCK"] = 0x91,
            ["NUMLOCK"] = 0x90
        };
    }

    /// <summary>
    /// Флаги модификаторов, участвующих в хоткее.
    /// </summary>
    [Flags]
    private enum HotkeyModifiers
    {
        None = 0,
        Control = 1,
        Shift = 2,
        Alt = 4,
        Win = 8
    }

    /// <summary>
    /// Сигнатура callback для WinAPI keyboard hook.
    /// </summary>
    private delegate nint LowLevelKeyboardProc(int nCode, nint wParam, nint lParam);

    /// <summary>
    /// Данные о клавише, передаваемые Windows в low-level keyboard hook.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct KbdLlHookStruct
    {
        public uint VkCode;
        public uint ScanCode;
        public uint Flags;
        public uint Time;
        public nint ExtraInfo;
    }

    /// <summary>
    /// Устанавливает low-level keyboard hook через WinAPI.
    /// </summary>
    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, nint hMod, uint dwThreadId);

    /// <summary>
    /// Снимает ранее установленный keyboard hook.
    /// </summary>
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(nint hhk);

    /// <summary>
    /// Передает событие клавиатуры следующему обработчику в цепочке hook.
    /// </summary>
    [DllImport("user32.dll")]
    private static extern nint CallNextHookEx(nint hhk, int nCode, nint wParam, nint lParam);

    /// <summary>
    /// Возвращает текущее состояние виртуальной клавиши.
    /// </summary>
    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    /// <summary>
    /// Возвращает handle модуля процесса для установки hook.
    /// </summary>
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint GetModuleHandle(string lpModuleName);
}
