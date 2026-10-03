namespace Test.Shared
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net.Http;

    /// <summary>
    /// An immutable snapshot of an outgoing request: its URI, HTTP method, and merged request headers
    /// (which include any <see cref="System.Net.Http.HttpClient.DefaultRequestHeaders"/> such as User-Agent
    /// and Authorization that the client applies before the handler is invoked).
    /// </summary>
    internal sealed class CapturedRequest
    {
        private readonly Dictionary<string, string[]> _Headers;

        private CapturedRequest(string uri, HttpMethod method, Dictionary<string, string[]> headers)
        {
            Uri = uri;
            Method = method;
            _Headers = headers;
        }

        /// <summary>
        /// The absolute request URI.
        /// </summary>
        internal string Uri { get; }

        /// <summary>
        /// The HTTP method.
        /// </summary>
        internal HttpMethod Method { get; }

        /// <summary>
        /// Returns true if a header with the given name (case-insensitive) was present on the request.
        /// </summary>
        internal bool HasHeader(string name)
        {
            return _Headers.ContainsKey(name);
        }

        /// <summary>
        /// Returns the values of the named header joined as they appear on the wire (space-separated product tokens for
        /// User-Agent, comma-separated otherwise), or null if the header was not present.
        /// </summary>
        internal string? Header(string name)
        {
            if (!_Headers.TryGetValue(name, out string[]? values)) return null;
            string separator = string.Equals(name, "User-Agent", StringComparison.OrdinalIgnoreCase) ? " " : ", ";
            return string.Join(separator, values);
        }

        internal static CapturedRequest From(HttpRequestMessage request)
        {
            Dictionary<string, string[]> headers = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

            foreach (KeyValuePair<string, IEnumerable<string>> header in request.Headers)
            {
                headers[header.Key] = header.Value.ToArray();
            }

            return new CapturedRequest(request.RequestUri?.ToString() ?? string.Empty, request.Method, headers);
        }
    }
}
