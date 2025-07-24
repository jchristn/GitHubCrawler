namespace GitHubCrawler
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net.Http;
    using System.Text.Json;
    using System.Threading.Tasks;

    /// <summary>
    /// GitHub repository crawler that implements IDisposable for proper resource cleanup.
    /// </summary>
    public class GitHubRepoCrawler : IDisposable
    {
        private HttpClient _httpClient = null;
        private readonly string _githubToken = null;
        private bool _disposed = false;

        /// <summary>
        /// Initializes a new instance of the GitHubRepoCrawler class.
        /// </summary>
        /// <param name="token">Optional GitHub personal access token for authenticated requests.</param>
        public GitHubRepoCrawler(string token = null)
        {
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "GitHubRepoCrawler/1.0");

            if (!string.IsNullOrEmpty(token))
            {
                _githubToken = token;
                _httpClient.DefaultRequestHeaders.Add("Authorization", $"token {token}");
            }
        }

        /// <summary>
        /// Asynchronously retrieves all file URLs from a GitHub repository.
        /// </summary>
        /// <param name="gitUrl">The GitHub repository URL.</param>
        /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
        /// <returns>An async enumerable of file download URLs.</returns>
        /// <exception cref="ObjectDisposedException">Thrown when this method is called after the object has been disposed.</exception>
        /// <exception cref="ArgumentException">Thrown when the provided URL is invalid.</exception>
        /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled.</exception>
        public async IAsyncEnumerable<string> GetRepositoryContentsAsync(
            string gitUrl, 
            [System.Runtime.CompilerServices.EnumeratorCancellation] System.Threading.CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            var (owner, repo) = ParseGitUrl(gitUrl);
            if (string.IsNullOrEmpty(owner) || string.IsNullOrEmpty(repo))
            {
                throw new ArgumentException("Invalid GitHub repository URL");
            }

            await foreach (var url in CrawlDirectoryAsync(owner, repo, "", cancellationToken))
            {
                yield return url;
            }
        }

        /// <summary>
        /// Asynchronously downloads file contents from a GitHub URL.
        /// </summary>
        /// <param name="url">The file download URL.</param>
        /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
        /// <returns>A GitHubFileResponse containing the file content and metadata.</returns>
        /// <exception cref="ObjectDisposedException">Thrown when this method is called after the object has been disposed.</exception>
        /// <exception cref="ArgumentException">Thrown when the URL is null or empty.</exception>
        /// <exception cref="Exception">Thrown when the file cannot be fetched.</exception>
        /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled.</exception>
        public async Task<GitHubFileResponse> GetFileContentsAsync(
            string url, 
            System.Threading.CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            if (string.IsNullOrWhiteSpace(url))
                throw new ArgumentException("Download URL cannot be null or empty.", nameof(url));

            var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            var contentBytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);

            return new GitHubFileResponse
            {
                Content = contentBytes,
                ContentType = response.Content.Headers.ContentType?.ToString(),
                StatusCode = response.StatusCode,
                FinalUrl = response.RequestMessage.RequestUri,
                Headers = response.Headers.ToDictionary(
                    h => h.Key,
                    h => h.Value
                )
            };
        }

        private async IAsyncEnumerable<string> CrawlDirectoryAsync(string owner, string repo, string path, [System.Runtime.CompilerServices.EnumeratorCancellation] System.Threading.CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            var apiUrl = $"https://api.github.com/repos/{owner}/{repo}/contents/{path}";

            HttpResponseMessage response = await _httpClient.GetAsync(apiUrl, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                    throw new Exception($"Repository not found: {owner}/{repo}");
                if (response.StatusCode == System.Net.HttpStatusCode.Forbidden)
                    throw new Exception("API rate limit exceeded. Consider using an authentication token.");

                throw new Exception($"API request failed: {response.StatusCode}");
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            List<GitHubContent> items = JsonSerializer.Deserialize<List<GitHubContent>>(json);

            foreach (var item in items)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!String.IsNullOrEmpty(item.DownloadUrl))
                    yield return item.DownloadUrl;

                if (item.Type == "dir")
                {
                    await foreach (var subItem in CrawlDirectoryAsync(owner, repo, item.Path, cancellationToken))
                    {
                        yield return subItem;
                    }
                }
            }
        }

        private (string owner, string repo) ParseGitUrl(string gitUrl)
        {
            if (gitUrl.EndsWith(".git"))
            {
                gitUrl = gitUrl.Substring(0, gitUrl.Length - 4);
            }

            if (gitUrl.StartsWith("https://github.com/") || gitUrl.StartsWith("http://github.com/"))
            {
                var parts = gitUrl.Replace("https://github.com/", "")
                                  .Replace("http://github.com/", "")
                                  .Split('/');
                if (parts.Length >= 2)
                {
                    return (parts[0], parts[1]);
                }
            }
            else if (gitUrl.StartsWith("git@github.com:"))
            {
                var parts = gitUrl.Replace("git@github.com:", "").Split('/');
                if (parts.Length >= 2)
                {
                    return (parts[0], parts[1]);
                }
            }

            return (null, null);
        }

        /// <summary>
        /// Throws an ObjectDisposedException if this instance has been disposed.
        /// </summary>
        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(GitHubRepoCrawler));
            }
        }

        /// <summary>
        /// Releases all resources used by the GitHubRepoCrawler.
        /// </summary>
        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Releases the unmanaged resources used by the GitHubRepoCrawler and optionally releases the managed resources.
        /// </summary>
        /// <param name="disposing">true to release both managed and unmanaged resources; false to release only unmanaged resources.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    // Dispose managed resources
                    _httpClient?.Dispose();
                    _httpClient = null;
                }

                // Note: If there were unmanaged resources, they would be freed here

                _disposed = true;
            }
        }
    }
}