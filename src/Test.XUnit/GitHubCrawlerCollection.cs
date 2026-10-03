namespace Test.XUnit
{
    using Xunit;

    /// <summary>
    /// xUnit collection that disables parallel execution, so telemetry and HTTP scenarios never overlap.
    /// </summary>
    [CollectionDefinition("GitHubCrawler", DisableParallelization = true)]
    public sealed class GitHubCrawlerCollection
    {
    }
}
