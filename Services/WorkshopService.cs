using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;
using BinderApp.Models;

namespace BinderApp.Services
{
    public sealed class WorkshopService
    {
        private const int MaxDocumentBytes = 1_048_576;
        private static readonly HttpClient HttpClient = CreateHttpClient();
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        public async Task<IReadOnlyList<WorkshopItem>> LoadCatalogAsync()
        {
            string repository = GitHubRepositorySettings.GetRepository();
            if (string.IsNullOrEmpty(repository))
            {
                throw new InvalidOperationException("Каталог станет доступен после публикации приложения из GitHub.");
            }

            string url = $"https://raw.githubusercontent.com/{repository}/main/workshop/catalog.json";
            byte[] bytes = await ReadDocumentAsync(url);
            List<WorkshopItem>? items = JsonSerializer.Deserialize<List<WorkshopItem>>(bytes, JsonOptions);
            if (items == null || items.Any(item =>
                    string.IsNullOrWhiteSpace(item.Id) ||
                    string.IsNullOrWhiteSpace(item.Name) ||
                    string.IsNullOrWhiteSpace(item.File) ||
                    item.Type is not ("profile" or "binds")))
            {
                throw new InvalidDataException("Каталог мастерской пуст или содержит некорректные записи.");
            }

            return items;
        }

        public async Task<WorkshopPackage> DownloadPackageAsync(WorkshopItem item)
        {
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

        private static HttpClient CreateHttpClient()
        {
            var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
            client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("LumBinder", "1.0"));
            return client;
        }
    }
}
