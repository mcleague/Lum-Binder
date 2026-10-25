using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Text.RegularExpressions;
using BinderApp.Models;
using Serilog;

namespace BinderApp.Services
{
    /// <summary>
    /// Сервис последовательного выполнения шагов бинда:
    /// Копирует текст шага в буфер обмена → ждет Enter (или таймаут) → переходит к следующему.
    /// Поддерживает звуковые уведомления и опциональную авто-вставку.
    /// </summary>
    public class BindExecutor
    {
        private static readonly Regex VariablePattern = new(@"\{(?<name>[^{}]+)\}", RegexOptions.Compiled);
        private readonly ClipboardService _clipboardService;
        private readonly AppSettings _settings;
        private CancellationTokenSource? _cts;

        public Bind? ActiveBind { get; private set; }
        public int CurrentStepIndex { get; private set; }

        public event Action<Bind, int>? StepChanged;
        public event Action? BindFinished;

        public bool IsRunning => ActiveBind != null && _cts != null && !_cts.IsCancellationRequested;

        public BindExecutor(ClipboardService clipboardService, AppSettings settings)
        {
            _clipboardService = clipboardService;
            _settings = settings;
        }

        public void Start(Bind bind)
        {
            if (bind == null || bind.Steps == null || bind.Steps.Count == 0) return;

            Stop(notify: false);

            ActiveBind = bind;
            CurrentStepIndex = 0;
            _cts = new CancellationTokenSource();
            var cancellationToken = _cts.Token;

            Log.Information("Запуск бинда «{BindName}» ({StepsCount} шагов)", bind.Name, bind.Steps.Count);
            PlaySoundNotification();

            Task.Run(() => RunLoopAsync(cancellationToken));
        }

        public void Stop(bool notify = true)
        {
            if (_cts != null && !_cts.IsCancellationRequested)
            {
                _cts.Cancel();
            }

            ActiveBind = null;

            if (notify)
            {
                Application.Current?.Dispatcher.Invoke(() => BindFinished?.Invoke());
            }
        }

        private async Task RunLoopAsync(CancellationToken token)
        {
            var bind = ActiveBind;
            if (bind == null) return;

            int timeout = _settings.StepTimeout > 0 ? _settings.StepTimeout : 30;
            bool completed = true;

            for (int i = 0; i < bind.Steps.Count; i++)
            {
                if (token.IsCancellationRequested)
                {
                    completed = false;
                    break;
                }

                CurrentStepIndex = i;
                string text = ExpandVariables(bind.Steps[i]);

                Application.Current?.Dispatcher.Invoke(() => StepChanged?.Invoke(bind, i));

                // Копируем текст текущего шага в буфер обмена
                _clipboardService.CopyToClipboard(text);

                if (token.IsCancellationRequested) break;

                // Ждем нажатия Enter в игре / приложении
                bool enterPressed = await _clipboardService.WaitForEnterAsync(timeout, token);

                if (!enterPressed)
                {
                    Log.Information("Бинд «{BindName}» остановлен по таймауту/отмене на шаге {Step}", bind.Name, i + 1);
                    completed = false;
                    break;
                }

                // Звуковой клик при переходе к следующему шагу
                if (i < bind.Steps.Count - 1)
                {
                    PlaySoundNotification();
                }

                // Микро-пауза перед следующим шагом для стабильности
                await Task.Delay(130, token).ContinueWith(_ => { });
            }

            if (token.IsCancellationRequested) return;

            if (completed)
            {
                PlaySoundNotification();
            }

            Application.Current?.Dispatcher.Invoke(() =>
            {
                if (ReferenceEquals(ActiveBind, bind))
                {
                    ActiveBind = null;
                    BindFinished?.Invoke();
                }
            });
        }

        private string ExpandVariables(string text)
        {
            var variables = _settings.GetProfileVariables(_settings.ActiveProfile);
            return VariablePattern.Replace(text, match =>
            {
                return match.Groups["name"].Value.ToLowerInvariant() switch
                {
                    "позывной" => variables.CallSign,
                    "звание" => variables.Rank,
                    "фио" or "имя" => variables.FullName,
                    "подразделение" => variables.Department,
                    "организация" => variables.Organization,
                    _ => match.Value
                };
            });
        }

        private void PlaySoundNotification()
        {
            if (!_settings.SoundNotifications) return;

            string soundPath = Path.Combine(
                AppContext.BaseDirectory,
                "Assets",
                _settings.NotificationSound);
            if (!File.Exists(soundPath))
            {
                Log.Error("Файл звукового уведомления не найден: {SoundPath}", soundPath);
                return;
            }

            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null)
            {
                Log.Error("Не удалось воспроизвести звуковое уведомление: диспетчер приложения недоступен");
                return;
            }

            dispatcher.BeginInvoke(() =>
            {
                var player = new MediaPlayer();
                player.MediaOpened += (_, _) => player.Play();
                player.MediaEnded += (_, _) => player.Close();
                player.MediaFailed += (_, args) =>
                {
                    Log.Error(args.ErrorException, "Ошибка воспроизведения звука {SoundPath}", soundPath);
                    player.Close();
                };

                try
                {
                    player.Open(new Uri(soundPath, UriKind.Absolute));
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Ошибка воспроизведения звука {SoundPath}", soundPath);
                    player.Close();
                }
            });
        }
    }
}
