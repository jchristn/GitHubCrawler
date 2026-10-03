namespace GitHubCrawler
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Globalization;
    using System.Linq;
    using System.Net;
    using System.Net.Http;
    using System.Runtime.CompilerServices;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// GitHub repository crawler that discovers file URLs through the GitHub REST API (v3) and downloads file contents.
    /// Implements <see cref="IDisposable"/>; the crawler owns its <see cref="HttpClient"/> and message handler.
    /// Thread safety: <see cref="GetRepositoryContentsAsync"/> and <see cref="GetFileContentsAsync"/> may be called
    /// concurrently from multiple threads. Set <see cref="ApiBaseUrl"/> and <see cref="UserAgent"/> before issuing requests;
    /// changing them while requests are in flight is not supported. <see cref="Dispose()"/> must not race with in-flight calls.
    /// </summary>
    public class GitHubRepoCrawler : IDisposable
    {
        /// <summary>
        /// Default value of <see cref="ApiBaseUrl"/>: the public GitHub REST API.
        /// </summary>
        public const string DefaultApiBaseUrl = "https://api.github.com/";

        /// <summary>
        /// Default value of <see cref="UserAgent"/>.
        /// </summary>
        public const string DefaultUserAgent = "GitHubRepoCrawler/1.0";

        /// <summary>
        /// Base URL of the GitHub REST API. Override it to target GitHub Enterprise Server (for example
        /// "https://github.example.com/api/v3/"). Must be an absolute http or https URL.
        /// Default: <see cref="DefaultApiBaseUrl"/>. A trailing slash is optional.
        /// Never null.
        /// </summary>
        /// <exception cref="ArgumentNullException">Thrown when set to null.</exception>
        /// <exception cref="ArgumentException">Thrown when set to a value that is not an absolute http or https URL.</exception>
        public string ApiBaseUrl
        {
            get
            {
                return _ApiBaseUrl;
            }
            set
            {
                ArgumentNullException.ThrowIfNull(value);

                if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? uri)
                    || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                {
                    throw new ArgumentException("ApiBaseUrl must be an absolute http or https URL, but was '" + value + "'.", nameof(value));
                }

                _ApiBaseUrl = value.EndsWith("/", StringComparison.Ordinal) ? value : value + "/";
                _ApiHost = uri.Host;
            }
        }

        /// <summary>
        /// User-Agent header sent with every request. GitHub requires a User-Agent and recommends one that identifies
        /// your application. Default: <see cref="DefaultUserAgent"/>. Must not be null, empty, or whitespace.
        /// Never null.
        /// </summary>
        /// <exception cref="ArgumentNullException">Thrown when set to null.</exception>
        /// <exception cref="ArgumentException">Thrown when set to an empty or whitespace value.</exception>
        /// <exception cref="ObjectDisposedException">Thrown when set after the crawler has been disposed.</exception>
        public string UserAgent
        {
            get
            {
                return _UserAgent;
            }
            set
            {
                ArgumentNullException.ThrowIfNull(value);
                if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("UserAgent must not be empty or whitespace.", nameof(value));

                HttpClient client = GetClient();
                client.DefaultRequestHeaders.UserAgent.Clear();
                client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", value);
                _UserAgent = value;
            }
        }

        private readonly string _Auth = GitHubCrawlerTelemetry.AuthAnonymous;
        private HttpClient? _HttpClient = null;
        private string _ApiBaseUrl = DefaultApiBaseUrl;
        private string _ApiHost = "api.github.com";
        private string _UserAgent = DefaultUserAgent;
        private bool _Disposed = false;

        /// <summary>
        /// Initializes a new instance of the GitHubRepoCrawler class.
        /// </summary>
        /// <param name="token">Optional GitHub personal access token for authenticated requests. Null or empty sends no Authorization header.</param>
        public GitHubRepoCrawler(string? token = null)
            : this(new HttpClientHandler(), token)
        {
        }

        /// <summary>
        /// Initializes a new instance of the GitHubRepoCrawler class using a caller-supplied message handler.
        /// This overload is useful for injecting a custom handler (for example, a proxy configuration) or a
        /// fake handler for testing.
        /// </summary>
        /// <param name="handler">The HTTP message handler used to send requests. The handler is owned by this instance and is disposed when the crawler is disposed.</param>
        /// <param name="token">Optional GitHub personal access token for authenticated requests. Null or empty sends no Authorization header.</param>
        /// <exception cref="ArgumentNullException">Thrown when the handler is null.</exception>
        public GitHubRepoCrawler(HttpMessageHandler handler, string? token = null)
        {
            ArgumentNullException.ThrowIfNull(handler);

            _HttpClient = new HttpClient(handler);
            _HttpClient.DefaultRequestHeaders.Add("User-Agent", DefaultUserAgent);

            if (!string.IsNullOrEmpty(token))
            {
                _HttpClient.DefaultRequestHeaders.Add("Authorization", $"token {token}");
                _Auth = GitHubCrawlerTelemetry.AuthToken;
            }

            CrawlerInstrumentation.CrawlerCreated(_Auth);
        }

        /// <summary>
        /// Asynchronously retrieves all file URLs from a GitHub repository, recursing into every directory.
        /// Emits the "githubcrawler crawl_repository" span with one "github contents.list" child per directory, and the
        /// githubcrawler.operation.* and githubcrawler.github.* metrics. See TELEMETRY.md.
        /// </summary>
        /// <param name="gitUrl">The GitHub repository URL (https://github.com/owner/repo, optionally ending in .git, or git@github.com:owner/repo.git).</param>
        /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
        /// <returns>An async enumerable of file download URLs. Never null; items are never null or empty.</returns>
        /// <exception cref="ObjectDisposedException">Thrown when this method is called after the object has been disposed.</exception>
        /// <exception cref="ArgumentException">Thrown when the provided URL is invalid.</exception>
        /// <exception cref="GitHubRepositoryNotFoundException">Thrown when GitHub returns HTTP 404 for the repository or a directory.</exception>
        /// <exception cref="GitHubRateLimitException">Thrown when GitHub returns HTTP 403 or 429.</exception>
        /// <exception cref="GitHubCrawlerException">Thrown when GitHub returns any other unsuccessful status code.</exception>
        /// <exception cref="JsonException">Thrown when a directory listing response is not valid JSON.</exception>
        /// <exception cref="HttpRequestException">Thrown when the request fails at the network level.</exception>
        /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled or times out.</exception>
        public async IAsyncEnumerable<string> GetRepositoryContentsAsync(
            string gitUrl,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            OperationScope scope = OperationScope.Start(
                GitHubCrawlerTelemetry.OperationCrawlRepository,
                GitHubCrawlerTelemetry.SpanCrawlRepository,
                cancellationToken);

            CrawlCounters counters = new CrawlCounters();
            IAsyncEnumerator<string>? enumerator = null;

            try
            {
                try
                {
                    ThrowIfDisposed();

                    if (string.IsNullOrWhiteSpace(gitUrl))
                    {
                        throw new ArgumentException("Invalid GitHub repository URL", nameof(gitUrl));
                    }

                    GitHubRepositoryReference? repository = GitHubRepositoryReference.Parse(gitUrl);
                    if (repository == null)
                    {
                        throw new ArgumentException("Invalid GitHub repository URL");
                    }

                    scope.Activity?.SetTag(GitHubCrawlerTelemetry.AttributeGitHubOwner, repository.Owner);
                    scope.Activity?.SetTag(GitHubCrawlerTelemetry.AttributeGitHubRepo, repository.Repository);

                    enumerator = CrawlDirectoryAsync(repository, string.Empty, 0, scope.Context, counters, cancellationToken)
                        .GetAsyncEnumerator(cancellationToken);
                }
                catch (Exception e)
                {
                    scope.Fail(e);
                    throw;
                }

                while (true)
                {
                    string url;

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
        /// Asynchronously downloads file contents from a GitHub URL. The response is fully buffered into
        /// <see cref="GitHubFileResponse.Content"/> and the underlying HTTP response is disposed before returning.
        /// A response with a status code of 400 or above is returned to the caller (it is not thrown), and is recorded in
        /// telemetry as a failure with error.type set to the status code.
        /// Emits the "githubcrawler get_file_contents" span with a "github file.download" child, and the
        /// githubcrawler.operation.*, githubcrawler.github.*, and githubcrawler.file.download.size metrics.
        /// </summary>
        /// <param name="url">The file download URL.</param>
        /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
        /// <returns>A GitHubFileResponse containing the file content and metadata. Never null.</returns>
        /// <exception cref="ObjectDisposedException">Thrown when this method is called after the object has been disposed.</exception>
        /// <exception cref="ArgumentException">Thrown when the URL is null or empty.</exception>
        /// <exception cref="InvalidOperationException">Thrown when the URL is not an absolute URL.</exception>
        /// <exception cref="HttpRequestException">Thrown when the request fails at the network level.</exception>
        /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled or times out.</exception>
        public async Task<GitHubFileResponse> GetFileContentsAsync(
            string url,
            CancellationToken cancellationToken = default)
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
            if (_Disposed) return;

            if (disposing)
            {
                _HttpClient?.Dispose();
                _HttpClient = null;
            }

            CrawlerInstrumentation.CrawlerDisposed(_Auth);
            _Disposed = true;
        }

        private async Task<GitHubFileResponse> DownloadFileAsync(string url, CancellationToken cancellationToken)
        {
            HttpClient client = GetClient();
            long start = Stopwatch.GetTimestamp();
            Activity? activity = CrawlerInstrumentation.StartActivity(
                GitHubCrawlerTelemetry.SpanGitHubFileDownload,
                ActivityKind.Client,
                default(ActivityContext));

            activity?.SetTag(GitHubCrawlerTelemetry.AttributeGitHubOperation, GitHubCrawlerTelemetry.GitHubOperationFileDownload);
            activity?.SetTag(GitHubCrawlerTelemetry.AttributeHttpRequestMethod, "GET");

            if (activity != null && Uri.TryCreate(url, UriKind.Absolute, out Uri? requestUri))
            {
                activity.SetTag(GitHubCrawlerTelemetry.AttributeServerAddress, requestUri.Host);
                activity.SetTag(GitHubCrawlerTelemetry.AttributeUrlFull, CrawlerInstrumentation.SanitizeUrl(requestUri));
            }

            int statusCode = 0;

            try
            {
                using (HttpResponseMessage response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false))
                {
                    statusCode = (int)response.StatusCode;
                    activity?.SetTag(GitHubCrawlerTelemetry.AttributeHttpResponseStatusCode, statusCode);
                    CrawlerInstrumentation.ObserveRateLimit(response, _Auth, activity);

                    byte[] contentBytes = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);

                    GitHubFileResponse result = new GitHubFileResponse
                    {
                        Content = contentBytes,
                        ContentType = response.Content.Headers.ContentType?.ToString(),
                        StatusCode = response.StatusCode,
                        FinalUrl = response.RequestMessage?.RequestUri,
                        Headers = response.Headers.ToDictionary(
                            h => h.Key,
                            h => (IEnumerable<string>)h.Value.ToArray())
                    };

                    long size = contentBytes.LongLength;
                    activity?.SetTag(GitHubCrawlerTelemetry.AttributeHttpResponseBodySize, size);

                    string outcome = statusCode >= 400 ? GitHubCrawlerTelemetry.OutcomeFailure : GitHubCrawlerTelemetry.OutcomeSuccess;
                    string? errorType = statusCode >= 400 ? CrawlerInstrumentation.StatusErrorType(response.StatusCode) : null;

                    if (errorType != null)
                        CrawlerInstrumentation.MarkFailure(activity, outcome, errorType, "HTTP " + statusCode, null);
                    else
                        CrawlerInstrumentation.MarkSuccess(activity);

                    CrawlerInstrumentation.GitHubRequestCompleted(
                        GitHubCrawlerTelemetry.GitHubOperationFileDownload, _Auth, statusCode,
                        outcome, errorType, CrawlerInstrumentation.ElapsedSeconds(start));
                    CrawlerInstrumentation.FileDownloaded(outcome, size);

                    return result;
                }
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
            GitHubRepositoryReference repository,
            string path,
            int depth,
            ActivityContext parent,
            CrawlCounters counters,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            List<GitHubContent> items = await ListDirectoryAsync(repository, path, depth, parent, cancellationToken).ConfigureAwait(false);
            counters.Directories++;

            foreach (GitHubContent item in items)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!string.IsNullOrEmpty(item.DownloadUrl))
                    yield return item.DownloadUrl;

                if (item.Type == "dir" && item.Path != null)
                {
                    await foreach (string subItem in CrawlDirectoryAsync(repository, item.Path, depth + 1, parent, counters, cancellationToken).ConfigureAwait(false))
                    {
                        yield return subItem;
                    }
                }
            }
        }

        private async Task<List<GitHubContent>> ListDirectoryAsync(
            GitHubRepositoryReference repository,
            string path,
            int depth,
            ActivityContext parent,
            CancellationToken cancellationToken)
        {
            HttpClient client = GetClient();
            string apiUrl = $"{_ApiBaseUrl}repos/{repository.Owner}/{repository.Repository}/contents/{path}";

            long start = Stopwatch.GetTimestamp();
            Activity? activity = CrawlerInstrumentation.StartActivity(
                GitHubCrawlerTelemetry.SpanGitHubContentsList,
                ActivityKind.Client,
                parent);

            if (activity != null)
            {
                activity.SetTag(GitHubCrawlerTelemetry.AttributeGitHubOperation, GitHubCrawlerTelemetry.GitHubOperationContentsList);
                activity.SetTag(GitHubCrawlerTelemetry.AttributeGitHubOwner, repository.Owner);
                activity.SetTag(GitHubCrawlerTelemetry.AttributeGitHubRepo, repository.Repository);
                activity.SetTag(GitHubCrawlerTelemetry.AttributeGitHubPath, path);
                activity.SetTag(GitHubCrawlerTelemetry.AttributeDepth, depth);
                activity.SetTag(GitHubCrawlerTelemetry.AttributeHttpRequestMethod, "GET");
                activity.SetTag(GitHubCrawlerTelemetry.AttributeServerAddress, _ApiHost);
                activity.SetTag(GitHubCrawlerTelemetry.AttributeUrlFull, apiUrl);
            }

            int statusCode = 0;

            try
            {
                using (HttpResponseMessage response = await client.GetAsync(apiUrl, cancellationToken).ConfigureAwait(false))
                {
                    statusCode = (int)response.StatusCode;
                    activity?.SetTag(GitHubCrawlerTelemetry.AttributeHttpResponseStatusCode, statusCode);
                    CrawlerInstrumentation.ObserveRateLimit(response, _Auth, activity);

                    if (!response.IsSuccessStatusCode)
                    {
                        throw CreateStatusException(response, repository);
                    }

                    string json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                    List<GitHubContent> items = JsonSerializer.Deserialize<List<GitHubContent>>(json) ?? new List<GitHubContent>();

                    activity?.SetTag(GitHubCrawlerTelemetry.AttributeItems, items.Count);
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

        private static GitHubCrawlerException CreateStatusException(HttpResponseMessage response, GitHubRepositoryReference repository)
        {
            if (response.StatusCode == HttpStatusCode.NotFound)
                return new GitHubRepositoryNotFoundException(repository.Owner, repository.Repository);

            if (response.StatusCode == HttpStatusCode.Forbidden || response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                long? remaining = ReadLongHeader(response, "X-RateLimit-Remaining");
                long? resetSeconds = ReadLongHeader(response, "X-RateLimit-Reset");
                DateTimeOffset? reset = resetSeconds.HasValue ? DateTimeOffset.FromUnixTimeSeconds(resetSeconds.Value) : null;
                return new GitHubRateLimitException(response.StatusCode, remaining, reset);
            }

            return new GitHubCrawlerException($"API request failed: {response.StatusCode}", response.StatusCode);
        }

        private static long? ReadLongHeader(HttpResponseMessage response, string name)
        {
            if (!response.Headers.TryGetValues(name, out IEnumerable<string>? values)) return null;

            string? raw = values.FirstOrDefault();
            if (long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out long parsed)) return parsed;
            return null;
        }

        private void RecordRequestException(
            Activity? activity,
            string gitHubOperation,
            int statusCode,
            long start,
            Exception exception,
            CancellationToken cancellationToken)
        {
            string outcome = CrawlerInstrumentation.ClassifyOutcome(exception, cancellationToken);
            string? errorType = outcome == GitHubCrawlerTelemetry.OutcomeFailure ? CrawlerInstrumentation.ClassifyErrorType(exception) : null;

            CrawlerInstrumentation.MarkFailure(activity, outcome, errorType, exception.Message, exception);
            CrawlerInstrumentation.GitHubRequestCompleted(
                gitHubOperation, _Auth, statusCode, outcome, errorType, CrawlerInstrumentation.ElapsedSeconds(start));
        }

        private HttpClient GetClient()
        {
            HttpClient? client = _HttpClient;
            if (_Disposed || client == null) throw new ObjectDisposedException(nameof(GitHubRepoCrawler));
            return client;
        }

        private void ThrowIfDisposed()
        {
            if (_Disposed)
            {
                throw new ObjectDisposedException(nameof(GitHubRepoCrawler));
            }
        }
    }
}
