using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Win32;
using BinderApp.Models;

namespace BinderApp.Services
{
    public sealed class WorkshopService
    {
        private const int MaxDocumentBytes = 1_048_576;
        private const int MaxWorkshopPhotoBytes = 3 * 1_048_576;
        private static readonly HttpClient HttpClient = CreateHttpClient();
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        public string GetCurrentHardwareId()
        {
            using RegistryKey baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using RegistryKey? key = baseKey.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography");
            string? machineGuid = key?.GetValue("MachineGuid") as string;
            if (string.IsNullOrWhiteSpace(machineGuid))
            {
                throw new InvalidOperationException("Не удалось получить идентификатор Windows для проверки доступа.");
            }

            byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(machineGuid.Trim().ToUpperInvariant()));
            return Convert.ToHexString(hash);
        }

        public async Task<bool> CheckAdminHardwareIdAsync(string hardwareId)
        {
            string url = GetAdminEndpoint("status");
            using var response = await HttpClient.PostAsJsonAsync(url, new { hardwareId }, JsonOptions);
            if (response.StatusCode == System.Net.HttpStatusCode.Forbidden)
            {
                return false;
            }

            await EnsureAdminResponseSuccessAsync(response);
            return true;
        }

        public async Task PublishBindsAsync(
            string hardwareId,
            string name,
            string description,
            IReadOnlyList<Bind> binds)
        {
            var request = new
            {
                hardwareId,
                name,
                description,
                package = new
                {
                    binds = binds.Select(bind => new
                    {
                        name = bind.Name,
                        hotkey = bind.Hotkey,
                        enabled = bind.Enabled,
                        steps = bind.Steps.ToArray()
                    })
                }
            };

            byte[] requestBytes = JsonSerializer.SerializeToUtf8Bytes(request, JsonOptions);
            using var content = new ByteArrayContent(requestBytes);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            using var response = await HttpClient.PostAsync(GetAdminEndpoint("publish"), content);
            await EnsureAdminResponseSuccessAsync(response);
        }

        public async Task PublishPhotoAsync(
            string hardwareId,
            string name,
            string description,
            string photoPath)
        {
            var image = new FileInfo(photoPath);
            if (!image.Exists || image.Length is < 1 or > MaxWorkshopPhotoBytes)
            {
                throw new InvalidDataException("Фото должно существовать и быть размером не более 3 МБ.");
            }

            string extension = Path.GetExtension(photoPath).ToLowerInvariant();
            string contentType = extension switch
            {
                ".jpg" or ".jpeg" => "image/jpeg",
                ".png" => "image/png",
                _ => throw new InvalidDataException("Поддерживаются только изображения JPG и PNG.")
            };

            byte[] bytes = await File.ReadAllBytesAsync(photoPath);
            byte[] requestBytes = JsonSerializer.SerializeToUtf8Bytes(new
            {
                hardwareId,
                name,
                description,
                contentType,
                imageBase64 = Convert.ToBase64String(bytes)
            }, JsonOptions);
            using var content = new ByteArrayContent(requestBytes);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            using var response = await HttpClient.PostAsync(GetAdminEndpoint("publish-photo"), content);
            await EnsureAdminResponseSuccessAsync(response);
        }

        public async Task UpdateCatalogEntryAsync(
            string hardwareId,
            string id,
            string name,
            string description)
        {
            using var response = await HttpClient.PostAsJsonAsync(
                GetAdminEndpoint("update-entry"),
                new { hardwareId, id, name, description },
                JsonOptions);
            await EnsureAdminResponseSuccessAsync(response);
        }

        public async Task HideCatalogEntryAsync(string hardwareId, string id)
        {
            using var response = await HttpClient.PostAsJsonAsync(
                GetAdminEndpoint("hide-entry"),
                new { hardwareId, id },
                JsonOptions);
            await EnsureAdminResponseSuccessAsync(response);
        }

