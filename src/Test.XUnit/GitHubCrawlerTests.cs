namespace Test.XUnit
{
    using System.Threading;
    using System.Threading.Tasks;

    using Test.Shared;
    using Touchstone.Core;
    using Xunit;

    /// <summary>
    /// xUnit host that surfaces every shared Touchstone descriptor as an individual theory case,
    /// so the shared suite runs under <c>dotnet test</c> via xUnit.
    /// </summary>
    [CollectionDefinition("GitHubCrawler", DisableParallelization = true)]
    public sealed class GitHubCrawlerCollection
    {
    }

    [Collection("GitHubCrawler")]
    public sealed class GitHubCrawlerTests
    {
        public static TheoryData<TestCaseDescriptor> TestCases()
        {
            TheoryData<TestCaseDescriptor> data = new TheoryData<TestCaseDescriptor>();

            foreach (TestSuiteDescriptor suite in GitHubCrawlerSuites.All)
            {
                foreach (TestCaseDescriptor testCase in suite.Cases)
                {
                    if (!testCase.Skip) data.Add(testCase);
                }
            }

            return data;
        }

        [Theory]
        [MemberData(nameof(TestCases))]
        public async Task Run(TestCaseDescriptor testCase)
        {
            await testCase.ExecuteAsync(CancellationToken.None);
        }
    }
}
