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
public GitHubRepoCrawler(string token = null)
```

Creates a new crawler instance. Supply a [personal access token](https://docs.github.com/en/authentication/keeping-your-account-and-data-secure/creating-a-personal-access-token) for:
- Access to private repositories
- Higher API rate limits (5,000 requests/hour vs 60 for unauthenticated)
- Avoiding rate limit errors in large repositories

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
- `OperationCanceledException`: Operation was cancelled
- `Exception`: API errors (rate limits, network issues, etc.)

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
- `OperationCanceledException`: Operation was cancelled
- `Exception`: Download failed

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
catch (Exception ex) when (ex.Message.Contains("rate limit"))
{
    Console.WriteLine("GitHub API rate limit exceeded. Please authenticate or wait.");
}
catch (Exception ex)
{
    Console.WriteLine($"Error: {ex.Message}");
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

When rate limited, the API returns status code 403 with a "rate limit exceeded" message.

## Contributing

Contributions are welcome! Please feel free to submit a Pull Request.

## License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.
