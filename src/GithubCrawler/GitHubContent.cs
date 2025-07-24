namespace GitHubCrawler
{
    using GetSomeInput;
    using System;
    using System.Collections.Generic;
    using System.Net.Http;
    using System.Text.Json;
    using System.Text.Json.Serialization;
    using System.Threading.Tasks;

    /// <summary>
    /// GitHub content.
    /// </summary>
    public class GitHubContent
    {
        /// <summary>
        /// Name.
        /// </summary>
        [JsonPropertyName("name")]
        public string Name { get; set; }

        /// <summary>
        /// Path.
        /// </summary>
        [JsonPropertyName("path")]
        public string Path { get; set; }

        /// <summary>
        /// Type.
        /// </summary>
        [JsonPropertyName("type")]
        public string Type { get; set; }

        /// <summary>
        /// HTML URL.
        /// </summary>
        [JsonPropertyName("html_url")]
        public string HtmlUrl { get; set; }

        /// <summary>
        /// Download URL.
        /// </summary>
        [JsonPropertyName("download_url")]
        public string DownloadUrl { get; set; }

        /// <summary>
        /// GitHub content.
        /// </summary>
        public GitHubContent()
        {

        }
    }
}
