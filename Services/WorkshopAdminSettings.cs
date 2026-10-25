using System;
using System.Linq;
using System.Reflection;

namespace BinderApp.Services
{
    public static class WorkshopAdminSettings
    {
        public static bool IsAdminBuild =>
            string.Equals(
                Assembly.GetEntryAssembly()?
                    .GetCustomAttributes<AssemblyMetadataAttribute>()
                    .FirstOrDefault(attribute => attribute.Key == "WorkshopAdminBuild")?.Value,
                "true",
                StringComparison.OrdinalIgnoreCase);

        public static string GetWorkerUrl()
        {
            string? configuredUrl = Assembly.GetEntryAssembly()?
                .GetCustomAttributes<AssemblyMetadataAttribute>()
                .FirstOrDefault(attribute => attribute.Key == "WorkshopApiUrl")?.Value;

            if (!Uri.TryCreate(configuredUrl, UriKind.Absolute, out Uri? uri) ||
                uri.Scheme != Uri.UriSchemeHttps ||
                !string.IsNullOrEmpty(uri.UserInfo) ||
                uri.AbsolutePath != "/" ||
                !string.IsNullOrEmpty(uri.Query) ||
                !string.IsNullOrEmpty(uri.Fragment) ||
                uri.Host.Contains("REPLACE_WITH_", StringComparison.OrdinalIgnoreCase))
            {
                return string.Empty;
            }

            return uri.GetLeftPart(UriPartial.Authority);
        }
    }
}
