namespace GitHubCrawler
{
    using System;
    using System.Net;

    /// <summary>
    /// Thrown when GitHub refuses a request with HTTP 403 or 429, which GitHub uses for rate limiting.
    /// Supply a personal access token to raise the limit from 60 to 5,000 requests per hour.
    /// Instances are immutable after construction and are safe to share across threads.
    /// </summary>
    public class GitHubRateLimitException : GitHubCrawlerException
    {
        /// <summary>
        /// The X-RateLimit-Remaining value GitHub returned, or null when the header was absent or invalid.
        /// </summary>
        public long? RateLimitRemaining { get; }

        /// <summary>
        /// The time at which the rate limit window resets (from X-RateLimit-Reset), or null when the header was absent or invalid.
        /// </summary>
        public DateTimeOffset? RateLimitReset { get; }

        /// <summary>
        /// Initializes a new instance.
        /// </summary>
        /// <param name="statusCode">The HTTP status code GitHub returned (403 or 429).</param>
        /// <param name="rateLimitRemaining">The X-RateLimit-Remaining value, or null when unknown.</param>
        /// <param name="rateLimitReset">The rate limit reset time, or null when unknown.</param>
        public GitHubRateLimitException(HttpStatusCode statusCode, long? rateLimitRemaining, DateTimeOffset? rateLimitReset)
            : base("API rate limit exceeded. Consider using an authentication token.", statusCode)
        {
            RateLimitRemaining = rateLimitRemaining;
            RateLimitReset = rateLimitReset;
        }
    }
}
