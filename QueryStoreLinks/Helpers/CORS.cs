namespace QueryStoreLinks.Helpers
{
    public class CORS
    {
        public static bool IsOriginAllowed(string? origin, string[] patterns)
        {
            if (string.IsNullOrWhiteSpace(origin))
                return false;
            if (patterns == null || patterns.Length == 0)
                return false;

            if (!Uri.TryCreate(origin, UriKind.Absolute, out var originUri))
                return false;
            var originScheme = originUri.Scheme;
            var originHost = originUri.Host;

            foreach (var p in patterns)
            {
                if (string.IsNullOrWhiteSpace(p))
                    continue;

                string pat = p.Trim();
                string? patScheme = null;
                string patHost = pat;

                if (pat.Contains("://"))
                {
                    // Split into scheme and host portion without requiring the host to be a valid DNS name
                    var parts = pat.Split(new[] { "://" }, 2, StringSplitOptions.None);
                    patScheme = parts[0];
                    patHost = parts.Length > 1 ? parts[1] : string.Empty;
                }

                // Trim any trailing slashes that might be present
                if (patHost.EndsWith("/"))
                    patHost = patHost.TrimEnd('/');

                // If scheme is specified in pattern and differs -> not match
                if (
                    !string.IsNullOrEmpty(patScheme)
                    && !string.Equals(patScheme, originScheme, StringComparison.OrdinalIgnoreCase)
                )
                    continue;

                // Host pattern may contain leading wildcard *.example.com
                if (patHost.StartsWith("*.", StringComparison.Ordinal))
                {
                    var suffix = patHost.Substring(1);
                    if (
                        originHost.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
                        || originHost.Equals(
                            suffix.TrimStart('.'),
                            StringComparison.OrdinalIgnoreCase
                        )
                    )
                        return true;
                }
                else
                {
                    if (string.Equals(originHost, patHost, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }

            return false;
        }
    }
}
