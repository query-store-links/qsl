using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using QueryStoreLinks.Helpers;
using static System.Console;

internal class Program
{
    private static readonly CancellationTokenSource _cts = new();

    public static async Task Main(string[] args)
    {
        Console.CancelKeyPress += (s, e) =>
        {
            e.Cancel = true;
            _cts.Cancel();
            WriteLine("Cancelling...");
        };

        WriteLine("== Query Store Links CLI ==");
        WriteLine(
            "Place the SOAP template files under the XML folder at the application directory:"
        );
        WriteLine(" - XML/cookie.xml");
        WriteLine(" - XML/wu.xml");
        WriteLine(" - XML/url.xml");
        WriteLine();

        string xmlDir = Path.Combine(AppContext.BaseDirectory, "XML");
        string cookieSoap = await ReadTextOrPromptAsync(
            Path.Combine(xmlDir, "cookie.xml"),
            "cookie.xml SOAP template"
        );
        string wuSoap = await ReadTextOrPromptAsync(
            Path.Combine(xmlDir, "wu.xml"),
            "wu.xml SOAP template"
        );
        string urlSoap = await ReadTextOrPromptAsync(
            Path.Combine(xmlDir, "url.xml"),
            "url.xml SOAP template"
        );

        string productInput = GetArgOrAsk(
            args,
            "--pid",
            "Enter Product ID or full URL:",
            defaultValue: null
        );
        string productId = ParseProductIdSafe(productInput);

        string market = GetArgOrAsk(
            args,
            "--market",
            "Enter market (default: CN):",
            defaultValue: "CN"
        );
        string locale = GetArgOrAsk(
            args,
            "--locale",
            "Enter locale (default: zh-CN):",
            defaultValue: "zh-CN"
        );
        string ring = GetArgOrAsk(
            args,
            "--ring",
            "Enter ring (default: Retail):",
            defaultValue: "Retail"
        );

        WriteLine();
        WriteLine($"ProductId: {productId}");
        WriteLine($"Market: {market}, Locale: {locale}, Ring: {ring}");
        WriteLine();

        try
        {
            // 1) 获取 Cookie
            WriteLine("1) Fetching cookie...");
            EnsureNotEmpty(
                cookieSoap,
                "cookie.xml SOAP template is empty. Please provide the correct template."
            );
            string cookie = await QueryLinksHelper_GetCookieAsync(cookieSoap);
            if (string.IsNullOrWhiteSpace(cookie))
            {
                WriteLine("Failed to obtain cookie (EncryptedData was empty).");
            }
            else
            {
                WriteLine($"Cookie (EncryptedData): {Truncate(cookie, 120)}");
            }

            // 2) 获取应用信息
            WriteLine("2) Fetching app information...");
            var (ok, appInfo) = await QueryLinksHelper_GetAppInformationAsync(
                productId,
                market,
                locale
            );
            if (!ok)
            {
                WriteLine("App information request failed.");
            }
            else
            {
                WriteLine($"Name: {appInfo.Name}");
                WriteLine($"Publisher: {appInfo.Publisher}");
                WriteLine($"CategoryId: {appInfo.CategoryId}");
                WriteLine();
            }

            // 3) 获取文件列表 XML（只有在存在 CategoryId 与 Cookie 时执行）
            string fileListXml = string.Empty;
            if (
                !string.IsNullOrWhiteSpace(appInfo.CategoryId) && !string.IsNullOrWhiteSpace(cookie)
            )
            {
                WriteLine("3) Fetching file list XML...");
                EnsureNotEmpty(
                    wuSoap,
                    "wu.xml SOAP template is empty. Please provide the correct template."
                );
                fileListXml = await QueryLinksHelper_GetFileListXmlAsync(
                    cookie,
                    appInfo.CategoryId,
                    ring,
                    wuSoap
                );
                WriteLine($"FileListXml (snippet): {Truncate(fileListXml, 200)}");
                WriteLine();
            }
            else
            {
                WriteLine(
                    "Skipping file list retrieval: missing CategoryId or Cookie (possibly a non-packaged app or cookie acquisition failed)."
                );
            }

            // 4) 解析 APPX 包并获取下载链接（需要 fileListXml + urlSoap）
            if (!string.IsNullOrWhiteSpace(fileListXml))
            {
                WriteLine("4) Resolving APPX packages and download URLs...");
                EnsureNotEmpty(
                    urlSoap,
                    "url.xml SOAP template is empty. Please provide the correct template."
                );
                var appxPackages = await QueryLinksHelper_GetAppxPackagesAsync(
                    fileListXml,
                    ring,
                    urlSoap
                );
                DumpDownloads("APPX Packages", appxPackages);
                SaveDownloads("appx_packages.txt", appxPackages);
            }
            else
            {
                WriteLine("No APPX file list available to parse.");
            }

            // 5) 获取非 APPX 安装包
            WriteLine("5) Fetching non-APPX installers...");
            var nonAppx = await QueryLinksHelper_GetNonAppxPackagesAsync(productId, market);
            DumpDownloads("Non-APPX Installers", nonAppx);
            SaveDownloads("nonappx_packages.txt", nonAppx);

            WriteLine();
            WriteLine("Done. Output files generated: appx_packages.txt, nonappx_packages.txt");
        }
        catch (OperationCanceledException)
        {
            WriteLine("Operation cancelled.");
        }
        catch (Exception ex)
        {
            WriteLine("An error occurred:");
            WriteLine(ex.ToString());
        }

        WriteLine();
        WriteLine("Press any key to exit...");
        ReadKey();
    }

