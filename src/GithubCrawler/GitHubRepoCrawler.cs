namespace GitHubCrawler
{
    using GetSomeInput;
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net.Http;
    using System.Text.Json;
    using System.Threading.Tasks;

    /// <summary>
    /// GitHub repository crawler.
    /// </summary>
    public class GitHubRepoCrawler
    {
        private readonly HttpClient _httpClient = null;
        private readonly string _githubToken = null;

        /// <summary>
        /// GitHub repository crawler.
        /// </summary>
        /// <param name="token">GitHub token.</param>
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

        public async IAsyncEnumerable<string> GetRepositoryContentsAsync(string gitUrl)
        {
            var (owner, repo) = ParseGitUrl(gitUrl);
            if (string.IsNullOrEmpty(owner) || string.IsNullOrEmpty(repo))
            {
                throw new ArgumentException("Invalid GitHub repository URL");
            }

            await foreach (var url in CrawlDirectoryAsync(owner, repo, ""))
            {
                yield return url;
            }
        }

        public async Task<GitHubFileResponse> GetFileContentsAsync(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                throw new ArgumentException("Download URL cannot be null or empty.", nameof(url));

            try
            {
                var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
                var contentBytes = await response.Content.ReadAsByteArrayAsync();

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
            catch (HttpRequestException ex)
            {
                throw new Exception($"Failed to fetch file from GitHub: {ex.Message}", ex);
            }
        }

        private async IAsyncEnumerable<string> CrawlDirectoryAsync(string owner, string repo, string path)
        {
            var apiUrl = $"https://api.github.com/repos/{owner}/{repo}/contents/{path}";

            HttpResponseMessage response;
            try
            {
                response = await _httpClient.GetAsync(apiUrl);
            }
            catch (HttpRequestException ex)
            {
                throw new Exception($"Network error while crawling repository: {ex.Message}", ex);
            }

            if (!response.IsSuccessStatusCode)
            {
                if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                    throw new Exception($"Repository not found: {owner}/{repo}");
                if (response.StatusCode == System.Net.HttpStatusCode.Forbidden)
                    throw new Exception("API rate limit exceeded. Consider using an authentication token.");

                throw new Exception($"API request failed: {response.StatusCode}");
            }

            List<GitHubContent> items;
            try
            {
                var json = await response.Content.ReadAsStringAsync();
                items = JsonSerializer.Deserialize<List<GitHubContent>>(json);
            }
            catch (JsonException ex)
            {
                throw new Exception($"Error parsing GitHub API response: {ex.Message}", ex);
            }

            foreach (var item in items)
            {
                if (!String.IsNullOrEmpty(item.DownloadUrl)) 
                    yield return item.DownloadUrl;

                if (item.Type == "dir")
                {
                    await foreach (var subItem in CrawlDirectoryAsync(owner, repo, item.Path))
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
    }
}
