namespace GitHubCrawler
{
    using System;
    using System.Diagnostics;
    using System.Threading;

    internal sealed class OperationScope
    {
        private readonly string _Operation;
        private readonly long _StartTimestamp;
        private readonly Activity? _Activity;
        private readonly CancellationToken _Token;
        private int _Completed = 0;
        private string? _Outcome = null;

        private OperationScope(string operation, string spanName, CancellationToken token)
        {
            _Operation = operation;
            _Token = token;
            _StartTimestamp = Stopwatch.GetTimestamp();
            _Activity = CrawlerInstrumentation.StartActivity(spanName, ActivityKind.Internal, default(ActivityContext));
            _Activity?.SetTag(GitHubCrawlerTelemetry.AttributeOperation, operation);
            CrawlerInstrumentation.OperationStarted(operation);
        }

        internal Activity? Activity
        {
            get { return _Activity; }
        }

        internal ActivityContext Context
        {
            get { return _Activity != null ? _Activity.Context : default(ActivityContext); }
        }

        internal string? Outcome
        {
            get { return _Outcome; }
        }

        internal static OperationScope Start(string operation, string spanName, CancellationToken token)
        {
            return new OperationScope(operation, spanName, token);
        }

        internal void Succeed()
        {
            if (!TryComplete(GitHubCrawlerTelemetry.OutcomeSuccess)) return;
            CrawlerInstrumentation.MarkSuccess(_Activity);
            Finish(GitHubCrawlerTelemetry.OutcomeSuccess, null);
        }

        internal void Fail(Exception exception)
        {
            string outcome = CrawlerInstrumentation.ClassifyOutcome(exception, _Token);
            if (!TryComplete(outcome)) return;

            string? errorType = outcome == GitHubCrawlerTelemetry.OutcomeFailure ? CrawlerInstrumentation.ClassifyErrorType(exception) : null;
            CrawlerInstrumentation.MarkFailure(_Activity, outcome, errorType, exception?.Message, exception);
            Finish(outcome, errorType);
        }

        internal void Fail(string errorType, string description)
        {
            if (!TryComplete(GitHubCrawlerTelemetry.OutcomeFailure)) return;
            CrawlerInstrumentation.MarkFailure(_Activity, GitHubCrawlerTelemetry.OutcomeFailure, errorType, description, null);
            Finish(GitHubCrawlerTelemetry.OutcomeFailure, errorType);
        }

        internal void Abandon()
        {
            if (!TryComplete(GitHubCrawlerTelemetry.OutcomeAbandoned)) return;
            CrawlerInstrumentation.MarkFailure(_Activity, GitHubCrawlerTelemetry.OutcomeAbandoned, null, "Enumeration stopped by the caller before completion", null);
            Finish(GitHubCrawlerTelemetry.OutcomeAbandoned, null);
        }

        private bool TryComplete(string outcome)
        {
            if (Interlocked.CompareExchange(ref _Completed, 1, 0) != 0) return false;
            _Outcome = outcome;
            return true;
        }

        private void Finish(string outcome, string? errorType)
        {
            CrawlerInstrumentation.OperationCompleted(_Operation, outcome, errorType, CrawlerInstrumentation.ElapsedSeconds(_StartTimestamp));

            try
            {
                _Activity?.Dispose();
            }
            catch
            {
            }
        }
    }
}