    // 解析 ProductId
    private static string ParseProductIdSafe(string input)
    {
        try
        {
            return QueryLinksHelper.ParseRequestContent(input);
        }
        catch
        {
            return input;
        }
    }

    private static Task<string> QueryLinksHelper_GetCookieAsync(string cookieSoapTemplate) =>
        QueryLinksHelper.GetCookieAsync(cookieSoapTemplate);

    private static Task<(
        bool requestResult,
        QueryLinksHelper.AppInfo appInfo
    )> QueryLinksHelper_GetAppInformationAsync(string productId, string market, string locale) =>
        QueryLinksHelper.GetAppInformationAsync(productId, market, locale);

    private static Task<string> QueryLinksHelper_GetFileListXmlAsync(
        string cookie,
        string categoryId,
        string ring,
        string wuSoapTemplate
    ) => QueryLinksHelper.GetFileListXmlAsync(cookie, categoryId, ring, wuSoapTemplate);

    private static Task<List<QueryLinksHelper.DownloadItem>> QueryLinksHelper_GetAppxPackagesAsync(
        string fileListXml,
        string ring,
        string urlSoapTemplate
    ) => QueryLinksHelper.GetAppxPackagesAsync(fileListXml, ring, urlSoapTemplate);

    private static Task<
        List<QueryLinksHelper.DownloadItem>
    > QueryLinksHelper_GetNonAppxPackagesAsync(string productId, string market) =>
        QueryLinksHelper.GetNonAppxPackagesAsync(productId, market);

    private static async Task<string> ReadTextOrPromptAsync(string path, string displayName)
    {
        if (File.Exists(path))
        {
            WriteLine($"Reading {displayName}: {path}");
            return await File.ReadAllTextAsync(path, Encoding.UTF8);
        }

        WriteLine($"{displayName} not found at: {path}");
        WriteLine(
            $"Paste the {displayName} content below, then press Enter on an empty line to finish:"
        );
        return ReadMultilineFromConsole();
    }

    private static string ReadMultilineFromConsole()
    {
        var sb = new StringBuilder();
        string? line;
        while (!string.IsNullOrEmpty(line = ReadLine()))
        {
            sb.AppendLine(line);
        }
        return sb.ToString();
    }

    private static string GetArgOrAsk(
        string[] args,
        string key,
        string prompt,
        string? defaultValue = null
    )
    {
        string? value = null;

        // 支持 --key=value
        foreach (var a in args)
        {
            if (a.StartsWith(key + "=", StringComparison.OrdinalIgnoreCase))
            {
                value = a[(key.Length + 1)..];
                break;
            }
        }

        // 支持 --key value
        if (value is null)
        {
            for (int i = 0; i < args.Length; i++)
            {
                if (
                    string.Equals(args[i], key, StringComparison.OrdinalIgnoreCase)
                    && i + 1 < args.Length
                )
                {
                    value = args[i + 1];
                    break;
                }
            }
        }

        if (string.IsNullOrWhiteSpace(value))
        {
            Write(prompt);
            if (!string.IsNullOrEmpty(defaultValue))
            {
                Write($" (default: {defaultValue})");
            }
            WriteLine();
            string? input = ReadLine();
            value = string.IsNullOrWhiteSpace(input) ? defaultValue : input;
        }

        return value ?? string.Empty;
    }

    // 判空校验
    private static void EnsureNotEmpty(string text, string message)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new InvalidOperationException(message);
    }

    private static string Truncate(string text, int max)
    {
        if (string.IsNullOrEmpty(text))
            return text;
        return text.Length <= max ? text : (text.Substring(0, max) + " …");
    }

    private static void DumpDownloads(string title, List<QueryLinksHelper.DownloadItem> items)
    {
        WriteLine($"== {title} (count: {items.Count}) ==");
        foreach (var d in items)
        {
            WriteLine($" - Name: {d.FileName}");
            WriteLine($"   Size: {d.FileSize}");
            WriteLine($"   Link: {d.FileLink}");
        }
        WriteLine();
    }

    private static void SaveDownloads(string fileName, List<QueryLinksHelper.DownloadItem> items)
    {
        try
        {
            var lines = new List<string> { $"Count={items.Count}" };
            lines.AddRange(items.Select(i => $"{i.FileName}\t{i.FileSize}\t{i.FileLink}"));
            File.WriteAllLines(fileName, lines, Encoding.UTF8);
            WriteLine($"Saved: {fileName}");
        }
        catch (Exception ex)
        {
            WriteLine($"Failed to save {fileName}: {ex.Message}");
        }
    }
}
