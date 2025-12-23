using System.Text.Json.Serialization;

namespace QueryStoreLinks.Models.StoreEdgeFD
{
    public class PackageManifestResponse
    {
        [JsonPropertyName("Data")]
        public PackageManifestData? Data { get; set; }
    }

    public class PackageManifestData
    {
        [JsonPropertyName("PackageIdentifier")]
        public string PackageIdentifier { get; set; } = string.Empty;

        [JsonPropertyName("Versions")]
        public List<PackageManifestVersion> Versions { get; set; } = new();
    }

    public class PackageManifestVersion
    {
        [JsonPropertyName("PackageVersion")]
        public string PackageVersion { get; set; } = string.Empty;

        [JsonPropertyName("DefaultLocale")]
        public DefaultLocale? DefaultLocale { get; set; }

        [JsonPropertyName("Installers")]
        public List<SparkInstaller> Installers { get; set; } = new();
    }

    public class DefaultLocale
    {
        [JsonPropertyName("PackageName")]
        public string PackageName { get; set; } = string.Empty;

        [JsonPropertyName("Publisher")]
        public string Publisher { get; set; } = string.Empty;

        [JsonPropertyName("ShortDescription")]
        public string ShortDescription { get; set; } = string.Empty;

        [JsonPropertyName("Description")]
        public string Description { get; set; } = string.Empty;

        [JsonPropertyName("Agreements")]
        public List<AgreementDetail> Agreements { get; set; } = new();
    }

    public class AgreementDetail
    {
        [JsonPropertyName("AgreementLabel")]
        public string AgreementLabel { get; set; } = string.Empty;

        [JsonPropertyName("Agreement")]
        public string? Agreement { get; set; }

        [JsonPropertyName("AgreementUrl")]
        public string? AgreementUrl { get; set; }
    }

    public class SparkInstaller
    {
        [JsonPropertyName("InstallerUrl")]
        public string InstallerUrl { get; set; } = string.Empty;

        [JsonPropertyName("Architecture")]
        public string Architecture { get; set; } = string.Empty;

        [JsonPropertyName("InstallerType")]
        public string InstallerType { get; set; } = string.Empty;

        [JsonPropertyName("InstallerSha256")]
        public string InstallerSha256 { get; set; } = string.Empty;
    }
}
