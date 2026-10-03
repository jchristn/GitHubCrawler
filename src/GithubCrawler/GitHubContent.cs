namespace GitHubCrawler
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// One entry returned by the GitHub Contents API for a directory listing.
    /// All properties are null when the corresponding JSON field is absent or null.
    /// Thread safety: instances are not synchronized; do not mutate an instance while other threads read it.
    /// </summary>
    public class GitHubContent
    {
        /// <summary>
        /// Entry name (for example "README.md"). May be null.
        /// </summary>
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        /// <summary>
        /// Repository-relative path (for example "docs/README.md"). May be null.
        /// </summary>
        [JsonPropertyName("path")]
        public string? Path { get; set; }

        /// <summary>
        /// Entry type: "file", "dir", "symlink", or "submodule". May be null.
        /// </summary>
        [JsonPropertyName("type")]
        public string? Type { get; set; }

        /// <summary>
        /// URL of the entry on github.com. May be null.
        /// </summary>
        [JsonPropertyName("html_url")]
        public string? HtmlUrl { get; set; }

        /// <summary>
        /// Raw download URL. Null for directories and submodules.
        /// </summary>
        [JsonPropertyName("download_url")]
        public string? DownloadUrl { get; set; }

        /// <summary>
        /// Initializes a new instance with all properties null.
        /// </summary>
        public GitHubContent()
        {
        }
    }
}
