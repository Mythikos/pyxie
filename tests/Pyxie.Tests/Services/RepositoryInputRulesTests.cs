using Pyxie.Data.Enums;
using Pyxie.Data.Limits;
using Pyxie.Data.Services.Inputs;
using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Services;

public sealed class RepositoryInputRulesTests
{
	private const RepositorySource UndefinedSource = (RepositorySource)2;

	[Theory]
	[InlineData(RepositorySource.Folder)]
	[InlineData(RepositorySource.Url)]
	public void ValidateSource_AcceptsEveryDefinedSource(RepositorySource source)
	{
		Assert.Null(RepositoryInputRules.ValidateSource(source));
	}

	[Theory]
	[InlineData(UndefinedSource)]
	[InlineData((RepositorySource)(-1))]
	[InlineData((RepositorySource)int.MaxValue)]
	public void ValidateSource_RejectsAValueThatIsNotASource(RepositorySource source)
	{
		Assert.Equal("Source is not recognized.", RepositoryInputRules.ValidateSource(source));
	}

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("   ")]
	[InlineData("\t\r\n")]
	public void ValidatePath_RejectsMissingOrBlankPath(string? path)
	{
		Assert.Equal("Path is required.", RepositoryInputRules.ValidatePath(path));
	}

	[Theory]
	[InlineData("repo")]
	[InlineData("./repo")]
	[InlineData("../repo")]
	[InlineData("src/app")]
	[InlineData("C:repo")]
	[InlineData("\\repo")]
	[InlineData("..")]
	[InlineData(".")]
	public void ValidatePath_RejectsAPathThatIsNotAbsolute(string path)
	{
		Assert.Equal("Path must be an absolute path.", RepositoryInputRules.ValidatePath(path));
	}

	[Fact]
	public void ValidatePath_AcceptsAnAbsolutePath_AfterTrimming()
	{
		string path = RepositoryTestInput.PathFor("app");

		Assert.Null(RepositoryInputRules.ValidatePath(path));
		Assert.Null(RepositoryInputRules.ValidatePath($"  {path}\t"));
	}

	[Fact]
	public void ValidatePath_AcceptsPathAtLimit_AndRejectsOneOver()
	{
		string root = Path.GetTempPath();
		string atLimit = root + new string('p', PyxieLimits.MaxPathLength - root.Length);

		Assert.Equal(PyxieLimits.MaxPathLength, atLimit.Length);
		Assert.Null(RepositoryInputRules.ValidatePath(atLimit));
		Assert.Equal("Path must be 1000 characters or fewer.", RepositoryInputRules.ValidatePath(atLimit + "p"));
	}

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("   ")]
	[InlineData("\t\r\n")]
	public void ValidateOrigin_RejectsMissingOrBlankFolder(string? origin)
	{
		Assert.Equal("Folder is required.", RepositoryInputRules.ValidateOrigin(RepositorySource.Folder, origin));
	}

	[Theory]
	[InlineData("repo")]
	[InlineData("./repo")]
	[InlineData("../repo")]
	[InlineData("src/app")]
	[InlineData("C:repo")]
	[InlineData("\\repo")]
	[InlineData("..")]
	[InlineData(".")]
	[InlineData("https://example.com/team/app.git")]
	public void ValidateOrigin_RejectsAFolderThatIsNotAFullPath(string origin)
	{
		Assert.Equal("Folder must be a full path.", RepositoryInputRules.ValidateOrigin(RepositorySource.Folder, origin));
	}

	[Fact]
	public void ValidateOrigin_AcceptsAFullFolderPath_AfterTrimming()
	{
		string folder = RepositoryTestInput.PathFor("app");

		Assert.Null(RepositoryInputRules.ValidateOrigin(RepositorySource.Folder, folder));
		Assert.Null(RepositoryInputRules.ValidateOrigin(RepositorySource.Folder, $"  {folder}\t"));
	}

	[Fact]
	public void ValidateOrigin_AcceptsAFolderAtTheLimit_AndRejectsOneOver()
	{
		string root = Path.GetTempPath();
		string atLimit = root + new string('f', PyxieLimits.MaxOriginLength - root.Length);

		Assert.Equal(PyxieLimits.MaxOriginLength, atLimit.Length);
		Assert.Null(RepositoryInputRules.ValidateOrigin(RepositorySource.Folder, $"  {atLimit}  "));
		Assert.Equal("Folder must be 2000 characters or fewer.", RepositoryInputRules.ValidateOrigin(RepositorySource.Folder, atLimit + "f"));
	}

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("   ")]
	[InlineData("\t\r\n")]
	public void ValidateOrigin_RejectsMissingOrBlankUrl(string? origin)
	{
		Assert.Equal("URL is required.", RepositoryInputRules.ValidateOrigin(RepositorySource.Url, origin));
	}

	[Theory]
	[InlineData("https://example.com/team/app.git")]
	[InlineData("git@example.com:team/app.git")]
	[InlineData("ssh://git@example.com/team/app.git")]
	public void ValidateOrigin_AcceptsAnyUrlText_AfterTrimming(string url)
	{
		Assert.Null(RepositoryInputRules.ValidateOrigin(RepositorySource.Url, url));
		Assert.Null(RepositoryInputRules.ValidateOrigin(RepositorySource.Url, $"  {url}  "));
	}

	[Fact]
	public void ValidateOrigin_AcceptsAUrlAtTheLimit_AndRejectsOneOver()
	{
		Assert.Null(RepositoryInputRules.ValidateOrigin(RepositorySource.Url, $"  {new string('u', PyxieLimits.MaxOriginLength)}  "));
		Assert.Equal("URL must be 2000 characters or fewer.", RepositoryInputRules.ValidateOrigin(RepositorySource.Url, new string('u', PyxieLimits.MaxOriginLength + 1)));
	}

	[Fact]
	public void ValidateOrigin_LeavesAnUndefinedSourceToValidateSource()
	{
		Assert.Null(RepositoryInputRules.ValidateOrigin(UndefinedSource, null));
		Assert.Null(RepositoryInputRules.ValidateOrigin(UndefinedSource, "relative"));
		Assert.Null(RepositoryInputRules.ValidateOrigin(UndefinedSource, new string('u', PyxieLimits.MaxOriginLength + 1)));
	}

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("   ")]
	[InlineData("\t\r\n")]
	public void ValidateTargetBranch_RejectsMissingOrBlankBranch(string? targetBranch)
	{
		Assert.Equal("Target branch is required.", RepositoryInputRules.ValidateTargetBranch(targetBranch));
	}

	[Fact]
	public void ValidateTargetBranch_AcceptsBranchAtLimitAfterTrimming_AndRejectsOneOver()
	{
		Assert.Null(RepositoryInputRules.ValidateTargetBranch($"  {new string('b', PyxieLimits.MaxBranchNameLength)}  "));
		Assert.Equal("Target branch must be 250 characters or fewer.", RepositoryInputRules.ValidateTargetBranch(new string('b', PyxieLimits.MaxBranchNameLength + 1)));
	}

	[Theory]
	[InlineData("main")]
	[InlineData("  main  ")]
	[InlineData("develop")]
	[InlineData("release/1.0")]
	[InlineData("feature/add-gates_page")]
	public void ValidateTargetBranch_AcceptsOrdinaryBranchNames(string targetBranch)
	{
		Assert.Null(RepositoryInputRules.ValidateTargetBranch(targetBranch));
	}

	[Theory]
	[InlineData("my branch")]
	[InlineData("my\tbranch")]
	[InlineData("my\nbranch")]
	[InlineData("my\r\nbranch")]
	[InlineData("my branch")]
	[InlineData("feature/two words")]
	public void ValidateTargetBranch_RejectsWhitespaceInsideTheName(string targetBranch)
	{
		Assert.Equal("Target branch cannot contain whitespace.", RepositoryInputRules.ValidateTargetBranch(targetBranch));
	}
}
