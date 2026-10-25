using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using BinderApp.Models;
using Serilog;

namespace BinderApp.Services
{
    /// <summary>
    /// Сервис управления путями, структурой папок на диске (Program Files / LocalAppData)
    /// и загрузкой/сохранением binder.json и settings.json.
    /// </summary>
    public class ConfigService
    {
        private readonly JsonSerializerOptions _jsonOptions;

        public string RootDir { get; private set; } = string.Empty;
        public string ConfigDir { get; private set; } = string.Empty;
        public string LogsDir { get; private set; } = string.Empty;
        public string UpdatesDir { get; private set; } = string.Empty;

        public ConfigService()
        {
            _jsonOptions = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNameCaseInsensitive = true,
                Encoder = JavaScriptEncoder.Create(UnicodeRanges.All)
            };

            InitializeDirectories();
            InitializeLogging();
        }

        private void InitializeDirectories()
        {
            string preferredConfig = Path.Combine(AppContext.BaseDirectory, "config");
            string preferredUpdates = Path.Combine(AppContext.BaseDirectory, "updates");

            bool hasWriteAccess = false;
            try
            {
                if (!Directory.Exists(preferredConfig))
                {
                    Directory.CreateDirectory(preferredConfig);
                }
                string testFile = Path.Combine(preferredConfig, ".test");
                File.WriteAllText(testFile, "test");
                File.Delete(testFile);
                hasWriteAccess = true;

                RootDir = AppContext.BaseDirectory;
                ConfigDir = preferredConfig;
                UpdatesDir = preferredUpdates;
            }
            catch
            {
                hasWriteAccess = false;
            }

            if (!hasWriteAccess)
            {
                string localApp = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Binder");
                RootDir = localApp;
                ConfigDir = Path.Combine(localApp, "config");
                UpdatesDir = Path.Combine(localApp, "updates");

                Directory.CreateDirectory(ConfigDir);
            }

            LogsDir = Path.Combine(ConfigDir, "logs");
            Directory.CreateDirectory(LogsDir);
            Directory.CreateDirectory(UpdatesDir);
        }

        private void InitializeLogging()
        {
            try
            {
                string logFilePattern = Path.Combine(LogsDir, "binder_.log");

                Log.Logger = new LoggerConfiguration()
                    .MinimumLevel.Debug()
                    .WriteTo.File(
                        logFilePattern,
                        rollingInterval: RollingInterval.Day,
                        retainedFileCountLimit: 7,
                        outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss}] [{Level:u3}] {Message:lj}{NewLine}{Exception}"
                    )
                    .CreateLogger();

