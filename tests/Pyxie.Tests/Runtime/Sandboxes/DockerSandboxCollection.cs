using Xunit;

namespace Pyxie.Tests.Runtime.Sandboxes;

/// <summary>The collection of tests that drive a real container engine. They share one image (<see cref="DockerImageFixture"/>) and run one at a time, so they never compete for the engine; each names its containers and volumes with a project key of its own and removes them when it ends (see <see cref="DockerSandboxRig"/>).</summary>
[CollectionDefinition(Name)]
public sealed class DockerSandboxCollection : ICollectionFixture<DockerImageFixture>
{
	/// <summary>The collection name a test class names in its <c>Collection</c> attribute.</summary>
	public const string Name = "Docker sandboxes";
}
