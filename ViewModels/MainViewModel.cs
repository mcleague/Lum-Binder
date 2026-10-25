using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
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
        private readonly WorkshopService _workshopService;

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
        private string _workshopStatus = "Загрузка каталога...";
        private string _workshopAdminHardwareId = string.Empty;
        private string _workshopAdminPackageName = string.Empty;
        private string _workshopAdminPackageDescription = string.Empty;
        private string _workshopPhotoName = string.Empty;
        private string _workshopPhotoDescription = string.Empty;
        private string _workshopPhotoPath = string.Empty;
        private string _workshopAdminStatus = "Откройте вкладку, чтобы проверить доступ.";
        private bool _isLoadingWorkshop;
        private bool _isCheckingWorkshopAdmin;
        private bool _isPublishingWorkshopPackage;
        private bool _isWorkshopAdminAuthorized;
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
            AppSettings settings,
            WorkshopService workshopService)
        {
            _configService = configService;
            _profileService = profileService;
            _hotkeyManager = hotkeyManager;
            _bindExecutor = bindExecutor;
            _trayService = trayService;
            _settings = settings;
            _workshopService = workshopService;

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
            RefreshWorkshopCommand = new RelayCommand(() => _ = LoadWorkshopAsync());
            InstallWorkshopItemCommand = new RelayCommand<WorkshopItem>(item => _ = InstallWorkshopItemAsync(item));
            CheckWorkshopAdminAccessCommand = new RelayCommand(() => _ = CheckWorkshopAdminAccessAsync());
            CopyWorkshopAdminHardwareIdCommand = new RelayCommand(CopyWorkshopAdminHardwareId);
            PublishWorkshopBindsCommand = new RelayCommand(
                () => _ = PublishWorkshopBindsAsync(),
                () => IsWorkshopAdminAuthorized && !_isPublishingWorkshopPackage);
            ChooseWorkshopPhotoCommand = new RelayCommand(ChooseWorkshopPhoto);
            PublishWorkshopPhotoCommand = new RelayCommand(
                () => _ = PublishWorkshopPhotoAsync(),
                () => IsWorkshopAdminAuthorized && !_isPublishingWorkshopPackage);

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
                if (SetProperty(ref _selectedTab, value))
                {
                    OnPropertyChanged(nameof(CurrentBinds));
                    OnPropertyChanged(nameof(FilteredBinds));
                    RegisterHotkeys();
                }
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
                    OnPropertyChanged(nameof(IsWorkshopView));
                    OnPropertyChanged(nameof(IsWorkshopAdminView));
                    if (value == 5)
                    {
                        _ = LoadWorkshopAsync();
                    }
                    else if (value == 6)
                    {
                        _ = CheckWorkshopAdminAccessAsync();
                    }
                }
            }
        }

        public bool IsBindsView => CurrentViewMode == 0;
        public bool IsVariablesView => CurrentViewMode == 1;
        public bool IsNotepadView => CurrentViewMode == 2;
        public bool IsDescriptionView => CurrentViewMode == 3;
        public bool IsSettingsView => CurrentViewMode == 4;
        public bool IsWorkshopView => CurrentViewMode == 5;
        public bool ShowWorkshopAdmin => WorkshopAdminSettings.IsAdminBuild;
        public bool IsWorkshopAdminView => ShowWorkshopAdmin && CurrentViewMode == 6;
        public string VersionLabel =>
            $"Version - {Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "1.0.2"}";

        public ObservableCollection<WorkshopItem> WorkshopItems { get; } = new();

        public string WorkshopAdminHardwareId
        {
            get => _workshopAdminHardwareId;
            private set => SetProperty(ref _workshopAdminHardwareId, value);
        }

        public string WorkshopAdminPackageName
        {
            get => _workshopAdminPackageName;
            set => SetProperty(ref _workshopAdminPackageName, value);
        }

        public string WorkshopAdminPackageDescription
        {
            get => _workshopAdminPackageDescription;
            set => SetProperty(ref _workshopAdminPackageDescription, value);
        }

        public string WorkshopPhotoName
        {
            get => _workshopPhotoName;
            set => SetProperty(ref _workshopPhotoName, value);
        }

        public string WorkshopPhotoDescription
        {
            get => _workshopPhotoDescription;
            set => SetProperty(ref _workshopPhotoDescription, value);
        }

        public string WorkshopPhotoPath
        {
            get => _workshopPhotoPath;
            private set
            {
                if (SetProperty(ref _workshopPhotoPath, value))
                {
                    OnPropertyChanged(nameof(WorkshopPhotoPreview));
                }
            }
        }

        public string WorkshopPhotoPreview => WorkshopPhotoPath;

        public string WorkshopAdminStatus
        {
            get => _workshopAdminStatus;
            private set => SetProperty(ref _workshopAdminStatus, value);
        }

        public bool IsWorkshopAdminAuthorized
        {
            get => _isWorkshopAdminAuthorized;
            private set
            {
                if (SetProperty(ref _isWorkshopAdminAuthorized, value))
                {
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        public string WorkshopStatus
        {
            get => _workshopStatus;
            private set => SetProperty(ref _workshopStatus, value);
        }

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
        public ICommand RefreshWorkshopCommand { get; }
        public ICommand InstallWorkshopItemCommand { get; }
        public ICommand CheckWorkshopAdminAccessCommand { get; }
        public ICommand CopyWorkshopAdminHardwareIdCommand { get; }
        public ICommand PublishWorkshopBindsCommand { get; }
        public ICommand ChooseWorkshopPhotoCommand { get; }
        public ICommand PublishWorkshopPhotoCommand { get; }

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
            _trayService.RebuildMenu(_config);
        }

        private async Task LoadWorkshopAsync()
        {
            if (_isLoadingWorkshop) return;

            _isLoadingWorkshop = true;
            WorkshopStatus = "Загрузка каталога...";
            try
            {
                var items = await _workshopService.LoadCatalogAsync();
                WorkshopItems.Clear();
                foreach (var item in items)
                {
                    WorkshopItems.Add(item);
                }

                WorkshopStatus = $"Доступно наборов: {WorkshopItems.Count}";
            }
            catch (Exception ex)
            {
                Serilog.Log.Error(ex, "Не удалось загрузить каталог мастерской");
                WorkshopStatus = $"Не удалось загрузить мастерскую: {ex.Message}";
            }
            finally
            {
                _isLoadingWorkshop = false;
            }
        }

        private async Task CheckWorkshopAdminAccessAsync()
        {
            if (_isCheckingWorkshopAdmin) return;

            _isCheckingWorkshopAdmin = true;
            IsWorkshopAdminAuthorized = false;
            WorkshopAdminStatus = "Проверка доступа к мастерской...";
            try
            {
                WorkshopAdminHardwareId = _workshopService.GetCurrentHardwareId();
                IsWorkshopAdminAuthorized = await _workshopService.CheckAdminHardwareIdAsync(WorkshopAdminHardwareId);
                WorkshopAdminStatus = IsWorkshopAdminAuthorized
                    ? "Доступ разрешён. Можно публиковать бинды выбранной вкладки."
                    : "Для этого устройства доступ не настроен. Добавьте его HWID в секрет Worker.";
            }
            catch (Exception ex)
            {
                Serilog.Log.Error(ex, "Не удалось проверить доступ к админке мастерской");
                WorkshopAdminStatus = $"Не удалось проверить доступ: {ex.Message}";
            }
            finally
            {
                _isCheckingWorkshopAdmin = false;
            }
        }

        private void CopyWorkshopAdminHardwareId()
        {
            if (string.IsNullOrEmpty(WorkshopAdminHardwareId))
            {
                WorkshopAdminStatus = "Сначала проверьте доступ, чтобы получить HWID этого устройства.";
                return;
            }

            try
            {
                Clipboard.SetText(WorkshopAdminHardwareId);
                WorkshopAdminStatus = "Хэш HWID скопирован.";
            }
            catch (Exception ex)
            {
                Serilog.Log.Error(ex, "Не удалось скопировать HWID админки мастерской");
                WorkshopAdminStatus = $"Не удалось скопировать HWID: {ex.Message}";
            }
        }

        private async Task PublishWorkshopBindsAsync()
        {
            if (!IsWorkshopAdminAuthorized || _isPublishingWorkshopPackage) return;

            string name = WorkshopAdminPackageName.Trim();
            string description = WorkshopAdminPackageDescription.Trim();
            if (name.Length is < 1 or > 60)
            {
                WorkshopAdminStatus = "Название должно содержать от 1 до 60 символов.";
                return;
            }

            if (description.Length is < 1 or > 250)
            {
                WorkshopAdminStatus = "Описание должно содержать от 1 до 250 символов.";
                return;
            }

            if (SelectedTab == null || SelectedTab.Binds.Count is < 1 or > 100)
            {
                WorkshopAdminStatus = "Выберите вкладку, содержащую от 1 до 100 биндов.";
                return;
            }

            if (SelectedTab.Binds.Any(bind =>
                    string.IsNullOrWhiteSpace(bind.Name) ||
                    bind.Name.Length > 80 ||
                    bind.Hotkey.Length > 40 ||
                    bind.Steps.Count is < 1 or > 25 ||
                    bind.Steps.Any(step => step.Length > 1000)))
            {
                WorkshopAdminStatus = "Проверьте бинды: название до 80 символов, клавиша до 40, от 1 до 25 шагов по 1000 символов.";
                return;
            }

            _isPublishingWorkshopPackage = true;
            CommandManager.InvalidateRequerySuggested();
            WorkshopAdminStatus = $"Публикация биндов вкладки «{SelectedTab.Name}»...";
            try
            {
                await _workshopService.PublishBindsAsync(
                    WorkshopAdminHardwareId,
                    name,
                    description,
                    SelectedTab.Binds);
                WorkshopAdminStatus = $"Набор «{name}» опубликован. Обновляю каталог...";
                await LoadWorkshopAsync();
                WorkshopAdminStatus = $"Набор «{name}» опубликован в мастерской.";
            }
            catch (Exception ex)
            {
                Serilog.Log.Error(ex, "Не удалось опубликовать бинды мастерской");
                WorkshopAdminStatus = $"Не удалось опубликовать набор: {ex.Message}";
            }
            finally
            {
                _isPublishingWorkshopPackage = false;
                CommandManager.InvalidateRequerySuggested();
            }
        }

        private void ChooseWorkshopPhoto()
        {
            var dialog = new OpenFileDialog
            {
                Title = "Выбери фото для новой записи мастерской",
                Filter = "Изображения JPG и PNG|*.jpg;*.jpeg;*.png",
                CheckFileExists = true,
                Multiselect = false
            };

            if (dialog.ShowDialog() != true) return;

            var photo = new FileInfo(dialog.FileName);
            if (photo.Length is < 1 or > 3 * 1_048_576)
            {
                WorkshopAdminStatus = "Фото должно быть размером не более 3 МБ.";
                return;
            }

            WorkshopPhotoPath = photo.FullName;
            WorkshopAdminStatus = $"Фото выбрано: {photo.Name}";
        }

        private async Task PublishWorkshopPhotoAsync()
        {
            if (!IsWorkshopAdminAuthorized || _isPublishingWorkshopPackage) return;

            string name = WorkshopPhotoName.Trim();
            string description = WorkshopPhotoDescription.Trim();
            if (name.Length is < 1 or > 60)
            {
                WorkshopAdminStatus = "Название записи должно содержать от 1 до 60 символов.";
                return;
            }

            if (description.Length is < 1 or > 250)
            {
                WorkshopAdminStatus = "Описание записи должно содержать от 1 до 250 символов.";
                return;
            }

            if (string.IsNullOrWhiteSpace(WorkshopPhotoPath))
            {
                WorkshopAdminStatus = "Сначала выбери фото JPG или PNG.";
                return;
            }

            _isPublishingWorkshopPackage = true;
            CommandManager.InvalidateRequerySuggested();
            WorkshopAdminStatus = $"Публикация фото-записи «{name}»...";
            try
            {
                await _workshopService.PublishPhotoAsync(
                    WorkshopAdminHardwareId,
                    name,
                    description,
                    WorkshopPhotoPath);
                await LoadWorkshopAsync();
                WorkshopAdminStatus = $"Фото-запись «{name}» опубликована. Старые записи не изменялись.";
            }
            catch (Exception ex)
            {
                Serilog.Log.Error(ex, "Не удалось опубликовать фото-запись мастерской");
                WorkshopAdminStatus = $"Не удалось опубликовать фото-запись: {ex.Message}";
            }
            finally
            {
                _isPublishingWorkshopPackage = false;
                CommandManager.InvalidateRequerySuggested();
            }
        }

        private async Task InstallWorkshopItemAsync(WorkshopItem? item)
        {
            if (item == null) return;

            WorkshopStatus = $"Загрузка «{item.Name}»...";
            try
            {
                var package = await _workshopService.DownloadPackageAsync(item);
                if (item.Type == "profile")
                {
                    string profileId = $"workshop_{Guid.NewGuid():N}";
                    _configService.SaveBinds(new BinderConfig { Tabs = package.Tabs }, profileId);
                    _settings.ProfileNamesById[profileId] = item.Name;
                    _configService.SaveSettings(_settings);
                    var profile = new Profile { Id = profileId, Name = item.Name };
                    _profileService.AddProfile(profile);
                    OnPropertyChanged(nameof(Profiles));
                    _profileService.SwitchProfile(profileId);
                    WorkshopStatus = $"Профиль «{item.Name}» установлен и активирован";
                    return;
                }

                if (item.Type != "binds")
                {
                    throw new InvalidDataException($"Неизвестный тип набора: {item.Type}");
                }

                if (SelectedTab == null)
                {
                    WorkshopStatus = "Сначала выбери вкладку для установки биндов";
                    return;
                }

                int addedCount = 0;
                foreach (var bind in package.Binds)
                {
                    if (bind.Steps.Count == 0 ||
                        (bind.Enabled && !string.Equals(bind.Hotkey, "Не назначен", StringComparison.OrdinalIgnoreCase) &&
                         SelectedTab.Binds.Any(existing =>
                             existing.Enabled && HotkeysEquivalent(existing.Hotkey, bind.Hotkey))))
                    {
                        continue;
                    }

                    var importedBind = bind.Clone();
                    importedBind.Name = GetUniqueBindName(SelectedTab.Binds, importedBind.Name);
                    SelectedTab.Binds.Add(importedBind);
                    addedCount++;
                }

                if (addedCount > 0)
                {
                    SaveCurrentConfig();
                    RegisterHotkeys();
                    OnPropertyChanged(nameof(FilteredBinds));
                }

                WorkshopStatus = addedCount == 0
                    ? "Новые бинды не добавлены: сочетания клавиш заняты или пакет пуст"
                    : $"Добавлено биндов: {addedCount}";
            }
            catch (Exception ex)
            {
                Serilog.Log.Error(ex, "Не удалось установить набор мастерской {ItemId}", item.Id);
                WorkshopStatus = $"Ошибка установки «{item.Name}»: {ex.Message}";
            }
        }

        public void SaveCurrentConfig()
        {
            _configService.SaveBinds(_config, _loadedProfileId);
        }

        public void RegisterHotkeys()
        {
            _hotkeyManager.UnregisterAll();
            _activeHotkeyIds.Clear();
            if (SelectedTab != null)
            {
                foreach (var bind in SelectedTab.Binds)
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
                            Serilog.Log.Warning("Не зарегистрирована клавиша {Hotkey} для бинда {BindName}", b.Hotkey, b.Name);
                        }
                    }
                }
            }

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
                edited.Name = GetUniqueBindName(_editingTab.Binds, edited.Name);
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

        private static string GetUniqueBindName(IEnumerable<Bind> binds, string name)
        {
            string baseName = name.Trim();
            var existingNames = new HashSet<string>(
                binds.Select(bind => bind.Name.Trim()),
                StringComparer.OrdinalIgnoreCase);
            if (!existingNames.Contains(baseName))
            {
                return baseName;
            }

            for (int suffix = 1; ; suffix++)
            {
                string candidate = $"{baseName} ({suffix})";
                if (!existingNames.Contains(candidate))
                {
                    return candidate;
                }
            }
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
