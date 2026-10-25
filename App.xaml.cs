using System;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using BinderApp.Models;
using BinderApp.Services;
using BinderApp.ViewModels;
using BinderApp.Views;
using Serilog;

namespace BinderApp
{
    /// <summary>
    /// Точка входа приложения.
    /// Управляет инициализацией сервисов, системным треем и главным окном.
    /// </summary>
    public partial class App : Application
    {
        private ConfigService? _configService;
        private ProfileService? _profileService;
        private GlobalHotkeyManager? _hotkeyManager;
        private ClipboardService? _clipboardService;
        private BindExecutor? _bindExecutor;
        private TrayService? _trayService;
        private AutoUpdater? _autoUpdater;

        private AppSettings? _settings;
        private MainViewModel? _mainVm;
        private OverlayViewModel? _overlayVm;

        private MainWindow? _mainWindow;
        private OverlayWindow? _overlayWindow;
        private SplashWindow? _splashWindow;
        private DateTime _splashShownAt;
        private bool _isShuttingDown;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            DispatcherUnhandledException += OnDispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;

            _splashShownAt = DateTime.UtcNow;
            _splashWindow = new SplashWindow();
            _splashWindow.Show();
            Dispatcher.BeginInvoke(new Action(InitializeApplication), DispatcherPriority.Background);
        }

        private async void InitializeApplication()
        {
            try
            {
                await InitializeApplicationCoreAsync();
            }
            finally
            {
                TimeSpan remainingSplashTime = TimeSpan.FromMilliseconds(900) - (DateTime.UtcNow - _splashShownAt);
                if (remainingSplashTime > TimeSpan.Zero)
                {
                    await Task.Delay(remainingSplashTime);
                }

                _splashWindow?.Close();
                _splashWindow = null;
            }
        }

        private async Task InitializeApplicationCoreAsync()
        {
            _configService = new ConfigService();
            _settings = _configService.LoadSettings();

            _autoUpdater = new AutoUpdater(_configService);
            bool updateScheduled = await _autoUpdater.CheckAndApplyPendingUpdatesAsync(
                status => _splashWindow?.SetStatus(status));
            if (updateScheduled)
            {
                _isShuttingDown = true;
                Log.CloseAndFlush();
                Shutdown();
                return;
            }

            _profileService = new ProfileService(_configService, _settings);
            _clipboardService = new ClipboardService();
            _bindExecutor = new BindExecutor(_clipboardService, _settings);
            _hotkeyManager = new GlobalHotkeyManager();

            // Сервис системного трея
            _trayService = new TrayService(_profileService, _configService);

            // ViewModels и окна
            _mainVm = new MainViewModel(
                _configService,
                _profileService,
                _hotkeyManager,
                _bindExecutor,
                _trayService,
                _settings,
                new WorkshopService());
            _overlayVm = new OverlayViewModel(_bindExecutor);

            _overlayWindow = new OverlayWindow(_overlayVm, _settings);
            _mainWindow = new MainWindow { DataContext = _mainVm };
            MainWindow = _mainWindow;

            // Настраиваем коллбэки контекстного меню трея
            _trayService.SetupCallbacks(
                onRunBind: bind => _mainVm.ExecuteBind(bind),
                onShowWindow: () => ShowMainWindow(),
                onExit: () => ExitApplication()
            );

            // Пересобираем меню трея с начальными биндами
            _mainVm.RegisterHotkeys();

            _mainWindow.Show();
        }

        public void ShowMainWindow()
        {
            if (_mainWindow == null) return;
            _mainWindow.Show();
            _mainWindow.WindowState = WindowState.Normal;
            _mainWindow.Activate();
        }

        public void ExitApplication()
        {
            if (_isShuttingDown) return;
            _isShuttingDown = true;

            _mainVm?.Cleanup();
            _hotkeyManager?.Dispose();
            _clipboardService?.Dispose();
            _trayService?.Dispose();
            Log.CloseAndFlush();
            Shutdown();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            ExitApplication();
            base.OnExit(e);
        }

        private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            Log.Fatal(e.Exception, "Необработанная ошибка интерфейса");
            MessageBox.Show(
                $"Произошла ошибка приложения. Подробности записаны в журнал:\n{_configService?.LogsDir ?? "папка config/logs"}",
                "Ошибка Lum Binder",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }

        private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            if (e.ExceptionObject is Exception exception)
            {
                Log.Fatal(exception, "Необработанная ошибка приложения");
            }
            else
            {
                Log.Fatal("Необработанное исключение завершает процесс: {ExceptionObject}", e.ExceptionObject);
            }
        }
    }
}
