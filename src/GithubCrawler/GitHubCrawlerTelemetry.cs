namespace GitHubCrawler
{
    /// <summary>
    /// Public, stable names for every telemetry point GitHubCrawler emits.
    /// The library emits only through the base class library (<see cref="System.Diagnostics.Metrics.Meter"/> and
    /// <see cref="System.Diagnostics.ActivitySource"/>) and takes no exporter or SDK dependency. Emission is effectively
    /// free until a host subscribes to <see cref="MeterName"/> and <see cref="ActivitySourceName"/>.
    /// These names are a public contract consumed by dashboards and alerts. Do not rename them casually.
    /// See TELEMETRY.md in the repository root for the full catalog.
    /// This class is thread-safe (it contains only constants).
    /// </summary>
    public static class GitHubCrawlerTelemetry
    {
        #region Sources

        /// <summary>
        /// Name of the <see cref="System.Diagnostics.Metrics.Meter"/> that carries all GitHubCrawler metrics.
        /// </summary>
        public const string MeterName = "GitHubCrawler";

        /// <summary>
        /// Name of the <see cref="System.Diagnostics.ActivitySource"/> that carries all GitHubCrawler spans.
        /// </summary>
        public const string ActivitySourceName = "GitHubCrawler";

        #endregion

        #region Metrics

        /// <summary>
        /// Histogram (s): duration of each public operation. Labels: githubcrawler.operation, outcome, error.type.
        /// For a repository crawl this spans the full enumeration, including time the caller spends between items.
        /// </summary>
        public const string MetricOperationDuration = "githubcrawler.operation.duration";

        /// <summary>
        /// Counter ({operation}): completed public operations. Labels: githubcrawler.operation, outcome, error.type.
        /// </summary>
        public const string MetricOperations = "githubcrawler.operations";

        /// <summary>
        /// UpDownCounter ({operation}): public operations currently in flight. Labels: githubcrawler.operation.
        /// </summary>
        public const string MetricOperationsActive = "githubcrawler.operations.active";

        /// <summary>
        /// Observable gauge (s): Unix time of the most recent successful operation. Labels: githubcrawler.operation.
        /// Only reported once an operation of that type has succeeded in this process.
        /// </summary>
        public const string MetricOperationLastSuccess = "githubcrawler.operation.last_success.timestamp";

        /// <summary>
        /// Histogram (s): duration of each outbound GitHub HTTP call, including reading the response body.
        /// Labels: github.operation, outcome, http.response.status_code, error.type, githubcrawler.auth.
        /// </summary>
        public const string MetricGitHubRequestDuration = "githubcrawler.github.request.duration";

        /// <summary>
        /// Counter ({request}): outbound GitHub HTTP calls.
        /// Labels: github.operation, outcome, http.response.status_code, error.type, githubcrawler.auth.
        /// </summary>
        public const string MetricGitHubRequests = "githubcrawler.github.requests";

        /// <summary>
        /// Counter ({request}): GitHub responses that indicate a rate limit (HTTP 403 or 429).
        /// Labels: github.operation, githubcrawler.auth.
        /// </summary>
        public const string MetricGitHubRateLimitExceeded = "githubcrawler.github.rate_limit.exceeded";

        /// <summary>
        /// Observable gauge ({request}): the most recent X-RateLimit-Remaining value returned by GitHub, per auth mode.
        /// Labels: githubcrawler.auth. Only reported once a response carrying the header has been observed.
        /// </summary>
        public const string MetricGitHubRateLimitRemaining = "githubcrawler.github.rate_limit.remaining";

        /// <summary>
        /// Counter ({item}): repository entries discovered while crawling. Labels: githubcrawler.item.type.
        /// </summary>
        public const string MetricCrawlItems = "githubcrawler.crawl.items";

        /// <summary>
        /// Histogram ({file}): number of file URLs yielded per repository crawl. Labels: outcome.
        /// </summary>
        public const string MetricCrawlFiles = "githubcrawler.crawl.files";

        /// <summary>
        /// Histogram ({directory}): number of directories listed per repository crawl. Labels: outcome.
        /// </summary>
        public const string MetricCrawlDirectories = "githubcrawler.crawl.directories";

        /// <summary>
        /// Histogram (By): size of downloaded file bodies. Labels: outcome.
        /// </summary>
        public const string MetricFileDownloadSize = "githubcrawler.file.download.size";

        /// <summary>
        /// UpDownCounter ({crawler}): crawler instances constructed and not yet disposed. Labels: githubcrawler.auth.
        /// </summary>
        public const string MetricCrawlersActive = "githubcrawler.crawlers.active";

        /// <summary>
        /// Observable gauge ({info}): constant 1 carrying the library version. Labels: githubcrawler.version.
        /// </summary>
        public const string MetricBuildInfo = "githubcrawler.build.info";

        #endregion

        #region Spans

        /// <summary>
        /// Internal root span for <see cref="GitHubRepoCrawler.GetRepositoryContentsAsync"/>.
        /// </summary>
        public const string SpanCrawlRepository = "githubcrawler crawl_repository";

        /// <summary>
        /// Internal root span for <see cref="GitHubRepoCrawler.GetFileContentsAsync"/>.
        /// </summary>
        public const string SpanGetFileContents = "githubcrawler get_file_contents";

        /// <summary>
        /// Client span for one GitHub Contents API directory listing.
        /// </summary>
        public const string SpanGitHubContentsList = "github contents.list";

        /// <summary>
        /// Client span for one file download.
        /// </summary>
        public const string SpanGitHubFileDownload = "github file.download";

        #endregion

        #region Attribute-Keys

        /// <summary>
        /// Metric label and span attribute: the public operation (see Operation* constants).
        /// </summary>
        public const string AttributeOperation = "githubcrawler.operation";

        /// <summary>
        /// Metric label and span attribute: the result of an operation or request (see Outcome* constants).
        /// </summary>
        public const string AttributeOutcome = "outcome";

        /// <summary>
        /// Metric label and span attribute (OpenTelemetry semantic convention): the error class.
        /// An HTTP status code string (for example "404") for HTTP failures, otherwise the exception type full name.
        /// </summary>
        public const string AttributeErrorType = "error.type";

        /// <summary>
        /// Metric label and span attribute: the outbound GitHub operation (see GitHubOperation* constants).
        /// </summary>
        public const string AttributeGitHubOperation = "github.operation";

        /// <summary>
        /// Metric label: whether the crawler sends a token (see Auth* constants).
        /// </summary>
        public const string AttributeAuth = "githubcrawler.auth";

        /// <summary>
        /// Metric label: the GitHub content entry type (file, dir, symlink, submodule, or other).
        /// </summary>
        public const string AttributeItemType = "githubcrawler.item.type";

        /// <summary>
        /// Metric label: library version on the build info gauge.
        /// </summary>
        public const string AttributeVersion = "githubcrawler.version";

        /// <summary>
        /// Span attribute: repository owner. Span only, never a metric label.
        /// </summary>
        public const string AttributeGitHubOwner = "github.owner";

        /// <summary>
        /// Span attribute: repository name. Span only, never a metric label.
        /// </summary>
        public const string AttributeGitHubRepo = "github.repo";

        /// <summary>
        /// Span attribute: repository-relative directory path being listed. Span only, never a metric label.
        /// </summary>
        public const string AttributeGitHubPath = "github.path";

        /// <summary>
        /// Span attribute: directory depth of a listing (0 is the repository root).
        /// </summary>
        public const string AttributeDepth = "githubcrawler.depth";

        /// <summary>
        /// Span attribute: number of entries returned by a directory listing.
        /// </summary>
        public const string AttributeItems = "githubcrawler.items";

        /// <summary>
        /// Span attribute: number of file URLs yielded by a crawl.
        /// </summary>
        public const string AttributeFiles = "githubcrawler.files";

        /// <summary>
        /// Span attribute: number of directories listed by a crawl.
        /// </summary>
        public const string AttributeDirectories = "githubcrawler.directories";

        /// <summary>
        /// Span attribute: the last X-RateLimit-Remaining value observed on the response.
        /// </summary>
        public const string AttributeRateLimitRemaining = "github.rate_limit.remaining";

        /// <summary>
        /// Span attribute (OpenTelemetry semantic convention): HTTP request method.
        /// </summary>
        public const string AttributeHttpRequestMethod = "http.request.method";

        /// <summary>
        /// Metric label and span attribute (OpenTelemetry semantic convention): HTTP response status code.
        /// </summary>
        public const string AttributeHttpResponseStatusCode = "http.response.status_code";

        /// <summary>
        /// Span attribute (OpenTelemetry semantic convention): response body size in bytes.
        /// </summary>
        public const string AttributeHttpResponseBodySize = "http.response.body.size";

        /// <summary>
        /// Span attribute (OpenTelemetry semantic convention): server host name. Span only, never a metric label.
        /// </summary>
        public const string AttributeServerAddress = "server.address";

        /// <summary>
        /// Span attribute (OpenTelemetry semantic convention): request URL with any query string and user info removed.
        /// </summary>
        public const string AttributeUrlFull = "url.full";

        #endregion

        #region Attribute-Values

        /// <summary>
        /// Operation value for <see cref="GitHubRepoCrawler.GetRepositoryContentsAsync"/>.
        /// </summary>
        public const string OperationCrawlRepository = "crawl_repository";

        /// <summary>
        /// Operation value for <see cref="GitHubRepoCrawler.GetFileContentsAsync"/>.
        /// </summary>
        public const string OperationGetFileContents = "get_file_contents";

        /// <summary>
        /// GitHub operation value for a Contents API directory listing.
        /// </summary>
        public const string GitHubOperationContentsList = "contents.list";

        /// <summary>
        /// GitHub operation value for a file download.
        /// </summary>
        public const string GitHubOperationFileDownload = "file.download";

        /// <summary>
        /// Outcome: the operation or request completed successfully.
        /// </summary>
        public const string OutcomeSuccess = "success";

        /// <summary>
        /// Outcome: the operation or request failed. See error.type for the cause.
        /// </summary>
        public const string OutcomeFailure = "failure";

        /// <summary>
        /// Outcome: the caller cancelled via its CancellationToken. Not treated as an error.
        /// </summary>
        public const string OutcomeCancelled = "cancelled";

        /// <summary>
        /// Outcome: the caller stopped enumerating a crawl before it finished (for example, took the first N files).
        /// </summary>
        public const string OutcomeAbandoned = "abandoned";

        /// <summary>
        /// Auth value: the crawler sends a personal access token.
        /// </summary>
        public const string AuthToken = "token";

        /// <summary>
        /// Auth value: the crawler sends no token.
        /// </summary>
        public const string AuthAnonymous = "anonymous";

        #endregion
    }
}
