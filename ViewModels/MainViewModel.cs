using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using BinderApp.Models;
using BinderApp.Services;

namespace BinderApp.ViewModels
{
    /// <summary>
    /// Главная ViewModel приложения: управляет биндами, переменными профиля и настройками.
    /// </summary>
    public class MainViewModel : ViewModelBase
    {
        private readonly ConfigService _configService;
        private readonly ProfileService _profileService;
        private readonly GlobalHotkeyManager _hotkeyManager;
        private readonly BindExecutor _bindExecutor;
        private readonly TrayService _trayService;
        private readonly AppSettings _settings;
        private readonly NotepadService _notepadService;

        private readonly Dictionary<string, int> _activeHotkeyIds = new();

        private BinderConfig _config;
        private Tab? _selectedTab;
        private Bind? _selectedBind;
        private Bind? _editingTarget;
        private Tab? _editingTab;
        private Tab? _tabBeingEdited;
        private bool _isAddingBind;
        private BindEditViewModel? _bindEditor;
        private string _tabName = string.Empty;
        private string _tabValidationMessage = string.Empty;
        private string _searchFilter = string.Empty;
        private string _hotkeyStatus = string.Empty;
        private bool _isBindEditorOpen;
        private bool _isTabEditorOpen;
        private string _loadedProfileId;

        // Вкладки верхнего уровня: 0 = Бинды, 1 = Переменные, 2 = Блокнот
        private int _currentViewMode = 0;

        // Блокнот
        private string _notepadContent = string.Empty;

        public MainViewModel(
            ConfigService configService,
            ProfileService profileService,
            GlobalHotkeyManager hotkeyManager,
            BindExecutor bindExecutor,
            TrayService trayService,
            AppSettings settings)
        {
            _configService = configService;
            _profileService = profileService;
            _hotkeyManager = hotkeyManager;
            _bindExecutor = bindExecutor;
            _trayService = trayService;
            _settings = settings;

            _notepadService = new NotepadService(_configService);
            _loadedProfileId = _profileService.ActiveProfile.Id;
            _config = _configService.LoadBinds(_loadedProfileId);

            _profileService.ProfileChanged += OnProfileChanged;

            // Команды для биндов
            AddBindCommand = new RelayCommand(AddBind);
            EditBindCommand = new RelayCommand(EditBind, () => SelectedBind != null);
            SaveBindEditCommand = new RelayCommand(SaveBindEdit);
            CancelBindEditCommand = new RelayCommand(CancelBindEdit);
            DeleteBindCommand = new RelayCommand(DeleteBind, () => SelectedBind != null);
            DuplicateBindCommand = new RelayCommand(DuplicateBind, () => SelectedBind != null);
            RunBindCommand = new RelayCommand<Bind>(ExecuteBind);

            // Команды для категорий
            AddTabCommand = new RelayCommand(AddTab);
            RenameTabCommand = new RelayCommand<Tab>(RenameTab);
            SaveTabCommand = new RelayCommand(SaveTab);
            CancelTabEditCommand = new RelayCommand(() => IsTabEditorOpen = false);
            DeleteTabCommand = new RelayCommand<Tab>(DeleteTab);

            // Режимы экрана (Бинды, Переменные, Блокнот)
            SwitchViewModeCommand = new RelayCommand<string>(mode =>
            {
                if (int.TryParse(mode, out int m))
                {
                    CurrentViewMode = m;
                }
            });

            _notepadContent = _notepadService.Load();

            if (Tabs.Count > 0)
            {
                SelectedTab = Tabs[0];
            }

        }

        public ObservableCollection<Tab> Tabs => _config.Tabs;

        public Tab? SelectedTab
        {
            get => _selectedTab;
            set
            {
                SetProperty(ref _selectedTab, value);
                OnPropertyChanged(nameof(CurrentBinds));
                OnPropertyChanged(nameof(FilteredBinds));
            }
        }

