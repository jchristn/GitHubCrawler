namespace GitHubCrawler
{
    using System;
    using System.Net;

    /// <summary>
    /// Base exception for failures reported by the GitHub API or raw content host while crawling.
    /// Derives from <see cref="Exception"/>, so existing handlers that catch <see cref="Exception"/> continue to work.
    /// Instances are immutable after construction and are safe to share across threads.
    /// </summary>
    public class GitHubCrawlerException : Exception
    {
        /// <summary>
        /// The HTTP status code returned by GitHub, or null when the failure did not come from an HTTP response.
        /// </summary>
        public HttpStatusCode? StatusCode { get; }

        /// <summary>
        /// Initializes a new instance with a message.
        /// </summary>
        /// <param name="message">A message describing the failure.</param>
        public GitHubCrawlerException(string message)
            : base(message)
        {
        }

        /// <summary>
        /// Initializes a new instance with a message and an inner exception.
        /// </summary>
        /// <param name="message">A message describing the failure.</param>
        /// <param name="innerException">The exception that caused this failure. May be null.</param>
        public GitHubCrawlerException(string message, Exception? innerException)
            : base(message, innerException)
        {
        }

        /// <summary>
        /// Initializes a new instance with a message and the HTTP status code returned by GitHub.
        /// </summary>
        /// <param name="message">A message describing the failure.</param>
        /// <param name="statusCode">The HTTP status code returned by GitHub. May be null.</param>
        public GitHubCrawlerException(string message, HttpStatusCode? statusCode)
            : base(message)
        {
            StatusCode = statusCode;
        }
    }
}
