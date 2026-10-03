namespace Test.Shared
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Diagnostics.Metrics;
    using System.Linq;

    using GitHubCrawler;

    /// <summary>
    /// In-memory collector for GitHubCrawler telemetry. Subscribes a <see cref="MeterListener"/> to the
    /// GitHubCrawler meter and an <see cref="ActivityListener"/> to the GitHubCrawler activity source (and the
    /// test parent source), and records every measurement and completed span for assertions.
    /// Scenarios run sequentially in every runner, so a capture sees only the work of its own scenario.
    /// </summary>
    internal sealed class TelemetryCapture : IDisposable
    {
        internal const string TestSourceName = "Test.GitHubCrawler";

        internal static readonly ActivitySource TestSource = new ActivitySource(TestSourceName);

        private readonly object _Lock = new object();
        private readonly MeterListener _MeterListener;
        private readonly ActivityListener _ActivityListener;
        private readonly List<CapturedMeasurement> _Measurements = new List<CapturedMeasurement>();
        private readonly List<Activity> _Activities = new List<Activity>();

        internal TelemetryCapture()
        {
            _MeterListener = new MeterListener();
            _MeterListener.InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == GitHubCrawlerTelemetry.MeterName) listener.EnableMeasurementEvents(instrument);
            };
            _MeterListener.SetMeasurementEventCallback<long>((i, v, t, s) => Add(i, v, t));
            _MeterListener.SetMeasurementEventCallback<int>((i, v, t, s) => Add(i, v, t));
            _MeterListener.SetMeasurementEventCallback<double>((i, v, t, s) => Add(i, v, t));
            _MeterListener.Start();

            _ActivityListener = new ActivityListener
            {
                ShouldListenTo = source => source.Name == GitHubCrawlerTelemetry.ActivitySourceName || source.Name == TestSourceName,
                Sample = (ref ActivityCreationOptions<ActivityContext> options) => ActivitySamplingResult.AllDataAndRecorded,
                ActivityStopped = activity =>
                {
                    lock (_Lock) _Activities.Add(activity);
                }
            };
            ActivitySource.AddActivityListener(_ActivityListener);
        }

        internal void CollectObservables()
        {
            _MeterListener.RecordObservableInstruments();
        }

        internal List<CapturedMeasurement> Measurements(string instrument)
        {
            lock (_Lock) return _Measurements.Where(m => m.Instrument == instrument).ToList();
        }

        internal List<CapturedMeasurement> Measurements(string instrument, params string[] tagPairs)
        {
            return Measurements(instrument).Where(m => m.Matches(tagPairs)).ToList();
        }

        internal double Sum(string instrument, params string[] tagPairs)
        {
            return Measurements(instrument, tagPairs).Sum(m => m.Value);
        }

        internal List<Activity> Activities(string name)
        {
            lock (_Lock) return _Activities.Where(a => a.OperationName == name).ToList();
        }

        internal Activity SingleActivity(string name)
        {
            List<Activity> matches = Activities(name);
            TestAssert.Single(matches, "Expected exactly one span named '" + name + "' but found " + matches.Count + ".");
            return matches[0];
        }

        public void Dispose()
        {
            _MeterListener.Dispose();
            _ActivityListener.Dispose();
        }

        private void Add(Instrument instrument, double value, ReadOnlySpan<KeyValuePair<string, object?>> tags)
        {
            Dictionary<string, string> copy = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, object?> tag in tags) copy[tag.Key] = Convert.ToString(tag.Value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;

            lock (_Lock) _Measurements.Add(new CapturedMeasurement(instrument.Name, instrument.Unit, value, copy));
        }
    }
}
