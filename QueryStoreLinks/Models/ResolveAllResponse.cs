namespace QueryStoreLinks.Models
{
    public class ResolveAllResponse
    {
        public string? ProductId { get; set; }
        public AppInfo? AppInfo { get; set; }
        public List<DownloadItem>? AppxPackages { get; set; }
        public List<DownloadItem>? NonAppxPackages { get; set; }
        public List<string>? Errors { get; set; }
    }
}
