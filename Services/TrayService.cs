using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using BinderApp.Models;
using Hardcodet.Wpf.TaskbarNotification;
using Serilog;

namespace BinderApp.Services
{
    /// <summary>
    /// Сервис управления системным треем с кастомным тёмным дизайном контекстного меню:
    /// Секция профилей, секции биндов по вкладкам, управление окном и выход.
    /// </summary>
    public class TrayService : IDisposable
    {
        private readonly TaskbarIcon _trayIcon;
        private readonly ProfileService _profileService;
        private readonly ConfigService _configService;
        private Action<Bind>? _onRunBind;
        private Action? _onShowWindow;
        private Action? _onExit;

        public TrayService(ProfileService profileService, ConfigService configService)
        {
            _profileService = profileService;
            _configService = configService;

            _trayIcon = new TaskbarIcon
            {
                ToolTipText = "Lum Binder"
            };

            var iconResource = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/icon.ico"));
            if (iconResource != null)
            {
                using (iconResource.Stream)
                using (var icon = new System.Drawing.Icon(iconResource.Stream))
                {
                    _trayIcon.Icon = (System.Drawing.Icon)icon.Clone();
                }
            }
            else
            {
                Log.Warning("Не найден встроенный ресурс иконки трея Assets/icon.ico");
                _trayIcon.Icon = System.Drawing.SystemIcons.Application;
            }

            _trayIcon.TrayMouseDoubleClick += (_, _) => _onShowWindow?.Invoke();
        }

        public void SetupCallbacks(Action<Bind> onRunBind, Action onShowWindow, Action onExit)
        {
            _onRunBind = onRunBind;
            _onShowWindow = onShowWindow;
            _onExit = onExit;
        }

        /// <summary>
        /// Пересобирает контекстное меню с кастомным стильным темным дизайном.
        /// </summary>
        public void RebuildMenu(BinderConfig config)
        {
            var menu = new ContextMenu();
            menu.Style = Application.Current.FindResource("DarkContextMenuStyle") as Style;

            // Заголовок
            var headerItem = new MenuItem
            {
                Header = "Lum Binder",
                IsEnabled = false,
                FontWeight = FontWeights.Bold,
                Foreground = (Brush)Application.Current.FindResource("AccentBrush")
            };
            menu.Items.Add(headerItem);
            menu.Items.Add(new Separator { Style = Application.Current.FindResource("DarkSeparatorStyle") as Style });

            // 1. Меню профилей (переключение)
            var profilesSubmenu = new MenuItem
            {
                Header = $"Профиль: {_profileService.ActiveProfile.Name}",
                Style = Application.Current.FindResource("DarkMenuItemStyle") as Style
            };

            foreach (var prof in _profileService.Profiles)
            {
                var profItem = new MenuItem
                {
                    Header = prof.Name,
                    IsChecked = prof.Id == _profileService.ActiveProfile.Id,
                    Style = Application.Current.FindResource("DarkMenuItemStyle") as Style
                };
                string pId = prof.Id;
                profItem.Click += (_, _) => _profileService.SwitchProfile(pId);
                profilesSubmenu.Items.Add(profItem);
            }
            menu.Items.Add(profilesSubmenu);
            menu.Items.Add(new Separator { Style = Application.Current.FindResource("DarkSeparatorStyle") as Style });

            // 2. Бинды по вкладкам
            if (config != null && config.Tabs != null)
            {
                foreach (var tab in config.Tabs)
                {
                    if (tab.Binds == null || tab.Binds.Count == 0) continue;

                    var tabMenu = new MenuItem
                    {
                        Header = tab.Name,
                        Style = Application.Current.FindResource("DarkMenuItemStyle") as Style
                    };

                    foreach (var bind in tab.Binds)
                    {
                        var b = bind;
                        var bindItem = new MenuItem
                        {
                            Header = $"{b.Name}  ({b.Hotkey})",
                            IsEnabled = b.Enabled && b.Steps.Count > 0,
                            Style = Application.Current.FindResource("DarkMenuItemStyle") as Style
                        };
                        bindItem.Click += (_, _) => _onRunBind?.Invoke(b);
                        tabMenu.Items.Add(bindItem);
                    }

                    menu.Items.Add(tabMenu);
                }
            }

            menu.Items.Add(new Separator { Style = Application.Current.FindResource("DarkSeparatorStyle") as Style });

            // 3. Управление
            var showItem = new MenuItem
            {
                Header = "Показать окно",
                Style = Application.Current.FindResource("DarkMenuItemStyle") as Style
            };
            showItem.Click += (_, _) => _onShowWindow?.Invoke();
            menu.Items.Add(showItem);

            var exitItem = new MenuItem
            {
                Header = "Выход",
                Style = Application.Current.FindResource("DarkMenuItemStyle") as Style
            };
            exitItem.Click += (_, _) => _onExit?.Invoke();
            menu.Items.Add(exitItem);

            _trayIcon.ContextMenu = menu;
        }

        public void Dispose()
        {
            _trayIcon.Dispose();
        }
    }
}
