namespace Test
{
    using System;
    using System.Collections.Generic;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;

    using GetSomeInput;
    using GitHubCrawler;

    /// <summary>
    /// Interactive console that crawls a repository and optionally downloads one file.
    /// </summary>
    public static class Program
    {
        /// <summary>
        /// Entry point.
        /// </summary>
        /// <param name="args">Command line arguments (unused).</param>
        /// <returns>A task that completes when the session ends.</returns>
        public static async Task Main(string[] args)
        {
            string githubToken = Inputty.GetString("Github token :", null, true);
            string gitUrl = Inputty.GetString("Git URL      :", null, false);

            using (CancellationTokenSource cts = new CancellationTokenSource())
            {
                // Set up Ctrl+C handler
                Console.CancelKeyPress += (sender, e) =>
                {
                    e.Cancel = true;
                    cts.Cancel();
                    Console.WriteLine("\nCancellation requested...");
                };

                using (GitHubRepoCrawler crawler = new GitHubRepoCrawler(githubToken))
                {
                    try
                    {
                        Console.WriteLine("Crawling repository (press Ctrl+C to cancel)...\n");

                        IAsyncEnumerable<string> urls = crawler.GetRepositoryContentsAsync(gitUrl, cts.Token);

                        await foreach (string url in urls.ConfigureAwait(false))
                        {
                            Console.WriteLine(url);
                        }

                        string filename = Inputty.GetString("Paste the URL of a file to retrieve it:", null, true);
                        if (String.IsNullOrEmpty(filename)) return;

                        Console.WriteLine("Downloading file (press Ctrl+C to cancel)...");
                        GitHubFileResponse file = await crawler.GetFileContentsAsync(filename, cts.Token).ConfigureAwait(false);
                        Console.WriteLine(Encoding.UTF8.GetString(file.Content ?? Array.Empty<byte>()));
                    }
                    catch (TaskCanceledException)
                    {
                        Console.WriteLine("\nTask was cancelled.");
                    }
                    catch (OperationCanceledException)
                    {
                        Console.WriteLine("\nOperation was cancelled.");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error: {ex.Message}");
                    }
                }
            }
        }
    }
}