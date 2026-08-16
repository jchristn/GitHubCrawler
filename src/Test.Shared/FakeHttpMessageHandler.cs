namespace Test.Shared
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net;
    using System.Net.Http;
    using System.Net.Http.Headers;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// A configurable <see cref="HttpMessageHandler"/> that returns caller-supplied responses without any network I/O.
    /// This allows the crawler's HTTP behavior (URL construction, recursion, status handling) to be tested deterministically.
    /// </summary>
    internal sealed class FakeHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        /// <summary>
        /// The absolute URIs, in order, that were requested through this handler.
        /// </summary>
        internal List<string> RequestedUris { get; } = new List<string>();

        /// <summary>
        /// Snapshots of the requests (URI, method, and headers), in order, that were sent through this handler.
        /// Header snapshots are taken at send time so they remain valid after the underlying request is disposed.
        /// </summary>
        internal List<CapturedRequest> Requests { get; } = new List<CapturedRequest>();

        /// <summary>
        /// Initializes a new instance with a responder that maps a request to a response.
        /// </summary>
        /// <param name="responder">Function that produces a response for a given request.</param>
        internal FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        {
            _responder = responder ?? throw new ArgumentNullException(nameof(responder));
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            RequestedUris.Add(request.RequestUri?.ToString());
            Requests.Add(CapturedRequest.From(request));

            HttpResponseMessage response = _responder(request);
            if (response.RequestMessage == null) response.RequestMessage = request;
            return Task.FromResult(response);
        }

        /// <summary>
        /// Builds a JSON response with the specified status code.
        /// </summary>
        internal static HttpResponseMessage Json(HttpStatusCode statusCode, string json)
        {
            HttpResponseMessage response = new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(json ?? string.Empty, Encoding.UTF8, "application/json")
            };
            return response;
        }

        /// <summary>
        /// Builds a binary response with the specified status code, bytes, content type, and optional response headers.
        /// </summary>
        internal static HttpResponseMessage Bytes(
            HttpStatusCode statusCode,
            byte[] content,
            string contentType,
            IReadOnlyDictionary<string, string> responseHeaders = null)
        {
            HttpResponseMessage response = new HttpResponseMessage(statusCode);
            ByteArrayContent byteContent = new ByteArrayContent(content ?? Array.Empty<byte>());

            if (!string.IsNullOrEmpty(contentType))
            {
                byteContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);
            }

            response.Content = byteContent;

            if (responseHeaders != null)
            {
                foreach (KeyValuePair<string, string> header in responseHeaders)
                {
                    response.Headers.TryAddWithoutValidation(header.Key, header.Value);
                }
            }

            return response;
        }
    }

    /// <summary>
    /// An immutable snapshot of an outgoing request: its URI, HTTP method, and merged request headers
    /// (which include any <see cref="System.Net.Http.HttpClient.DefaultRequestHeaders"/> such as User-Agent
    /// and Authorization that the client applies before the handler is invoked).
    /// </summary>
    internal sealed class CapturedRequest
    {
        private readonly Dictionary<string, string[]> _headers;

        private CapturedRequest(string uri, HttpMethod method, Dictionary<string, string[]> headers)
        {
            Uri = uri;
            Method = method;
            _headers = headers;
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
            return _headers.ContainsKey(name);
        }

        /// <summary>
        /// Returns the comma-joined values of the named header, or null if the header was not present.
        /// </summary>
        internal string Header(string name)
        {
            return _headers.TryGetValue(name, out string[] values) ? string.Join(", ", values) : null;
        }

        internal static CapturedRequest From(HttpRequestMessage request)
        {
            Dictionary<string, string[]> headers = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

            foreach (KeyValuePair<string, IEnumerable<string>> header in request.Headers)
            {
                headers[header.Key] = header.Value.ToArray();
            }

            return new CapturedRequest(request.RequestUri?.ToString(), request.Method, headers);
        }
    }
}
