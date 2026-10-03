# GitHubCrawler Telemetry

GitHubCrawler (v1.1.0 and later) emits metrics and traces so an operator can see, from Grafana alone, where the time went in a crawl or a download and what failed. It is a **library**, so it follows the library rules from the telemetry standard:

- It emits only through the base class library: one `System.Diagnostics.Metrics.Meter` and one `System.Diagnostics.ActivitySource`. It takes no dependency on Radiant, the OpenTelemetry SDK, or any exporter. (The only added package is `System.Diagnostics.DiagnosticSource`, the Microsoft out-of-band build of the same BCL types. It lets the library suggest histogram bucket boundaries to the host.)
- Emission is effectively free when nobody is listening. Instruments check `Enabled` before building tags, and `StartActivity` returns null without a listener.
- Instrumentation is best-effort. Every recording path is wrapped so a telemetry failure never changes crawler behavior, return values, or exceptions.
- The library never exports anything. Your host process subscribes and exports to Prometheus, Tempo, or any OTLP backend.

All names below are public constants on `GitHubCrawler.GitHubCrawlerTelemetry`, so code and dashboards can reference them without string literals.

## Sources

| Kind | Name | Version |
| --- | --- | --- |
| Meter | `GitHubCrawler` | library version (for example `1.1.0`) |
| ActivitySource | `GitHubCrawler` | library version |

Related sources worth subscribing to in the same host:

| Kind | Name | What it adds |
| --- | --- | --- |
| Meter | `System.Net.Http` | `http.client.request.duration`, `http.client.open_connections`, `http.client.request.time_in_queue` (the HttpClient connection pool) |
| ActivitySource | `System.Net.Http` | the raw HTTP client span under each `github ...` span, plus W3C `traceparent` injection on outbound requests |
| Meter | `System.Runtime` (.NET 9+ hosts) or `OpenTelemetry.Instrumentation.Runtime` | GC, thread pool, and allocation metrics |

## Subscribing from a host

### Radiant

```csharp
using Radiant;

RadiantSettings settings = new RadiantSettings("my-service");
settings.Otlp.Endpoint = "http://127.0.0.1:4317";
settings.Prometheus.Enable = true;
settings.Sources.AddMeter("GitHubCrawler");          // GitHubCrawlerTelemetry.MeterName
settings.Sources.AddActivitySource("GitHubCrawler"); // GitHubCrawlerTelemetry.ActivitySourceName
settings.Sources.AddMeter("System.Net.Http");
settings.Sources.AddActivitySource("System.Net.Http");

using (RadiantHost host = RadiantHost.Start(settings))
{
    // run the application
}
```

### OpenTelemetry SDK

```csharp
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

using MeterProvider meters = Sdk.CreateMeterProviderBuilder()
    .AddMeter(GitHubCrawlerTelemetry.MeterName)
    .AddMeter("System.Net.Http")
    .AddPrometheusHttpListener()
    .Build();

using TracerProvider tracer = Sdk.CreateTracerProviderBuilder()
    .AddSource(GitHubCrawlerTelemetry.ActivitySourceName)
    .AddHttpClientInstrumentation()
    .AddOtlpExporter(o => o.Endpoint = new Uri("http://127.0.0.1:4317"))
    .Build();
```

### Plain BCL (tests, custom collectors)

Attach a `MeterListener` to the `GitHubCrawler` meter and an `ActivityListener` to the `GitHubCrawler` source. `src/Test.Shared/TelemetryCapture.cs` is a complete, working example.

### Configuration

There is nothing to configure inside the library. Telemetry is always emitted and costs nothing until a host subscribes. To turn it off, do not subscribe to the `GitHubCrawler` names. Exporter endpoints, service name, sampling, and Prometheus exposure belong to the host (for example `RadiantSettings`), with loopback defaults of `127.0.0.1`.

## Metrics catalog

Names are shown as emitted, then as rendered by a Prometheus exporter. Units follow UCUM. No quantiles are computed in-process; derive p50/p95/p99 from histogram buckets with `histogram_quantile`.

