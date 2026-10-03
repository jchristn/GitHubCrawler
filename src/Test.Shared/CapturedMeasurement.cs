namespace Test.Shared
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// A single metric measurement captured by <see cref="TelemetryCapture"/>.
    /// </summary>
    internal sealed class CapturedMeasurement
    {
        internal CapturedMeasurement(string instrument, string? unit, double value, Dictionary<string, string> tags)
        {
            Instrument = instrument;
            Unit = unit;
            Value = value;
            Tags = tags;
        }

        internal string Instrument { get; }

        internal string? Unit { get; }

        internal double Value { get; }

        internal Dictionary<string, string> Tags { get; }

        internal string? Tag(string key)
        {
            return Tags.TryGetValue(key, out string? value) ? value : null;
        }

        /// <summary>
        /// Returns true when every key/value pair in <paramref name="tagPairs"/> (alternating key, value) matches.
        /// A null expected value asserts the tag is absent.
        /// </summary>
        internal bool Matches(string?[]? tagPairs)
        {
            if (tagPairs == null) return true;
            if (tagPairs.Length % 2 != 0) throw new ArgumentException("Tag pairs must be key/value pairs.", nameof(tagPairs));

            for (int i = 0; i < tagPairs.Length; i += 2)
            {
                if (!string.Equals(Tag(tagPairs[i] ?? string.Empty), tagPairs[i + 1], StringComparison.Ordinal)) return false;
            }

            return true;
        }
    }
}