        public ObservableCollection<Bind>? CurrentBinds => SelectedTab?.Binds;

        public IEnumerable<Bind>? FilteredBinds
        {
            get
            {
                if (CurrentBinds == null) return null;
                if (string.IsNullOrWhiteSpace(SearchFilter)) return CurrentBinds;
                return CurrentBinds.Where(b => 
                    (b.Name != null && b.Name.Contains(SearchFilter, StringComparison.OrdinalIgnoreCase)) ||
                    (b.Hotkey != null && b.Hotkey.Contains(SearchFilter, StringComparison.OrdinalIgnoreCase)));
            }
        }

        public string SearchFilter
        {
            get => _searchFilter;
            set
            {
                if (SetProperty(ref _searchFilter, value))
                {
                    OnPropertyChanged(nameof(FilteredBinds));
                }
            }
        }

        public Bind? SelectedBind
        {
            get => _selectedBind;
            set
            {
                SetProperty(ref _selectedBind, value);
                ((RelayCommand)EditBindCommand).RaiseCanExecuteChanged();
                ((RelayCommand)DeleteBindCommand).RaiseCanExecuteChanged();
                ((RelayCommand)DuplicateBindCommand).RaiseCanExecuteChanged();
            }
        }

        // --- Режимы отображения (0 - Бинды, 1 - Переменные, 2 - Блокнот, 3 - Прочее, 4 - Настройки) ---

        public int CurrentViewMode
        {
            get => _currentViewMode;
            set
            {
                if (SetProperty(ref _currentViewMode, value))
                {
                    OnPropertyChanged(nameof(IsBindsView));
                    OnPropertyChanged(nameof(IsVariablesView));
                    OnPropertyChanged(nameof(IsNotepadView));
                    OnPropertyChanged(nameof(IsDescriptionView));
                    OnPropertyChanged(nameof(IsSettingsView));
                }
            }
        }

        public bool IsBindsView => CurrentViewMode == 0;
        public bool IsVariablesView => CurrentViewMode == 1;
        public bool IsNotepadView => CurrentViewMode == 2;
        public bool IsDescriptionView => CurrentViewMode == 3;
        public bool IsSettingsView => CurrentViewMode == 4;

        // --- Блокнот ---

        public string NotepadContent
        {
            get => _notepadContent;
            set
            {
                if (SetProperty(ref _notepadContent, value))
                {
                    _notepadService.Save(value);
                }
            }
        }

        public int StepTimeout
        {
            get => _settings.StepTimeout;
            set
            {
                _settings.StepTimeout = value;
                OnPropertyChanged();
                _configService.SaveSettings(_settings);
            }
        }

        public string OverlayPosition
        {
            get => _settings.OverlayPosition;
            set
            {
                _settings.OverlayPosition = value;
                OnPropertyChanged();
                _configService.SaveSettings(_settings);
            }
        }

        public bool SoundNotifications
        {
            get => _settings.SoundNotifications;
            set
            {
                _settings.SoundNotifications = value;
                OnPropertyChanged();
                _configService.SaveSettings(_settings);
            }
        }

        public string NotificationSound
        {
            get => _settings.NotificationSound;
            set
            {
                if (string.IsNullOrWhiteSpace(value)) return;
                _settings.NotificationSound = value;
                OnPropertyChanged();
                _configService.SaveSettings(_settings);
            }
        }

        public bool AutoStart
        {
            get => _settings.AutoStart;
            set
            {
                _settings.AutoStart = value;
                OnPropertyChanged();
                ApplyAutoStartRegistry(value);
                _configService.SaveSettings(_settings);
            }
        }

        public bool MinimizeToTray
        {
            get => _settings.MinimizeToTray;
            set
            {
                _settings.MinimizeToTray = value;
                OnPropertyChanged();
                _configService.SaveSettings(_settings);
            }
        }

        public string CallSign
        {
            get => ActiveProfileVariables.CallSign;
            set => SaveProfileVariable(value, ActiveProfileVariables.CallSign, v => ActiveProfileVariables.CallSign = v, nameof(CallSign));
        }

