using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Alexa.Services;

public enum GlobalHotkeyAction
{
    ShowWindow,
    ToggleVoiceRecording
}

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

    public GlobalHotkeyService()
    {
        _keyboardProc = KeyboardHookCallback;
    }

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

    public void Dispose()
    {
        Stop();
    }

    public void SetSuspended(bool isSuspended)
    {
        _isSuspended = isSuspended;
        if (isSuspended)
            _pressedKeys.Clear();
    }

    private void AddHotkey(string hotkeyText, GlobalHotkeyAction action)
    {
        if (Hotkey.TryParse(hotkeyText, out var hotkey))
            _hotkeys.Add(new RegisteredHotkey(hotkey, action));
    }

    private void Start()
    {
        if (_hookHandle != 0)
            return;

        using var process = Process.GetCurrentProcess();
        using var module = process.MainModule;
        var moduleHandle = module is null ? 0 : GetModuleHandle(module.ModuleName);
        _hookHandle = SetWindowsHookEx(WhKeyboardLl, _keyboardProc, moduleHandle, 0);
    }

    private void Stop()
    {
        if (_hookHandle == 0)
            return;

        UnhookWindowsHookEx(_hookHandle);
        _hookHandle = 0;
    }

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

    private static bool IsKeyDown(int virtualKey)
    {
        return (GetAsyncKeyState(virtualKey) & 0x8000) != 0;
    }

    private sealed record RegisteredHotkey(Hotkey Hotkey, GlobalHotkeyAction Action);

    private readonly record struct Hotkey(int VirtualKey, HotkeyModifiers Modifiers)
    {
        public bool Matches(int virtualKey, HotkeyModifiers modifiers)
        {
            return VirtualKey == virtualKey && Modifiers == modifiers;
        }

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

    [Flags]
    private enum HotkeyModifiers
    {
        None = 0,
        Control = 1,
        Shift = 2,
        Alt = 4,
        Win = 8
    }

    private delegate nint LowLevelKeyboardProc(int nCode, nint wParam, nint lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct KbdLlHookStruct
    {
        public uint VkCode;
        public uint ScanCode;
        public uint Flags;
        public uint Time;
        public nint ExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, nint hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(nint hhk);

    [DllImport("user32.dll")]
    private static extern nint CallNextHookEx(nint hhk, int nCode, nint wParam, nint lParam);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint GetModuleHandle(string lpModuleName);
}
