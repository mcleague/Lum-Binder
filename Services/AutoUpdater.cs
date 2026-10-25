using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using Serilog;

namespace BinderApp.Services
{
    public class AutoUpdater
    {
        private const string ReleaseAssetName = "Lum-Binder-win-x64.zip";
        private static readonly HttpClient HttpClient = CreateHttpClient();
        private readonly ConfigService _configService;

        public AutoUpdater(ConfigService configService)
        {
            _configService = configService;
        }

        public async Task<bool> CheckAndApplyPendingUpdatesAsync(Action<string> setStatus)
        {
            string? temporaryPackagePath = null;
            if (WorkshopAdminSettings.IsAdminBuild)
            {
                Log.Information("Автообновление отключено для приватной админской сборки");
                setStatus("Автообновление отключено для админской сборки");
                return false;
            }

            string repository = GitHubRepositorySettings.GetRepository();

            if (string.IsNullOrWhiteSpace(repository))
            {
                Log.Information("Автообновление не настроено: не указан публичный GitHub-репозиторий");
                setStatus("Автообновление пока не настроено");
                return false;
            }

            try
            {
                setStatus("Проверка обновлений");
                string releaseUrl = $"https://api.github.com/repos/{repository}/releases/latest";
                using var releaseResponse = await HttpClient.GetAsync(releaseUrl);
                if (releaseResponse.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    Log.Information("В репозитории {Repository} пока нет опубликованных релизов", repository);
                    setStatus("Обновлений пока нет");
                    return false;
                }

                releaseResponse.EnsureSuccessStatusCode();
                await using var releaseStream = await releaseResponse.Content.ReadAsStreamAsync();
                using var release = await JsonDocument.ParseAsync(releaseStream);
                string tagName = release.RootElement.GetProperty("tag_name").GetString() ?? string.Empty;
                if (!TryParseVersion(tagName, out Version? latestVersion))
                {
                    throw new InvalidDataException($"Версия релиза имеет некорректный формат: {tagName}");
                }

                Version currentVersion = Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(0, 0);
                if (latestVersion <= currentVersion)
                {
                    Log.Information("Установлена актуальная версия {Version}", currentVersion);
                    setStatus("У вас актуальная версия");
                    return false;
                }

                JsonElement asset = release.RootElement.GetProperty("assets")
                    .EnumerateArray()
                    .FirstOrDefault(item =>
                        string.Equals(item.GetProperty("name").GetString(), ReleaseAssetName, StringComparison.Ordinal));
                if (asset.ValueKind == JsonValueKind.Undefined)
                {
                    throw new InvalidDataException($"В релизе {tagName} отсутствует файл {ReleaseAssetName}");
                }

                string downloadUrl = asset.GetProperty("browser_download_url").GetString()
                    ?? throw new InvalidDataException("У файла обновления отсутствует ссылка для скачивания");

                setStatus($"Загрузка обновления {tagName}");
                string packageId = $"{Environment.ProcessId}-{Guid.NewGuid():N}";
                string packagePath = Path.Combine(_configService.UpdatesDir, $"{ReleaseAssetName}.{packageId}.zip");
                temporaryPackagePath = Path.Combine(
                    _configService.UpdatesDir,
                    $"{ReleaseAssetName}.{packageId}.download");
                using (var packageResponse = await HttpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead))
                {
                    packageResponse.EnsureSuccessStatusCode();
                    await using var output = new FileStream(
                        temporaryPackagePath,
                        FileMode.CreateNew,
                        FileAccess.Write,
                        FileShare.None);
                    await packageResponse.Content.CopyToAsync(output);
                }
                File.Move(temporaryPackagePath, packagePath, overwrite: true);

                string stagingDirectory = Path.Combine(
                    _configService.UpdatesDir,
                    $"staging-{tagName}-{Environment.ProcessId}-{Guid.NewGuid():N}");
                ExtractPackage(packagePath, stagingDirectory);

                string executablePath = Environment.ProcessPath
                    ?? throw new InvalidOperationException("Не удалось определить путь к исполняемому файлу");
                string installDirectory = Path.GetDirectoryName(executablePath)
                    ?? throw new InvalidOperationException("Не удалось определить папку приложения");
                if (!File.Exists(Path.Combine(stagingDirectory, Path.GetFileName(executablePath))))
                {
                    throw new InvalidDataException("В архиве обновления отсутствует исполняемый файл приложения");
                }

                string scriptPath = Path.Combine(
                    _configService.UpdatesDir,
                    $"install-update-{Environment.ProcessId}-{Guid.NewGuid():N}.ps1");
                File.WriteAllText(scriptPath, InstallScript);
                var installer = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                installer.ArgumentList.Add("-NoProfile");
                installer.ArgumentList.Add("-ExecutionPolicy");
                installer.ArgumentList.Add("Bypass");
                installer.ArgumentList.Add("-File");
                installer.ArgumentList.Add(scriptPath);
                installer.ArgumentList.Add("-ProcessId");
                installer.ArgumentList.Add(Environment.ProcessId.ToString());
                installer.ArgumentList.Add("-InstallDirectory");
                installer.ArgumentList.Add(installDirectory);
                installer.ArgumentList.Add("-StagingDirectory");
                installer.ArgumentList.Add(stagingDirectory);
                installer.ArgumentList.Add("-PackagePath");
                installer.ArgumentList.Add(packagePath);
                installer.ArgumentList.Add("-ExecutableName");
                installer.ArgumentList.Add(Path.GetFileName(executablePath));

                if (Process.Start(installer) == null)
                {
                    throw new InvalidOperationException("Не удалось запустить установщик обновления");
                }

                Log.Information("Загружена версия {Version}; установка начнется после закрытия приложения", latestVersion);
                setStatus("Установка обновления");
                return true;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Не удалось проверить или установить обновления");
                setStatus("Не удалось проверить обновления; запуск приложения");
                return false;
            }
            finally
            {
                if (Directory.Exists(_configService.UpdatesDir))
                {
                    if (temporaryPackagePath != null && File.Exists(temporaryPackagePath))
                    {
                        try
                        {
                            File.Delete(temporaryPackagePath);
                        }
                        catch (IOException ex)
                        {
                            Log.Warning(ex, "Временный файл обновления еще используется: {Path}", temporaryPackagePath);
                        }
                        catch (UnauthorizedAccessException ex)
                        {
                            Log.Warning(ex, "Не удалось удалить временный файл обновления: {Path}", temporaryPackagePath);
                        }
                    }
                }
            }
        }