        public string Rank
        {
            get => ActiveProfileVariables.Rank;
            set => SaveProfileVariable(value, ActiveProfileVariables.Rank, v => ActiveProfileVariables.Rank = v, nameof(Rank));
        }

        public string FullName
        {
            get => ActiveProfileVariables.FullName;
            set => SaveProfileVariable(value, ActiveProfileVariables.FullName, v => ActiveProfileVariables.FullName = v, nameof(FullName));
        }

        public string Department
        {
            get => ActiveProfileVariables.Department;
            set => SaveProfileVariable(value, ActiveProfileVariables.Department, v => ActiveProfileVariables.Department = v, nameof(Department));
        }

        public string Organization
        {
            get => ActiveProfileVariables.Organization;
            set => SaveProfileVariable(value, ActiveProfileVariables.Organization, v => ActiveProfileVariables.Organization = v, nameof(Organization));
        }

        private ProfileVariables ActiveProfileVariables => _settings.GetProfileVariables(_profileService.ActiveProfile.Id);

        private void SaveProfileVariable(string value, string currentValue, Action<string> update, string propertyName)
        {
            string normalized = value ?? string.Empty;
            if (currentValue == normalized) return;

            update(normalized);
            OnPropertyChanged(propertyName);
            _configService.SaveSettings(_settings);
        }

        public List<Profile> Profiles => _profileService.Profiles;

        public Profile SelectedProfile
        {
            get => _profileService.ActiveProfile;
            set
            {
                if (value != null && value.Id != _profileService.ActiveProfile.Id)
                {
                    _profileService.SwitchProfile(value.Id);
                }
            }
        }

        // Команды
        public ICommand AddBindCommand { get; }
        public ICommand EditBindCommand { get; }
        public ICommand SaveBindEditCommand { get; }
        public ICommand CancelBindEditCommand { get; }
        public ICommand DeleteBindCommand { get; }
        public ICommand DuplicateBindCommand { get; }
        public ICommand RunBindCommand { get; }

        public ICommand AddTabCommand { get; }
        public ICommand RenameTabCommand { get; }
        public ICommand SaveTabCommand { get; }
        public ICommand CancelTabEditCommand { get; }
        public ICommand DeleteTabCommand { get; }

        public ICommand SwitchViewModeCommand { get; }
        public BindEditViewModel? BindEditor
        {
            get => _bindEditor;
            private set => SetProperty(ref _bindEditor, value);
        }

        public bool IsBindEditorOpen
        {
            get => _isBindEditorOpen;
            private set => SetProperty(ref _isBindEditorOpen, value);
        }

        public string TabName
        {
            get => _tabName;
            set
            {
                if (SetProperty(ref _tabName, value))
                {
                    TabValidationMessage = string.Empty;
                }
            }
        }

        public string TabValidationMessage
        {
            get => _tabValidationMessage;
            private set => SetProperty(ref _tabValidationMessage, value);
        }

        public string HotkeyStatus
        {
            get => string.IsNullOrEmpty(_hotkeyStatus)
                ? "Горячие клавиши запускают бинды."
                : $"Не зарегистрированы горячие клавиши: {_hotkeyStatus}";
            private set => SetProperty(ref _hotkeyStatus, value);
        }

        public bool IsTabEditorOpen
        {
            get => _isTabEditorOpen;
            private set => SetProperty(ref _isTabEditorOpen, value);
        }

        private void OnProfileChanged(Profile profile)
        {
            SaveCurrentConfig();
            _loadedProfileId = profile.Id;
            _config = _configService.LoadBinds(profile.Id);
            _settings.ActiveProfile = profile.Id;
            _configService.SaveSettings(_settings);
            OnPropertyChanged(nameof(Tabs));
            OnPropertyChanged(nameof(SelectedProfile));
            OnPropertyChanged(nameof(CallSign));
            OnPropertyChanged(nameof(Rank));
            OnPropertyChanged(nameof(FullName));
            OnPropertyChanged(nameof(Department));
            OnPropertyChanged(nameof(Organization));
            SelectedBind = null;
            SelectedTab = Tabs.FirstOrDefault();
            RegisterHotkeys();
            _trayService.RebuildMenu(_config);
        }

