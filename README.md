![alt tag](https://github.com/jchristn/githubcrawler/blob/main/assets/logo.ico)

# GitHubCrawler

GitHubCrawler is a lightweight C# library for recursively discovering and downloading files from GitHub repositories via the GitHub REST API v3. It provides simple asynchronous access to repository contents with support for cancellation, proper resource management, and modern .NET async streams.

[![NuGet](https://img.shields.io/nuget/v/GitHubCrawler.svg)](https://www.nuget.org/packages/GitHubCrawler/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)

## New in v1.x

* 🔐 **Authentication Support** - Use personal access tokens for private repos and higher rate limits
* 🔄 **Async Enumerable** - Modern async streaming API for efficient memory usage
* ❌ **Cancellation Support** - All operations support `CancellationToken` for graceful termination
* 🧹 **Proper Resource Management** - Implements `IDisposable` for clean HttpClient disposal
* 📁 **Recursive Discovery** - Automatically traverses entire repository structure
* 🔍 **Metadata Included** - Returns full HTTP response metadata alongside file content
* 🚀 **Minimal Dependencies** - Lightweight with minimal external dependencies
* 🎯 **Specific Exceptions** (v1.2.0) - `GitHubRepositoryNotFoundException`, `GitHubRateLimitException`, and `GitHubCrawlerException` instead of a bare `Exception`
* ⚙️ **GitHub Enterprise Support** (v1.2.0) - Configurable `ApiBaseUrl` and `UserAgent`
* 📈 **Built-in Telemetry** (v1.1.0) - OpenTelemetry-compatible metrics and traces through a `Meter` and `ActivitySource` named `GitHubCrawler`, free until a host subscribes

## Installation

```bash
dotnet add package GitHubCrawler
```

Or via Package Manager:

```bash
Install-Package GitHubCrawler
```

## Quick Start

```csharp
using GitHubCrawler;
using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

static async Task Main(string[] args)
{
    // Create crawler with optional GitHub token
    using var crawler = new GitHubRepoCrawler("your-github-token");

    // Enumerate all files in a repository
    var cts = new CancellationTokenSource();
    await foreach (var url in crawler.GetRepositoryContentsAsync(
        "https://github.com/owner/repo", 
        cts.Token))
    {
        Console.WriteLine(url);
    }

    // Download a specific file
    var file = await crawler.GetFileContentsAsync(
        "https://raw.githubusercontent.com/owner/repo/main/file.txt",
        cts.Token);
    
    Console.WriteLine(Encoding.UTF8.GetString(file.Content));
}
```

## API Reference

### Constructor

```csharp
public GitHubRepoCrawler(string? token = null)
public GitHubRepoCrawler(HttpMessageHandler handler, string? token = null)
```

Creates a new crawler instance. Supply a [personal access token](https://docs.github.com/en/authentication/keeping-your-account-and-data-secure/creating-a-personal-access-token) for:
- Access to private repositories
- Higher API rate limits (5,000 requests/hour vs 60 for unauthenticated)
- Avoiding rate limit errors in large repositories

The second overload accepts your own `HttpMessageHandler` (for a proxy, custom TLS, or testing). The crawler owns and disposes it.

### Properties

| Property | Default | Description |
|----------|---------|-------------|
| `ApiBaseUrl` | `https://api.github.com/` | GitHub REST API base URL. Set it to target GitHub Enterprise Server, for example `https://github.example.com/api/v3/`. Must be an absolute http or https URL; a trailing slash is added if missing. |
| `UserAgent` | `GitHubRepoCrawler/1.0` | User-Agent sent with every request. GitHub asks that it identify your application. Must not be empty. |

Set these before issuing requests.

### Methods

#### GetRepositoryContentsAsync

```csharp
public async IAsyncEnumerable<string> GetRepositoryContentsAsync(
    string gitUrl, 
    CancellationToken cancellationToken = default)
```

Recursively discovers all file download URLs in a repository.

**Parameters:**
- `gitUrl`: Repository URL (supports multiple formats):
  - `https://github.com/owner/repo`
  - `https://github.com/owner/repo.git`
  - `git@github.com:owner/repo.git`
- `cancellationToken`: Optional cancellation token

**Returns:** An async enumerable of raw file download URLs

**Exceptions:**
- `ArgumentException`: Invalid repository URL format
- `ObjectDisposedException`: Crawler has been disposed
- `OperationCanceledException`: Operation was cancelled or timed out
- `GitHubRepositoryNotFoundException`: GitHub returned 404 (missing repository, or private without a token). Exposes `Owner` and `Repository`
- `GitHubRateLimitException`: GitHub returned 403 or 429. Exposes `RateLimitRemaining` and `RateLimitReset`
- `GitHubCrawlerException`: Any other unsuccessful GitHub status. Exposes `StatusCode`; the two exceptions above derive from it
- `JsonException`: A directory listing response was not valid JSON
- `HttpRequestException`: Network failure

#### GetFileContentsAsync

```csharp
public async Task<GitHubFileResponse> GetFileContentsAsync(
    string url, 
    CancellationToken cancellationToken = default)
```

Downloads file content from a GitHub raw URL.

**Parameters:**
- `url`: Raw file URL (e.g., from `GetRepositoryContentsAsync`)
- `cancellationToken`: Optional cancellation token

**Returns:** `GitHubFileResponse` containing:
- `byte[] Content`: Raw file bytes
- `string ContentType`: MIME type
- `HttpStatusCode StatusCode`: HTTP response status
- `Uri FinalUrl`: Final URL after redirects
- `Dictionary<string, IEnumerable<string>> Headers`: Response headers

**Exceptions:**
- `ArgumentException`: URL is null or empty
- `ObjectDisposedException`: Crawler has been disposed
- `OperationCanceledException`: Operation was cancelled or timed out
- `HttpRequestException`: Network failure

A non-success status (for example 404) is not thrown; check `StatusCode` on the result. The body is fully buffered and the HTTP response is disposed before the method returns.

### Resource Management

The crawler implements `IDisposable` and should be used with a `using` statement:

```csharp
using var crawler = new GitHubRepoCrawler(token);
// Use crawler...
// Automatically disposed when leaving scope
```

## Advanced Examples

### Handling Cancellation

```csharp
using var cts = new CancellationTokenSource();

// Cancel after 30 seconds
cts.CancelAfter(TimeSpan.FromSeconds(30));

// Or cancel on user input
Console.CancelKeyPress += (s, e) => {
    e.Cancel = true;
    cts.Cancel();
};

try 
{
    await foreach (var url in crawler.GetRepositoryContentsAsync(gitUrl, cts.Token))
    {
        Console.WriteLine(url);
    }
}
catch (OperationCanceledException)
{
    Console.WriteLine("Operation cancelled");
}
```

### Filtering Files

```csharp
// Get only C# source files
await foreach (var url in crawler.GetRepositoryContentsAsync(gitUrl))
{
    if (url.EndsWith(".cs"))
    {
        var file = await crawler.GetFileContentsAsync(url);
        // Process C# file...
    }
}
```

### Error Handling

```csharp
try 
{
    await foreach (var url in crawler.GetRepositoryContentsAsync(gitUrl))
    {
        Console.WriteLine(url);
    }
}
catch (ArgumentException ex)
{
    Console.WriteLine($"Invalid URL: {ex.Message}");
}
catch (GitHubRepositoryNotFoundException ex)
{
    Console.WriteLine($"Repository {ex.Owner}/{ex.Repository} not found (or private without a token).");
}
catch (GitHubRateLimitException ex)
{
    Console.WriteLine($"GitHub API rate limit exceeded. Resets at {ex.RateLimitReset}. Please authenticate or wait.");
}
catch (GitHubCrawlerException ex)
{
    Console.WriteLine($"GitHub returned {ex.StatusCode}: {ex.Message}");
}
```

### Progress Tracking

```csharp
int fileCount = 0;
await foreach (var url in crawler.GetRepositoryContentsAsync(gitUrl))
{
    fileCount++;
    Console.Write($"\rDiscovered {fileCount} files...");
}
Console.WriteLine($"\nTotal files: {fileCount}");
```

## Observability

GitHubCrawler emits metrics and traces through the standard .NET `Meter` and `ActivitySource` APIs, both named `GitHubCrawler`. It has no exporter dependency and costs effectively nothing until your host subscribes. Once subscribed you get:

- A `githubcrawler crawl_repository` span per crawl with one `github contents.list` child per directory, and a `githubcrawler get_file_contents` span with a `github file.download` child per download
- Operation and GitHub-call counters and latency histograms by outcome and `error.type` (for example `404`, `403`, `System.Text.Json.JsonException`)
- GitHub rate-limit headroom (`X-RateLimit-Remaining`) and a rate-limit-exceeded counter
- Crawl size, items discovered by type, download sizes, in-flight operations, live crawler instances, last-success timestamps, and build info

Subscribe with [Radiant](https://www.nuget.org/packages/Radiant/) or the OpenTelemetry SDK:

```csharp
settings.Sources.AddMeter(GitHubCrawlerTelemetry.MeterName);           // "GitHubCrawler"
settings.Sources.AddActivitySource(GitHubCrawlerTelemetry.ActivitySourceName);
```

See [TELEMETRY.md](TELEMETRY.md) for the full metrics and spans catalog, label values, recommended PromQL alerts, and suggested Grafana panels.

## Best Practices

1. **Always use authentication** for production applications to avoid rate limits
2. **Implement cancellation** for user-facing applications
3. **Handle rate limit errors** gracefully with retry logic
4. **Dispose properly** using `using` statements
5. **Consider memory usage** when downloading large files
6. **Validate URLs** before passing to the crawler

## Rate Limits

| Authentication | Requests per Hour |
|---------------|------------------|
| None | 60 |
| Personal Access Token | 5,000 |
| GitHub App | 5,000-15,000 |

When rate limited, the API returns status code 403 or 429, and the crawler throws `GitHubRateLimitException`.

## Testing

The same Touchstone test suites (in `src/Test.Shared`) run under three runners:

```bash
dotnet test src/Test.XUnit
dotnet test src/Test.NUnit
dotnet run --project src/Test.Automated -- --results results.json
```

Test dependencies: Touchstone 0.2.0, xUnit 2.9.3, NUnit 5.0.0, NUnit3TestAdapter 6.3.0, Microsoft.NET.Test.Sdk 18.10.1.

## Contributing

Contributions are welcome! Please feel free to submit a Pull Request.

## License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.
