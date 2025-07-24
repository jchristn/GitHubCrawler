namespace Test
{
    using System;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;

    using GetSomeInput;
    using GitHubCrawler;

    public static class Program
    {
        public static async Task Main(string[] args)
        {
            string githubToken = Inputty.GetString("Github token :", null, true);
            string gitUrl = Inputty.GetString("Git URL      :", null, false);

            using (var cts = new CancellationTokenSource())
            {
                // Set up Ctrl+C handler
                Console.CancelKeyPress += (sender, e) =>
                {
                    e.Cancel = true;
                    cts.Cancel();
                    Console.WriteLine("\nCancellation requested...");
                };

                using (var crawler = new GitHubRepoCrawler(githubToken))
                {
                    try
                    {
                        Console.WriteLine("Crawling repository (press Ctrl+C to cancel)...\n");

                        var urls = crawler.GetRepositoryContentsAsync(gitUrl, cts.Token);

                        await foreach (var url in urls)
                        {
                            Console.WriteLine(url);
                        }

                        string filename = Inputty.GetString("Paste the URL of a file to retrieve it:", null, true);
                        if (String.IsNullOrEmpty(filename)) return;

                        Console.WriteLine("Downloading file (press Ctrl+C to cancel)...");
                        GitHubFileResponse file = await crawler.GetFileContentsAsync(filename, cts.Token);
                        Console.WriteLine(Encoding.UTF8.GetString(file.Content));
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