namespace Test.Shared
{
    using System;

    /// <summary>
    /// Marks a static, parameterless method as an automated test scenario and assigns it to a suite.
    /// Scenario methods must return <see cref="void"/> or <see cref="System.Threading.Tasks.Task"/>.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
    internal sealed class ScenarioAttribute : Attribute
    {
        /// <summary>
        /// The identifier of the suite this scenario belongs to.
        /// </summary>
        public string Suite { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="ScenarioAttribute"/> class.
        /// </summary>
        /// <param name="suite">The suite identifier.</param>
        public ScenarioAttribute(string suite)
        {
            Suite = suite;
        }
    }
}
