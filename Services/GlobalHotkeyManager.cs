using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using BinderApp.Native;
using Serilog;

namespace BinderApp.Services
{
    /// <summary>
    /// Менеджер глобальных горячих клавиш через P/Invoke RegisterHotKey.
    /// Поддерживает парсинг комбинаций вроде "Ctrl+Shift+F1", "Alt+1", "Win+X".
    /// </summary>
    public class GlobalHotkeyManager : IDisposable
    {
        private const int HotkeyIdBase = 0x9000;
        private readonly Dictionary<int, Action> _callbacks = new();
        private int _currentId = HotkeyIdBase;
        private HwndSource? _hwndSource;
        private Window? _msgWindow;

        public GlobalHotkeyManager()
        {
            InitMessageWindow();
        }

        private void InitMessageWindow()
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                _msgWindow = new Window
                {
                    Width = 0,
                    Height = 0,
                    WindowStyle = WindowStyle.None,
                    ShowInTaskbar = false,
                    Opacity = 0,
                    ResizeMode = ResizeMode.NoResize,
                    AllowsTransparency = true,
                    Background = System.Windows.Media.Brushes.Transparent
                };
                _msgWindow.Show();

                var helper = new WindowInteropHelper(_msgWindow);
                _hwndSource = HwndSource.FromHwnd(helper.Handle);
                _hwndSource?.AddHook(WndProc);
            });
        }

        public int Register(string hotkeyString, Action callback)
        {
            if (string.IsNullOrWhiteSpace(hotkeyString)) return -1;
            if (!TryParseHotkey(hotkeyString, out Key key, out ModifierKeys modifiers)) return -1;

            uint vk = (uint)KeyInterop.VirtualKeyFromKey(key);
            uint mods = ConvertModifiers(modifiers) | NativeMethods.MOD_NOREPEAT;

            int id = _currentId++;
            bool success = false;
            int error = 0;

            Application.Current.Dispatcher.Invoke(() =>
            {
                if (_msgWindow == null) return;
                var helper = new WindowInteropHelper(_msgWindow);
                success = NativeMethods.RegisterHotKey(helper.Handle, id, mods, vk);
                if (!success)
                {
                    error = Marshal.GetLastWin32Error();
                }
            });

            if (success)
            {
                _callbacks[id] = callback;
                Log.Debug("Зарегистрирован хоткей [{Hotkey}] с ID {Id}", hotkeyString, id);
                return id;
            }
            else
            {
                Log.Warning("Не удалось зарегистрировать хоткей [{Hotkey}]. Ошибка: {Error}", hotkeyString, error);
                return -1;
            }
        }

        public void Unregister(int id)
        {
            if (id < 0) return;

            Application.Current.Dispatcher.Invoke(() =>
            {
                if (_msgWindow == null) return;
                var helper = new WindowInteropHelper(_msgWindow);
                NativeMethods.UnregisterHotKey(helper.Handle, id);
            });

            _callbacks.Remove(id);
        }

        public void UnregisterAll()
        {
            if (_msgWindow == null) return;

            Application.Current.Dispatcher.Invoke(() =>
            {
                var helper = new WindowInteropHelper(_msgWindow);
                foreach (var id in _callbacks.Keys)
                {
                    NativeMethods.UnregisterHotKey(helper.Handle, id);
                }
            });

            _callbacks.Clear();
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == NativeMethods.WM_HOTKEY)
            {
                int id = wParam.ToInt32();
                if (_callbacks.TryGetValue(id, out var action))
                {
                    try
                    {
                        action?.Invoke();
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, "Ошибка выполнения действия по хоткею {Id}", id);
                    }
                    handled = true;
                }
            }
            return IntPtr.Zero;
        }

        public static bool TryParseHotkey(string hotkeyStr, out Key key, out ModifierKeys modifiers)
        {
            key = Key.None;
            modifiers = ModifierKeys.None;

            if (string.IsNullOrWhiteSpace(hotkeyStr)) return false;

            var parts = hotkeyStr.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length == 0) return false;

            for (int i = 0; i < parts.Length; i++)
            {
                string p = parts[i];
                if (p.Equals("Ctrl", StringComparison.OrdinalIgnoreCase) || p.Equals("Control", StringComparison.OrdinalIgnoreCase))
                {
                    modifiers |= ModifierKeys.Control;
                }
                else if (p.Equals("Alt", StringComparison.OrdinalIgnoreCase))
                {
                    modifiers |= ModifierKeys.Alt;
                }
                else if (p.Equals("Shift", StringComparison.OrdinalIgnoreCase))
                {
                    modifiers |= ModifierKeys.Shift;
                }
                else if (p.Equals("Win", StringComparison.OrdinalIgnoreCase) || p.Equals("Windows", StringComparison.OrdinalIgnoreCase))
                {
                    modifiers |= ModifierKeys.Windows;
                }
                else
                {
                    // Клавиша
                    if (Enum.TryParse<Key>(p, true, out var parsedKey))
                    {
                        key = parsedKey;
                    }
                    else if (p.StartsWith("Num", StringComparison.OrdinalIgnoreCase) && Enum.TryParse<Key>("NumPad" + p.Substring(3), true, out var numPadKey))
                    {
                        key = numPadKey;
                    }
                    else if (p.Length == 1 && char.IsDigit(p[0]))
                    {
                        key = (Key)((int)Key.D0 + (p[0] - '0'));
                    }
                }
            }

            return key != Key.None;
        }

        private static uint ConvertModifiers(ModifierKeys modifiers)
        {
            uint mods = NativeMethods.MOD_NONE;
            if (modifiers.HasFlag(ModifierKeys.Alt)) mods |= NativeMethods.MOD_ALT;
            if (modifiers.HasFlag(ModifierKeys.Control)) mods |= NativeMethods.MOD_CONTROL;
            if (modifiers.HasFlag(ModifierKeys.Shift)) mods |= NativeMethods.MOD_SHIFT;
            if (modifiers.HasFlag(ModifierKeys.Windows)) mods |= NativeMethods.MOD_WIN;
            return mods;
        }

        public void Dispose()
        {
            UnregisterAll();
            _hwndSource?.Dispose();
            Application.Current?.Dispatcher.Invoke(() => _msgWindow?.Close());
            GC.SuppressFinalize(this);
        }
    }
}
