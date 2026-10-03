namespace Test.Shared
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;
    using System.Net;
    using System.Net.Http;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;

    using GitHubCrawler;

    /// <summary>
    /// Telemetry scenarios: prove that every public operation, outbound GitHub call, domain counter, lifecycle
    /// gauge, and failure path emits the metrics and spans documented in TELEMETRY.md.
    /// </summary>
    public static partial class GitHubCrawlerScenarios
    {
        private const string T = "telemetry";

        #region Telemetry

        [Scenario(T)]
        public static void Telemetry_Names_AreStable()
        {
            TestAssert.Equal("GitHubCrawler", GitHubCrawlerTelemetry.MeterName);
            TestAssert.Equal("GitHubCrawler", GitHubCrawlerTelemetry.ActivitySourceName);
            TestAssert.Equal("githubcrawler.operation.duration", GitHubCrawlerTelemetry.MetricOperationDuration);
            TestAssert.Equal("githubcrawler.github.request.duration", GitHubCrawlerTelemetry.MetricGitHubRequestDuration);
            TestAssert.Equal("github contents.list", GitHubCrawlerTelemetry.SpanGitHubContentsList);
        }

        [Scenario(T)]
        public static async Task Telemetry_NoListener_CrawlAndDownloadDoNotThrow()
        {
            using (GitHubRepoCrawler crawler = CreateCrawler(request =>
            {
                if (request.RequestUri.Host == "api.github.com")
                    return FakeHttpMessageHandler.Json(HttpStatusCode.OK, GitHubJson.Array(GitHubJson.File("a.txt", "a.txt", "https://raw/a.txt")));
                return FakeHttpMessageHandler.Bytes(HttpStatusCode.OK, new byte[] { 1, 2, 3 }, "application/octet-stream");
            }))
            {
                List<string> results = await DrainAsync(crawler.GetRepositoryContentsAsync(ValidRepoUrl));
                TestAssert.Single(results);

                GitHubFileResponse file = await crawler.GetFileContentsAsync("https://raw/a.txt");
                TestAssert.Equal(3, file.Content.Length);

                await TestAssert.ThrowsAsync<Exception>(async () =>
                {
                    using (GitHubRepoCrawler failing = CreateCrawler(_ => FakeHttpMessageHandler.Json(HttpStatusCode.NotFound, "{}")))
                    {
                        await DrainAsync(failing.GetRepositoryContentsAsync(ValidRepoUrl));
                    }
                });
            }
        }

        [Scenario(T)]
        public static async Task Telemetry_Crawl_Success_EmitsRootAndPerDirectorySpans()
        {
            using (TelemetryCapture capture = new TelemetryCapture())
            using (GitHubRepoCrawler crawler = CreateCrawler(NestedRepoResponder))
            {
                List<string> results = await DrainAsync(crawler.GetRepositoryContentsAsync(ValidRepoUrl));
                TestAssert.Count(2, results);

                Activity root = capture.SingleActivity(GitHubCrawlerTelemetry.SpanCrawlRepository);
                TestAssert.Equal(ActivityKind.Internal, root.Kind);
                TestAssert.Equal(ActivityStatusCode.Ok, root.Status);
                TestAssert.Equal("owner", root.GetTagItem(GitHubCrawlerTelemetry.AttributeGitHubOwner) as string);
                TestAssert.Equal("repo", root.GetTagItem(GitHubCrawlerTelemetry.AttributeGitHubRepo) as string);
                TestAssert.Equal(2L, Convert.ToInt64(root.GetTagItem(GitHubCrawlerTelemetry.AttributeFiles)));
                TestAssert.Equal(2L, Convert.ToInt64(root.GetTagItem(GitHubCrawlerTelemetry.AttributeDirectories)));

                List<Activity> lists = capture.Activities(GitHubCrawlerTelemetry.SpanGitHubContentsList);
                TestAssert.Count(2, lists);

                foreach (Activity list in lists)
                {
                    TestAssert.Equal(ActivityKind.Client, list.Kind);
                    TestAssert.Equal(ActivityStatusCode.Ok, list.Status);
                    TestAssert.Equal(root.TraceId, list.TraceId, "Directory span must share the crawl trace.");
                    TestAssert.Equal(root.SpanId, list.ParentSpanId, "Directory span must be a child of the crawl span.");
                    TestAssert.Equal(200, Convert.ToInt32(list.GetTagItem(GitHubCrawlerTelemetry.AttributeHttpResponseStatusCode)));
                    TestAssert.Equal("api.github.com", list.GetTagItem(GitHubCrawlerTelemetry.AttributeServerAddress) as string);
                }

                TestAssert.True(lists.Any(a => (a.GetTagItem(GitHubCrawlerTelemetry.AttributeGitHubPath) as string) == "sub"
                    && Convert.ToInt32(a.GetTagItem(GitHubCrawlerTelemetry.AttributeDepth)) == 1), "Expected a depth-1 span for 'sub'.");
            }
        }

        [Scenario(T)]
        public static async Task Telemetry_Crawl_Success_EmitsOperationRequestAndDomainMetrics()
        {
            using (TelemetryCapture capture = new TelemetryCapture())
            using (GitHubRepoCrawler crawler = CreateCrawler(NestedRepoResponder))
            {
                await DrainAsync(crawler.GetRepositoryContentsAsync(ValidRepoUrl));

                string[] crawlSuccess = { GitHubCrawlerTelemetry.AttributeOperation, GitHubCrawlerTelemetry.OperationCrawlRepository, GitHubCrawlerTelemetry.AttributeOutcome, GitHubCrawlerTelemetry.OutcomeSuccess };
                TestAssert.Equal(1d, capture.Sum(GitHubCrawlerTelemetry.MetricOperations, crawlSuccess));
                List<CapturedMeasurement> durations = capture.Measurements(GitHubCrawlerTelemetry.MetricOperationDuration, crawlSuccess);
                TestAssert.Single(durations);
                TestAssert.Equal("s", durations[0].Unit);
                TestAssert.Null(durations[0].Tag(GitHubCrawlerTelemetry.AttributeErrorType), "Success must not carry error.type.");

                TestAssert.Equal(0d, capture.Sum(GitHubCrawlerTelemetry.MetricOperationsActive), "Active operations must net to zero.");
                TestAssert.Count(2, capture.Measurements(GitHubCrawlerTelemetry.MetricOperationsActive));

                string[] listOk = { GitHubCrawlerTelemetry.AttributeGitHubOperation, GitHubCrawlerTelemetry.GitHubOperationContentsList, GitHubCrawlerTelemetry.AttributeOutcome, GitHubCrawlerTelemetry.OutcomeSuccess, GitHubCrawlerTelemetry.AttributeHttpResponseStatusCode, "200", GitHubCrawlerTelemetry.AttributeAuth, GitHubCrawlerTelemetry.AuthAnonymous };
                TestAssert.Equal(2d, capture.Sum(GitHubCrawlerTelemetry.MetricGitHubRequests, listOk));
                TestAssert.Count(2, capture.Measurements(GitHubCrawlerTelemetry.MetricGitHubRequestDuration, listOk));

                TestAssert.Equal(2d, capture.Sum(GitHubCrawlerTelemetry.MetricCrawlItems, GitHubCrawlerTelemetry.AttributeItemType, "file"));
                TestAssert.Equal(1d, capture.Sum(GitHubCrawlerTelemetry.MetricCrawlItems, GitHubCrawlerTelemetry.AttributeItemType, "dir"));

                TestAssert.Equal(2d, capture.Sum(GitHubCrawlerTelemetry.MetricCrawlFiles, GitHubCrawlerTelemetry.AttributeOutcome, GitHubCrawlerTelemetry.OutcomeSuccess));
                TestAssert.Equal(2d, capture.Sum(GitHubCrawlerTelemetry.MetricCrawlDirectories, GitHubCrawlerTelemetry.AttributeOutcome, GitHubCrawlerTelemetry.OutcomeSuccess));

                capture.CollectObservables();
                List<CapturedMeasurement> lastSuccess = capture.Measurements(GitHubCrawlerTelemetry.MetricOperationLastSuccess, GitHubCrawlerTelemetry.AttributeOperation, GitHubCrawlerTelemetry.OperationCrawlRepository);
                TestAssert.True(lastSuccess.Any(), "Expected a last-success timestamp for crawl_repository.");
                double nowUnix = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000d;
                TestAssert.True(Math.Abs(nowUnix - lastSuccess.Last().Value) < 60, "Last-success timestamp should be recent Unix seconds.");
            }
        }

        [Scenario(T)]
        public static async Task Telemetry_Crawl_Propagates_ParentContext()
        {
            using (TelemetryCapture capture = new TelemetryCapture())
            using (GitHubRepoCrawler crawler = CreateCrawler(NestedRepoResponder))
            {
                ActivityTraceId traceId;
                ActivitySpanId parentSpanId;

                using (Activity parent = TelemetryCapture.TestSource.StartActivity("test parent"))
                {
                    TestAssert.NotNull(parent);
                    traceId = parent.TraceId;
                    parentSpanId = parent.SpanId;
                    await DrainAsync(crawler.GetRepositoryContentsAsync(ValidRepoUrl));
                }

                Activity root = capture.SingleActivity(GitHubCrawlerTelemetry.SpanCrawlRepository);
                TestAssert.Equal(traceId, root.TraceId, "Crawl span must join the caller's trace.");
                TestAssert.Equal(parentSpanId, root.ParentSpanId, "Crawl span must be a child of the caller's span.");
                TestAssert.True(capture.Activities(GitHubCrawlerTelemetry.SpanGitHubContentsList).All(a => a.TraceId == traceId));
            }
        }

        [Scenario(T)]
        public static async Task Telemetry_Crawl_NotFound_RecordsFailureWithStatusErrorType()
        {
            using (TelemetryCapture capture = new TelemetryCapture())
            using (GitHubRepoCrawler crawler = CreateCrawler(_ => FakeHttpMessageHandler.Json(HttpStatusCode.NotFound, "{}")))
            {
                await TestAssert.ThrowsAsync<Exception>(() => DrainAsync(crawler.GetRepositoryContentsAsync(ValidRepoUrl)));

                AssertCrawlFailure(capture, "404");

                Activity list = capture.SingleActivity(GitHubCrawlerTelemetry.SpanGitHubContentsList);
                TestAssert.Equal(ActivityStatusCode.Error, list.Status);
                TestAssert.Equal("404", list.GetTagItem(GitHubCrawlerTelemetry.AttributeErrorType) as string);
                TestAssert.True(list.Events.Any(e => e.Name == "exception"), "Expected an exception event on the client span.");

                TestAssert.Equal(1d, capture.Sum(
                    GitHubCrawlerTelemetry.MetricGitHubRequests,
                    GitHubCrawlerTelemetry.AttributeOutcome, GitHubCrawlerTelemetry.OutcomeFailure,
                    GitHubCrawlerTelemetry.AttributeErrorType, "404",
                    GitHubCrawlerTelemetry.AttributeHttpResponseStatusCode, "404"));
                TestAssert.Equal(0d, capture.Sum(GitHubCrawlerTelemetry.MetricGitHubRateLimitExceeded));
                TestAssert.Equal(0d, capture.Sum(GitHubCrawlerTelemetry.MetricOperationsActive));
            }
        }

        [Scenario(T)]
        public static async Task Telemetry_Crawl_RateLimited_CountsRateLimitAndGaugesRemaining()
        {
            using (TelemetryCapture capture = new TelemetryCapture())
            using (GitHubRepoCrawler crawler = new GitHubRepoCrawler(new FakeHttpMessageHandler(_ =>
            {
                HttpResponseMessage response = FakeHttpMessageHandler.Json(HttpStatusCode.Forbidden, "{}");
                response.Headers.TryAddWithoutValidation("X-RateLimit-Remaining", "0");
                return response;
            }), "secret-token"))
            {
                await TestAssert.ThrowsAsync<Exception>(() => DrainAsync(crawler.GetRepositoryContentsAsync(ValidRepoUrl)));

                AssertCrawlFailure(capture, "403");
                TestAssert.Equal(1d, capture.Sum(
                    GitHubCrawlerTelemetry.MetricGitHubRateLimitExceeded,
                    GitHubCrawlerTelemetry.AttributeGitHubOperation, GitHubCrawlerTelemetry.GitHubOperationContentsList,
                    GitHubCrawlerTelemetry.AttributeAuth, GitHubCrawlerTelemetry.AuthToken));

                capture.CollectObservables();
                List<CapturedMeasurement> remaining = capture.Measurements(
                    GitHubCrawlerTelemetry.MetricGitHubRateLimitRemaining, GitHubCrawlerTelemetry.AttributeAuth, GitHubCrawlerTelemetry.AuthToken);
                TestAssert.True(remaining.Any(), "Expected the rate-limit remaining gauge to report.");
                TestAssert.Equal(0d, remaining.Last().Value);

                Activity list = capture.SingleActivity(GitHubCrawlerTelemetry.SpanGitHubContentsList);
                TestAssert.Equal(0L, Convert.ToInt64(list.GetTagItem(GitHubCrawlerTelemetry.AttributeRateLimitRemaining)));
                AssertNoSecretInTelemetry(capture, "secret-token");
            }
        }

        [Scenario(T)]
        public static async Task Telemetry_Crawl_InvalidJson_RecordsJsonExceptionErrorType()
        {
            using (TelemetryCapture capture = new TelemetryCapture())
            using (GitHubRepoCrawler crawler = CreateCrawler(_ => FakeHttpMessageHandler.Json(HttpStatusCode.OK, "{ not json")))
            {
                await TestAssert.ThrowsAsync<System.Text.Json.JsonException>(() => DrainAsync(crawler.GetRepositoryContentsAsync(ValidRepoUrl)));

                AssertCrawlFailure(capture, "System.Text.Json.JsonException");
                TestAssert.Equal(1d, capture.Sum(
                    GitHubCrawlerTelemetry.MetricGitHubRequests,
                    GitHubCrawlerTelemetry.AttributeOutcome, GitHubCrawlerTelemetry.OutcomeFailure,
                    GitHubCrawlerTelemetry.AttributeHttpResponseStatusCode, "200",
                    GitHubCrawlerTelemetry.AttributeErrorType, "System.Text.Json.JsonException"));
            }
        }

        [Scenario(T)]
        public static async Task Telemetry_Crawl_InvalidUrl_RecordsArgumentFailureWithoutRequests()
        {
            using (TelemetryCapture capture = new TelemetryCapture())
            using (GitHubRepoCrawler crawler = CreateCrawler(_ => FakeHttpMessageHandler.Json(HttpStatusCode.OK, "[]")))
            {
                await TestAssert.ThrowsAsync<ArgumentException>(() => DrainAsync(crawler.GetRepositoryContentsAsync("https://gitlab.com/owner/repo")));

                AssertCrawlFailure(capture, "System.ArgumentException");
                TestAssert.Empty(capture.Measurements(GitHubCrawlerTelemetry.MetricGitHubRequests));
                TestAssert.Empty(capture.Activities(GitHubCrawlerTelemetry.SpanGitHubContentsList));
            }
        }

        [Scenario(T)]
        public static async Task Telemetry_Crawl_Cancelled_RecordsCancelledNotError()
        {
            using (TelemetryCapture capture = new TelemetryCapture())
            using (CancellationTokenSource cts = new CancellationTokenSource())
            using (GitHubRepoCrawler crawler = CreateCrawler(NestedRepoResponder))
            {
                await TestAssert.ThrowsAsync<OperationCanceledException>(async () =>
                {
                    await foreach (string url in crawler.GetRepositoryContentsAsync(ValidRepoUrl, cts.Token).ConfigureAwait(false))
                    {
                        cts.Cancel();
                    }
                });

                List<CapturedMeasurement> ops = capture.Measurements(
                    GitHubCrawlerTelemetry.MetricOperations,
                    GitHubCrawlerTelemetry.AttributeOutcome, GitHubCrawlerTelemetry.OutcomeCancelled);
                TestAssert.Single(ops);
                TestAssert.Null(ops[0].Tag(GitHubCrawlerTelemetry.AttributeErrorType), "Cancellation must not carry error.type.");

                Activity root = capture.SingleActivity(GitHubCrawlerTelemetry.SpanCrawlRepository);
                TestAssert.Equal(ActivityStatusCode.Unset, root.Status);
                TestAssert.Equal(GitHubCrawlerTelemetry.OutcomeCancelled, root.GetTagItem(GitHubCrawlerTelemetry.AttributeOutcome) as string);
                TestAssert.Equal(0d, capture.Sum(GitHubCrawlerTelemetry.MetricOperationsActive));
            }
        }

        [Scenario(T)]
        public static async Task Telemetry_Crawl_EarlyExit_RecordsAbandoned()
        {
            using (TelemetryCapture capture = new TelemetryCapture())
            using (GitHubRepoCrawler crawler = CreateCrawler(NestedRepoResponder))
            {
                await foreach (string url in crawler.GetRepositoryContentsAsync(ValidRepoUrl).ConfigureAwait(false))
                {
                    break;
                }

                TestAssert.Equal(1d, capture.Sum(
                    GitHubCrawlerTelemetry.MetricOperations,
                    GitHubCrawlerTelemetry.AttributeOutcome, GitHubCrawlerTelemetry.OutcomeAbandoned));
                Activity root = capture.SingleActivity(GitHubCrawlerTelemetry.SpanCrawlRepository);
                TestAssert.Equal(1L, Convert.ToInt64(root.GetTagItem(GitHubCrawlerTelemetry.AttributeFiles)));
                TestAssert.Equal(1d, capture.Sum(GitHubCrawlerTelemetry.MetricCrawlFiles, GitHubCrawlerTelemetry.AttributeOutcome, GitHubCrawlerTelemetry.OutcomeAbandoned));
                TestAssert.Equal(0d, capture.Sum(GitHubCrawlerTelemetry.MetricOperationsActive));
            }
        }

        [Scenario(T)]
        public static async Task Telemetry_GetFile_Success_EmitsSpansAndSizeMetric()
        {
            byte[] payload = Encoding.UTF8.GetBytes("hello world");

            using (TelemetryCapture capture = new TelemetryCapture())
            using (GitHubRepoCrawler crawler = CreateCrawler(_ => FakeHttpMessageHandler.Bytes(HttpStatusCode.OK, payload, "text/plain")))
            {
                await crawler.GetFileContentsAsync("https://raw.githubusercontent.com/owner/repo/main/a.txt?token=SECRETQUERY");

                Activity root = capture.SingleActivity(GitHubCrawlerTelemetry.SpanGetFileContents);
                Activity download = capture.SingleActivity(GitHubCrawlerTelemetry.SpanGitHubFileDownload);
                TestAssert.Equal(ActivityStatusCode.Ok, root.Status);
                TestAssert.Equal(ActivityStatusCode.Ok, download.Status);
                TestAssert.Equal(ActivityKind.Client, download.Kind);
                TestAssert.Equal(root.SpanId, download.ParentSpanId, "Download span must be a child of the operation span.");
                TestAssert.Equal("raw.githubusercontent.com", download.GetTagItem(GitHubCrawlerTelemetry.AttributeServerAddress) as string);
                TestAssert.Equal("https://raw.githubusercontent.com/owner/repo/main/a.txt", download.GetTagItem(GitHubCrawlerTelemetry.AttributeUrlFull) as string);
                TestAssert.Equal((long)payload.Length, Convert.ToInt64(download.GetTagItem(GitHubCrawlerTelemetry.AttributeHttpResponseBodySize)));

                TestAssert.Equal(1d, capture.Sum(
                    GitHubCrawlerTelemetry.MetricOperations,
                    GitHubCrawlerTelemetry.AttributeOperation, GitHubCrawlerTelemetry.OperationGetFileContents,
                    GitHubCrawlerTelemetry.AttributeOutcome, GitHubCrawlerTelemetry.OutcomeSuccess));
                TestAssert.Equal(1d, capture.Sum(
                    GitHubCrawlerTelemetry.MetricGitHubRequests,
                    GitHubCrawlerTelemetry.AttributeGitHubOperation, GitHubCrawlerTelemetry.GitHubOperationFileDownload,
                    GitHubCrawlerTelemetry.AttributeOutcome, GitHubCrawlerTelemetry.OutcomeSuccess));

                List<CapturedMeasurement> sizes = capture.Measurements(GitHubCrawlerTelemetry.MetricFileDownloadSize);
                TestAssert.Single(sizes);
                TestAssert.Equal("By", sizes[0].Unit);
                TestAssert.Equal((double)payload.Length, sizes[0].Value);

                AssertNoSecretInTelemetry(capture, "SECRETQUERY");
            }
        }

        [Scenario(T)]
        public static async Task Telemetry_GetFile_NotFound_ReturnsResponseAndRecordsFailure()
        {
            using (TelemetryCapture capture = new TelemetryCapture())
            using (GitHubRepoCrawler crawler = CreateCrawler(_ => FakeHttpMessageHandler.Bytes(HttpStatusCode.NotFound, Encoding.UTF8.GetBytes("404"), "text/plain")))
            {
                GitHubFileResponse response = await crawler.GetFileContentsAsync("https://raw/missing.txt");
                TestAssert.Equal(HttpStatusCode.NotFound, response.StatusCode);

                TestAssert.Equal(1d, capture.Sum(
                    GitHubCrawlerTelemetry.MetricOperations,
                    GitHubCrawlerTelemetry.AttributeOperation, GitHubCrawlerTelemetry.OperationGetFileContents,
                    GitHubCrawlerTelemetry.AttributeOutcome, GitHubCrawlerTelemetry.OutcomeFailure,
                    GitHubCrawlerTelemetry.AttributeErrorType, "404"));
                TestAssert.Equal(1d, capture.Sum(
                    GitHubCrawlerTelemetry.MetricGitHubRequests,
                    GitHubCrawlerTelemetry.AttributeGitHubOperation, GitHubCrawlerTelemetry.GitHubOperationFileDownload,
                    GitHubCrawlerTelemetry.AttributeErrorType, "404"));
                TestAssert.Equal(ActivityStatusCode.Error, capture.SingleActivity(GitHubCrawlerTelemetry.SpanGetFileContents).Status);
                TestAssert.Equal(ActivityStatusCode.Error, capture.SingleActivity(GitHubCrawlerTelemetry.SpanGitHubFileDownload).Status);
            }
        }

        [Scenario(T)]
        public static async Task Telemetry_GetFile_NetworkError_RecordsExceptionType()
        {
            using (TelemetryCapture capture = new TelemetryCapture())
            using (GitHubRepoCrawler crawler = CreateCrawler(_ => throw new HttpRequestException("boom")))
            {
                await TestAssert.ThrowsAsync<HttpRequestException>(() => crawler.GetFileContentsAsync("https://raw/a.txt"));

                TestAssert.Equal(1d, capture.Sum(
                    GitHubCrawlerTelemetry.MetricOperations,
                    GitHubCrawlerTelemetry.AttributeOperation, GitHubCrawlerTelemetry.OperationGetFileContents,
                    GitHubCrawlerTelemetry.AttributeErrorType, "System.Net.Http.HttpRequestException"));

                List<CapturedMeasurement> requests = capture.Measurements(GitHubCrawlerTelemetry.MetricGitHubRequests);
                TestAssert.Single(requests);
                TestAssert.Null(requests[0].Tag(GitHubCrawlerTelemetry.AttributeHttpResponseStatusCode), "No status code when no response was received.");
                TestAssert.Equal(0d, capture.Sum(GitHubCrawlerTelemetry.MetricFileDownloadSize));
            }
        }

        [Scenario(T)]
        public static async Task Telemetry_GetFile_Timeout_RecordsTimeoutErrorType()
        {
            using (TelemetryCapture capture = new TelemetryCapture())
            using (GitHubRepoCrawler crawler = CreateCrawler(_ => throw new TaskCanceledException("timed out", new TimeoutException())))
            {
                await TestAssert.ThrowsAsync<TaskCanceledException>(() => crawler.GetFileContentsAsync("https://raw/a.txt"));

                TestAssert.Equal(1d, capture.Sum(
                    GitHubCrawlerTelemetry.MetricOperations,
                    GitHubCrawlerTelemetry.AttributeOutcome, GitHubCrawlerTelemetry.OutcomeFailure,
                    GitHubCrawlerTelemetry.AttributeErrorType, "System.TimeoutException"));
            }
        }

        [Scenario(T)]
        public static async Task Telemetry_GetFile_AfterDispose_RecordsObjectDisposed()
        {
            using (TelemetryCapture capture = new TelemetryCapture())
            {
                GitHubRepoCrawler crawler = CreateCrawler(_ => FakeHttpMessageHandler.Json(HttpStatusCode.OK, "[]"));
                crawler.Dispose();

                await TestAssert.ThrowsAsync<ObjectDisposedException>(() => crawler.GetFileContentsAsync("https://raw/a.txt"));

                TestAssert.Equal(1d, capture.Sum(
                    GitHubCrawlerTelemetry.MetricOperations,
                    GitHubCrawlerTelemetry.AttributeErrorType, "System.ObjectDisposedException"));
            }
        }

        [Scenario(T)]
        public static void Telemetry_CrawlerLifecycle_TracksActiveInstancesByAuth()
        {
            using (TelemetryCapture capture = new TelemetryCapture())
            {
                GitHubRepoCrawler anonymous = CreateCrawler(_ => FakeHttpMessageHandler.Json(HttpStatusCode.OK, "[]"));
                GitHubRepoCrawler authed = new GitHubRepoCrawler(new FakeHttpMessageHandler(_ => FakeHttpMessageHandler.Json(HttpStatusCode.OK, "[]")), "tok");

                TestAssert.Equal(1d, capture.Sum(GitHubCrawlerTelemetry.MetricCrawlersActive, GitHubCrawlerTelemetry.AttributeAuth, GitHubCrawlerTelemetry.AuthAnonymous));
                TestAssert.Equal(1d, capture.Sum(GitHubCrawlerTelemetry.MetricCrawlersActive, GitHubCrawlerTelemetry.AttributeAuth, GitHubCrawlerTelemetry.AuthToken));

                anonymous.Dispose();
                authed.Dispose();
                authed.Dispose();

                TestAssert.Equal(0d, capture.Sum(GitHubCrawlerTelemetry.MetricCrawlersActive), "Dispose (even twice) must net active crawlers to zero.");
            }
        }

        [Scenario(T)]
        public static void Telemetry_BuildInfo_ReportsVersion()
        {
            using (TelemetryCapture capture = new TelemetryCapture())
            {
                capture.CollectObservables();

                List<CapturedMeasurement> info = capture.Measurements(GitHubCrawlerTelemetry.MetricBuildInfo);
                TestAssert.True(info.Any(), "Expected the build info gauge to report.");
                TestAssert.Equal(1d, info.Last().Value);
                string version = info.Last().Tag(GitHubCrawlerTelemetry.AttributeVersion);
                TestAssert.False(string.IsNullOrEmpty(version) || version == "unknown", "Expected a real version label.");
                TestAssert.False(version.Contains("+"), "Version label must not carry the commit hash suffix.");
            }
        }

        #endregion

        #region Telemetry-Helpers

        private static HttpResponseMessage NestedRepoResponder(HttpRequestMessage request)
        {
            string url = request.RequestUri.AbsoluteUri;

            if (url == RootContentsApiUrl)
            {
                return FakeHttpMessageHandler.Json(HttpStatusCode.OK, GitHubJson.Array(
                    GitHubJson.File("a.txt", "a.txt", "https://raw/a.txt"),
                    GitHubJson.Directory("sub", "sub")));
            }

            if (url == RootContentsApiUrl + "sub")
            {
                return FakeHttpMessageHandler.Json(HttpStatusCode.OK, GitHubJson.Array(
                    GitHubJson.File("b.txt", "sub/b.txt", "https://raw/sub/b.txt")));
            }

            return FakeHttpMessageHandler.Json(HttpStatusCode.NotFound, "{}");
        }

        private static void AssertCrawlFailure(TelemetryCapture capture, string errorType)
        {
            List<CapturedMeasurement> ops = capture.Measurements(
                GitHubCrawlerTelemetry.MetricOperations,
                GitHubCrawlerTelemetry.AttributeOperation, GitHubCrawlerTelemetry.OperationCrawlRepository);
            TestAssert.Single(ops);
            TestAssert.Equal(GitHubCrawlerTelemetry.OutcomeFailure, ops[0].Tag(GitHubCrawlerTelemetry.AttributeOutcome));
            TestAssert.Equal(errorType, ops[0].Tag(GitHubCrawlerTelemetry.AttributeErrorType));
            TestAssert.Single(capture.Measurements(GitHubCrawlerTelemetry.MetricOperationDuration, GitHubCrawlerTelemetry.AttributeErrorType, errorType));

            Activity root = capture.SingleActivity(GitHubCrawlerTelemetry.SpanCrawlRepository);
            TestAssert.Equal(ActivityStatusCode.Error, root.Status);
            TestAssert.Equal(errorType, root.GetTagItem(GitHubCrawlerTelemetry.AttributeErrorType) as string);
            TestAssert.True(root.Events.Any(e => e.Name == "exception"), "Expected an exception event on the crawl span.");
        }

        private static void AssertNoSecretInTelemetry(TelemetryCapture capture, string secret)
        {
            foreach (string name in new[] { GitHubCrawlerTelemetry.SpanCrawlRepository, GitHubCrawlerTelemetry.SpanGitHubContentsList, GitHubCrawlerTelemetry.SpanGetFileContents, GitHubCrawlerTelemetry.SpanGitHubFileDownload })
            {
                foreach (Activity activity in capture.Activities(name))
                {
                    foreach (KeyValuePair<string, object> tag in activity.TagObjects)
                    {
                        string value = Convert.ToString(tag.Value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
                        TestAssert.False(value.Contains(secret), "Span " + name + " tag " + tag.Key + " leaked a secret.");
                    }
                }
            }
        }

        #endregion
    }
}