| Instrument | Prometheus series | Type | Unit | Labels | Description |
| --- | --- | --- | --- | --- | --- |
| `githubcrawler.operation.duration` | `githubcrawler_operation_duration_seconds` | Histogram | `s` | `githubcrawler.operation`, `outcome`, `error.type` | Duration of each public operation. For a crawl this covers the whole enumeration, including time the caller spends between items. |
| `githubcrawler.operations` | `githubcrawler_operations_total` | Counter | `{operation}` | `githubcrawler.operation`, `outcome`, `error.type` | Completed public operations. Errors by type are `outcome="failure"` grouped by `error.type`. |
| `githubcrawler.operations.active` | `githubcrawler_operations_active` | UpDownCounter | `{operation}` | `githubcrawler.operation` | Operations currently in flight (concurrency). |
| `githubcrawler.operation.last_success.timestamp` | `githubcrawler_operation_last_success_timestamp_seconds` | ObservableGauge | `s` | `githubcrawler.operation` | Unix time of the last successful operation of each type in this process. Absent until one succeeds. |
| `githubcrawler.github.request.duration` | `githubcrawler_github_request_duration_seconds` | Histogram | `s` | `github.operation`, `outcome`, `http.response.status_code`, `error.type`, `githubcrawler.auth` | Duration of each outbound GitHub call, including reading the body. One per directory listed, one per file downloaded. |
| `githubcrawler.github.requests` | `githubcrawler_github_requests_total` | Counter | `{request}` | same as above | Outbound GitHub calls by operation and outcome. |
| `githubcrawler.github.rate_limit.exceeded` | `githubcrawler_github_rate_limit_exceeded_total` | Counter | `{request}` | `github.operation`, `githubcrawler.auth` | Responses with HTTP 403 or 429 (GitHub rate limiting). |
| `githubcrawler.github.rate_limit.remaining` | `githubcrawler_github_rate_limit_remaining` | ObservableGauge | `{request}` | `githubcrawler.auth` | Last `X-RateLimit-Remaining` value seen, per auth mode, process-wide. Absent until a response carries the header. |
| `githubcrawler.crawl.items` | `githubcrawler_crawl_items_total` | Counter | `{item}` | `githubcrawler.item.type` | Repository entries discovered. |
| `githubcrawler.crawl.files` | `githubcrawler_crawl_files` | Histogram | `{file}` | `outcome` | File URLs yielded per crawl. |
| `githubcrawler.crawl.directories` | `githubcrawler_crawl_directories` | Histogram | `{directory}` | `outcome` | Directories listed per crawl (each is one GitHub API call). |
| `githubcrawler.file.download.size` | `githubcrawler_file_download_size_bytes` | Histogram | `By` | `outcome` | Size of downloaded file bodies. |
| `githubcrawler.crawlers.active` | `githubcrawler_crawlers_active` | UpDownCounter | `{crawler}` | `githubcrawler.auth` | `GitHubRepoCrawler` instances constructed and not yet disposed. A steady climb means callers are leaking crawlers (and their HttpClients). |
| `githubcrawler.build.info` | `githubcrawler_build_info` | ObservableGauge | `{info}` | `githubcrawler.version` | Constant 1 carrying the library version. |

### Label values (all bounded)

