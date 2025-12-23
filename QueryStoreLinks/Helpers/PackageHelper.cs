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
    }
}
