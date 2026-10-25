using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace BinderApp.Models
{
    /// <summary>
    /// Модель настроек приложения (settings.json).
    /// </summary>
    public class AppSettings : INotifyPropertyChanged
    {
        private string _accentColor = "#C74343";
        private string _overlayPosition = "top-right";
        private int _stepTimeout = 30;
        private bool _autoStart = false;
        private bool _minimizeToTray = true;
        private string _activeProfile = "default";
        private bool _soundNotifications = true;
        private string _notificationSound = "zvuk-soobsheniya-vk-1.mp3";
        private bool _autoPaste = false;
        private string _callSign = string.Empty;
        private string _rank = string.Empty;
        private string _fullName = string.Empty;
        private string _department = string.Empty;
        private string _organization = string.Empty;
        private Dictionary<string, ProfileVariables> _profileVariablesById = new(StringComparer.OrdinalIgnoreCase);
        private Dictionary<string, string> _profileNamesById = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Шестнадцатеричный цвет акцента (например, "#5294E2").
        /// </summary>
        public string AccentColor
        {
            get => _accentColor;
            set { _accentColor = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// Позиция оверлея на экране: "top-right", "top-left", "bottom-right", "bottom-left".
        /// </summary>
        public string OverlayPosition
        {
            get => _overlayPosition;
            set { _overlayPosition = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// Таймаут ожидания Enter на каждом шаге (в секундах).
        /// </summary>
        public int StepTimeout
        {
            get => _stepTimeout;
            set { _stepTimeout = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// Автозапуск программы при старте Windows.
        /// </summary>
        public bool AutoStart
        {
            get => _autoStart;
            set { _autoStart = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// Сворачивание главного окна в трей при закрытии (крестик).
        /// </summary>
        public bool MinimizeToTray
        {
            get => _minimizeToTray;
            set { _minimizeToTray = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// Имя активного профиля настроек (например, "default", "work", "game").
        /// </summary>
        public string ActiveProfile
        {
            get => _activeProfile;
            set { _activeProfile = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// Звуковой сигнал при переходе к следующему шагу или завершении бинда.
        /// </summary>
        public bool SoundNotifications
        {
            get => _soundNotifications;
            set { _soundNotifications = value; OnPropertyChanged(); }
        }

        public string NotificationSound
        {
            get => _notificationSound;
            set { _notificationSound = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// Автоматическая эмуляция нажатия Ctrl+V при каждом шаге.
        /// </summary>
        public bool AutoPaste
        {
            get => _autoPaste;
            set { _autoPaste = value; OnPropertyChanged(); }
        }

        public string CallSign
        {
            get => _callSign;
            set { _callSign = value ?? string.Empty; OnPropertyChanged(); }
        }

        public string Rank
        {
            get => _rank;
            set { _rank = value ?? string.Empty; OnPropertyChanged(); }
        }

        public string FullName
        {
            get => _fullName;
            set { _fullName = value ?? string.Empty; OnPropertyChanged(); }
        }

        public string Department
        {
            get => _department;
            set { _department = value ?? string.Empty; OnPropertyChanged(); }
        }

        public string Organization
        {
            get => _organization;
            set { _organization = value ?? string.Empty; OnPropertyChanged(); }
        }

        public Dictionary<string, ProfileVariables> ProfileVariablesById
        {
            get => _profileVariablesById;
            set => _profileVariablesById = value ?? new Dictionary<string, ProfileVariables>(StringComparer.OrdinalIgnoreCase);
        }

        public Dictionary<string, string> ProfileNamesById
        {
            get => _profileNamesById;
            set => _profileNamesById = value ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        public ProfileVariables GetProfileVariables(string profileId)
        {
            profileId = string.IsNullOrWhiteSpace(profileId) ? "default" : profileId.Trim();
            string? matchingId = null;
            foreach (var entry in ProfileVariablesById)
            {
                if (entry.Key.Equals(profileId, StringComparison.OrdinalIgnoreCase))
                {
                    matchingId = entry.Key;
                    break;
                }
            }

            if (matchingId != null)
            {
                if (ProfileVariablesById[matchingId] is { } existing) return existing;

                var replacement = new ProfileVariables();
                ProfileVariablesById[matchingId] = replacement;
                return replacement;
            }

            var variables = new ProfileVariables();
            ProfileVariablesById[profileId] = variables;
            return variables;
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? propName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propName));
        }
    }

    public sealed class ProfileVariables
    {
        public string CallSign { get; set; } = string.Empty;
        public string Rank { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string Department { get; set; } = string.Empty;
        public string Organization { get; set; } = string.Empty;
    }
}
