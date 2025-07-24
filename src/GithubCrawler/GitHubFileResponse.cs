using GetSomeInput;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace GitHubCrawler
{
    /// <summary>
    /// GitHub file response.
    /// </summary>
    public class GitHubFileResponse
    {
        /// <summary>
        /// Content.
        /// </summary>
        public byte[] Content { get; set; }

        /// <summary>
        /// Content type.
        /// </summary>
        public string ContentType { get; set; }

        /// <summary>
        /// Status code.
        /// </summary>
        public System.Net.HttpStatusCode StatusCode { get; set; }

        /// <summary>
        /// Headers.
        /// </summary>
        public Dictionary<string, IEnumerable<string>> Headers { get; set; }

        /// <summary>
        /// Final URL.
        /// </summary>
        public Uri FinalUrl { get; set; }

        /// <summary>
        /// GitHub file response.
        /// </summary>
        public GitHubFileResponse()
        {

        }
    }
}
