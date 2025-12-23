using System;
using System.Net.Http.Headers;
using System.Net.Mime;
using StoreLib.Services;

namespace QueryStoreLinks.Helpers
{
    public class PackageHelper
    {
        private static readonly MSHttpClient _httpClient = new MSHttpClient();

        private static String BytesToString(long byteCount)
        {
            string[] suf = { "B", "KB", "MB", "GB", "TB", "PB", "EB" }; //Longs run out around EB
            if (byteCount == 0)
                return "0" + suf[0];
            long bytes = Math.Abs(byteCount);
            int place = Convert.ToInt32(Math.Floor(Math.Log(bytes, 1024)));
            double num = Math.Round(bytes / Math.Pow(1024, place), 1);
            return (Math.Sign(byteCount) * num).ToString() + suf[place];
        }

        public static async Task<string> GetFileSizeAsync(
            string url,
            CancellationToken ct = default
        )
        {
            if (string.IsNullOrEmpty(url))
                return "Unknown";

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Head, url);
                using var response = await _httpClient.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    ct
                );

                if (response.IsSuccessStatusCode && response.Content.Headers.ContentLength.HasValue)
                {
                    return BytesToString(response.Content.Headers.ContentLength.Value);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            { /* Do Nothing */
            }

            return "Unknown";
        }

        public static async Task<string> GetFileName(string url, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(url))
                return string.Empty;

            async Task<HttpResponseMessage> SendAsync(HttpMethod method)
            {
                var req = new HttpRequestMessage(method, url);
                req.Headers.TryAddWithoutValidation(
                    "User-Agent",
                    "Microsoft-Delivery-Optimization/10.0"
                );

                return await _httpClient.SendAsync(
                    req,
                    HttpCompletionOption.ResponseHeadersRead,
                    ct
                );
            }

            using var response = await SendAsync(HttpMethod.Head);

            if (!response.IsSuccessStatusCode)
            {
                response.Dispose();
                using var getResponse = await SendAsync(HttpMethod.Get);
                if (!getResponse.IsSuccessStatusCode)
                    return string.Empty;

                return ExtractFileName(getResponse, url);
            }

            return ExtractFileName(response, url);
        }

        private static string ExtractFileName(HttpResponseMessage response, string url)
        {
            var cd = response.Content.Headers.ContentDisposition;

            if (!string.IsNullOrEmpty(cd?.FileNameStar))
                return cd.FileNameStar.Trim('"');

            if (!string.IsNullOrEmpty(cd?.FileName))
                return cd.FileName.Trim('"');

            try
            {
                var uri = new Uri(url);
                var name = Path.GetFileName(uri.AbsolutePath);
                if (!string.IsNullOrEmpty(name))
                    return name;
            }
            catch
            {
                // ignore
            }

            return String.Empty;
        }
    }
}
