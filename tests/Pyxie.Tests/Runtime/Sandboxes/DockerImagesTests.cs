using Pyxie.Data.Sandboxes;
using Pyxie.Runtime.Sandboxes;

using Xunit;

namespace Pyxie.Tests.Runtime.Sandboxes;

/// <summary>The tags <see cref="DockerImages"/> builds images under: the base image is tagged with the hash of the files it is built from, and a project's image with a hash of the base tag and its Dockerfile, so a changed Dockerfile gets a new image and an unchanged one, whatever its line endings, is never built twice.</summary>
public sealed class DockerImagesTests
{
	private static readonly string s_key = SandboxNames.ProjectKey(Path.Combine(Path.GetTempPath(), "pyxie-images", "projects", "shop"));

	[Fact]
	public void BaseTag_IsTheBaseImageTaggedWithTheHashOfItsFiles()
	{
		Assert.Equal($"{SandboxNames.BaseImage}:{SandboxImageFiles.Hash}", DockerImages.BaseTag);
	}

	[Fact]
	public void ProjectTag_Dockerfile_IsTheProjectsImageWithASixteenCharacterTag()
	{
		Assert.Matches($"^{SandboxNames.ProjectImage(s_key)}:[0-9a-f]{{16}}$", DockerImages.ProjectTag(s_key, SandboxDefaults.Dockerfile));
	}

	[Fact]
	public void ProjectTag_SameDockerfile_IsTheSameTag()
	{
		Assert.Equal(DockerImages.ProjectTag(s_key, SandboxDefaults.Dockerfile), DockerImages.ProjectTag(s_key, SandboxDefaults.Dockerfile));
	}

	[Fact]
	public void ProjectTag_DockerfileWithWindowsLineEndings_IsTheSameTag()
	{
		string unix = "ARG PYXIE_BASE\nFROM ${PYXIE_BASE}\nRUN echo one\n";

		Assert.Equal(DockerImages.ProjectTag(s_key, unix), DockerImages.ProjectTag(s_key, unix.Replace("\n", "\r\n", StringComparison.Ordinal)));
	}

	[Fact]
	public void ProjectTag_ChangedDockerfile_GetsANewTag()
	{
		string before = SandboxDefaults.Dockerfile;
		string after = SandboxDefaults.Dockerfile + "RUN echo one more tool\n";

		Assert.NotEqual(DockerImages.ProjectTag(s_key, before), DockerImages.ProjectTag(s_key, after));
	}

	[Fact]
	public void ProjectTag_AnotherProject_GetsAnImageOfItsOwn()
	{
		string other = SandboxNames.ProjectKey(Path.Combine(Path.GetTempPath(), "pyxie-images", "projects", "blog"));

		string shopImage = DockerImages.ProjectTag(s_key, SandboxDefaults.Dockerfile).Split(':')[0];
		string blogImage = DockerImages.ProjectTag(other, SandboxDefaults.Dockerfile).Split(':')[0];

		Assert.NotEqual(shopImage, blogImage);
	}
}
