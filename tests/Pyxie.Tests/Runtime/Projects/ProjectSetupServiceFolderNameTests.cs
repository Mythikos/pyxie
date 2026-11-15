using Pyxie.Runtime.Projects;

using Xunit;

namespace Pyxie.Tests.Runtime.Projects;

/// <summary>A project folder is named after its source, made safe as a folder name on every file system Pyxie runs on.</summary>
public sealed class ProjectSetupServiceFolderNameTests
{
	[Theory]
	[InlineData("C:\\work\\app", "app")]
	[InlineData("C:\\work\\app\\", "app")]
	[InlineData("https://example.com/team/app.git", "app")]
	[InlineData("git@example.com:team/app.git", "app")]
	[InlineData("https://example.com/team/release...", "release")]
	[InlineData("https://example.com/team/my app ", "my app")]
	[InlineData("https://example.com/team/con.git", "con-project")]
	[InlineData("https://example.com/team/LPT1.txt", "LPT1.txt-project")]
	[InlineData("https://example.com/team/...", "project")]
	[InlineData("///", "project")]
	public void FolderNameOf_MakesTheSourcesNameSafeAsAFolderName(string source, string expected)
	{
		Assert.Equal(expected, ProjectSetupService.FolderNameOf(source));
	}

	[Fact]
	public void FolderNameOf_ALongName_IsCutToSixtyFourCharacters()
	{
		string name = new('a', 100);

		Assert.Equal(new string('a', 64), ProjectSetupService.FolderNameOf($"https://example.com/team/{name}.git"));
	}
}
