namespace Test.Shared
{
    using System;
    using System.Linq;

    /// <summary>
    /// Helpers for building GitHub Contents API JSON payloads used by scenarios.
    /// </summary>
    internal static class GitHubJson
    {
        /// <summary>
        /// Wraps raw item JSON fragments into a JSON array.
        /// </summary>
        internal static string Array(params string[] items)
        {
            return "[" + string.Join(",", items ?? System.Array.Empty<string>()) + "]";
        }

        /// <summary>
        /// Builds a JSON object representing a file entry (with a non-null download_url).
        /// </summary>
        internal static string File(string name, string path, string downloadUrl)
        {
            return "{"
                + "\"name\":" + Quote(name) + ","
                + "\"path\":" + Quote(path) + ","
                + "\"type\":\"file\","
                + "\"html_url\":" + Quote("https://github.com/owner/repo/blob/main/" + path) + ","
                + "\"download_url\":" + Quote(downloadUrl)
                + "}";
        }

        /// <summary>
        /// Builds a JSON object representing a directory entry (with a null download_url).
        /// </summary>
        internal static string Directory(string name, string path)
        {
            return "{"
                + "\"name\":" + Quote(name) + ","
                + "\"path\":" + Quote(path) + ","
                + "\"type\":\"dir\","
                + "\"html_url\":" + Quote("https://github.com/owner/repo/tree/main/" + path) + ","
                + "\"download_url\":null"
                + "}";
        }

        private static string Quote(string value)
        {
            if (value == null) return "null";
            return "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        }
    }
}
