using Pyxie.Data.Entities;
using Pyxie.Data.Enums;
using Pyxie.Web.Mcp;

using Xunit;

namespace Pyxie.Tests.Web;

public sealed class McpRepositoryTests
{
	[Theory]
	[InlineData("https://token@example.org/team/repo.git", "https://example.org/team/repo.git")]
	[InlineData("https://user:password@example.org/repo.git?signed=temporary#fragment", "https://example.org/repo.git")]
	[InlineData("ssh://git:password@example.org/repo.git", "ssh://git@example.org/repo.git")]
	[InlineData("/work/repo#2", "/work/repo#2")]
	public void OldRepositoryOriginsAreRedactedBeforeReturningMetadata(string stored, string display)
	{
		Repository repository = new() { Source = RepositorySource.Url, Origin = stored, Path = "/workspace/projects/demo/hub", TargetBranch = "main" };
		McpRepository result = McpRepository.From(27, repository);
		Assert.Equal(display, result.Origin);
		Assert.Equal(27, result.ProjectId);
		Assert.Equal(stored == display ? repository.Path : null, result.Hub);
	}

	[Theory]
	[InlineData("https://example.org/demo.git?signed=private-value", "demo.git-signed=private-value", "demo")]
	[InlineData("https://example.org/demo.git#private-value", "demo.git#private-value", "demo")]
	[InlineData("https://example.org", "private-value@example.org", "example.org")]
	public void OldCatalogNamesAndPathsDerivedFromCredentialsAreWithheld(string stored, string folderName, string displayName)
	{
		Project project = new() { Id = 42, Name = folderName, Source = stored, Folder = Path.Combine("workspace", "projects", folderName), IsOpen = true };
		McpProject result = McpProject.From(project);

		Assert.Equal(42, result.Id);
		Assert.Equal(displayName, result.Name);
		Assert.DoesNotContain("private-value", result.Source, StringComparison.Ordinal);
		Assert.Null(result.Folder);
	}
}
