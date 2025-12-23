using StoreLib.Models;
using StoreLib.Services;

namespace QueryStoreLinks.Models
{
    public class ResolveAllRequest
    {
        public string? ProductInput { get; set; }
        public string? Market { get; set; } = "US";
        public string? Locale { get; set; } = "en-US";
        public string? Ring { get; set; } = "Retail";
        public string? IdentifierType { get; set; } = "ProductId";
        public bool IncludeAppx { get; set; } = true;
        public bool IncludeNonAppx { get; set; } = true;

        public Locale GetMappedLocale()
        {
            var parts = Locale?.Split('-') ?? Array.Empty<string>();

            string marketPart = parts.Length > 1 ? parts[1] : (Market ?? "US");
            string langPart = parts.Length > 0 ? parts[0] : "en";

            Enum.TryParse<Market>(marketPart, true, out var m);
            Enum.TryParse<Lang>(langPart, true, out var l);

            return new Locale(m, l, true);
        }

        public IdentifierType GetIdentifierType()
        {
            if (Enum.TryParse<IdentifierType>(IdentifierType, true, out var result))
            {
                return result;
            }
            return StoreLib.Models.IdentifierType.ProductID;
        }
    }
}
