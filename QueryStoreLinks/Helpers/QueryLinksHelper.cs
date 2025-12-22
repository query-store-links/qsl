using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;

namespace QueryStoreLinks.Helpers
{
    public static class QueryLinksHelper
    {
        // Endpoints（保持与原版一致）
        private static readonly Uri CookieUri = new(
            "https://fe3.delivery.mp.microsoft.com/ClientWebService/client.asmx"
        );
        private static readonly Uri FileListXmlUri = new(
            "https://fe3.delivery.mp.microsoft.com/ClientWebService/client.asmx"
        );
        private static readonly Uri UrlUri = new(
            "https://fe3.delivery.mp.microsoft.com/ClientWebService/client.asmx/secured"
        );

        // 默认 HttpClient（建议长期重用）
        private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };

        /// <summary>
        /// 解析输入框输入的字符串（去掉 URL 中的 path 与 query，只保留最后的 ID）
        /// </summary>
        public static string ParseRequestContent(string requestContent)
        {
            if (string.IsNullOrWhiteSpace(requestContent))
                return string.Empty;

            string result = requestContent;
            if (result.Contains('/'))
            {
                result = result[(result.LastIndexOf('/') + 1)..];
            }
            if (result.Contains('?'))
            {
                result = result[..result.IndexOf('?')];
            }
            return result;
        }

        // =========================
        // 轻量内部模型（避免外部 Models 依赖）
        // =========================

        /// <summary>
        /// 应用信息（对应原 AppInfoModel）
        /// </summary>
        public sealed class AppInfo
        {
            public string Name { get; set; } = string.Empty;
            public string Publisher { get; set; } = string.Empty;
            public string Description { get; set; } = string.Empty;
            public string CategoryId { get; set; } = string.Empty;
            public string ProductId { get; set; } = string.Empty;
        }

        /// <summary>
        /// 下载项（对应原 QueryLinksModel）
        /// </summary>
        public sealed class DownloadItem
        {
            public string FileName { get; set; } = string.Empty;
            public string FileLink { get; set; } = string.Empty;
            public string FileSize { get; set; } = "0 B";
            public bool IsSelected { get; set; } = false;
            public bool IsSelectMode { get; set; } = false;
        }

        // =========================
        // 核心功能
        // =========================

        /// <summary>
        /// 获取微软商店服务器储存在用户本地终端上的数据（Cookie）
        /// 注意：需要你提供 cookie SOAP 模板（即原 Files/Assets/Embed/cookie.xml 的文本）。
        /// 返回 response 中的 EncryptedData 节点文本。
        /// </summary>
        /// <param name="cookieSoapTemplate">SOAP XML 文本，发送给 CookieUri</param>
        public static async Task<string> GetCookieAsync(
            string cookieSoapTemplate,
            CancellationToken ct = default
        )
        {
            if (string.IsNullOrWhiteSpace(cookieSoapTemplate))
                throw new ArgumentException(
                    "cookieSoapTemplate 不能为空（请提供原 cookie.xml 的 SOAP 内容）。",
                    nameof(cookieSoapTemplate)
                );

            using var content = new StringContent(cookieSoapTemplate);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/soap+xml")
            {
                CharSet = "utf-8",
            };

            using var req = new HttpRequestMessage(HttpMethod.Post, CookieUri)
            {
                Content = content,
            };
            using var resp = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct)
                .ConfigureAwait(false);

            if (!resp.IsSuccessStatusCode)
                return string.Empty;

            string responseString = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (string.IsNullOrEmpty(responseString))
                return string.Empty;