        public async Task<IReadOnlyList<WorkshopItem>> LoadCatalogAsync()
        {
            string repository = GitHubRepositorySettings.GetRepository();
            if (string.IsNullOrEmpty(repository))
            {
                throw new InvalidOperationException("Каталог станет доступен после публикации приложения из GitHub.");
            }

            string url = $"https://raw.githubusercontent.com/{repository}/main/workshop/catalog.json?refresh={DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
            byte[] bytes = await ReadDocumentAsync(url);
            List<WorkshopItem>? items = JsonSerializer.Deserialize<List<WorkshopItem>>(bytes, JsonOptions);
            string imageBaseUrl = $"https://raw.githubusercontent.com/{repository}/main/workshop/images/";
            if (items == null || items.Any(item =>
                    string.IsNullOrWhiteSpace(item.Id) ||
                    string.IsNullOrWhiteSpace(item.Name) ||
                    item.Type is not ("profile" or "binds" or "photo") ||
                    (item.Type == "photo"
                        ? string.IsNullOrWhiteSpace(item.Image) ||
                          !System.Text.RegularExpressions.Regex.IsMatch(
                              item.Image,
                              @"^[A-Za-z0-9_-]+\.(jpg|jpeg|png)$",
                              System.Text.RegularExpressions.RegexOptions.IgnoreCase)
                        : string.IsNullOrWhiteSpace(item.File))))
            {
                throw new InvalidDataException("Каталог мастерской пуст или содержит некорректные записи.");
            }

            foreach (var item in items.Where(item => item.Type == "photo"))
            {
                item.ImageUrl = imageBaseUrl + Uri.EscapeDataString(item.Image);
            }

            return items;
        }

        public async Task<WorkshopPackage> DownloadPackageAsync(WorkshopItem item)
        {
            if (item.Type == "photo")
            {
                throw new InvalidOperationException("Фото-запись не содержит устанавливаемого набора.");
            }

            string repository = GitHubRepositorySettings.GetRepository();
            if (string.IsNullOrEmpty(repository))
            {
                throw new InvalidOperationException("Скачивание будет доступно после публикации приложения из GitHub.");
            }

            if (Path.GetFileName(item.File) != item.File ||
                item.File.Contains("..", StringComparison.Ordinal) ||
                !item.File.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("Указан недопустимый файл пакета.");
            }

            string url = $"https://raw.githubusercontent.com/{repository}/main/workshop/packs/{Uri.EscapeDataString(item.File)}";
            byte[] bytes = await ReadDocumentAsync(url);
            WorkshopPackage? package = JsonSerializer.Deserialize<WorkshopPackage>(bytes, JsonOptions);
            if (package == null ||
                (item.Type == "profile" && package.Tabs.Count == 0) ||
                (item.Type == "binds" && package.Binds.Count == 0))
            {
                throw new InvalidDataException($"Пакет «{item.Name}» не содержит данных для импорта.");
            }

            return package;
        }

        private static async Task<byte[]> ReadDocumentAsync(string url)
        {
            using var response = await HttpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            if (response.Content.Headers.ContentLength > MaxDocumentBytes)
            {
                throw new InvalidDataException("Файл мастерской превышает допустимый размер 1 МБ.");
            }

            await using var stream = await response.Content.ReadAsStreamAsync();
            using var output = new MemoryStream();
            byte[] buffer = new byte[8192];
            int read;
            while ((read = await stream.ReadAsync(buffer)) > 0)
            {
                if (output.Length + read > MaxDocumentBytes)
                {
                    throw new InvalidDataException("Файл мастерской превышает допустимый размер 1 МБ.");
                }

                output.Write(buffer, 0, read);
            }

            return output.ToArray();
        }

        private static string GetAdminEndpoint(string path)
        {
            string workerUrl = WorkshopAdminSettings.GetWorkerUrl();
            if (string.IsNullOrEmpty(workerUrl))
            {
                throw new InvalidOperationException(
                    "Панель публикации не настроена. Задайте WORKSHOP_API_URL в настройках GitHub Actions и соберите приложение.");
            }

            return $"{workerUrl}/admin/{path}";
        }

        private static async Task EnsureAdminResponseSuccessAsync(HttpResponseMessage response)
        {
            if (response.IsSuccessStatusCode)
            {
                return;
            }

            string detail = await response.Content.ReadAsStringAsync();
            string message = "Не удалось выполнить запрос к админке мастерской.";
            try
            {
                using JsonDocument document = JsonDocument.Parse(detail);
                if (document.RootElement.TryGetProperty("error", out JsonElement error) &&
                    error.ValueKind == JsonValueKind.String &&
                    !string.IsNullOrWhiteSpace(error.GetString()))
                {
                    message = error.GetString()!;
                }
            }
            catch (JsonException)
            {
                message = $"Worker вернул ошибку {response.StatusCode}.";
            }

            throw new HttpRequestException(message, null, response.StatusCode);
        }

        private static HttpClient CreateHttpClient()
        {
            var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
            client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("LumBinder", "1.0"));
            return client;
        }
    }
}
