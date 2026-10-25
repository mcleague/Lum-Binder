using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using BinderApp.Native;
using Serilog;

namespace BinderApp.Services
{
    /// <summary>
    /// Сервис для надежного копирования текста в буфер обмена 
    /// и глобального ожидания нажатия клавиши Enter.
    /// </summary>
    public class ClipboardService : IDisposable
    {
        private readonly NativeMethods.LowLevelKeyboardProc _proc;
        private IntPtr _hookId = IntPtr.Zero;
        private event Action? EnterPressed;

        public ClipboardService()
        {
            _proc = HookCallback;
            InstallHook();
        }

        /// <summary>
        /// Надежно копирует переданный текст в буфер обмена Windows с повторными попытками.
        /// </summary>
        public bool Copy(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                text = string.Empty;
            }

            const int maxRetries = 10;
            const int retryDelayMs = 40;

            for (int i = 0; i < maxRetries; i++)
            {
                try
                {
                    if (Application.Current != null)
                    {
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            Clipboard.Clear();
                            Clipboard.SetDataObject(text, true);
                        });
                    }
                    else
                    {
                        Clipboard.Clear();
                        Clipboard.SetDataObject(text, true);
                    }
                    return true;
                }
                catch (ExternalException)
                {
                    // Буфер занят другим процессом, ждем и повторяем попытку
                    Thread.Sleep(retryDelayMs);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[ClipboardService] Ошибка буфера обмена: {ex.Message}");
                    Thread.Sleep(retryDelayMs);
                }
            }

            Log.Warning("Не удалось записать текст в буфер обмена после {RetryCount} попыток.", maxRetries);
            return false;
        }

        public bool CopyToClipboard(string text) => Copy(text);

        /// <summary>
        /// Ожидает нажатия клавиши Enter пользователем с учетом таймаута и токена отмены.
        /// Возвращает true, если был нажат Enter, и false при таймауте или отмене.
        /// </summary>
        public async Task<bool> WaitForEnterAsync(int timeoutSeconds, CancellationToken cancellationToken)
        {
            if (timeoutSeconds <= 0)
            {
                timeoutSeconds = 30;
            }

            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            void OnEnter()
            {
                tcs.TrySetResult(true);
            }

            EnterPressed += OnEnter;

            try
            {
                using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

                var registration = linkedCts.Token.Register(() =>
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        tcs.TrySetCanceled(cancellationToken);
                    }
                    else
                    {
                        // Сработал таймаут
                        tcs.TrySetResult(false);
                    }
                });

                using (registration)
                {
                    return await tcs.Task;
                }
            }
            catch (OperationCanceledException)
            {
                return false;
            }
            finally
            {
                EnterPressed -= OnEnter;
            }
        }

        private void InstallHook()
        {
            try
            {
                using var curProcess = Process.GetCurrentProcess();
                using var curModule = curProcess.MainModule;
                IntPtr moduleHandle = NativeMethods.GetModuleHandle(curModule?.ModuleName);
                _hookId = NativeMethods.SetWindowsHookEx(NativeMethods.WH_KEYBOARD_LL, _proc, moduleHandle, 0);
                if (_hookId == IntPtr.Zero)
                    Log.Error("SetWindowsHookEx завершился ошибкой Win32={Win32Error}.", Marshal.GetLastWin32Error());
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Не удалось установить глобальный хук клавиатуры");
                Debug.WriteLine($"[ClipboardService] Не удалось установить хук клавиатуры: {ex.Message}");
            }
        }

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                int msg = wParam.ToInt32();
                if (msg == NativeMethods.WM_KEYDOWN || msg == NativeMethods.WM_SYSKEYDOWN)
                {
                    var kb = Marshal.PtrToStructure<NativeMethods.KBDLLHOOKSTRUCT>(lParam);
                    if (kb.vkCode == NativeMethods.VK_RETURN)
                    {
                        // Вызываем подписчиков ожидания Enter
                        try
                        {
                            EnterPressed?.Invoke();
                        }
                        catch (Exception ex)
                        {
                            Log.Error(ex, "Ошибка обработки нажатия Enter");
                            Debug.WriteLine($"[ClipboardService] Ошибка в обработчике Enter: {ex.Message}");
                        }
                    }
                }
            }

            return NativeMethods.CallNextHookEx(_hookId, nCode, wParam, lParam);
        }

        public void Dispose()
        {
            if (_hookId != IntPtr.Zero)
            {
                NativeMethods.UnhookWindowsHookEx(_hookId);
                _hookId = IntPtr.Zero;
            }
            GC.SuppressFinalize(this);
        }
    }
}
