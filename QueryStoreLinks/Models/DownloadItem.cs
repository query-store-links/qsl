namespace QueryStoreLinks.Models
{
    public sealed class DownloadItem
    {
        public string FileName { get; set; } = string.Empty;
        public string FileLink { get; set; } = string.Empty;
        public string FileSize { get; set; } = "0 B";
    }
}
