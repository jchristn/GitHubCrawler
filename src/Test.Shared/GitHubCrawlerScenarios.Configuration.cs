namespace Test.Shared
{
    using System;
    using System.Collections.Generic;
    using System.Net;
    using System.Net.Http;
    using System.Text;
    using System.Threading.Tasks;

    using GitHubCrawler;

    /// <summary>
    /// Scenarios for configurable members (ApiBaseUrl, UserAgent), the specific exception types, and HTTP response disposal.
    /// </summary>
    public static partial class GitHubCrawlerScenarios
    {
        #region Configuration

        [Scenario("configuration")]
        public static void Config_Defaults()
        {
            using (GitHubRepoCrawler crawler = CreateCrawler(_ => FakeHttpMessageHandler.Json(HttpStatusCode.OK, "[]")))
            {
                TestAssert.Equal(GitHubRepoCrawler.DefaultApiBaseUrl, crawler.ApiBaseUrl);
                TestAssert.Equal("https://api.github.com/", crawler.ApiBaseUrl);
                TestAssert.Equal(GitHubRepoCrawler.DefaultUserAgent, crawler.UserAgent);
                TestAssert.Equal("GitHubRepoCrawler/1.0", crawler.UserAgent);
            }
        }

        [Scenario("configuration")]
        public static async Task Config_ApiBaseUrl_TargetsEnterpriseHost()
        {
            FakeHttpMessageHandler handler = new FakeHttpMessageHandler(_ => FakeHttpMessageHandler.Json(HttpStatusCode.OK, "[]"));
            using (GitHubRepoCrawler crawler = new GitHubRepoCrawler(handler))
            {
                crawler.ApiBaseUrl = "https://github.example.com/api/v3";
                TestAssert.Equal("https://github.example.com/api/v3/", crawler.ApiBaseUrl, "A trailing slash should be appended.");

                await DrainAsync(crawler.GetRepositoryContentsAsync(ValidRepoUrl)).ConfigureAwait(false);
                TestAssert.Single(handler.RequestedUris);
                TestAssert.Equal("https://github.example.com/api/v3/repos/owner/repo/contents/", handler.RequestedUris[0]);
            }
        }

        [Scenario("configuration")]
        public static void Config_ApiBaseUrl_RejectsInvalidValues()
        {
            using (GitHubRepoCrawler crawler = CreateCrawler(_ => FakeHttpMessageHandler.Json(HttpStatusCode.OK, "[]")))
            {
                TestAssert.Throws<ArgumentNullException>(() => crawler.ApiBaseUrl = null!);
                TestAssert.Throws<ArgumentException>(() => crawler.ApiBaseUrl = "not a url");
                TestAssert.Throws<ArgumentException>(() => crawler.ApiBaseUrl = "/relative/path");
                TestAssert.Throws<ArgumentException>(() => crawler.ApiBaseUrl = "ftp://github.example.com/");
                TestAssert.Equal(GitHubRepoCrawler.DefaultApiBaseUrl, crawler.ApiBaseUrl, "A rejected value must not be applied.");
            }
        }

        [Scenario("configuration")]
        public static async Task Config_UserAgent_SentOnRequests()
        {
            FakeHttpMessageHandler handler = new FakeHttpMessageHandler(_ => FakeHttpMessageHandler.Bytes(HttpStatusCode.OK, new byte[] { 1 }, "text/plain"));
            using (GitHubRepoCrawler crawler = new GitHubRepoCrawler(handler))
            {
                crawler.UserAgent = "MyApp/2.0 (+https://example.com)";
                await crawler.GetFileContentsAsync("https://raw/a.txt").ConfigureAwait(false);

                TestAssert.Single(handler.Requests);
                TestAssert.Equal("MyApp/2.0 (+https://example.com)", handler.Requests[0].Header("User-Agent"));
                TestAssert.Equal("MyApp/2.0 (+https://example.com)", crawler.UserAgent);
            }
        }

        [Scenario("configuration")]
        public static void Config_UserAgent_RejectsInvalidValues()
        {
            GitHubRepoCrawler crawler = CreateCrawler(_ => FakeHttpMessageHandler.Json(HttpStatusCode.OK, "[]"));
            TestAssert.Throws<ArgumentNullException>(() => crawler.UserAgent = null!);
            TestAssert.Throws<ArgumentException>(() => crawler.UserAgent = "   ");
            TestAssert.Equal(GitHubRepoCrawler.DefaultUserAgent, crawler.UserAgent);

            crawler.Dispose();
            TestAssert.Throws<ObjectDisposedException>(() => crawler.UserAgent = "Late/1.0");
        }

        #endregion

        #region Exceptions

        [Scenario("exceptions")]
        public static async Task Exceptions_TooManyRequests_ThrowsRateLimitWithHeaders()
        {
            long resetUnix = 1893456000;

            using (GitHubRepoCrawler crawler = CreateCrawler(_ =>
            {
                HttpResponseMessage response = FakeHttpMessageHandler.Json(HttpStatusCode.TooManyRequests, "{}");
                response.Headers.TryAddWithoutValidation("X-RateLimit-Remaining", "0");
                response.Headers.TryAddWithoutValidation("X-RateLimit-Reset", resetUnix.ToString(System.Globalization.CultureInfo.InvariantCulture));
                return response;
            }))
            {
                GitHubRateLimitException ex = await TestAssert.ThrowsAsync<GitHubRateLimitException>(
                    () => DrainAsync(crawler.GetRepositoryContentsAsync(ValidRepoUrl))).ConfigureAwait(false);

                TestAssert.Equal((HttpStatusCode?)HttpStatusCode.TooManyRequests, ex.StatusCode);
                TestAssert.Equal((long?)0, ex.RateLimitRemaining);
                TestAssert.Equal((DateTimeOffset?)DateTimeOffset.FromUnixTimeSeconds(resetUnix), ex.RateLimitReset);
            }
        }

        [Scenario("exceptions")]
        public static async Task Exceptions_RateLimit_MissingHeaders_LeavesNulls()
        {
            using (GitHubRepoCrawler crawler = CreateCrawler(_ => FakeHttpMessageHandler.Json(HttpStatusCode.Forbidden, "{}")))
            {
                GitHubRateLimitException ex = await TestAssert.ThrowsAsync<GitHubRateLimitException>(
                    () => DrainAsync(crawler.GetRepositoryContentsAsync(ValidRepoUrl))).ConfigureAwait(false);

                TestAssert.Null(ex.RateLimitRemaining);
                TestAssert.Null(ex.RateLimitReset);
            }
        }

        [Scenario("exceptions")]
        public static void Exceptions_Hierarchy_RemainsCatchableAsException()
        {
            Exception notFound = new GitHubRepositoryNotFoundException("o", "r");
            Exception rateLimit = new GitHubRateLimitException(HttpStatusCode.Forbidden, null, null);

            TestAssert.True(notFound is GitHubCrawlerException);
            TestAssert.True(rateLimit is GitHubCrawlerException);
            TestAssert.Equal("Repository not found: o/r", notFound.Message);
            TestAssert.Equal("API rate limit exceeded. Consider using an authentication token.", rateLimit.Message);

            GitHubCrawlerException withInner = new GitHubCrawlerException("outer", new InvalidOperationException("inner"));
            TestAssert.Null(withInner.StatusCode);
            TestAssert.NotNull(withInner.InnerException);

            GitHubRepositoryNotFoundException nulls = new GitHubRepositoryNotFoundException(null!, null!);
            TestAssert.Equal(string.Empty, nulls.Owner);
            TestAssert.Equal(string.Empty, nulls.Repository);
        }

        [Scenario("exceptions")]
        public static async Task Exceptions_NullJsonListing_YieldsNothing()
        {
            using (GitHubRepoCrawler crawler = CreateCrawler(_ => FakeHttpMessageHandler.Json(HttpStatusCode.OK, "null")))
            {
                List<string> results = await DrainAsync(crawler.GetRepositoryContentsAsync(ValidRepoUrl)).ConfigureAwait(false);
                TestAssert.Empty(results);
            }
        }

        #endregion

        #region Response-Disposal

        [Scenario("response-disposal")]
        public static async Task Disposal_FileDownload_DisposesResponse()
        {
            List<DisposalTrackingContent> contents = new List<DisposalTrackingContent>();

            using (GitHubRepoCrawler crawler = CreateCrawler(_ =>
            {
                DisposalTrackingContent content = new DisposalTrackingContent(Encoding.UTF8.GetBytes("payload"));
                contents.Add(content);
                HttpResponseMessage response = new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
                response.Headers.TryAddWithoutValidation("ETag", "\"abc\"");
                return response;
            }))
            {
                GitHubFileResponse file = await crawler.GetFileContentsAsync("https://raw/a.txt").ConfigureAwait(false);

                TestAssert.Single(contents);
                TestAssert.True(contents[0].IsDisposed, "The HTTP response must be disposed before GetFileContentsAsync returns.");
                TestAssert.Equal("payload", Encoding.UTF8.GetString(file.Content!));
                TestAssert.NotNull(file.Headers);
                TestAssert.True(file.Headers!.ContainsKey("ETag"), "Headers must remain readable after the response is disposed.");
                TestAssert.Equal("https://raw/a.txt", file.FinalUrl?.ToString());
            }
        }

        [Scenario("response-disposal")]
        public static async Task Disposal_FileDownloadError_DisposesResponse()
        {
            List<DisposalTrackingContent> contents = new List<DisposalTrackingContent>();

            using (GitHubRepoCrawler crawler = CreateCrawler(_ =>
            {
                DisposalTrackingContent content = new DisposalTrackingContent(Encoding.UTF8.GetBytes("missing"));
                contents.Add(content);
                return new HttpResponseMessage(HttpStatusCode.NotFound) { Content = content };
            }))
            {
                GitHubFileResponse file = await crawler.GetFileContentsAsync("https://raw/missing.txt").ConfigureAwait(false);

                TestAssert.Equal(HttpStatusCode.NotFound, file.StatusCode);
                TestAssert.True(contents[0].IsDisposed, "The HTTP response must be disposed for non-success status codes too.");
            }
        }

        [Scenario("response-disposal")]
        public static async Task Disposal_DirectoryListing_DisposesResponses()
        {
            List<DisposalTrackingContent> contents = new List<DisposalTrackingContent>();

            using (GitHubRepoCrawler crawler = CreateCrawler(_ =>
            {
                DisposalTrackingContent content = new DisposalTrackingContent(Encoding.UTF8.GetBytes("[]"));
                contents.Add(content);
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
            }))
            {
                await DrainAsync(crawler.GetRepositoryContentsAsync(ValidRepoUrl)).ConfigureAwait(false);

                TestAssert.Single(contents);
                TestAssert.True(contents[0].IsDisposed, "Directory listing responses must be disposed.");
            }
        }

        #endregion
    }
}
