namespace GitHubCrawler
{
    using System;
    using System.Collections.Generic;
    using System.Net;

    /// <summary>
    /// The result of <see cref="GitHubRepoCrawler.GetFileContentsAsync"/>: the fully buffered file body plus response metadata.
    /// The object does not hold the underlying HTTP response, which has already been disposed.
    /// Thread safety: instances are not synchronized; do not mutate an instance while other threads read it.
    /// </summary>
    public class GitHubFileResponse
    {
        /// <summary>
        /// Raw file bytes. Null only on a default-constructed instance; never null when returned by the crawler.
        /// </summary>
        public byte[]? Content { get; set; }

        /// <summary>
        /// Content type of the response (for example "text/plain; charset=utf-8"). Null when the server sent none.
        /// </summary>
        public string? ContentType { get; set; }

        /// <summary>
        /// HTTP status code of the response. Default: 0 on a default-constructed instance.
        /// </summary>
        public HttpStatusCode StatusCode { get; set; }

        /// <summary>
        /// Response headers (not content headers). Null only on a default-constructed instance.
        /// </summary>
        public Dictionary<string, IEnumerable<string>>? Headers { get; set; }

        /// <summary>
        /// Final request URL after redirects. May be null when the response carried no request message.
        /// </summary>
        public Uri? FinalUrl { get; set; }

        /// <summary>
        /// Initializes a new instance with all properties at their defaults.
        /// </summary>
        public GitHubFileResponse()
        {
        }
    }
}
