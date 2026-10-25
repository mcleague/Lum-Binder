using System;
using System.Collections.Generic;
using System.IO;
using BinderApp.Models;
using Serilog;

namespace BinderApp.Services
{
    /// <summary>
    /// Сервис управления профилями пользователей.
    /// Переключает активный binder.json.
    /// </summary>
    public class ProfileService
    {
        private readonly ConfigService _configService;
        private readonly AppSettings _settings;
        public List<Profile> Profiles { get; private set; } = new();
        public Profile ActiveProfile { get; private set; }

        public event Action<Profile>? ProfileChanged;

        public ProfileService(ConfigService configService, AppSettings settings)
        {
            _configService = configService;
            _settings = settings;

            // Загружаем существующие профили или создаем базовые
            DiscoverProfiles();

            string currentId = settings.ActiveProfile ?? "default";
            ActiveProfile = Profiles.Find(p => p.Id.Equals(currentId, StringComparison.OrdinalIgnoreCase)) ?? Profiles[0];
        }

        public void DiscoverProfiles()
        {
            Profiles.Clear();
            Profiles.Add(new Profile { Id = "default", Name = "Основной" });
            Profiles.Add(new Profile { Id = "work", Name = "Рабочий" });
            Profiles.Add(new Profile { Id = "game", Name = "Игровой" });

            try
            {
                if (Directory.Exists(_configService.ConfigDir))
                {
                    var files = Directory.GetFiles(_configService.ConfigDir, "binder_*.json");
                    foreach (var file in files)
                    {
                        string fn = Path.GetFileNameWithoutExtension(file);
                        string profileId = fn.Substring("binder_".Length);
                        if (!Profiles.Exists(p => p.Id.Equals(profileId, StringComparison.OrdinalIgnoreCase)))
                        {
                            string displayName = _settings.ProfileNamesById.TryGetValue(profileId, out string? savedName)
                                ? savedName
                                : profileId;
                            Profiles.Add(new Profile { Id = profileId, Name = displayName });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Ошибка обнаружения профилей");
            }
        }

        public void AddProfile(Profile profile)
        {
            if (!Profiles.Exists(existing => existing.Id.Equals(profile.Id, StringComparison.OrdinalIgnoreCase)))
            {
                Profiles.Add(profile);
            }
        }

        public void SwitchProfile(string profileId)
        {
            if (string.IsNullOrWhiteSpace(profileId)) return;

            var target = Profiles.Find(p => p.Id.Equals(profileId, StringComparison.OrdinalIgnoreCase));
            if (target == null)
            {
                target = new Profile { Id = profileId, Name = profileId };
                Profiles.Add(target);
            }

            ActiveProfile = target;
            Log.Information("Переключен профиль на {ProfileId}", profileId);
            ProfileChanged?.Invoke(ActiveProfile);
        }
    }
}
