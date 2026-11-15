using Xunit;

namespace Pyxie.Tests.Runtime.Processes;

/// <summary>The collection of tests that change <c>PATH</c> or <c>PATHEXT</c>. xUnit runs it after the parallel collections and never beside another test, so no other test can see the changed environment or lose the program it looks up on <c>PATH</c>.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ProcessEnvironmentCollection
{
	/// <summary>The collection name a test class names in its <c>Collection</c> attribute.</summary>
	public const string Name = "Process environment";
}
