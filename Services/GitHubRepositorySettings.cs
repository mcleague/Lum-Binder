using System.Linq;
using System.Reflection;

namespace BinderApp.Services
{
    public static class GitHubRepositorySettings
    {
        public static string GetRepository()
        {
            string repository = Assembly.GetEntryAssembly()?
                .GetCustomAttributes<AssemblyMetadataAttribute>()
                .FirstOrDefault(attribute => attribute.Key == "UpdateRepository")?.Value
                ?? string.Empty;

            if (repository.Contains('/') &&
                repository.Split('/').Length == 2 &&
                repository.Split('/').All(part =>
                    part.Length > 0 && part.All(character =>
                        char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.')) &&
                !repository.Contains("REPLACE_WITH_"))
            {
                return repository;
            }

            return string.Empty;
        }
    }
}
