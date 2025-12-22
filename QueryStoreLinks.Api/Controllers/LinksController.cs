using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using QueryStoreLinks.Helpers;

namespace QueryStoreLinks.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class LinksController : ControllerBase
    {
        private readonly IWebHostEnvironment _env;
        private readonly ILogger<LinksController> _logger;

        public LinksController(IWebHostEnvironment env, ILogger<LinksController> logger)
        {
            _env = env;
            _logger = logger;
        }

        [HttpPost("resolve-all")]
        public async Task<ActionResult<ResolveAllResponse>> ResolveAll(
            [FromBody] ResolveAllRequest req,
            CancellationToken ct
        )
        {
            if (req is null)
                return BadRequest("Request body is required.");

            var errors = new List<string>();
            var result = new ResolveAllResponse
            {
                ProductId = QueryLinksHelper.ParseRequestContent(req.ProductInput ?? string.Empty),
            };

            try
            {
                // Ensure cookie template exists locally under ./xml (download default if missing)
                var cookieTemplate = await GetOrDownloadTemplateAsync("cookie.xml", ct);
                if (string.IsNullOrWhiteSpace(cookieTemplate))
                {
                    errors.Add("Missing cookie.xml in local ./xml and failed to download default.");
                }
                else
                {
                    try
                    {
                        result.Cookie = await QueryLinksHelper.GetCookieAsync(cookieTemplate, ct);
                        if (string.IsNullOrWhiteSpace(result.Cookie))
                            errors.Add("Cookie not obtained or empty.");
                    }
                    catch (Exception ex)
                    {
                        errors.Add("Cookie error: " + ex.Message);
                        await LogExceptionAsync(ex, "Cookie error", result.ProductId, req);
                    }
                }

                // App info
                try
                {
                    var (ok, info) = await QueryLinksHelper.GetAppInformationAsync(
                        result.ProductId,
                        req.Market ?? "CN",
                        req.Locale ?? "zh-CN",
                        ct
                    );
                    if (ok)
                        result.AppInfo = info;
                    else
                        errors.Add("Failed to get app information.");
                }
                catch (Exception ex)
                {
                    errors.Add("AppInfo error: " + ex.Message);
                    await LogExceptionAsync(ex, "AppInfo error", result.ProductId, req);
                }

                // File list
                if (
                    !string.IsNullOrWhiteSpace(result.AppInfo?.CategoryId)
                    && !string.IsNullOrWhiteSpace(result.Cookie)
                )
                {
                    var wuTemplate = await GetOrDownloadTemplateAsync("wu.xml", ct);
                    if (string.IsNullOrWhiteSpace(wuTemplate))
                    {
                        errors.Add("Missing wu.xml in local ./xml and failed to download default.");
                    }
                    else
                    {
                        try
                        {
                            result.FileListXml = await QueryLinksHelper.GetFileListXmlAsync(
                                result.Cookie,
                                result.AppInfo.CategoryId,
                                req.Ring ?? "Retail",
                                wuTemplate,
                                ct
                            );
                        }
                        catch (Exception ex)
                        {
                            errors.Add("FileList error: " + ex.Message);
                            await LogExceptionAsync(ex, "FileList error", result.ProductId, req);
                        }
                    }
                }

                // APPX
                if (req.IncludeAppx && !string.IsNullOrWhiteSpace(result.FileListXml))
                {
                    var urlTemplate = await GetOrDownloadTemplateAsync("url.xml", ct);
                    if (string.IsNullOrWhiteSpace(urlTemplate))
                    {
                        errors.Add(
                            "Missing url.xml in local ./xml and failed to download default."
                        );
                    }
                    else
                    {
                        try
                        {
                            result.AppxPackages = await QueryLinksHelper.GetAppxPackagesAsync(
                                result.FileListXml,
                                req.Ring ?? "Retail",
                                urlTemplate,
                                ct
                            );
                        }
                        catch (Exception ex)
                        {
                            errors.Add("Appx parse error: " + ex.Message);
                            await LogExceptionAsync(ex, "Appx parse error", result.ProductId, req);
                        }
                    }
                }

                // Non-APPX
                if (req.IncludeNonAppx)
                {
                    try
                    {
                        result.NonAppxPackages = await QueryLinksHelper.GetNonAppxPackagesAsync(
                            result.ProductId,
                            req.Market ?? "CN",
                            ct
                        );
                    }
                    catch (Exception ex)
                    {
                        errors.Add("NonAppx error: " + ex.Message);
                        await LogExceptionAsync(ex, "NonAppx error", result.ProductId, req);
                    }
                }

                result.Errors = errors;
                return Ok(result);
            }
            catch (OperationCanceledException)
            {
                return StatusCode(499); // Client Closed Request
            }
        }

        // Log full exception to configured logger and a local file (logs/exceptions.log). This ensures concise responses but preserves full trace in logs.
        private async Task LogExceptionAsync(Exception ex, string context, string? productId, ResolveAllRequest? req)
        {
            try
            {
                _logger.LogError(ex, "{Context} for product {ProductId}", context, productId);

                var logsDir = Path.Combine(AppContext.BaseDirectory, "logs");
                Directory.CreateDirectory(logsDir);
                var logFile = Path.Combine(logsDir, "exceptions.log");

                var sb = new System.Text.StringBuilder();
                sb.AppendLine($"[{DateTime.UtcNow:O}] {context} Product={productId} RequestInput={req?.ProductInput} Market={req?.Market} Locale={req?.Locale} Ring={req?.Ring}");
                sb.AppendLine(ex.ToString());
                sb.AppendLine("----");

                await System.IO.File.AppendAllTextAsync(logFile, sb.ToString()).ConfigureAwait(false);
            }
            catch
            {
                // Silently ignore logging failures
            }
        }

        // Ensure local ./xml/{fileName} exists; otherwise download from asset server and return content.
        private static readonly HttpClient _http = new();

        private static async Task<string> GetOrDownloadTemplateAsync(
            string fileName,
            CancellationToken ct
        )
        {
            var localDir = Path.Combine(AppContext.BaseDirectory, "xml");
            var localPath = Path.Combine(localDir, fileName);

            try
            {
                if (System.IO.File.Exists(localPath))
                    return await System
                        .IO.File.ReadAllTextAsync(localPath, ct)
                        .ConfigureAwait(false);
            }
            catch
            {
                // ignore and attempt download
            }

            try
            {
                Directory.CreateDirectory(localDir);
                var url = $"https://assets.krnl64.win/qsl/xml/{fileName}";
                using var resp = await _http.GetAsync(url, ct).ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode)
                    return string.Empty;
                var content = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                await System
                    .IO.File.WriteAllTextAsync(localPath, content, ct)
                    .ConfigureAwait(false);
                return content;
            }
            catch
            {
                return string.Empty;
            }
        }
    }

    // ======================
    // DTOs
    // ======================
    public class ResolveAllRequest
    {
        public string? ProductInput { get; set; }
        public string? Market { get; set; } = "CN";
        public string? Locale { get; set; } = "zh-CN";
        public string? Ring { get; set; } = "Retail";
        public bool IncludeAppx { get; set; } = true;
        public bool IncludeNonAppx { get; set; } = true;
    }

    public class ResolveAllResponse
    {
        public string? ProductId { get; set; }
        public QueryLinksHelper.AppInfo? AppInfo { get; set; }
        public string? Cookie { get; set; }
        public string? FileListXml { get; set; }
        public List<QueryLinksHelper.DownloadItem>? AppxPackages { get; set; }
        public List<QueryLinksHelper.DownloadItem>? NonAppxPackages { get; set; }
        public List<string>? Errors { get; set; }
    }
}
