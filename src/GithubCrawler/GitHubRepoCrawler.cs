namespace GitHubCrawler
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
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
        private readonly string _Auth = GitHubCrawlerTelemetry.AuthAnonymous;
        private bool _disposed = false;

        /// <summary>
        /// Initializes a new instance of the GitHubRepoCrawler class.
        /// </summary>
        /// <param name="token">Optional GitHub personal access token for authenticated requests.</param>
        public GitHubRepoCrawler(string token = null)
            : this(new HttpClientHandler(), token)
        {
        }

        /// <summary>
        /// Initializes a new instance of the GitHubRepoCrawler class using a caller-supplied message handler.
        /// This overload is useful for injecting a custom handler (for example, a proxy configuration) or a
        /// fake handler for testing.
        /// </summary>
        /// <param name="handler">The HTTP message handler used to send requests. The handler is owned by this instance and is disposed when the crawler is disposed.</param>
        /// <param name="token">Optional GitHub personal access token for authenticated requests.</param>
        /// <exception cref="ArgumentNullException">Thrown when the handler is null.</exception>
        public GitHubRepoCrawler(HttpMessageHandler handler, string token = null)
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));

            _httpClient = new HttpClient(handler);
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "GitHubRepoCrawler/1.0");

            if (!string.IsNullOrEmpty(token))
            {
                _githubToken = token;
                _httpClient.DefaultRequestHeaders.Add("Authorization", $"token {token}");
                _Auth = GitHubCrawlerTelemetry.AuthToken;
            }

            CrawlerInstrumentation.CrawlerCreated(_Auth);
        }

        /// <summary>
        /// Asynchronously retrieves all file URLs from a GitHub repository.
        /// Emits the "githubcrawler crawl_repository" span with one "github contents.list" child per directory, and the
        /// githubcrawler.operation.* and githubcrawler.github.* metrics. See TELEMETRY.md.
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
            OperationScope scope = OperationScope.Start(
                GitHubCrawlerTelemetry.OperationCrawlRepository,
                GitHubCrawlerTelemetry.SpanCrawlRepository,
                cancellationToken);

            CrawlCounters counters = new CrawlCounters();
            IAsyncEnumerator<string> enumerator = null;

            try
            {
                try
                {
                    ThrowIfDisposed();

                    if (string.IsNullOrWhiteSpace(gitUrl))
                    {
                        throw new ArgumentException("Invalid GitHub repository URL", nameof(gitUrl));
                    }

                    var (owner, repo) = ParseGitUrl(gitUrl);
                    if (string.IsNullOrEmpty(owner) || string.IsNullOrEmpty(repo))
                    {
                        throw new ArgumentException("Invalid GitHub repository URL");
                    }

                    scope.Activity?.SetTag(GitHubCrawlerTelemetry.AttributeGitHubOwner, owner);
                    scope.Activity?.SetTag(GitHubCrawlerTelemetry.AttributeGitHubRepo, repo);

                    enumerator = CrawlDirectoryAsync(owner, repo, "", 0, scope.Context, counters, cancellationToken)
                        .GetAsyncEnumerator(cancellationToken);
                }
                catch (Exception e)
                {
                    scope.Fail(e);
                    throw;
                }

                while (true)
                {
                    string url = null;

                    try
                    {
                        if (!await enumerator.MoveNextAsync().ConfigureAwait(false)) break;
                        url = enumerator.Current;
                    }
                    catch (Exception e)
                    {
                        scope.Fail(e);
                        throw;
                    }

                    counters.Files++;
                    yield return url;
                }

                scope.Succeed();
            }
            finally
            {
                if (enumerator != null) await enumerator.DisposeAsync().ConfigureAwait(false);

                scope.Activity?.SetTag(GitHubCrawlerTelemetry.AttributeFiles, counters.Files);
                scope.Activity?.SetTag(GitHubCrawlerTelemetry.AttributeDirectories, counters.Directories);
                scope.Abandon();
                CrawlerInstrumentation.CrawlCompleted(scope.Outcome, counters.Files, counters.Directories);
            }
        }

        /// <summary>
        /// Asynchronously downloads file contents from a GitHub URL.
        /// Emits the "githubcrawler get_file_contents" span with a "github file.download" child, and the
        /// githubcrawler.operation.*, githubcrawler.github.*, and githubcrawler.file.download.size metrics.
        /// A response with a status code of 400 or above is returned to the caller as before, and is recorded as a failure
        /// with error.type set to the status code.
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
            OperationScope scope = OperationScope.Start(
                GitHubCrawlerTelemetry.OperationGetFileContents,
                GitHubCrawlerTelemetry.SpanGetFileContents,
                cancellationToken);

            try
            {
                ThrowIfDisposed();

                if (string.IsNullOrWhiteSpace(url))
                    throw new ArgumentException("Download URL cannot be null or empty.", nameof(url));

                GitHubFileResponse result = await DownloadFileAsync(url, cancellationToken).ConfigureAwait(false);

                if ((int)result.StatusCode >= 400)
                    scope.Fail(CrawlerInstrumentation.StatusErrorType(result.StatusCode), "HTTP " + (int)result.StatusCode);
                else
                    scope.Succeed();

                return result;
            }
            catch (Exception e)
            {
                scope.Fail(e);
                throw;
            }
        }

        private async Task<GitHubFileResponse> DownloadFileAsync(string url, System.Threading.CancellationToken cancellationToken)
        {
            long start = Stopwatch.GetTimestamp();
            Activity activity = CrawlerInstrumentation.StartActivity(
                GitHubCrawlerTelemetry.SpanGitHubFileDownload,
                ActivityKind.Client,
                default(ActivityContext));

            activity?.SetTag(GitHubCrawlerTelemetry.AttributeGitHubOperation, GitHubCrawlerTelemetry.GitHubOperationFileDownload);
            activity?.SetTag(GitHubCrawlerTelemetry.AttributeHttpRequestMethod, "GET");

            if (activity != null && Uri.TryCreate(url, UriKind.Absolute, out Uri requestUri))
            {
                activity.SetTag(GitHubCrawlerTelemetry.AttributeServerAddress, requestUri.Host);
                activity.SetTag(GitHubCrawlerTelemetry.AttributeUrlFull, CrawlerInstrumentation.SanitizeUrl(requestUri));
            }

            int statusCode = 0;

            try
            {
                HttpResponseMessage response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
                statusCode = (int)response.StatusCode;
                activity?.SetTag(GitHubCrawlerTelemetry.AttributeHttpResponseStatusCode, statusCode);
                CrawlerInstrumentation.ObserveRateLimit(response, _Auth, activity);

                byte[] contentBytes = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);

                GitHubFileResponse result = new GitHubFileResponse
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

                long size = contentBytes != null ? contentBytes.LongLength : 0;
                activity?.SetTag(GitHubCrawlerTelemetry.AttributeHttpResponseBodySize, size);

                if (statusCode >= 400)
                {
                    string errorType = CrawlerInstrumentation.StatusErrorType(response.StatusCode);
                    CrawlerInstrumentation.MarkFailure(activity, GitHubCrawlerTelemetry.OutcomeFailure, errorType, "HTTP " + statusCode, null);
                    CrawlerInstrumentation.GitHubRequestCompleted(
                        GitHubCrawlerTelemetry.GitHubOperationFileDownload, _Auth, statusCode,
                        GitHubCrawlerTelemetry.OutcomeFailure, errorType, CrawlerInstrumentation.ElapsedSeconds(start));
                    CrawlerInstrumentation.FileDownloaded(GitHubCrawlerTelemetry.OutcomeFailure, size);
                }
                else
                {
                    CrawlerInstrumentation.MarkSuccess(activity);
                    CrawlerInstrumentation.GitHubRequestCompleted(
                        GitHubCrawlerTelemetry.GitHubOperationFileDownload, _Auth, statusCode,
                        GitHubCrawlerTelemetry.OutcomeSuccess, null, CrawlerInstrumentation.ElapsedSeconds(start));
                    CrawlerInstrumentation.FileDownloaded(GitHubCrawlerTelemetry.OutcomeSuccess, size);
                }

                return result;
            }
            catch (Exception e)
            {
                RecordRequestException(activity, GitHubCrawlerTelemetry.GitHubOperationFileDownload, statusCode, start, e, cancellationToken);
                throw;
            }
            finally
            {
                activity?.Dispose();
            }
        }

        private async IAsyncEnumerable<string> CrawlDirectoryAsync(
            string owner,
            string repo,
            string path,
            int depth,
            ActivityContext parent,
            CrawlCounters counters,
            [System.Runtime.CompilerServices.EnumeratorCancellation] System.Threading.CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            List<GitHubContent> items = await ListDirectoryAsync(owner, repo, path, depth, parent, cancellationToken).ConfigureAwait(false);
            counters.Directories++;

            foreach (var item in items)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!String.IsNullOrEmpty(item.DownloadUrl))
                    yield return item.DownloadUrl;

                if (item.Type == "dir")
                {
                    await foreach (var subItem in CrawlDirectoryAsync(owner, repo, item.Path, depth + 1, parent, counters, cancellationToken).ConfigureAwait(false))
                    {
                        yield return subItem;
                    }
                }
            }
        }

        private async Task<List<GitHubContent>> ListDirectoryAsync(
            string owner,
            string repo,
            string path,
            int depth,
            ActivityContext parent,
            System.Threading.CancellationToken cancellationToken)
        {
            var apiUrl = $"https://api.github.com/repos/{owner}/{repo}/contents/{path}";

            long start = Stopwatch.GetTimestamp();
            Activity activity = CrawlerInstrumentation.StartActivity(
                GitHubCrawlerTelemetry.SpanGitHubContentsList,
                ActivityKind.Client,
                parent);

            if (activity != null)
            {
                activity.SetTag(GitHubCrawlerTelemetry.AttributeGitHubOperation, GitHubCrawlerTelemetry.GitHubOperationContentsList);
                activity.SetTag(GitHubCrawlerTelemetry.AttributeGitHubOwner, owner);
                activity.SetTag(GitHubCrawlerTelemetry.AttributeGitHubRepo, repo);
                activity.SetTag(GitHubCrawlerTelemetry.AttributeGitHubPath, path);
                activity.SetTag(GitHubCrawlerTelemetry.AttributeDepth, depth);
                activity.SetTag(GitHubCrawlerTelemetry.AttributeHttpRequestMethod, "GET");
                activity.SetTag(GitHubCrawlerTelemetry.AttributeServerAddress, "api.github.com");
                activity.SetTag(GitHubCrawlerTelemetry.AttributeUrlFull, apiUrl);
            }

            int statusCode = 0;

            try
            {
                using (HttpResponseMessage response = await _httpClient.GetAsync(apiUrl, cancellationToken).ConfigureAwait(false))
                {
                    statusCode = (int)response.StatusCode;
                    activity?.SetTag(GitHubCrawlerTelemetry.AttributeHttpResponseStatusCode, statusCode);
                    CrawlerInstrumentation.ObserveRateLimit(response, _Auth, activity);

                    if (!response.IsSuccessStatusCode)
                    {
                        Exception failure;

                        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                            failure = new Exception($"Repository not found: {owner}/{repo}");
                        else if (response.StatusCode == System.Net.HttpStatusCode.Forbidden)
                            failure = new Exception("API rate limit exceeded. Consider using an authentication token.");
                        else
                            failure = new Exception($"API request failed: {response.StatusCode}");

                        failure.Data[CrawlerInstrumentation.ErrorTypeDataKey] = CrawlerInstrumentation.StatusErrorType(response.StatusCode);
                        throw failure;
                    }

                    string json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                    List<GitHubContent> items = JsonSerializer.Deserialize<List<GitHubContent>>(json);

                    activity?.SetTag(GitHubCrawlerTelemetry.AttributeItems, items?.Count ?? 0);
                    CrawlerInstrumentation.ItemsDiscovered(items);
                    CrawlerInstrumentation.MarkSuccess(activity);
                    CrawlerInstrumentation.GitHubRequestCompleted(
                        GitHubCrawlerTelemetry.GitHubOperationContentsList, _Auth, statusCode,
                        GitHubCrawlerTelemetry.OutcomeSuccess, null, CrawlerInstrumentation.ElapsedSeconds(start));

                    return items;
                }
            }
            catch (Exception e)
            {
                RecordRequestException(activity, GitHubCrawlerTelemetry.GitHubOperationContentsList, statusCode, start, e, cancellationToken);
                throw;
            }
            finally
            {
                activity?.Dispose();
            }
        }

        private void RecordRequestException(
            Activity activity,
            string gitHubOperation,
            int statusCode,
            long start,
            Exception exception,
            System.Threading.CancellationToken cancellationToken)
        {
            string outcome = CrawlerInstrumentation.ClassifyOutcome(exception, cancellationToken);
            string errorType = outcome == GitHubCrawlerTelemetry.OutcomeFailure ? CrawlerInstrumentation.ClassifyErrorType(exception) : null;

            CrawlerInstrumentation.MarkFailure(activity, outcome, errorType, exception.Message, exception);
            CrawlerInstrumentation.GitHubRequestCompleted(
                gitHubOperation, _Auth, statusCode, outcome, errorType, CrawlerInstrumentation.ElapsedSeconds(start));
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

                CrawlerInstrumentation.CrawlerDisposed(_Auth);

                // Note: If there were unmanaged resources, they would be freed here

                _disposed = true;
            }
        }
    }
}