        private static HttpClient CreateHttpClient()
        {
            var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
            client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("LumBinder", "1.0"));
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            return client;
        }

        private static bool TryParseVersion(string tagName, out Version version)
        {
            string normalizedTag = tagName.StartsWith('v') || tagName.StartsWith('V')
                ? tagName.Substring(1)
                : tagName;
            if (Version.TryParse(normalizedTag, out Version? parsedVersion) && parsedVersion != null)
            {
                version = parsedVersion;
                return true;
            }

            version = new Version(0, 0);
            return false;
        }

        private static void ExtractPackage(string packagePath, string destinationDirectory)
        {
            Directory.CreateDirectory(destinationDirectory);
            string destinationRoot = Path.GetFullPath(destinationDirectory) + Path.DirectorySeparatorChar;

            using var archive = ZipFile.OpenRead(packagePath);
            foreach (var entry in archive.Entries)
            {
                string targetPath = Path.GetFullPath(Path.Combine(destinationDirectory, entry.FullName));
                if (!targetPath.StartsWith(destinationRoot, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException($"Архив содержит недопустимый путь: {entry.FullName}");
                }

                if (string.IsNullOrEmpty(entry.Name))
                {
                    Directory.CreateDirectory(targetPath);
                    continue;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
                entry.ExtractToFile(targetPath, overwrite: true);
            }
        }

        private const string InstallScript = @"
param(
    [int]$ProcessId,
    [string]$InstallDirectory,
    [string]$StagingDirectory,
    [string]$PackagePath,
    [string]$ExecutableName
)
$ErrorActionPreference = 'Stop'
try {
    while (Get-Process -Id $ProcessId -ErrorAction SilentlyContinue) {
        Start-Sleep -Milliseconds 300
    }
    Copy-Item -Path (Join-Path $StagingDirectory '*') -Destination $InstallDirectory -Recurse -Force
    Start-Process -FilePath (Join-Path $InstallDirectory $ExecutableName)
    Remove-Item -LiteralPath $StagingDirectory -Recurse -Force
    Remove-Item -LiteralPath $PackagePath -Force
} catch {
    Start-Process -FilePath (Join-Path $InstallDirectory $ExecutableName)
    throw
}";
    }
}
