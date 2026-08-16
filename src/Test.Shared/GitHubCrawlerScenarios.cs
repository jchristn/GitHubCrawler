namespace Test.Shared
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net;
    using System.Net.Http;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;

    using GitHubCrawler;

    /// <summary>
    /// The single source of truth for all automated GitHubCrawler test scenarios.
    /// Each public, static, parameterless method annotated with <see cref="ScenarioAttribute"/> is a test case.
    /// Scenarios are exposed to the Touchstone CLI runner (Test.Automated), xUnit (Test.XUnit), and NUnit (Test.NUnit).
    ///
    /// All HTTP behavior is exercised through an injected <see cref="FakeHttpMessageHandler"/> so the suite is
    /// deterministic and requires no network access or GitHub rate-limit budget.
    /// </summary>
    public static class GitHubCrawlerScenarios
    {
        private const string ValidRepoUrl = "https://github.com/owner/repo";
        private const string RootContentsApiUrl = "https://api.github.com/repos/owner/repo/contents/";

        #region GitHubContent-Model

        [Scenario("content-model")]
        public static void Content_DefaultConstructor_PropertiesAreNull()
        {
            GitHubContent content = new GitHubContent();
            TestAssert.Null(content.Name);
            TestAssert.Null(content.Path);
            TestAssert.Null(content.Type);
            TestAssert.Null(content.HtmlUrl);
            TestAssert.Null(content.DownloadUrl);
        }

        [Scenario("content-model")]
        public static void Content_Properties_RoundTrip()
        {
            GitHubContent content = new GitHubContent
            {
                Name = "readme.md",
                Path = "docs/readme.md",
                Type = "file",
                HtmlUrl = "https://github.com/owner/repo/blob/main/docs/readme.md",
                DownloadUrl = "https://raw.githubusercontent.com/owner/repo/main/docs/readme.md"
            };

            TestAssert.Equal("readme.md", content.Name);
            TestAssert.Equal("docs/readme.md", content.Path);
            TestAssert.Equal("file", content.Type);
            TestAssert.Equal("https://github.com/owner/repo/blob/main/docs/readme.md", content.HtmlUrl);
            TestAssert.Equal("https://raw.githubusercontent.com/owner/repo/main/docs/readme.md", content.DownloadUrl);
        }

        [Scenario("content-model")]
        public static void Content_Deserialize_MapsAllSnakeCaseFields()
        {
            string json = GitHubJson.File("a.txt", "a.txt", "https://raw.githubusercontent.com/owner/repo/main/a.txt");

            GitHubContent content = JsonSerializer.Deserialize<GitHubContent>(json);

            TestAssert.NotNull(content);
            TestAssert.Equal("a.txt", content.Name);
            TestAssert.Equal("a.txt", content.Path);
            TestAssert.Equal("file", content.Type);
            TestAssert.Equal("https://github.com/owner/repo/blob/main/a.txt", content.HtmlUrl);
            TestAssert.Equal("https://raw.githubusercontent.com/owner/repo/main/a.txt", content.DownloadUrl);
        }

        [Scenario("content-model")]
        public static void Content_Deserialize_DirectoryHasNullDownloadUrl()
        {
            string json = GitHubJson.Directory("src", "src");

            GitHubContent content = JsonSerializer.Deserialize<GitHubContent>(json);

            TestAssert.NotNull(content);
            TestAssert.Equal("dir", content.Type);
            TestAssert.Null(content.DownloadUrl);
        }

        [Scenario("content-model")]
        public static void Content_Deserialize_List()
        {
            string json = GitHubJson.Array(
                GitHubJson.File("a.txt", "a.txt", "https://raw/a.txt"),
                GitHubJson.Directory("sub", "sub"));

            List<GitHubContent> list = JsonSerializer.Deserialize<List<GitHubContent>>(json);

            TestAssert.NotNull(list);
            TestAssert.Count(2, list);
            TestAssert.Equal("a.txt", list[0].Name);
            TestAssert.Equal("sub", list[1].Name);
        }

        [Scenario("content-model")]
        public static void Content_Deserialize_UnknownFieldsIgnored()
        {
            string json = "{\"name\":\"a.txt\",\"path\":\"a.txt\",\"type\":\"file\",\"sha\":\"abc123\",\"size\":42,\"download_url\":\"https://raw/a.txt\"}";

            GitHubContent content = JsonSerializer.Deserialize<GitHubContent>(json);

            TestAssert.NotNull(content);
            TestAssert.Equal("a.txt", content.Name);
            TestAssert.Equal("https://raw/a.txt", content.DownloadUrl);
        }

        [Scenario("content-model")]
        public static void Content_Deserialize_MissingFields_LeaveNull()
        {
            GitHubContent content = JsonSerializer.Deserialize<GitHubContent>("{}");

            TestAssert.NotNull(content);
            TestAssert.Null(content.Name);
            TestAssert.Null(content.Path);
            TestAssert.Null(content.Type);
            TestAssert.Null(content.HtmlUrl);
            TestAssert.Null(content.DownloadUrl);
        }

        [Scenario("content-model")]
        public static void Content_Deserialize_EmptyArray_ProducesEmptyList()
        {
            List<GitHubContent> list = JsonSerializer.Deserialize<List<GitHubContent>>("[]");

            TestAssert.NotNull(list);
            TestAssert.Empty(list);
        }

        [Scenario("content-model")]
        public static void Content_Deserialize_InvalidJson_Throws()
        {
            TestAssert.Throws<JsonException>(() => JsonSerializer.Deserialize<GitHubContent>("{ not valid json"));
        }

        #endregion

        #region GitHubFileResponse-Model

        [Scenario("file-response")]
        public static void FileResponse_DefaultConstructor_Defaults()
        {
            GitHubFileResponse response = new GitHubFileResponse();

            TestAssert.Null(response.Content);
            TestAssert.Null(response.ContentType);
            TestAssert.Null(response.Headers);
            TestAssert.Null(response.FinalUrl);
            TestAssert.Equal(0, (int)response.StatusCode);
        }

        [Scenario("file-response")]
        public static void FileResponse_Properties_RoundTrip()
        {
            byte[] content = Encoding.UTF8.GetBytes("hello world");
            Dictionary<string, IEnumerable<string>> headers = new Dictionary<string, IEnumerable<string>>
            {
                { "X-Test", new[] { "value" } }
            };
            Uri finalUrl = new Uri("https://raw.githubusercontent.com/owner/repo/main/a.txt");

            GitHubFileResponse response = new GitHubFileResponse
            {
                Content = content,
                ContentType = "text/plain",
                StatusCode = HttpStatusCode.OK,
                Headers = headers,
                FinalUrl = finalUrl
            };

            TestAssert.Equal(content, response.Content);
            TestAssert.Equal("text/plain", response.ContentType);
            TestAssert.Equal(HttpStatusCode.OK, response.StatusCode);
            TestAssert.NotNull(response.Headers);
            TestAssert.True(response.Headers.ContainsKey("X-Test"));
            TestAssert.Equal(finalUrl, response.FinalUrl);
        }

        #endregion

        #region Constructor-and-Lifecycle

        [Scenario("lifecycle")]
        public static void Ctor_NullToken_Succeeds()
        {
            using (GitHubRepoCrawler crawler = new GitHubRepoCrawler((string)null))
            {
                TestAssert.NotNull(crawler);
            }
        }

        [Scenario("lifecycle")]
        public static void Ctor_EmptyToken_Succeeds()
        {
            using (GitHubRepoCrawler crawler = new GitHubRepoCrawler(string.Empty))
            {
                TestAssert.NotNull(crawler);
            }
        }

        [Scenario("lifecycle")]
        public static void Ctor_WithToken_Succeeds()
        {
            using (GitHubRepoCrawler crawler = new GitHubRepoCrawler("ghp_exampletoken"))
            {
                TestAssert.NotNull(crawler);
            }
        }

        [Scenario("lifecycle")]
        public static void Ctor_NullHandler_Throws()
        {
            TestAssert.Throws<ArgumentNullException>(() => new GitHubRepoCrawler((HttpMessageHandler)null));
        }

        [Scenario("lifecycle")]
        public static void Ctor_WithHandler_Succeeds()
        {
            FakeHttpMessageHandler handler = new FakeHttpMessageHandler(_ => FakeHttpMessageHandler.Json(HttpStatusCode.OK, "[]"));
            using (GitHubRepoCrawler crawler = new GitHubRepoCrawler(handler))
            {
                TestAssert.NotNull(crawler);
            }
        }

        [Scenario("lifecycle")]
        public static void Ctor_WithHandlerAndToken_Succeeds()
        {
            FakeHttpMessageHandler handler = new FakeHttpMessageHandler(_ => FakeHttpMessageHandler.Json(HttpStatusCode.OK, "[]"));
            using (GitHubRepoCrawler crawler = new GitHubRepoCrawler(handler, "ghp_exampletoken"))
            {
                TestAssert.NotNull(crawler);
            }
        }

        [Scenario("lifecycle")]
        public static void Dispose_IsIdempotent()
        {
            GitHubRepoCrawler crawler = new GitHubRepoCrawler();
            crawler.Dispose();
            crawler.Dispose();
        }

        [Scenario("lifecycle")]
        public static async Task Dispose_ThenGetContents_ThrowsObjectDisposed()
        {
            GitHubRepoCrawler crawler = new GitHubRepoCrawler();
            crawler.Dispose();

            await TestAssert.ThrowsAsync<ObjectDisposedException>(
                () => DrainAsync(crawler.GetRepositoryContentsAsync(ValidRepoUrl)));
        }

        [Scenario("lifecycle")]
        public static async Task Dispose_ThenGetFile_ThrowsObjectDisposed()
        {
            GitHubRepoCrawler crawler = new GitHubRepoCrawler();
            crawler.Dispose();

            await TestAssert.ThrowsAsync<ObjectDisposedException>(
                () => crawler.GetFileContentsAsync("https://raw/a.txt"));
        }

        #endregion

        #region URL-Parsing

        [Scenario("url-parsing")]
        public static async Task UrlParse_HttpsUrl_ParsesOwnerRepo()
        {
            await AssertFirstApiUrl("https://github.com/owner/repo", RootContentsApiUrl);
        }

        [Scenario("url-parsing")]
        public static async Task UrlParse_HttpUrl_ParsesOwnerRepo()
        {
            await AssertFirstApiUrl("http://github.com/owner/repo", RootContentsApiUrl);
        }

        [Scenario("url-parsing")]
        public static async Task UrlParse_SshUrl_ParsesOwnerRepo()
        {
            await AssertFirstApiUrl("git@github.com:owner/repo", RootContentsApiUrl);
        }

        [Scenario("url-parsing")]
        public static async Task UrlParse_HttpsGitSuffix_Trimmed()
        {
            await AssertFirstApiUrl("https://github.com/owner/repo.git", RootContentsApiUrl);
        }

        [Scenario("url-parsing")]
        public static async Task UrlParse_SshGitSuffix_Trimmed()
        {
            await AssertFirstApiUrl("git@github.com:owner/repo.git", RootContentsApiUrl);
        }

        [Scenario("url-parsing")]
        public static async Task UrlParse_ExtraPathSegments_UsesOwnerAndRepo()
        {
            await AssertFirstApiUrl("https://github.com/owner/repo/tree/main/src", RootContentsApiUrl);
        }

        [Scenario("url-parsing")]
        public static async Task UrlParse_TrailingSlash_ParsesOwnerRepo()
        {
            await AssertFirstApiUrl("https://github.com/owner/repo/", RootContentsApiUrl);
        }

        [Scenario("url-parsing")]
        public static async Task UrlParse_NullUrl_Throws()
        {
            using (GitHubRepoCrawler crawler = CreateCrawler(_ => FakeHttpMessageHandler.Json(HttpStatusCode.OK, "[]")))
            {
                await TestAssert.ThrowsAsync<ArgumentException>(() => DrainAsync(crawler.GetRepositoryContentsAsync(null)));
            }
        }

        [Scenario("url-parsing")]
        public static async Task UrlParse_EmptyUrl_Throws()
        {
            using (GitHubRepoCrawler crawler = CreateCrawler(_ => FakeHttpMessageHandler.Json(HttpStatusCode.OK, "[]")))
            {
                await TestAssert.ThrowsAsync<ArgumentException>(() => DrainAsync(crawler.GetRepositoryContentsAsync(string.Empty)));
            }
        }

        [Scenario("url-parsing")]
        public static async Task UrlParse_WhitespaceUrl_Throws()
        {
            using (GitHubRepoCrawler crawler = CreateCrawler(_ => FakeHttpMessageHandler.Json(HttpStatusCode.OK, "[]")))
            {
                await TestAssert.ThrowsAsync<ArgumentException>(() => DrainAsync(crawler.GetRepositoryContentsAsync("   ")));
            }
        }

        [Scenario("url-parsing")]
        public static async Task UrlParse_NonGithubHost_Throws()
        {
            using (GitHubRepoCrawler crawler = CreateCrawler(_ => FakeHttpMessageHandler.Json(HttpStatusCode.OK, "[]")))
            {
                await TestAssert.ThrowsAsync<ArgumentException>(() => DrainAsync(crawler.GetRepositoryContentsAsync("https://gitlab.com/owner/repo")));
            }
        }

        [Scenario("url-parsing")]
        public static async Task UrlParse_OwnerWithoutRepo_Throws()
        {
            using (GitHubRepoCrawler crawler = CreateCrawler(_ => FakeHttpMessageHandler.Json(HttpStatusCode.OK, "[]")))
            {
                await TestAssert.ThrowsAsync<ArgumentException>(() => DrainAsync(crawler.GetRepositoryContentsAsync("https://github.com/owner")));
            }
        }

        [Scenario("url-parsing")]
        public static async Task UrlParse_SshOwnerWithoutRepo_Throws()
        {
            using (GitHubRepoCrawler crawler = CreateCrawler(_ => FakeHttpMessageHandler.Json(HttpStatusCode.OK, "[]")))
            {
                await TestAssert.ThrowsAsync<ArgumentException>(() => DrainAsync(crawler.GetRepositoryContentsAsync("git@github.com:owner")));
            }
        }

        #endregion

        #region Crawler-Contents

        [Scenario("crawler-contents")]
        public static async Task Contents_SingleFile_YieldsDownloadUrl()
        {
            string downloadUrl = "https://raw.githubusercontent.com/owner/repo/main/a.txt";
            using (GitHubRepoCrawler crawler = CreateCrawler(request =>
            {
                if (request.RequestUri.AbsoluteUri == RootContentsApiUrl)
                {
                    return FakeHttpMessageHandler.Json(HttpStatusCode.OK, GitHubJson.Array(GitHubJson.File("a.txt", "a.txt", downloadUrl)));
                }

                return FakeHttpMessageHandler.Json(HttpStatusCode.NotFound, "not found");
            }))
            {
                List<string> results = await DrainAsync(crawler.GetRepositoryContentsAsync(ValidRepoUrl));
                TestAssert.Single(results);
                TestAssert.Equal(downloadUrl, results[0]);
            }
        }

        [Scenario("crawler-contents")]
        public static async Task Contents_MultipleFiles_YieldsAllInOrder()
        {
            using (GitHubRepoCrawler crawler = CreateCrawler(request =>
            {
                return FakeHttpMessageHandler.Json(HttpStatusCode.OK, GitHubJson.Array(
                    GitHubJson.File("a.txt", "a.txt", "https://raw/a.txt"),
                    GitHubJson.File("b.txt", "b.txt", "https://raw/b.txt"),
                    GitHubJson.File("c.txt", "c.txt", "https://raw/c.txt")));
            }))
            {
                List<string> results = await DrainAsync(crawler.GetRepositoryContentsAsync(ValidRepoUrl));
                TestAssert.Count(3, results);
                TestAssert.Equal("https://raw/a.txt", results[0]);
                TestAssert.Equal("https://raw/b.txt", results[1]);
                TestAssert.Equal("https://raw/c.txt", results[2]);
            }
        }

        [Scenario("crawler-contents")]
        public static async Task Contents_DirectorySkippedButRecursed()
        {
            using (GitHubRepoCrawler crawler = CreateCrawler(request =>
            {
                string url = request.RequestUri.AbsoluteUri;
                if (url == RootContentsApiUrl)
                {
                    return FakeHttpMessageHandler.Json(HttpStatusCode.OK, GitHubJson.Array(
                        GitHubJson.File("a.txt", "a.txt", "https://raw/a.txt"),
                        GitHubJson.Directory("sub", "sub")));
                }

                if (url == "https://api.github.com/repos/owner/repo/contents/sub")
                {
                    return FakeHttpMessageHandler.Json(HttpStatusCode.OK, GitHubJson.Array(
                        GitHubJson.File("b.txt", "sub/b.txt", "https://raw/sub/b.txt")));
                }

                return FakeHttpMessageHandler.Json(HttpStatusCode.NotFound, "not found");
            }))
            {
                List<string> results = await DrainAsync(crawler.GetRepositoryContentsAsync(ValidRepoUrl));
                TestAssert.Count(2, results);
                TestAssert.Contains(results, "https://raw/a.txt");
                TestAssert.Contains(results, "https://raw/sub/b.txt");
            }
        }

        [Scenario("crawler-contents")]
        public static async Task Contents_NestedDirectories_RecurseDeep()
        {
            using (GitHubRepoCrawler crawler = CreateCrawler(request =>
            {
                string url = request.RequestUri.AbsoluteUri;
                if (url == RootContentsApiUrl)
                {
                    return FakeHttpMessageHandler.Json(HttpStatusCode.OK, GitHubJson.Array(GitHubJson.Directory("a", "a")));
                }

                if (url == "https://api.github.com/repos/owner/repo/contents/a")
                {
                    return FakeHttpMessageHandler.Json(HttpStatusCode.OK, GitHubJson.Array(GitHubJson.Directory("b", "a/b")));
                }

                if (url == "https://api.github.com/repos/owner/repo/contents/a/b")
                {
                    return FakeHttpMessageHandler.Json(HttpStatusCode.OK, GitHubJson.Array(GitHubJson.File("c.txt", "a/b/c.txt", "https://raw/a/b/c.txt")));
                }

                return FakeHttpMessageHandler.Json(HttpStatusCode.NotFound, "not found");
            }))
            {
                List<string> results = await DrainAsync(crawler.GetRepositoryContentsAsync(ValidRepoUrl));
                TestAssert.Single(results);
                TestAssert.Equal("https://raw/a/b/c.txt", results[0]);
            }
        }

        [Scenario("crawler-contents")]
        public static async Task Contents_SubdirectoryYieldedBeforeLaterSiblings_DepthFirstOrder()
        {
            // Root order is [dir "a", file "z.txt"]. The crawler is depth-first pre-order, so the
            // contents of "a" must be yielded before the later sibling "z.txt".
            using (GitHubRepoCrawler crawler = CreateCrawler(request =>
            {
                string url = request.RequestUri.AbsoluteUri;
                if (url == RootContentsApiUrl)
                {
                    return FakeHttpMessageHandler.Json(HttpStatusCode.OK, GitHubJson.Array(
                        GitHubJson.Directory("a", "a"),
                        GitHubJson.File("z.txt", "z.txt", "https://raw/z.txt")));
                }

                if (url == "https://api.github.com/repos/owner/repo/contents/a")
                {
                    return FakeHttpMessageHandler.Json(HttpStatusCode.OK, GitHubJson.Array(
                        GitHubJson.File("a1.txt", "a/a1.txt", "https://raw/a/a1.txt")));
                }

                return FakeHttpMessageHandler.Json(HttpStatusCode.NotFound, "not found");
            }))
            {
                List<string> results = await DrainAsync(crawler.GetRepositoryContentsAsync(ValidRepoUrl));
                TestAssert.Count(2, results);
                TestAssert.Equal("https://raw/a/a1.txt", results[0]);
                TestAssert.Equal("https://raw/z.txt", results[1]);
            }
        }

        [Scenario("crawler-contents")]
        public static async Task Contents_CancelledMidStream_StopsEnumeration()
        {
            // Cancel after the first yielded item. The per-item cancellation check inside the crawl loop
            // must then abort enumeration rather than continuing to recurse into the "sub" directory.
            using (CancellationTokenSource cts = new CancellationTokenSource())
            using (GitHubRepoCrawler crawler = CreateCrawler(request =>
            {
                string url = request.RequestUri.AbsoluteUri;
                if (url == RootContentsApiUrl)
                {
                    return FakeHttpMessageHandler.Json(HttpStatusCode.OK, GitHubJson.Array(
                        GitHubJson.File("a.txt", "a.txt", "https://raw/a.txt"),
                        GitHubJson.Directory("sub", "sub")));
                }

                return FakeHttpMessageHandler.Json(HttpStatusCode.OK, GitHubJson.Array(
                    GitHubJson.File("b.txt", "sub/b.txt", "https://raw/sub/b.txt")));
            }))
            {
                List<string> collected = new List<string>();

                await TestAssert.ThrowsAsync<OperationCanceledException>(async () =>
                {
                    await foreach (string url in crawler.GetRepositoryContentsAsync(ValidRepoUrl, cts.Token))
                    {
                        collected.Add(url);
                        cts.Cancel();
                    }
                });

                TestAssert.Single(collected);
                TestAssert.Equal("https://raw/a.txt", collected[0]);
            }
        }

        [Scenario("crawler-contents")]
        public static async Task Contents_EmptyRepository_NoResults()
        {
            using (GitHubRepoCrawler crawler = CreateCrawler(_ => FakeHttpMessageHandler.Json(HttpStatusCode.OK, "[]")))
            {
                List<string> results = await DrainAsync(crawler.GetRepositoryContentsAsync(ValidRepoUrl));
                TestAssert.Empty(results);
            }
        }

        [Scenario("crawler-contents")]
        public static async Task Contents_FileWithNullDownloadUrl_NotYielded()
        {
            using (GitHubRepoCrawler crawler = CreateCrawler(_ => FakeHttpMessageHandler.Json(HttpStatusCode.OK, GitHubJson.Array(
                GitHubJson.File("submodule", "submodule", null)))))
            {
                List<string> results = await DrainAsync(crawler.GetRepositoryContentsAsync(ValidRepoUrl));
                TestAssert.Empty(results);
            }
        }

        [Scenario("crawler-contents")]
        public static async Task Contents_NotFound_ThrowsRepositoryNotFound()
        {
            using (GitHubRepoCrawler crawler = CreateCrawler(_ => FakeHttpMessageHandler.Json(HttpStatusCode.NotFound, "{}")))
            {
                Exception ex = await TestAssert.ThrowsAsync<Exception>(() => DrainAsync(crawler.GetRepositoryContentsAsync(ValidRepoUrl)));
                TestAssert.Contains(ex.Message, "Repository not found");
            }
        }

        [Scenario("crawler-contents")]
        public static async Task Contents_Forbidden_ThrowsRateLimit()
        {
            using (GitHubRepoCrawler crawler = CreateCrawler(_ => FakeHttpMessageHandler.Json(HttpStatusCode.Forbidden, "{}")))
            {
                Exception ex = await TestAssert.ThrowsAsync<Exception>(() => DrainAsync(crawler.GetRepositoryContentsAsync(ValidRepoUrl)));
                TestAssert.Contains(ex.Message, "rate limit");
            }
        }

        [Scenario("crawler-contents")]
        public static async Task Contents_ServerError_ThrowsApiFailed()
        {
            using (GitHubRepoCrawler crawler = CreateCrawler(_ => FakeHttpMessageHandler.Json(HttpStatusCode.InternalServerError, "{}")))
            {
                Exception ex = await TestAssert.ThrowsAsync<Exception>(() => DrainAsync(crawler.GetRepositoryContentsAsync(ValidRepoUrl)));
                TestAssert.Contains(ex.Message, "API request failed");
            }
        }

        [Scenario("crawler-contents")]
        public static async Task Contents_Cancelled_Throws()
        {
            using (GitHubRepoCrawler crawler = CreateCrawler(_ => FakeHttpMessageHandler.Json(HttpStatusCode.OK, "[]")))
            using (CancellationTokenSource cts = new CancellationTokenSource())
            {
                cts.Cancel();
                await TestAssert.ThrowsAsync<OperationCanceledException>(
                    () => DrainAsync(crawler.GetRepositoryContentsAsync(ValidRepoUrl, cts.Token)));
            }
        }

        [Scenario("crawler-contents")]
        public static async Task Contents_RequestUsesContentsEndpoint()
        {
            FakeHttpMessageHandler handler = new FakeHttpMessageHandler(_ => FakeHttpMessageHandler.Json(HttpStatusCode.OK, "[]"));
            using (GitHubRepoCrawler crawler = new GitHubRepoCrawler(handler))
            {
                await DrainAsync(crawler.GetRepositoryContentsAsync(ValidRepoUrl));
                TestAssert.Single(handler.RequestedUris);
                TestAssert.Equal(RootContentsApiUrl, handler.RequestedUris[0]);
            }
        }

        #endregion

        #region Crawler-Files

        [Scenario("crawler-files")]
        public static async Task Files_Success_ReturnsContentBytes()
        {
            byte[] payload = Encoding.UTF8.GetBytes("hello world");
            using (GitHubRepoCrawler crawler = CreateCrawler(_ => FakeHttpMessageHandler.Bytes(HttpStatusCode.OK, payload, "text/plain")))
            {
                GitHubFileResponse response = await crawler.GetFileContentsAsync("https://raw/a.txt");
                TestAssert.NotNull(response);
                TestAssert.NotNull(response.Content);
                TestAssert.Equal("hello world", Encoding.UTF8.GetString(response.Content));
            }
        }

        [Scenario("crawler-files")]
        public static async Task Files_Success_PopulatesStatusCode()
        {
            using (GitHubRepoCrawler crawler = CreateCrawler(_ => FakeHttpMessageHandler.Bytes(HttpStatusCode.OK, new byte[] { 1, 2, 3 }, "application/octet-stream")))
            {
                GitHubFileResponse response = await crawler.GetFileContentsAsync("https://raw/a.bin");
                TestAssert.Equal(HttpStatusCode.OK, response.StatusCode);
            }
        }

        [Scenario("crawler-files")]
        public static async Task Files_Success_PopulatesContentType()
        {
            using (GitHubRepoCrawler crawler = CreateCrawler(_ => FakeHttpMessageHandler.Bytes(HttpStatusCode.OK, new byte[] { 1 }, "text/plain")))
            {
                GitHubFileResponse response = await crawler.GetFileContentsAsync("https://raw/a.txt");
                TestAssert.NotNull(response.ContentType);
                TestAssert.Contains(response.ContentType, "text/plain");
            }
        }

        [Scenario("crawler-files")]
        public static async Task Files_Success_PopulatesFinalUrl()
        {
            string url = "https://raw.githubusercontent.com/owner/repo/main/a.txt";
            using (GitHubRepoCrawler crawler = CreateCrawler(_ => FakeHttpMessageHandler.Bytes(HttpStatusCode.OK, new byte[] { 1 }, "text/plain")))
            {
                GitHubFileResponse response = await crawler.GetFileContentsAsync(url);
                TestAssert.NotNull(response.FinalUrl);
                TestAssert.Equal(url, response.FinalUrl.ToString());
            }
        }

        [Scenario("crawler-files")]
        public static async Task Files_Success_PopulatesHeaders()
        {
            Dictionary<string, string> responseHeaders = new Dictionary<string, string> { { "X-Test", "abc" } };
            using (GitHubRepoCrawler crawler = CreateCrawler(_ => FakeHttpMessageHandler.Bytes(HttpStatusCode.OK, new byte[] { 1 }, "text/plain", responseHeaders)))
            {
                GitHubFileResponse response = await crawler.GetFileContentsAsync("https://raw/a.txt");
                TestAssert.NotNull(response.Headers);
                TestAssert.True(response.Headers.ContainsKey("X-Test"));
            }
        }

        [Scenario("crawler-files")]
        public static async Task Files_EmptyContent_ReturnsEmptyArray()
        {
            using (GitHubRepoCrawler crawler = CreateCrawler(_ => FakeHttpMessageHandler.Bytes(HttpStatusCode.OK, Array.Empty<byte>(), "text/plain")))
            {
                GitHubFileResponse response = await crawler.GetFileContentsAsync("https://raw/empty.txt");
                TestAssert.NotNull(response.Content);
                TestAssert.Equal(0, response.Content.Length);
            }
        }

        [Scenario("crawler-files")]
        public static async Task Files_NoContentType_ContentTypeIsNull()
        {
            using (GitHubRepoCrawler crawler = CreateCrawler(_ => FakeHttpMessageHandler.Bytes(HttpStatusCode.OK, new byte[] { 1 }, null)))
            {
                GitHubFileResponse response = await crawler.GetFileContentsAsync("https://raw/a.bin");
                TestAssert.Null(response.ContentType);
            }
        }

        [Scenario("crawler-files")]
        public static async Task Files_NonSuccessStatus_StillReturnsResponse()
        {
            using (GitHubRepoCrawler crawler = CreateCrawler(_ => FakeHttpMessageHandler.Bytes(HttpStatusCode.NotFound, Encoding.UTF8.GetBytes("nope"), "text/plain")))
            {
                GitHubFileResponse response = await crawler.GetFileContentsAsync("https://raw/missing.txt");
                TestAssert.NotNull(response);
                TestAssert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            }
        }

        [Scenario("crawler-files")]
        public static async Task Files_NullUrl_Throws()
        {
            using (GitHubRepoCrawler crawler = CreateCrawler(_ => FakeHttpMessageHandler.Bytes(HttpStatusCode.OK, new byte[] { 1 }, "text/plain")))
            {
                await TestAssert.ThrowsAsync<ArgumentException>(() => crawler.GetFileContentsAsync(null));
            }
        }

        [Scenario("crawler-files")]
        public static async Task Files_EmptyUrl_Throws()
        {
            using (GitHubRepoCrawler crawler = CreateCrawler(_ => FakeHttpMessageHandler.Bytes(HttpStatusCode.OK, new byte[] { 1 }, "text/plain")))
            {
                await TestAssert.ThrowsAsync<ArgumentException>(() => crawler.GetFileContentsAsync(string.Empty));
            }
        }

        [Scenario("crawler-files")]
        public static async Task Files_WhitespaceUrl_Throws()
        {
            using (GitHubRepoCrawler crawler = CreateCrawler(_ => FakeHttpMessageHandler.Bytes(HttpStatusCode.OK, new byte[] { 1 }, "text/plain")))
            {
                await TestAssert.ThrowsAsync<ArgumentException>(() => crawler.GetFileContentsAsync("   "));
            }
        }

        [Scenario("crawler-files")]
        public static async Task Files_Cancelled_Throws()
        {
            using (GitHubRepoCrawler crawler = CreateCrawler(_ => FakeHttpMessageHandler.Bytes(HttpStatusCode.OK, new byte[] { 1 }, "text/plain")))
            using (CancellationTokenSource cts = new CancellationTokenSource())
            {
                cts.Cancel();
                await TestAssert.ThrowsAsync<OperationCanceledException>(() => crawler.GetFileContentsAsync("https://raw/a.txt", cts.Token));
            }
        }

        #endregion

        #region Request-Headers

        private const string ExpectedUserAgent = "GitHubRepoCrawler/1.0";
        private const string SampleToken = "ghp_exampletoken";

        [Scenario("request-headers")]
        public static async Task Headers_UserAgent_SentOnContentsRequest()
        {
            FakeHttpMessageHandler handler = new FakeHttpMessageHandler(_ => FakeHttpMessageHandler.Json(HttpStatusCode.OK, "[]"));
            using (GitHubRepoCrawler crawler = new GitHubRepoCrawler(handler))
            {
                await DrainAsync(crawler.GetRepositoryContentsAsync(ValidRepoUrl));
                TestAssert.Single(handler.Requests);
                TestAssert.Equal(ExpectedUserAgent, handler.Requests[0].Header("User-Agent"));
            }
        }

        [Scenario("request-headers")]
        public static async Task Headers_NoToken_NoAuthorizationHeaderOnContents()
        {
            FakeHttpMessageHandler handler = new FakeHttpMessageHandler(_ => FakeHttpMessageHandler.Json(HttpStatusCode.OK, "[]"));
            using (GitHubRepoCrawler crawler = new GitHubRepoCrawler(handler))
            {
                await DrainAsync(crawler.GetRepositoryContentsAsync(ValidRepoUrl));
                TestAssert.Single(handler.Requests);
                TestAssert.False(handler.Requests[0].HasHeader("Authorization"));
            }
        }

        [Scenario("request-headers")]
        public static async Task Headers_WithToken_AuthorizationHeaderSentOnContents()
        {
            FakeHttpMessageHandler handler = new FakeHttpMessageHandler(_ => FakeHttpMessageHandler.Json(HttpStatusCode.OK, "[]"));
            using (GitHubRepoCrawler crawler = new GitHubRepoCrawler(handler, SampleToken))
            {
                await DrainAsync(crawler.GetRepositoryContentsAsync(ValidRepoUrl));
                TestAssert.Single(handler.Requests);
                TestAssert.True(handler.Requests[0].HasHeader("Authorization"));
                TestAssert.Equal("token " + SampleToken, handler.Requests[0].Header("Authorization"));
            }
        }

        [Scenario("request-headers")]
        public static async Task Headers_UserAgent_SentOnFileDownload()
        {
            FakeHttpMessageHandler handler = new FakeHttpMessageHandler(_ => FakeHttpMessageHandler.Bytes(HttpStatusCode.OK, new byte[] { 1 }, "text/plain"));
            using (GitHubRepoCrawler crawler = new GitHubRepoCrawler(handler))
            {
                await crawler.GetFileContentsAsync("https://raw/a.txt");
                TestAssert.Single(handler.Requests);
                TestAssert.Equal(ExpectedUserAgent, handler.Requests[0].Header("User-Agent"));
            }
        }

        [Scenario("request-headers")]
        public static async Task Headers_WithToken_AuthorizationHeaderSentOnFileDownload()
        {
            FakeHttpMessageHandler handler = new FakeHttpMessageHandler(_ => FakeHttpMessageHandler.Bytes(HttpStatusCode.OK, new byte[] { 1 }, "text/plain"));
            using (GitHubRepoCrawler crawler = new GitHubRepoCrawler(handler, SampleToken))
            {
                await crawler.GetFileContentsAsync("https://raw/a.txt");
                TestAssert.Single(handler.Requests);
                TestAssert.Equal("token " + SampleToken, handler.Requests[0].Header("Authorization"));
            }
        }

        [Scenario("request-headers")]
        public static async Task Headers_NoToken_NoAuthorizationHeaderOnFileDownload()
        {
            FakeHttpMessageHandler handler = new FakeHttpMessageHandler(_ => FakeHttpMessageHandler.Bytes(HttpStatusCode.OK, new byte[] { 1 }, "text/plain"));
            using (GitHubRepoCrawler crawler = new GitHubRepoCrawler(handler))
            {
                await crawler.GetFileContentsAsync("https://raw/a.txt");
                TestAssert.Single(handler.Requests);
                TestAssert.False(handler.Requests[0].HasHeader("Authorization"));
            }
        }

        #endregion

        #region Helpers

        private static GitHubRepoCrawler CreateCrawler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        {
            return new GitHubRepoCrawler(new FakeHttpMessageHandler(responder));
        }

        private static async Task AssertFirstApiUrl(string gitUrl, string expectedApiUrl)
        {
            FakeHttpMessageHandler handler = new FakeHttpMessageHandler(_ => FakeHttpMessageHandler.Json(HttpStatusCode.OK, "[]"));
            using (GitHubRepoCrawler crawler = new GitHubRepoCrawler(handler))
            {
                await DrainAsync(crawler.GetRepositoryContentsAsync(gitUrl));
                TestAssert.True(handler.RequestedUris.Count >= 1, "Expected at least one request to be made.");
                TestAssert.Equal(expectedApiUrl, handler.RequestedUris[0]);
            }
        }

        private static async Task<List<string>> DrainAsync(IAsyncEnumerable<string> source)
        {
            List<string> results = new List<string>();
            await foreach (string item in source.ConfigureAwait(false))
            {
                results.Add(item);
            }

            return results;
        }

        #endregion
    }
}
