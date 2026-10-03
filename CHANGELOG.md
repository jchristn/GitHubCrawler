# Change Log

## Current Version

v1.2.1

- No library API or behavior changes; library dependencies unchanged (`System.Diagnostics.DiagnosticSource` 10.0.12 is still current)
- Test dependencies updated: Touchstone.Core, Touchstone.Cli, Touchstone.XunitAdapter, Touchstone.NunitAdapter (0.1.12 -> 0.2.0), NUnit (4.6.1 -> 5.0.0), NUnit3TestAdapter (6.2.0 -> 6.3.0), Microsoft.NET.Test.Sdk (18.9.0 -> 18.10.1)
- All 94 tests pass under xUnit, NUnit, and the Touchstone CLI runner

v1.2.0

- Specific exception types: `GitHubCrawlerException` (with `StatusCode`), `GitHubRepositoryNotFoundException` (404, with `Owner` and `Repository`), and `GitHubRateLimitException` (403 or 429, with `RateLimitRemaining` and `RateLimitReset`). All derive from `Exception`, and messages are unchanged, so existing `catch (Exception)` handlers keep working
- HTTP 429 is now reported as `GitHubRateLimitException` (previously a generic "API request failed: TooManyRequests")
- New `ApiBaseUrl` property (GitHub Enterprise Server support) and `UserAgent` property, with `DefaultApiBaseUrl` and `DefaultUserAgent` constants
- `GetFileContentsAsync` now disposes the HTTP response after buffering the body
- A directory listing whose body is JSON `null` now yields nothing instead of throwing `NullReferenceException`
- Nullable reference type annotations across the public API
- Removed the unused `Inputty` package dependency
- Internal code style cleanup (no behavior change)

v1.1.0

- Built-in telemetry: metrics and traces through a BCL `Meter` and `ActivitySource` named `GitHubCrawler` (no exporter or SDK dependency, near-zero cost when unobserved)
- Spans: `githubcrawler crawl_repository`, `github contents.list` (one per directory), `githubcrawler get_file_contents`, `github file.download`, with explicit status, exception events, and caller context propagation
- Metrics: operation and GitHub request counters and duration histograms by outcome and `error.type`, rate-limit remaining gauge and exceeded counter, crawl items/files/directories, download size, in-flight operations, active crawler instances, last-success timestamps, and build info
- New public `GitHubCrawlerTelemetry` class holding every meter, source, instrument, span, and attribute name
- Directory listing responses are now disposed after being read
- Added dependency: `System.Diagnostics.DiagnosticSource` 10.0.12 (histogram bucket advice)
- See TELEMETRY.md for the full catalog

v1.0.x

- Authentication Support - Use personal access tokens for private repos and higher rate limits
- Async Enumerable - Modern async streaming API for efficient memory usage
- Cancellation Support - All operations support CancellationToken for graceful termination
- Proper Resource Management - Implements IDisposable for clean HttpClient disposal
- Recursive Discovery - Automatically traverses entire repository structure
- Metadata Included - Returns full HTTP response metadata alongside file content
- Minimal Dependencies - Lightweight with minimal external dependencies

## Previous Versions

Notes from previous versions will be placed here.
