namespace GitHubCrawler
{
    using System.Net;

    /// <summary>
    /// Thrown when GitHub returns HTTP 404 for a repository contents request, meaning the repository or path does not exist,
    /// or is private and the crawler has no token with access to it.
    /// Instances are immutable after construction and are safe to share across threads.
    /// </summary>
    public class GitHubRepositoryNotFoundException : GitHubCrawlerException
    {
        /// <summary>
        /// The repository owner that was requested. Never null.
        /// </summary>
        public string Owner { get; }

        /// <summary>
        /// The repository name that was requested. Never null.
        /// </summary>
        public string Repository { get; }

        /// <summary>
        /// Initializes a new instance for the specified repository.
        /// </summary>
        /// <param name="owner">The repository owner. Null is stored as an empty string.</param>
        /// <param name="repository">The repository name. Null is stored as an empty string.</param>
        public GitHubRepositoryNotFoundException(string owner, string repository)
            : base($"Repository not found: {owner}/{repository}", HttpStatusCode.NotFound)
        {
            Owner = owner ?? string.Empty;
            Repository = repository ?? string.Empty;
        }
    }
}
