namespace Test.Shared
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;

    /// <summary>
    /// Minimal, framework-agnostic assertion helpers so shared scenarios do not depend on xUnit or NUnit.
    /// Failures are surfaced as exceptions, which every runner (Touchstone CLI, xUnit, NUnit) reports as failures.
    /// </summary>
    internal static class TestAssert
    {
        internal static void True(bool condition, string message = null)
        {
            if (!condition) throw new InvalidOperationException(message ?? "Expected condition to be true.");
        }

        internal static void False(bool condition, string message = null)
        {
            if (condition) throw new InvalidOperationException(message ?? "Expected condition to be false.");
        }

        internal static void Equal<T>(T expected, T actual, string message = null)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
            {
                throw new InvalidOperationException(message ?? ("Expected '" + expected + "', got '" + actual + "'."));
            }
        }

        internal static void NotEqual<T>(T expected, T actual, string message = null)
        {
            if (EqualityComparer<T>.Default.Equals(expected, actual))
            {
                throw new InvalidOperationException(message ?? ("Did not expect '" + actual + "'."));
            }
        }

        internal static void Null(object value, string message = null)
        {
            if (value != null) throw new InvalidOperationException(message ?? "Expected value to be null.");
        }

        internal static void NotNull(object value, string message = null)
        {
            if (value == null) throw new InvalidOperationException(message ?? "Expected value to be non-null.");
        }

        internal static void Empty<T>(IEnumerable<T> values, string message = null)
        {
            if (values == null) throw new InvalidOperationException(message ?? "Expected an empty collection but it was null.");
            if (values.Any()) throw new InvalidOperationException(message ?? "Expected an empty collection.");
        }

        internal static void Single<T>(IEnumerable<T> values, string message = null)
        {
            if (values == null) throw new InvalidOperationException(message ?? "Expected a single value but collection was null.");

            int count = values.Count();
            if (count != 1)
            {
                throw new InvalidOperationException(message ?? ("Expected a single value but found " + count + "."));
            }
        }

        internal static void Count<T>(int expected, IEnumerable<T> values, string message = null)
        {
            if (values == null) throw new InvalidOperationException(message ?? "Expected a collection but it was null.");

            int count = values.Count();
            if (count != expected)
            {
                throw new InvalidOperationException(message ?? ("Expected " + expected + " item(s) but found " + count + "."));
            }
        }

        internal static void Contains(string haystack, string needle, string message = null)
        {
            if (haystack == null || needle == null || !haystack.Contains(needle))
            {
                throw new InvalidOperationException(message ?? ("Expected '" + haystack + "' to contain '" + needle + "'."));
            }
        }

        internal static void Contains<T>(IEnumerable<T> values, T expected, string message = null)
        {
            if (values == null || !values.Contains(expected))
            {
                throw new InvalidOperationException(message ?? ("Expected collection to contain '" + expected + "'."));
            }
        }

        internal static TException Throws<TException>(Action action) where TException : Exception
        {
            if (action == null) throw new ArgumentNullException(nameof(action));

            try
            {
                action();
            }
            catch (TException exception)
            {
                return exception;
            }

            throw new InvalidOperationException("Expected exception of type " + typeof(TException).Name + ".");
        }

        internal static async Task<TException> ThrowsAsync<TException>(Func<Task> action) where TException : Exception
        {
            if (action == null) throw new ArgumentNullException(nameof(action));

            try
            {
                await action().ConfigureAwait(false);
            }
            catch (TException exception)
            {
                return exception;
            }

            throw new InvalidOperationException("Expected exception of type " + typeof(TException).Name + ".");
        }
    }
}