            // 用 XDocument 解析（忽略命名空间：按 LocalName）
            var doc = XDocument.Parse(responseString);
            var encryptedData = doc.Descendants()
                .FirstOrDefault(e => e.Name.LocalName == "EncryptedData");
            return encryptedData?.Value ?? string.Empty;
        }

        /// <summary>
        /// 获取应用信息（打包应用：有，非打包应用：无）
        /// 访问 Microsoft Store 产品接口：storeedgefd.dsx.mp.microsoft.com/v9.0/products/{productId}
        /// </summary>
        /// <param name="productId">产品 ID</param>
        /// <param name="market">例如 "CN" / "US"。必须提供。</param>
        /// <param name="locale">例如 "zh-CN" / "en-US"。必须提供。</param>
        public static async Task<(bool requestResult, AppInfo appInfo)> GetAppInformationAsync(
            string productId,
            string market,
            string locale,
            CancellationToken ct = default
        )
        {
            bool requestResult = false;
            var appInfo = new AppInfo { ProductId = productId };

            if (string.IsNullOrWhiteSpace(productId))
                return (false, appInfo);

            if (string.IsNullOrWhiteSpace(market) || string.IsNullOrWhiteSpace(locale))
                throw new ArgumentException(
                    "market/locale 必须提供（原版从 StoreRegionService/LanguageService 读取）。"
                );

            string url =
                $"https://storeedgefd.dsx.mp.microsoft.com/v9.0/products/{productId}?market={market}&locale={locale}&deviceFamily=Windows.Desktop";

            using var resp = await Http.GetAsync(url, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
                return (false, appInfo);

            requestResult = true;
            string json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("Payload", out var payload))
            {
                appInfo.Name = payload.TryGetProperty("Title", out var title)
                    ? title.GetString() ?? string.Empty
                    : string.Empty;
                appInfo.Publisher = payload.TryGetProperty("PublisherName", out var pub)
                    ? pub.GetString() ?? string.Empty
                    : string.Empty;
                appInfo.Description = payload.TryGetProperty("Description", out var desc)
                    ? desc.GetString() ?? string.Empty
                    : string.Empty;

                // CategoryId 可能在 Skus[0].FulfillmentData 的 WuCategoryId 中
                if (
                    payload.TryGetProperty("Skus", out var skus)
                    && skus.ValueKind == JsonValueKind.Array
                    && skus.GetArrayLength() > 0
                )
                {
                    var sku0 = skus[0];
                    if (
                        sku0.TryGetProperty("FulfillmentData", out var fd)
                        && fd.ValueKind == JsonValueKind.String
                    )
                    {
                        var fdStr = fd.GetString();
                        if (!string.IsNullOrEmpty(fdStr))
                        {
                            using var fdDoc = JsonDocument.Parse(fdStr);
                            var fdRoot = fdDoc.RootElement;
                            appInfo.CategoryId = fdRoot.TryGetProperty(
                                "WuCategoryId",
                                out var catId
                            )
                                ? catId.GetString() ?? string.Empty
                                : string.Empty;
                        }
                    }
                }
            }

            return (requestResult, appInfo);
        }

        /// <summary>
        /// 获取文件信息字符串（WU：Windows Update SOAP）
        /// 注意：需要你提供 wu SOAP 模板（即原 Files/Assets/Embed/wu.xml 的文本），其中含占位符：
        /// {cookie} / {categoryId} / {ring}（如果你的模板用的是 {1}/{2}/{3}，我也可适配）。
        /// </summary>
        public static async Task<string> GetFileListXmlAsync(
            string cookie,
            string categoryId,
            string ring,
            string wuSoapTemplate,
            CancellationToken ct = default
        )
        {
            if (string.IsNullOrWhiteSpace(cookie))
                throw new ArgumentException("cookie 不能为空，请先调用 GetCookieAsync。");
            if (string.IsNullOrWhiteSpace(categoryId))
                throw new ArgumentException("categoryId 不能为空。");
            if (string.IsNullOrWhiteSpace(ring))
                throw new ArgumentException("ring 不能为空（如 Retail / WIF / RP 等）。");
            if (string.IsNullOrWhiteSpace(wuSoapTemplate))
                throw new ArgumentException(
                    "wuSoapTemplate 不能为空，请提供原 wu.xml 的 SOAP 内容。"
                );

            // 替换模板占位符（你可以确认实际占位符名称，我可调整）
            string body = wuSoapTemplate
                .Replace("{1}", cookie) // 兼容你的原占位
                .Replace("{2}", categoryId)
                .Replace("{3}", ring)
                .Replace("{cookie}", cookie) // 也支持语义化占位
                .Replace("{categoryId}", categoryId)
                .Replace("{ring}", ring);

            using var content = new StringContent(body);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/soap+xml")
            {
                CharSet = "utf-8",
            };

            using var req = new HttpRequestMessage(HttpMethod.Post, FileListXmlUri)
            {
                Content = content,
            };
            using var resp = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct)
                .ConfigureAwait(false);

            if (!resp.IsSuccessStatusCode)
                return string.Empty;

            string responseString = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            // 原版做了 HTML 实体转换
            return responseString
                .Replace("&lt;", "<")
                .Replace("&gt;", ">")
                .Replace("&amp;lt;", "<")
                .Replace("&amp;gt;", ">");
        }

        /// <summary>
        /// 获取商店应用安装包（解析 WU 返回的文件列表 XML，并通过 secured 接口获取 URL）
        /// {updateID} / {revisionNumber} / {ring}（或 {1}/{2}/{3}）。
        /// </summary>
        public static async Task<List<DownloadItem>> GetAppxPackagesAsync(
            string fileListXml,
            string ring,
            string urlSoapTemplate,
            CancellationToken ct = default
        )
        {
            var result = new List<DownloadItem>();
            if (string.IsNullOrWhiteSpace(fileListXml))
                return result;
            if (string.IsNullOrWhiteSpace(ring))
                throw new ArgumentException("ring 不能为空。");
            if (string.IsNullOrWhiteSpace(urlSoapTemplate))
                throw new ArgumentException(
                    "urlSoapTemplate 不能为空，请提供原 url.xml 的 SOAP 内容。"
                );

            // 解析 XML（忽略命名空间）
            var doc = XDocument.Parse(fileListXml);

            // 收集 File 节点的基本信息：InstallerSpecificIdentifier -> (ext,size,digest)
            var fileInfoDict = new Dictionary<string, (string ext, string size, string digest)>(
                StringComparer.OrdinalIgnoreCase
            );

            foreach (var fileElem in doc.Descendants().Where(e => e.Name.LocalName == "File"))
            {
                string installerId =
                    fileElem
                        .Attributes()
                        .FirstOrDefault(a => a.Name.LocalName == "InstallerSpecificIdentifier")
                        ?.Value
                    ?? string.Empty;
                string fileNameAttr =
                    fileElem.Attributes().FirstOrDefault(a => a.Name.LocalName == "FileName")?.Value
                    ?? string.Empty;
                string sizeAttr =
                    fileElem.Attributes().FirstOrDefault(a => a.Name.LocalName == "Size")?.Value
                    ?? "0";
                string digestAttr =
                    fileElem.Attributes().FirstOrDefault(a => a.Name.LocalName == "Digest")?.Value
                    ?? string.Empty;

                if (!string.IsNullOrEmpty(installerId))
                {
                    string ext = string.Empty;
                    if (!string.IsNullOrEmpty(fileNameAttr))
                    {
                        int dotIndex = fileNameAttr.LastIndexOf(".", StringComparison.Ordinal);
                        ext = dotIndex >= 0 ? fileNameAttr[dotIndex..] : string.Empty;
                    }
                    if (!fileInfoDict.ContainsKey(installerId))
                    {
                        fileInfoDict[installerId] = (ext, sizeAttr, digestAttr);
                    }
                }
            }

            // SecuredFragment -> 其上层应包含 UpdateIdentity 与 AppxMetadata(PackageMoniker)
            var securedFragments = doc.Descendants()
                .Where(e => e.Name.LocalName == "SecuredFragment")
                .ToList();
            var tasks = new List<Task>();

            var locker = new object();

            foreach (var sf in securedFragments)
            {
                tasks.Add(
                    Task.Run(
                        async () =>
                        {
                            // 找到祖先节点中包含 AppxMetadata 的块
                            var parentBlock = sf.Ancestors()
                                .FirstOrDefault(a =>
                                    a.Descendants().Any(d => d.Name.LocalName == "AppxMetadata")
                                    && a.Descendants()
                                        .Any(d => d.Name.LocalName == "UpdateIdentity")
                                );

                            if (parentBlock == null)
                                return;

                            var appxMetadata = parentBlock
                                .Descendants()
                                .FirstOrDefault(d => d.Name.LocalName == "AppxMetadata");
                            var packageMoniker =
                                appxMetadata
                                    ?.Attributes()
                                    .FirstOrDefault(a => a.Name.LocalName == "PackageMoniker")
                                    ?.Value
                                ?? string.Empty;

                            if (string.IsNullOrEmpty(packageMoniker))
                                return;

                            if (!fileInfoDict.TryGetValue(packageMoniker, out var finfo))
                                return;

                            string revisionNumber =
                                parentBlock
                                    .Descendants()
                                    .FirstOrDefault(d => d.Name.LocalName == "UpdateIdentity")
                                    ?.Attributes()
                                    .FirstOrDefault(a => a.Name.LocalName == "RevisionNumber")
                                    ?.Value
                                ?? string.Empty;

                            string updateID =
                                parentBlock
                                    .Descendants()
                                    .FirstOrDefault(d => d.Name.LocalName == "UpdateIdentity")
                                    ?.Attributes()
                                    .FirstOrDefault(a => a.Name.LocalName == "UpdateID")
                                    ?.Value
                                ?? string.Empty;

                            if (
                                string.IsNullOrEmpty(updateID)
                                || string.IsNullOrEmpty(revisionNumber)
                            )
                                return;

                            string uri = await GetAppxUrlAsync(
                                    updateID,
                                    revisionNumber,
                                    ring,
                                    finfo.digest,
                                    urlSoapTemplate,
                                    ct
                                )
                                .ConfigureAwait(false);

                            // 计算人类可读的大小
                            double sizeNum = 0;
                            _ = double.TryParse(
                                finfo.size,
                                NumberStyles.Any,
                                CultureInfo.InvariantCulture,
                                out sizeNum
                            );
                            string sizeStr = FormatBytes((long)sizeNum);

                            lock (locker)
                            {
                                result.Add(
                                    new DownloadItem
                                    {
                                        FileName = packageMoniker + finfo.ext,
                                        FileLink = uri,
                                        FileSize = sizeStr,
                                        IsSelected = false,
                                        IsSelectMode = false,
                                    }
                                );
                            }
                        },
                        ct
                    )
                );
            }

            await Task.WhenAll(tasks).ConfigureAwait(false);
            return result;
        }

        /// <summary>
        /// 获取商店应用安装包对应的下载链接（secured 接口）
        /// 需要你提供 url SOAP 模板（原 Files/Assets/Embed/url.xml）。
        /// </summary>
        private static async Task<string> GetAppxUrlAsync(
            string updateID,
            string revisionNumber,
            string ring,
            string digest,
            string urlSoapTemplate,
            CancellationToken ct
        )
        {
            if (string.IsNullOrWhiteSpace(urlSoapTemplate))
                throw new ArgumentException(
                    "urlSoapTemplate 不能为空（请提供原 url.xml 的 SOAP 内容）。",
                    nameof(urlSoapTemplate)
                );

            string body = urlSoapTemplate
                .Replace("{1}", updateID)
                .Replace("{2}", revisionNumber)
                .Replace("{3}", ring)
                .Replace("{updateID}", updateID)
                .Replace("{revisionNumber}", revisionNumber)
                .Replace("{ring}", ring);

            using var content = new StringContent(body);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/soap+xml")
            {
                CharSet = "utf-8",
            };

            using var req = new HttpRequestMessage(HttpMethod.Post, UrlUri) { Content = content };
            using var resp = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct)
                .ConfigureAwait(false);

            if (!resp.IsSuccessStatusCode)
                return string.Empty;

            string responseString = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (string.IsNullOrEmpty(responseString))
                return string.Empty;

            var doc = XDocument.Parse(responseString);

            // FileLocation/FileDigest == digest -> Url
            foreach (var fl in doc.Descendants().Where(e => e.Name.LocalName == "FileLocation"))
            {
                string fileDigest =
                    fl.Descendants().FirstOrDefault(e => e.Name.LocalName == "FileDigest")?.Value
                    ?? string.Empty;
                if (string.Equals(fileDigest, digest, StringComparison.OrdinalIgnoreCase))
                {
                    string url =
                        fl.Descendants().FirstOrDefault(e => e.Name.LocalName == "Url")?.Value
                        ?? string.Empty;
                    if (!string.IsNullOrEmpty(url))
                        return url;
                }
            }

            return string.Empty;
        }

        /// <summary>
        /// 获取非商店应用的安装包（EXE/MSI/MSIX 等）
        /// 访问 packageManifests 接口：/v9.0/packageManifests/{productId}?market={market}
        /// </summary>
        public static async Task<List<DownloadItem>> GetNonAppxPackagesAsync(
            string productId,
            string market,
            CancellationToken ct = default
        )
        {
            var result = new List<DownloadItem>();

            if (string.IsNullOrWhiteSpace(productId))
                return result;

            if (string.IsNullOrWhiteSpace(market))
                throw new ArgumentException("market 必须提供（原版从 StoreRegionService 读取）。");

            string url =
                $"https://storeedgefd.dsx.mp.microsoft.com/v9.0/packageManifests/{productId}?market={market}";

            using var resp = await Http.GetAsync(url, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
                return result;

            string json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!root.TryGetProperty("Data", out var data))
                return result;

            if (
                !data.TryGetProperty("Versions", out var versions)
                || versions.ValueKind != JsonValueKind.Array
                || versions.GetArrayLength() == 0
            )
            {
                return result;
            }

            var v0 = versions[0];
            if (
                !v0.TryGetProperty("Installers", out var installers)
                || installers.ValueKind != JsonValueKind.Array
            )
            {
                return result;
            }

            var tasks = new List<Task>();
            var locker = new object();

            foreach (var installer in installers.EnumerateArray())
            {
                tasks.Add(
                    Task.Run(
                        async () =>
                        {
                            string installerType = installer.TryGetProperty(
                                "InstallerType",
                                out var it
                            )
                                ? (it.GetString() ?? string.Empty)
                                : string.Empty;
                            string installerUrl = installer.TryGetProperty(
                                "InstallerUrl",
                                out var iu
                            )
                                ? (iu.GetString() ?? string.Empty)
                                : string.Empty;
                            string installerLocale = installer.TryGetProperty(
                                "InstallerLocale",
                                out var il
                            )
                                ? (il.GetString() ?? string.Empty)
                                : string.Empty;

                            if (string.IsNullOrEmpty(installerUrl))
                                return;

                            // HEAD 取大小
                            string sizeStr = FormatBytes(
                                await GetNonAppxPackageFileSizeAsync(installerUrl, ct)
                                    .ConfigureAwait(false)
                            );

                            // exe/msi 或无类型：按原逻辑直接用文件名无扩展
                            bool isExeOrMsi =
                                installerUrl.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                                || installerUrl.EndsWith(
                                    ".msi",
                                    StringComparison.OrdinalIgnoreCase
                                );

                            string fileName;
                            if (string.IsNullOrEmpty(installerType) || isExeOrMsi)
                            {
                                // 从 url 提取文件名（去扩展）
                                int slash = installerUrl.LastIndexOf('/');
                                int dot = installerUrl.LastIndexOf('.');
                                if (slash >= 0 && dot > slash)
                                    fileName = installerUrl[(slash + 1)..dot];
                                else
                                    fileName =
                                        installerUrl.Split('/').LastOrDefault() ?? installerUrl;
                            }
                            else
                            {
                                // MSIX / APPX 等，附上 Locale 与扩展类型
                                string name =
                                    installerUrl.Split('/').LastOrDefault() ?? installerUrl;
                                fileName = $"{name} ({installerLocale}).{installerType}";
                            }

                            lock (locker)
                            {
                                result.Add(
                                    new DownloadItem
                                    {
                                        FileName = fileName,
                                        FileLink = installerUrl,
                                        FileSize = sizeStr,
                                        IsSelected = false,
                                        IsSelectMode = false,
                                    }
                                );
                            }
                        },
                        ct
                    )
                );
            }

            await Task.WhenAll(tasks).ConfigureAwait(false);
            return result;
        }

        /// <summary>
        /// HEAD 请求获取非商店应用下载文件大小（字节数）
        /// </summary>
        private static async Task<long> GetNonAppxPackageFileSizeAsync(
            string url,
            CancellationToken ct
        )
        {
            using var req = new HttpRequestMessage(HttpMethod.Head, url);
            using var resp = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct)
                .ConfigureAwait(false);

            if (!resp.IsSuccessStatusCode)
                return 0L;

            var len = resp.Content.Headers.ContentLength;
            return len ?? 0L;
        }

        // =========================
        // 工具方法
        // =========================

        /// <summary>
        /// 字节数转可读字符串（类似原 VolumeSizeHelper）
        /// </summary>
        private static string FormatBytes(long bytes)
        {
            const long KB = 1024;
            const long MB = KB * 1024;
            const long GB = MB * 1024;
            const long TB = GB * 1024;

            if (bytes >= TB)
                return (bytes / (double)TB).ToString("0.##", CultureInfo.InvariantCulture) + " TB";
            if (bytes >= GB)
                return (bytes / (double)GB).ToString("0.##", CultureInfo.InvariantCulture) + " GB";
            if (bytes >= MB)
                return (bytes / (double)MB).ToString("0.##", CultureInfo.InvariantCulture) + " MB";
            if (bytes >= KB)
                return (bytes / (double)KB).ToString("0.##", CultureInfo.InvariantCulture) + " KB";
            return bytes.ToString(CultureInfo.InvariantCulture) + " B";
        }
    }
}
