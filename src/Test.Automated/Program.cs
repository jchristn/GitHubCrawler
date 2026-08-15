namespace Test.Automated
{
    using System;
    using System.IO;
    using System.Threading.Tasks;

    using Test.Shared;
    using Touchstone.Cli;

    /// <summary>
    /// Touchstone CLI runner. Executes every shared GitHubCrawler suite and, optionally, writes a JSON results file.
    /// Usage: dotnet run --project src/Test.Automated -- [--results &lt;path&gt;]
    /// </summary>
    internal static class Program
    {
        private static async Task<int> Main(string[] args)
        {
            string resultsPath = null;

            for (int i = 0; i < args.Length; i++)
            {
                if (string.Equals(args[i], "--results", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    resultsPath = args[i + 1];
                    string resultsDirectory = Path.GetDirectoryName(resultsPath);
                    if (!string.IsNullOrEmpty(resultsDirectory))
                    {
                        Directory.CreateDirectory(resultsDirectory);
                    }

                    i++;
                }
            }

            return await ConsoleRunner.RunAsync(GitHubCrawlerSuites.All, resultsPath: resultsPath).ConfigureAwait(false);
        }
    }
}
