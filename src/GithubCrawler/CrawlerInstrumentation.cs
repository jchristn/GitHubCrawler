namespace GitHubCrawler
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Diagnostics.Metrics;
    using System.Globalization;
    using System.Linq;
    using System.Net;
    using System.Net.Http;
    using System.Reflection;
    using System.Threading;
    using System.Threading.Tasks;

    internal static class CrawlerInstrumentation
    {
        internal const string ErrorTypeDataKey = "githubcrawler.error.type";

        internal static readonly string Version = ResolveVersion();

        internal static readonly ActivitySource Source = new ActivitySource(GitHubCrawlerTelemetry.ActivitySourceName, Version);

        internal static readonly Meter Meter = new Meter(GitHubCrawlerTelemetry.MeterName, Version);

        private static readonly double[] _OperationBuckets = { 0.005, 0.01, 0.025, 0.05, 0.1, 0.25, 0.5, 1, 2.5, 5, 10, 30, 60, 120, 300, 600 };
        private static readonly double[] _RequestBuckets = { 0.005, 0.01, 0.025, 0.05, 0.1, 0.25, 0.5, 1, 2.5, 5, 10, 30, 60 };
        private static readonly double[] _CountBuckets = { 1, 5, 10, 25, 50, 100, 250, 500, 1000, 2500, 5000, 10000, 50000 };
        private static readonly double[] _SizeBuckets = { 1024, 4096, 16384, 65536, 262144, 1048576, 4194304, 16777216, 67108864, 268435456 };

        private static readonly Histogram<double> _OperationDuration = Meter.CreateHistogram<double>(
            GitHubCrawlerTelemetry.MetricOperationDuration, "s", "Duration of each public GitHubCrawler operation.",
            null, new InstrumentAdvice<double> { HistogramBucketBoundaries = _OperationBuckets });

        private static readonly Counter<long> _Operations = Meter.CreateCounter<long>(
            GitHubCrawlerTelemetry.MetricOperations, "{operation}", "Completed public GitHubCrawler operations.");

        private static readonly UpDownCounter<long> _OperationsActive = Meter.CreateUpDownCounter<long>(
            GitHubCrawlerTelemetry.MetricOperationsActive, "{operation}", "Public GitHubCrawler operations currently in flight.");

        private static readonly Histogram<double> _GitHubRequestDuration = Meter.CreateHistogram<double>(
            GitHubCrawlerTelemetry.MetricGitHubRequestDuration, "s", "Duration of each outbound GitHub HTTP call.",
            null, new InstrumentAdvice<double> { HistogramBucketBoundaries = _RequestBuckets });

        private static readonly Counter<long> _GitHubRequests = Meter.CreateCounter<long>(
            GitHubCrawlerTelemetry.MetricGitHubRequests, "{request}", "Outbound GitHub HTTP calls.");

        private static readonly Counter<long> _GitHubRateLimitExceeded = Meter.CreateCounter<long>(
            GitHubCrawlerTelemetry.MetricGitHubRateLimitExceeded, "{request}", "GitHub responses indicating a rate limit (403 or 429).");

        private static readonly Counter<long> _CrawlItems = Meter.CreateCounter<long>(
            GitHubCrawlerTelemetry.MetricCrawlItems, "{item}", "Repository entries discovered while crawling.");

        private static readonly Histogram<long> _CrawlFiles = Meter.CreateHistogram<long>(
            GitHubCrawlerTelemetry.MetricCrawlFiles, "{file}", "File URLs yielded per repository crawl.",
            null, new InstrumentAdvice<long> { HistogramBucketBoundaries = _CountBuckets.Select(b => (long)b).ToArray() });

        private static readonly Histogram<long> _CrawlDirectories = Meter.CreateHistogram<long>(
            GitHubCrawlerTelemetry.MetricCrawlDirectories, "{directory}", "Directories listed per repository crawl.",
            null, new InstrumentAdvice<long> { HistogramBucketBoundaries = _CountBuckets.Select(b => (long)b).ToArray() });

        private static readonly Histogram<long> _FileDownloadSize = Meter.CreateHistogram<long>(
            GitHubCrawlerTelemetry.MetricFileDownloadSize, "By", "Size of downloaded file bodies.",
            null, new InstrumentAdvice<long> { HistogramBucketBoundaries = _SizeBuckets.Select(b => (long)b).ToArray() });

        private static readonly UpDownCounter<long> _CrawlersActive = Meter.CreateUpDownCounter<long>(
            GitHubCrawlerTelemetry.MetricCrawlersActive, "{crawler}", "Crawler instances constructed and not yet disposed.");

        private static long _LastCrawlSuccessTicks = 0;
        private static long _LastFileSuccessTicks = 0;
        private static long _RateLimitRemainingToken = -1;
        private static long _RateLimitRemainingAnonymous = -1;

        static CrawlerInstrumentation()
        {
            Meter.CreateObservableGauge<double>(
                GitHubCrawlerTelemetry.MetricOperationLastSuccess,
                ObserveLastSuccess,
                "s",
                "Unix time of the most recent successful GitHubCrawler operation.");

            Meter.CreateObservableGauge<long>(
                GitHubCrawlerTelemetry.MetricGitHubRateLimitRemaining,
                ObserveRateLimitRemaining,
                "{request}",
                "Most recent X-RateLimit-Remaining value returned by GitHub.");

            Meter.CreateObservableGauge<int>(
                GitHubCrawlerTelemetry.MetricBuildInfo,
                () => new Measurement<int>(1, new KeyValuePair<string, object>(GitHubCrawlerTelemetry.AttributeVersion, Version)),
                "{info}",
                "Constant 1 carrying the GitHubCrawler library version.");
        }

        internal static void CrawlerCreated(string auth)
        {
            try
            {
                if (_CrawlersActive.Enabled)
                    _CrawlersActive.Add(1, new KeyValuePair<string, object>(GitHubCrawlerTelemetry.AttributeAuth, auth));
            }
            catch
            {
            }
        }

        internal static void CrawlerDisposed(string auth)
        {
            try
            {
                if (_CrawlersActive.Enabled)
                    _CrawlersActive.Add(-1, new KeyValuePair<string, object>(GitHubCrawlerTelemetry.AttributeAuth, auth));
            }
            catch
            {
            }
        }

        internal static void OperationStarted(string operation)
        {
            try
            {
                if (_OperationsActive.Enabled)
                    _OperationsActive.Add(1, new KeyValuePair<string, object>(GitHubCrawlerTelemetry.AttributeOperation, operation));
            }
            catch
            {
            }
        }

        internal static void OperationCompleted(string operation, string outcome, string errorType, double seconds)
        {
            try
            {
                if (_OperationsActive.Enabled)
                    _OperationsActive.Add(-1, new KeyValuePair<string, object>(GitHubCrawlerTelemetry.AttributeOperation, operation));

                if (_Operations.Enabled || _OperationDuration.Enabled)
                {
                    TagList tags = new TagList();
                    tags.Add(GitHubCrawlerTelemetry.AttributeOperation, operation);
                    tags.Add(GitHubCrawlerTelemetry.AttributeOutcome, outcome);
                    if (errorType != null) tags.Add(GitHubCrawlerTelemetry.AttributeErrorType, errorType);

                    _Operations.Add(1, tags);
                    _OperationDuration.Record(seconds, tags);
                }

                if (outcome == GitHubCrawlerTelemetry.OutcomeSuccess)
                {
                    long now = DateTime.UtcNow.Ticks;
                    if (operation == GitHubCrawlerTelemetry.OperationCrawlRepository)
                        Interlocked.Exchange(ref _LastCrawlSuccessTicks, now);
                    else if (operation == GitHubCrawlerTelemetry.OperationGetFileContents)
                        Interlocked.Exchange(ref _LastFileSuccessTicks, now);
                }
            }
            catch
            {
            }
        }

        internal static void GitHubRequestCompleted(
            string gitHubOperation,
            string auth,
            int statusCode,
            string outcome,
            string errorType,
            double seconds)
        {
            try
            {
                if (_GitHubRequests.Enabled || _GitHubRequestDuration.Enabled)
                {
                    TagList tags = new TagList();
                    tags.Add(GitHubCrawlerTelemetry.AttributeGitHubOperation, gitHubOperation);
                    tags.Add(GitHubCrawlerTelemetry.AttributeOutcome, outcome);
                    tags.Add(GitHubCrawlerTelemetry.AttributeAuth, auth);
                    if (statusCode > 0) tags.Add(GitHubCrawlerTelemetry.AttributeHttpResponseStatusCode, statusCode);
                    if (errorType != null) tags.Add(GitHubCrawlerTelemetry.AttributeErrorType, errorType);

                    _GitHubRequests.Add(1, tags);
                    _GitHubRequestDuration.Record(seconds, tags);
                }

                if ((statusCode == 403 || statusCode == 429) && _GitHubRateLimitExceeded.Enabled)
                {
                    _GitHubRateLimitExceeded.Add(
                        1,
                        new KeyValuePair<string, object>(GitHubCrawlerTelemetry.AttributeGitHubOperation, gitHubOperation),
                        new KeyValuePair<string, object>(GitHubCrawlerTelemetry.AttributeAuth, auth));
                }
            }
            catch
            {
            }
        }

        internal static void ObserveRateLimit(HttpResponseMessage response, string auth, Activity activity)
        {
            try
            {
                if (response == null) return;
                if (!response.Headers.TryGetValues("X-RateLimit-Remaining", out IEnumerable<string> values)) return;

                string raw = values.FirstOrDefault();
                if (!long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out long remaining)) return;

                if (auth == GitHubCrawlerTelemetry.AuthToken)
                    Interlocked.Exchange(ref _RateLimitRemainingToken, remaining);
                else
                    Interlocked.Exchange(ref _RateLimitRemainingAnonymous, remaining);

                activity?.SetTag(GitHubCrawlerTelemetry.AttributeRateLimitRemaining, remaining);
            }
            catch
            {
            }
        }

        internal static void ItemsDiscovered(List<GitHubContent> items)
        {
            try
            {
                if (items == null || !_CrawlItems.Enabled) return;

                foreach (GitHubContent item in items)
                {
                    _CrawlItems.Add(1, new KeyValuePair<string, object>(GitHubCrawlerTelemetry.AttributeItemType, NormalizeItemType(item?.Type)));
                }
            }
            catch
            {
            }
        }

        internal static void CrawlCompleted(string outcome, long files, long directories)
        {
            try
            {
                KeyValuePair<string, object> tag = new KeyValuePair<string, object>(GitHubCrawlerTelemetry.AttributeOutcome, outcome);
                if (_CrawlFiles.Enabled) _CrawlFiles.Record(files, tag);
                if (_CrawlDirectories.Enabled) _CrawlDirectories.Record(directories, tag);
            }
            catch
            {
            }
        }

        internal static void FileDownloaded(string outcome, long bytes)
        {
            try
            {
                if (_FileDownloadSize.Enabled)
                    _FileDownloadSize.Record(bytes, new KeyValuePair<string, object>(GitHubCrawlerTelemetry.AttributeOutcome, outcome));
            }
            catch
            {
            }
        }

        internal static Activity StartActivity(string name, ActivityKind kind, ActivityContext parent)
        {
            try
            {
                if (!Source.HasListeners()) return null;
                return Source.StartActivity(name, kind, parent);
            }
            catch
            {
                return null;
            }
        }

        internal static void MarkSuccess(Activity activity)
        {
            if (activity == null) return;

            try
            {
                activity.SetTag(GitHubCrawlerTelemetry.AttributeOutcome, GitHubCrawlerTelemetry.OutcomeSuccess);
                activity.SetStatus(ActivityStatusCode.Ok);
            }
            catch
            {
            }
        }

        internal static void MarkFailure(Activity activity, string outcome, string errorType, string description, Exception exception)
        {
            if (activity == null) return;

            try
            {
                activity.SetTag(GitHubCrawlerTelemetry.AttributeOutcome, outcome);

                if (outcome == GitHubCrawlerTelemetry.OutcomeFailure)
                {
                    if (errorType != null) activity.SetTag(GitHubCrawlerTelemetry.AttributeErrorType, errorType);
                    activity.SetStatus(ActivityStatusCode.Error, description);
                    if (exception != null) activity.AddException(exception);
                }
                else
                {
                    activity.SetStatus(ActivityStatusCode.Unset, description);
                }
            }
            catch
            {
            }
        }

        internal static string ClassifyOutcome(Exception exception, CancellationToken token)
        {
            if (exception is OperationCanceledException && token.IsCancellationRequested) return GitHubCrawlerTelemetry.OutcomeCancelled;
            return GitHubCrawlerTelemetry.OutcomeFailure;
        }

        internal static string ClassifyErrorType(Exception exception)
        {
            if (exception == null) return null;

            try
            {
                if (exception.Data != null && exception.Data.Contains(ErrorTypeDataKey))
                {
                    string fromData = exception.Data[ErrorTypeDataKey] as string;
                    if (!string.IsNullOrEmpty(fromData)) return fromData;
                }

                if (exception is TaskCanceledException && exception.InnerException is TimeoutException)
                    return typeof(TimeoutException).FullName;

                if (exception is HttpRequestException httpException && httpException.HttpRequestError != HttpRequestError.Unknown)
                    return ToSnakeCase(httpException.HttpRequestError.ToString());
            }
            catch
            {
            }

            return exception.GetType().FullName;
        }

        internal static string StatusErrorType(HttpStatusCode statusCode)
        {
            return ((int)statusCode).ToString(CultureInfo.InvariantCulture);
        }

        internal static string SanitizeUrl(Uri uri)
        {
            if (uri == null || !uri.IsAbsoluteUri) return null;

            try
            {
                return uri.GetComponents(UriComponents.Scheme | UriComponents.Host | UriComponents.Port | UriComponents.Path, UriFormat.UriEscaped);
            }
            catch
            {
                return null;
            }
        }

        internal static double ElapsedSeconds(long startTimestamp)
        {
            return Stopwatch.GetElapsedTime(startTimestamp).TotalSeconds;
        }

        private static IEnumerable<Measurement<double>> ObserveLastSuccess()
        {
            List<Measurement<double>> measurements = new List<Measurement<double>>(2);

            long crawl = Interlocked.Read(ref _LastCrawlSuccessTicks);
            if (crawl > 0)
            {
                measurements.Add(new Measurement<double>(
                    ToUnixSeconds(crawl),
                    new KeyValuePair<string, object>(GitHubCrawlerTelemetry.AttributeOperation, GitHubCrawlerTelemetry.OperationCrawlRepository)));
            }

            long file = Interlocked.Read(ref _LastFileSuccessTicks);
            if (file > 0)
            {
                measurements.Add(new Measurement<double>(
                    ToUnixSeconds(file),
                    new KeyValuePair<string, object>(GitHubCrawlerTelemetry.AttributeOperation, GitHubCrawlerTelemetry.OperationGetFileContents)));
            }

            return measurements;
        }

        private static IEnumerable<Measurement<long>> ObserveRateLimitRemaining()
        {
            List<Measurement<long>> measurements = new List<Measurement<long>>(2);

            long token = Interlocked.Read(ref _RateLimitRemainingToken);
            if (token >= 0)
            {
                measurements.Add(new Measurement<long>(
                    token, new KeyValuePair<string, object>(GitHubCrawlerTelemetry.AttributeAuth, GitHubCrawlerTelemetry.AuthToken)));
            }

            long anonymous = Interlocked.Read(ref _RateLimitRemainingAnonymous);
            if (anonymous >= 0)
            {
                measurements.Add(new Measurement<long>(
                    anonymous, new KeyValuePair<string, object>(GitHubCrawlerTelemetry.AttributeAuth, GitHubCrawlerTelemetry.AuthAnonymous)));
            }

            return measurements;
        }

        private static double ToUnixSeconds(long ticks)
        {
            return (ticks - DateTime.UnixEpoch.Ticks) / (double)TimeSpan.TicksPerSecond;
        }

        private static string NormalizeItemType(string type)
        {
            switch (type)
            {
                case "file":
                case "dir":
                case "symlink":
                case "submodule":
                    return type;
                default:
                    return "other";
            }
        }

        private static string ToSnakeCase(string value)
        {
            if (string.IsNullOrEmpty(value)) return value;

            System.Text.StringBuilder sb = new System.Text.StringBuilder(value.Length + 8);
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (char.IsUpper(c))
                {
                    if (i > 0) sb.Append('_');
                    sb.Append(char.ToLowerInvariant(c));
                }
                else
                {
                    sb.Append(c);
                }
            }

            return sb.ToString();
        }

        private static string ResolveVersion()
        {
            try
            {
                Assembly assembly = typeof(CrawlerInstrumentation).Assembly;
                AssemblyInformationalVersionAttribute info = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
                string version = info?.InformationalVersion ?? assembly.GetName().Version?.ToString() ?? "unknown";
                int plus = version.IndexOf('+');
                return plus > 0 ? version.Substring(0, plus) : version;
            }
            catch
            {
                return "unknown";
            }
        }
    }
}
