using Newtonsoft.Json;
using QueryStoreLinks.Models;
using QueryStoreLinks.Models.StoreEdgeFD;

namespace QueryStoreLinks.Helpers
{
    public class NonAppxHandler
    {
        private static readonly HttpClient _httpClient = new HttpClient();

        public PackageManifestResponse? Manifest { get; private set; }
        public bool IsFound => Manifest?.Data != null;

        /// <summary>
        /// 模仿 StoreLib 的 Query 模式
        /// </summary>
        public async Task QueryAsync(
            string productId,
            string locale = "en-US",
            string market = "US",
            CancellationToken ct = default
        )
        {
            string url =
                $"http://storeedgefd.dsx.mp.microsoft.com/v9.0/packageManifests/{productId.ToLower()}?locale={locale.ToLower()}&market={market.ToUpper()}";

            try
            {
                var response = await _httpClient.GetAsync(url, ct);
                if (response.IsSuccessStatusCode)
                {
                    string content = await response.Content.ReadAsStringAsync(ct);
                    Manifest = JsonConvert.DeserializeObject<PackageManifestResponse>(content);
                }
            }
            catch
            {
                Manifest = null;
            }
        }

        /// <summary>
        /// 将结果转换为通用的 AppInfo 和 DownloadItem
        /// </summary>
        public async Task<(AppInfo? Info, List<DownloadItem> Downloads)> ResolveDetailsAsync(
            CancellationToken ct = default
        )
        {
            if (!IsFound || Manifest?.Data?.Versions == null || Manifest.Data.Versions.Count == 0)
                return (null, new List<DownloadItem>());

            var version = Manifest.Data.Versions[0];
            var locale = version.DefaultLocale;

            var appInfo = new AppInfo
            {
                ProductId = Manifest.Data.PackageIdentifier,
                Name = locale?.PackageName ?? "Unknown",
                Publisher = locale?.Publisher ?? "Unknown",
                Description = locale?.ShortDescription ?? string.Empty,
                CategoryId =
                    locale
                        ?.Agreements?.FirstOrDefault(a => a.AgreementLabel == "Category")
                        ?.Agreement
                    ?? "Unknown",
            };

            var downloadTasks = version.Installers.Select(async installer =>
            {
                string size = await PackageHelper.GetFileSizeAsync(installer.InstallerUrl, ct);
                return new DownloadItem
                {
                    FileName = $"{appInfo.Name}_{installer.Architecture}.{installer.InstallerType}",
                    FileLink = installer.InstallerUrl,
                    FileSize = size,
                };
            });

            var downloads = (await Task.WhenAll(downloadTasks)).ToList();

            return (appInfo, downloads);
        }
    }
}
