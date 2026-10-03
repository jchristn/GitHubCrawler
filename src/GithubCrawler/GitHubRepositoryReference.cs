namespace GitHubCrawler
{
    using System;

    internal sealed class GitHubRepositoryReference
    {
        private const string _HttpsPrefix = "https://github.com/";
        private const string _HttpPrefix = "http://github.com/";
        private const string _SshPrefix = "git@github.com:";

        internal string Owner { get; }

        internal string Repository { get; }

        private GitHubRepositoryReference(string owner, string repository)
        {
            Owner = owner;
            Repository = repository;
        }

        internal static GitHubRepositoryReference? Parse(string gitUrl)
        {
            if (string.IsNullOrWhiteSpace(gitUrl)) return null;

            if (gitUrl.EndsWith(".git", StringComparison.Ordinal))
            {
                gitUrl = gitUrl.Substring(0, gitUrl.Length - 4);
            }

            string remainder;

            if (gitUrl.StartsWith(_HttpsPrefix, StringComparison.Ordinal) || gitUrl.StartsWith(_HttpPrefix, StringComparison.Ordinal))
            {
                remainder = gitUrl.Replace(_HttpsPrefix, string.Empty).Replace(_HttpPrefix, string.Empty);
            }
            else if (gitUrl.StartsWith(_SshPrefix, StringComparison.Ordinal))
            {
                remainder = gitUrl.Replace(_SshPrefix, string.Empty);
            }
            else
            {
                return null;
            }

            string[] parts = remainder.Split('/');
            if (parts.Length < 2) return null;
            if (string.IsNullOrEmpty(parts[0]) || string.IsNullOrEmpty(parts[1])) return null;

            return new GitHubRepositoryReference(parts[0], parts[1]);
        }
    }
}
