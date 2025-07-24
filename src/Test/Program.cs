namespace Test
{
    using System;
    using System.Text;
    using System.Threading.Tasks;

    using GetSomeInput;
    using GitHubCrawler;

    public static class Program
    {
        public static async Task Main(string[] args)
        {
            string githubToken = Inputty.GetString("Github token :", null, true);
            string gitUrl      = Inputty.GetString("Git URL      :", null, false);
            var crawler = new GitHubRepoCrawler(githubToken);

            try
            {
                var urls = crawler.GetRepositoryContentsAsync(gitUrl);

                await foreach (var url in urls)
                {
                    Console.WriteLine(url);
                }

                string filename = Inputty.GetString("Paste the URL of a file to retrieve it:", null, true);
                if (String.IsNullOrEmpty(filename)) return;

                GitHubFileResponse file = await crawler.GetFileContentsAsync(filename);
                Console.WriteLine(Encoding.UTF8.GetString(file.Content));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
            }
        }
    }
}