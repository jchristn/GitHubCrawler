namespace Test.Shared
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Reflection;
    using System.Threading.Tasks;

    using Touchstone.Core;

    /// <summary>
    /// Builds the Touchstone <see cref="TestSuiteDescriptor"/> collection from the scenario methods declared on
    /// <see cref="GitHubCrawlerScenarios"/>. This is the shared entry point consumed by every runner.
    /// </summary>
    public static class GitHubCrawlerSuites
    {
        private static readonly IReadOnlyList<TestSuiteDescriptor> _All = BuildSuites();

        /// <summary>
        /// All test suites, ordered by suite identifier.
        /// </summary>
        public static IReadOnlyList<TestSuiteDescriptor> All
        {
            get { return _All; }
        }

        private static IReadOnlyList<TestSuiteDescriptor> BuildSuites()
        {
            List<MethodInfo> methods = typeof(GitHubCrawlerScenarios)
                .GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Where(m => m.GetParameters().Length == 0)
                .Where(m => m.ReturnType == typeof(void) || m.ReturnType == typeof(Task))
                .Where(m => m.GetCustomAttribute<ScenarioAttribute>() != null)
                .ToList();

            List<TestSuiteDescriptor> suites = methods
                .GroupBy(m => m.GetCustomAttribute<ScenarioAttribute>().Suite, StringComparer.Ordinal)
                .OrderBy(g => g.Key, StringComparer.Ordinal)
                .Select(g => new TestSuiteDescriptor(
                    g.Key,
                    ToDisplayName(g.Key),
                    g.OrderBy(m => m.Name, StringComparer.Ordinal)
                     .Select(m => CreateCase(g.Key, m))
                     .ToList()))
                .ToList();

            return suites;
        }

        private static TestCaseDescriptor CreateCase(string suiteId, MethodInfo method)
        {
            return new TestCaseDescriptor(
                suiteId,
                method.Name,
                ToDisplayName(method.Name),
                token =>
                {
                    try
                    {
                        object result = method.Invoke(null, null);
                        if (result is Task task) return task;
                        return Task.CompletedTask;
                    }
                    catch (TargetInvocationException e) when (e.InnerException != null)
                    {
                        return Task.FromException(e.InnerException);
                    }
                });
        }

        private static string ToDisplayName(string name)
        {
            if (string.IsNullOrEmpty(name)) return name;

            List<char> chars = new List<char>(name.Length + 8);

            for (int i = 0; i < name.Length; i++)
            {
                char current = name[i];

                if (current == '_' || current == '-')
                {
                    chars.Add(' ');
                    continue;
                }

                if (i > 0)
                {
                    char previous = name[i - 1];
                    if (char.IsUpper(current) && !char.IsUpper(previous) && previous != '_' && previous != '-')
                    {
                        chars.Add(' ');
                    }
                }

                chars.Add(current);
            }

            return new string(chars.ToArray());
        }
    }
}