        public void SaveCurrentConfig()
        {
            _configService.SaveBinds(_config, _loadedProfileId);
        }

        public void RegisterHotkeys()
        {
            _hotkeyManager.UnregisterAll();
            _activeHotkeyIds.Clear();
            var failedHotkeys = new List<string>();

            foreach (var tab in _config.Tabs)
            {
                foreach (var bind in tab.Binds)
                {
                    if (bind.Enabled && !string.IsNullOrWhiteSpace(bind.Hotkey) && bind.Hotkey != "Не назначен")
                    {
                        var b = bind;
                        int id = _hotkeyManager.Register(b.Hotkey, () => ExecuteBind(b));
                        if (id >= 0)
                        {
                            _activeHotkeyIds[b.Id] = id;
                        }
                        else
                        {
                            failedHotkeys.Add(b.Hotkey);
                        }
                    }
                }
            }

            HotkeyStatus = string.Join(", ", failedHotkeys.Distinct(StringComparer.OrdinalIgnoreCase));
            _trayService.RebuildMenu(_config);
        }

        public void ExecuteBind(Bind? bind)
        {
            if (bind == null || !bind.Enabled || bind.Steps.Count == 0) return;
            _bindExecutor.Start(bind);
        }

        private void AddBind()
        {
            if (SelectedTab == null) return;

            _editingTab = SelectedTab;
            _editingTarget = new Bind { Name = "Новый бинд", Hotkey = "Не назначен" };
            _editingTarget.Steps.Add("/me выполнил действие");
            _isAddingBind = true;
            BindEditor = new BindEditViewModel(_editingTarget);
            IsBindEditorOpen = true;
        }

        private void EditBind()
        {
            if (SelectedBind == null) return;

            _editingTab = SelectedTab;
            _editingTarget = SelectedBind;
            _isAddingBind = false;
            BindEditor = new BindEditViewModel(SelectedBind.Clone());
            IsBindEditorOpen = true;
        }

        private void SaveBindEdit()
        {
            if (BindEditor == null || _editingTarget == null) return;
            if (string.IsNullOrWhiteSpace(BindEditor.BindName))
            {
                BindEditor.ValidationMessage = "Укажи название бинда.";
                return;
            }

            var edited = BindEditor.EditedBind;
            if (edited.Enabled && !string.IsNullOrWhiteSpace(edited.Hotkey) &&
                !edited.Hotkey.Equals("Не назначен", StringComparison.OrdinalIgnoreCase))
            {
                if (!GlobalHotkeyManager.TryParseHotkey(edited.Hotkey, out _, out _))
                {
                    BindEditor.ValidationMessage = "Не удалось распознать эту горячую клавишу.";
                    return;
                }

                bool duplicate = _config.Tabs
                    .SelectMany(tab => tab.Binds)
                    .Any(bind => !ReferenceEquals(bind, _editingTarget) &&
                                 bind.Enabled &&
                                 HotkeysEquivalent(bind.Hotkey, edited.Hotkey));
                if (duplicate)
                {
                    BindEditor.ValidationMessage = "Эта горячая клавиша уже назначена другому бинду.";
                    return;
                }
            }

            edited.Steps = new ObservableCollection<string>(
                BindEditor.Steps.Select(step => step.Text));

            if (_isAddingBind)
            {
                if (_editingTab == null) return;
                _editingTab.Binds.Add(edited);
                SelectedBind = edited;
            }
            else
            {
                _editingTarget.Name = edited.Name;
                _editingTarget.Hotkey = edited.Hotkey;
                _editingTarget.Enabled = edited.Enabled;
                _editingTarget.Steps = edited.Steps;
            }

            CloseBindEditor();
            SaveCurrentConfig();
            RegisterHotkeys();
            OnPropertyChanged(nameof(FilteredBinds));
        }

