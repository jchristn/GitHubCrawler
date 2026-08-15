namespace Test.NUnit
{
    using System.Collections;
    using System.Threading;
    using System.Threading.Tasks;

    using global::NUnit.Framework;
    using Test.Shared;
    using Touchstone.Core;
    using Touchstone.NunitAdapter;

    /// <summary>
    /// NUnit host that surfaces every shared Touchstone descriptor as an individual test case,
    /// so the shared suite runs under <c>dotnet test</c> via NUnit.
    /// </summary>
    [NonParallelizable]
    public sealed class GitHubCrawlerNUnitTests
    {
        public static IEnumerable TestCases()
        {
            return new TouchstoneTestCaseSource(GitHubCrawlerSuites.All);
        }

        [TestCaseSource(nameof(TestCases))]
        public async Task Run(TestCaseDescriptor testCase)
        {
            await testCase.ExecuteAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }
}