| Label | Values |
| --- | --- |
| `githubcrawler.operation` | `crawl_repository`, `get_file_contents` |
| `github.operation` | `contents.list`, `file.download` |
| `outcome` | `success`, `failure`, `cancelled` (caller's token fired), `abandoned` (caller stopped enumerating early) |
| `error.type` | Present only when `outcome="failure"`. The HTTP status code (`"403"`, `"404"`, `"500"`, ...) for HTTP failures; `System.TimeoutException` for HttpClient timeouts; the snake_case `HttpRequestError` (for example `name_resolution_error`) for network errors that carry one; otherwise the exception type full name (`System.Text.Json.JsonException`, `System.ArgumentException`, `System.ObjectDisposedException`, `System.Net.Http.HttpRequestException`). |
| `http.response.status_code` | HTTP status code; absent when no response was received. |
| `githubcrawler.auth` | `token`, `anonymous` |
| `githubcrawler.item.type` | `file`, `dir`, `symlink`, `submodule`, `other` |
| `githubcrawler.version` | library version |

Owners, repository names, paths, URLs, and tokens are **never** metric labels. Owner, repo, and path appear only on spans; URLs on spans have their query string and user info removed; tokens appear nowhere.

### Histogram buckets

The library suggests bucket boundaries through `InstrumentAdvice` (honored by OpenTelemetry .NET 1.10+ and Radiant):

| Instruments | Boundaries |
| --- | --- |
| `githubcrawler.operation.duration` | 5 ms to 600 s (0.005, 0.01, 0.025, 0.05, 0.1, 0.25, 0.5, 1, 2.5, 5, 10, 30, 60, 120, 300, 600) |
| `githubcrawler.github.request.duration` | 5 ms to 60 s (0.005 ... 30, 60) |
| `githubcrawler.crawl.files`, `githubcrawler.crawl.directories` | 1 to 50,000 |
| `githubcrawler.file.download.size` | 1 KiB to 256 MiB |

Hosts can override these with an SDK view or a Radiant convention.

## Spans catalog

| Span name | Kind | Parent | Attributes | Status |
| --- | --- | --- | --- | --- |
| `githubcrawler crawl_repository` | Internal | caller's `Activity.Current` | `githubcrawler.operation`, `github.owner`, `github.repo`, `githubcrawler.files`, `githubcrawler.directories`, `outcome`, `error.type` | `Ok` on success; `Error` with an `exception` event on failure; `Unset` with `outcome` for `cancelled` and `abandoned` |
| `github contents.list` | Client | `githubcrawler crawl_repository` (explicit, so it survives `yield` boundaries) | `github.operation`, `github.owner`, `github.repo`, `github.path`, `githubcrawler.depth`, `http.request.method`, `server.address`, `url.full`, `http.response.status_code`, `github.rate_limit.remaining`, `githubcrawler.items`, `outcome`, `error.type` | as above |
| `githubcrawler get_file_contents` | Internal | caller's `Activity.Current` | `githubcrawler.operation`, `outcome`, `error.type` | `Ok`; `Error` for exceptions and for responses with status 400 or above (the response is still returned to the caller) |
| `github file.download` | Client | `githubcrawler get_file_contents` | `github.operation`, `http.request.method`, `server.address`, `url.full`, `http.response.status_code`, `http.response.body.size`, `github.rate_limit.remaining`, `outcome`, `error.type` | as above |

Reading a crawl trace: the root span covers the whole enumeration; each `github contents.list` child is one directory. Gaps between children are time spent in your own `await foreach` body (or in recursion bookkeeping), not in GitHub.

### Context propagation

- Inbound: both root spans start under the caller's `Activity.Current`, so a crawl started inside a Watson or ASP.NET request handler nests under the request span.
- Async iterator boundaries: directory spans are parented explicitly to the crawl span, so the hierarchy is correct even though `Activity.Current` resets on every `yield` to the consumer.
- Outbound: when the crawler uses the default `HttpClientHandler` and a `github ...` span is active (that is, the host is tracing), .NET's HTTP diagnostics handler injects a W3C `traceparent` header on every GitHub request. Subscribe to the `System.Net.Http` source as well to see the raw HTTP span between the two. A custom `HttpMessageHandler` passed to the constructor bypasses that handler-level injection.
- There are no background threads or queues in the library, so there are no background hand-offs to propagate across.

## Recommended PromQL alerts

```yaml
groups:
  - name: githubcrawler
    rules:
      - alert: GitHubCrawlerFailureRatioHigh
        expr: |
          sum by (githubcrawler_operation) (rate(githubcrawler_operations_total{outcome="failure"}[10m]))
            / clamp_min(sum by (githubcrawler_operation) (rate(githubcrawler_operations_total[10m])), 1e-9) > 0.1
        for: 10m
        annotations:
          summary: "More than 10% of {{ $labels.githubcrawler_operation }} operations are failing"

      - alert: GitHubCrawlerRateLimited
        expr: sum(increase(githubcrawler_github_rate_limit_exceeded_total[15m])) > 0
        annotations:
          summary: "GitHub is rate limiting the crawler (use a token, or slow down)"

      - alert: GitHubCrawlerRateLimitLow
        expr: min(githubcrawler_github_rate_limit_remaining) < 50
        for: 5m
        annotations:
          summary: "Fewer than 50 GitHub API requests remain in the current window"

      - alert: GitHubCrawlerApiSlow
        expr: |
          histogram_quantile(0.95, sum by (le, github_operation)
            (rate(githubcrawler_github_request_duration_seconds_bucket[10m]))) > 5
        for: 10m
        annotations:
          summary: "p95 GitHub {{ $labels.github_operation }} latency above 5s"

      - alert: GitHubCrawlerNoRecentSuccess
        expr: time() - githubcrawler_operation_last_success_timestamp_seconds{githubcrawler_operation="crawl_repository"} > 86400
        annotations:
          summary: "No successful repository crawl in 24h (adjust to your schedule)"

      - alert: GitHubCrawlerInstanceLeak
        expr: sum(githubcrawler_crawlers_active) > 100
        for: 30m
        annotations:
          summary: "Crawler instances are not being disposed"
```

## Suggested panels

GitHubCrawler is a library and ships no Grafana stack of its own; the host service owns `compose.yaml` and dashboards. Add an "Integrations: GitHub" dashboard (or row) to the host's product folder with:

- Operation rate by `outcome`: `sum by (githubcrawler_operation, outcome) (rate(githubcrawler_operations_total[5m]))`
- Errors by type: `sum by (error_type) (rate(githubcrawler_operations_total{outcome="failure"}[5m]))`
- GitHub call p95 by operation: `histogram_quantile(0.95, sum by (le, github_operation) (rate(githubcrawler_github_request_duration_seconds_bucket[5m])))`
- Rate-limit headroom: `githubcrawler_github_rate_limit_remaining`
- Crawl size p95: `histogram_quantile(0.95, sum by (le) (rate(githubcrawler_crawl_files_bucket[1h])))`
- In flight: `sum by (githubcrawler_operation) (githubcrawler_operations_active)`
- Tempo search: `{ name = "githubcrawler crawl_repository" && status = error }`

## Tests

`src/Test.Shared/GitHubCrawlerScenarios.Telemetry.cs` (suite `telemetry`) proves each signal with an in-memory `MeterListener` and `ActivityListener`: success paths, span hierarchy and caller-context propagation, 404, 403 rate limiting with the remaining gauge, invalid JSON, invalid URL, cancellation, early exit, file download success and 404, network errors, timeouts, use after dispose, crawler lifecycle, build info, no secrets in span attributes, and the no-listener path. Run with `dotnet run --project src/Test.Automated`, or `dotnet test src/GithubCrawler.sln`.