        private static bool HotkeysEquivalent(string first, string second)
        {
            return GlobalHotkeyManager.TryParseHotkey(first, out var firstKey, out var firstModifiers) &&
                   GlobalHotkeyManager.TryParseHotkey(second, out var secondKey, out var secondModifiers) &&
                   firstKey == secondKey &&
                   firstModifiers == secondModifiers;
        }

        private void CancelBindEdit()
        {
            CloseBindEditor();
        }

        private void CloseBindEditor()
        {
            IsBindEditorOpen = false;
            BindEditor = null;
            _editingTarget = null;
            _editingTab = null;
            _isAddingBind = false;
        }

        private void DeleteBind()
        {
            if (SelectedBind == null || SelectedTab == null) return;

            var res = MessageBox.Show($"Удалить бинд «{SelectedBind.Name}»?", "Подтверждение", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (res == MessageBoxResult.Yes)
            {
                SelectedTab.Binds.Remove(SelectedBind);
                SelectedBind = null;
                SaveCurrentConfig();
                RegisterHotkeys();
                OnPropertyChanged(nameof(FilteredBinds));
            }
        }

        private void DuplicateBind()
        {
            if (SelectedBind == null || SelectedTab == null) return;

            var dup = SelectedBind.Clone();
            dup.Name += " (Копия)";
            SelectedTab.Binds.Add(dup);
            SaveCurrentConfig();
            RegisterHotkeys();
            OnPropertyChanged(nameof(FilteredBinds));
        }

        private void AddTab()
        {
            _tabBeingEdited = null;
            TabName = $"Вкладка {Tabs.Count + 1}";
            IsTabEditorOpen = true;
        }

        private void RenameTab(Tab? tab)
        {
            var target = tab ?? SelectedTab;
            if (target == null) return;

            _tabBeingEdited = target;
            TabName = target.Name;
            IsTabEditorOpen = true;
        }

        private void SaveTab()
        {
            if (string.IsNullOrWhiteSpace(TabName))
            {
                TabValidationMessage = "Укажи название категории.";
                return;
            }

            if (_tabBeingEdited == null)
            {
                var tab = new Tab { Name = TabName.Trim() };
                Tabs.Add(tab);
                SelectedTab = tab;
            }
            else
            {
                _tabBeingEdited.Name = TabName.Trim();
            }

            IsTabEditorOpen = false;
            _tabBeingEdited = null;
            SaveCurrentConfig();
            _trayService.RebuildMenu(_config);
        }

        private void DeleteTab(Tab? tab)
        {
            var target = tab ?? SelectedTab;
            if (target == null || Tabs.Count <= 1)
            {
                MessageBox.Show("Нельзя удалить последнюю категорию.", "Внимание", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var res = MessageBox.Show($"Удалить категорию «{target.Name}» со всеми её биндами?", "Удаление вкладки", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (res == MessageBoxResult.Yes)
            {
                Tabs.Remove(target);
                SelectedTab = Tabs.FirstOrDefault();
                SaveCurrentConfig();
                RegisterHotkeys();
            }
        }

        private void ApplyAutoStartRegistry(bool enable)
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true);
                if (key != null)
                {
                    string exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName ?? "";
                    if (enable)
                    {
                        key.SetValue("RPBinder", $"\"{exePath}\"");
                    }
                    else
                    {
                        key.DeleteValue("RPBinder", false);
                    }
                }
            }
            catch (Exception ex)
            {
                Serilog.Log.Error(ex, "Ошибка настройки автозапуска");
            }
        }

        public void Cleanup()
        {
            SaveCurrentConfig();
            _notepadService.Save(_notepadContent);
            _hotkeyManager.UnregisterAll();
        }
    }
}