                Log.Information("Binder инициализирован. Директория: {RootDir}", RootDir);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ConfigService] Ошибка Serilog: {ex.Message}");
            }
        }

        public string GetProfileBinderPath(string profileName)
        {
            string clean = string.IsNullOrWhiteSpace(profileName) ? "default" : profileName.Trim();
            if (clean.Equals("default", StringComparison.OrdinalIgnoreCase))
            {
                return Path.Combine(ConfigDir, "binder.json");
            }
            return Path.Combine(ConfigDir, $"binder_{clean}.json");
        }

        public string SettingsFilePath => Path.Combine(ConfigDir, "settings.json");

        public AppSettings LoadSettings()
        {
            try
            {
                if (File.Exists(SettingsFilePath))
                {
                    string json = File.ReadAllText(SettingsFilePath);
                    var settings = JsonSerializer.Deserialize<AppSettings>(json, _jsonOptions);
                    if (settings != null)
                    {
                        if (settings.ProfileVariablesById.Count == 0 &&
                            (!string.IsNullOrWhiteSpace(settings.CallSign) ||
                             !string.IsNullOrWhiteSpace(settings.Rank) ||
                             !string.IsNullOrWhiteSpace(settings.FullName) ||
                             !string.IsNullOrWhiteSpace(settings.Department) ||
                             !string.IsNullOrWhiteSpace(settings.Organization)))
                        {
                            string profileId = string.IsNullOrWhiteSpace(settings.ActiveProfile)
                                ? "default"
                                : settings.ActiveProfile;
                            settings.ProfileVariablesById[profileId] = new ProfileVariables
                            {
                                CallSign = settings.CallSign,
                                Rank = settings.Rank,
                                FullName = settings.FullName,
                                Department = settings.Department,
                                Organization = settings.Organization
                            };

                            settings.CallSign = string.Empty;
                            settings.Rank = string.Empty;
                            settings.FullName = string.Empty;
                            settings.Department = string.Empty;
                            settings.Organization = string.Empty;
                            SaveSettings(settings);
                        }

                        return settings;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Ошибка чтения settings.json");
            }

            var defaultSettings = new AppSettings();
            SaveSettings(defaultSettings);
            return defaultSettings;
        }

        public void SaveSettings(AppSettings settings)
        {
            try
            {
                string json = JsonSerializer.Serialize(settings, _jsonOptions);
                File.WriteAllText(SettingsFilePath, json);
                Log.Information("Настройки сохранены в {Path}", SettingsFilePath);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Ошибка сохранения settings.json");
            }
        }

        public BinderConfig LoadBinds(string profileName = "default")
        {
            string path = GetProfileBinderPath(profileName);
            try
            {
                if (File.Exists(path))
                {
                    string json = File.ReadAllText(path);
                    var config = JsonSerializer.Deserialize<BinderConfig>(json, _jsonOptions);
                    if (config != null && config.Tabs != null && config.Tabs.Count > 0)
                    {
                        return config;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Ошибка загрузки binder.json для профиля {Profile}", profileName);
            }

            var defaultCfg = GetDefaultBinderConfig();
            SaveBinds(defaultCfg, profileName);
            return defaultCfg;
        }

        public void SaveBinds(BinderConfig config, string profileName = "default")
        {
            string path = GetProfileBinderPath(profileName);
            try
            {
                string json = JsonSerializer.Serialize(config, _jsonOptions);
                File.WriteAllText(path, json);
                Log.Information("Бинды сохранены в {Path}", path);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Ошибка сохранения binder.json");
            }
        }

        public BinderConfig GetDefaultBinderConfig()
        {
            var config = new BinderConfig();

            var fsbTab = new Tab { Name = "ФСБ" };
            fsbTab.Binds.Add(new Bind
            {
                Name = "Осмотр",
                Hotkey = "Ctrl+Shift+F1",
                Enabled = true,
                Steps = new ObservableCollection<string>
                {
                    "/me предъявил служебное удостоверение сотрудника ФСБ в развернутом виде",
                    "/do В удостоверении: Федеральная Служба Безопасности РФ.",
                    "/me надел стерильные перчатки и приступил к первичному осмотру"
                }
            });
            fsbTab.Binds.Add(new Bind
            {
                Name = "Задержание",
                Hotkey = "Ctrl+1",
                Enabled = true,
                Steps = new ObservableCollection<string>
                {
                    "/me резким движением заломил руки подозреваемого за спину",
                    "/me снял тактические наручники с пояса и защелкнул их на запястьях гражданина",
                    "/cuff"
                }
            });

            var mvdTab = new Tab { Name = "МВД" };
            mvdTab.Binds.Add(new Bind
            {
                Name = "Представление",
                Hotkey = "Ctrl+2",
                Enabled = true,
                Steps = new ObservableCollection<string>
                {
                    "Здравия желаю! Сотрудник МВД.",
                    "/me приложил правую руку к головному убору, затем достал удостоверение",
                    "/do В удостоверении: Отдел МВД России."
                }
            });

            var dpsTab = new Tab { Name = "ДПС" };
            dpsTab.Binds.Add(new Bind
            {
                Name = "Остановка ТС",
                Hotkey = "Ctrl+3",
                Enabled = true,
                Steps = new ObservableCollection<string>
                {
                    "/m Водитель транспортного средства, прижмитесь к обочине и остановитесь!",
                    "/m В случае неподчинения будет открыт огонь по колесам!"
                }
            });

            var medicTab = new Tab { Name = "Медик" };
            medicTab.Binds.Add(new Bind
            {
                Name = "Первая помощь",
                Hotkey = "Ctrl+4",
                Enabled = true,
                Steps = new ObservableCollection<string>
                {
                    "/me открыл медицинский саквояж и достал стерильные салфетки с антисептиком",
                    "/me обработал поврежденный участок кожи и наложил давящую повязку",
                    "/heal"
                }
            });

            config.Tabs.Add(fsbTab);
            config.Tabs.Add(mvdTab);
            config.Tabs.Add(dpsTab);
            config.Tabs.Add(medicTab);

            return config;
        }
    }
}
