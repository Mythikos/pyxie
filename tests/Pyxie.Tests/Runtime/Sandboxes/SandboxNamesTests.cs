using Pyxie.Runtime.Sandboxes;

using Xunit;

namespace Pyxie.Tests.Runtime.Sandboxes;

/// <summary><see cref="SandboxNames"/>: a project's key is a stable hash of its folder, and every container and volume name carries the key, the card, and what it is for, so two projects, two workspace roots, two cards, or two roles never share one.</summary>
public sealed class SandboxNamesTests
{
	// What Docker accepts as a container or volume name.
	private const string DockerNamePattern = "^[a-zA-Z0-9][a-zA-Z0-9_.-]+$";

	// What Docker accepts as an image repository name without a registry: lowercase only.
	private const string RepositoryNamePattern = "^[a-z0-9]+([._-][a-z0-9]+)*$";

	private static readonly string s_projectsFolder = Path.Combine(Path.GetTempPath(), "pyxie-names", "projects");

	[Fact]
	public void ProjectKey_SameFolder_IsTheSameTwelveHexadecimalCharacters()
	{
		string folder = Path.Combine(s_projectsFolder, "shop");

		string key = SandboxNames.ProjectKey(folder);

		Assert.Matches("^[0-9a-f]{12}$", key);
		Assert.Equal(key, SandboxNames.ProjectKey(folder));
	}

	[Fact]
	public void ProjectKey_TrailingSeparator_IsIgnored()
	{
		string folder = Path.Combine(s_projectsFolder, "shop");

		Assert.Equal(SandboxNames.ProjectKey(folder), SandboxNames.ProjectKey(folder + Path.DirectorySeparatorChar));
		Assert.Equal(SandboxNames.ProjectKey(folder), SandboxNames.ProjectKey(folder + Path.AltDirectorySeparatorChar));
	}

	[Fact]
	public void ProjectKey_RelativeFolder_IsTheKeyOfItsFullPath()
	{
		Assert.Equal(SandboxNames.ProjectKey(Path.GetFullPath("shop")), SandboxNames.ProjectKey("shop"));
	}

	[Fact]
	public void ProjectKey_FoldersThatDifferOnlyInCase_ShareAKeyOnlyOnWindows()
	{
		string lower = SandboxNames.ProjectKey(Path.Combine(s_projectsFolder, "shop"));
		string upper = SandboxNames.ProjectKey(Path.Combine(s_projectsFolder, "SHOP"));

		Assert.True(OperatingSystem.IsWindows() == (lower == upper), OperatingSystem.IsWindows() ? "Windows folders are case-insensitive, so SHOP and shop are one project and must share their containers." : "Folders are case-sensitive here, so SHOP and shop are two projects and must not share containers.");
	}

	[Fact]
	public void ProjectKey_DifferentFoldersOrWorkspaceRoots_GetDifferentKeys()
	{
		string shop = SandboxNames.ProjectKey(Path.Combine(s_projectsFolder, "shop"));
		string blog = SandboxNames.ProjectKey(Path.Combine(s_projectsFolder, "blog"));
		string shopUnderAnotherRoot = SandboxNames.ProjectKey(Path.Combine(Path.GetTempPath(), "pyxie-names-other", "projects", "shop"));

		Assert.NotEqual(shop, blog);
		Assert.True(shop != shopUnderAnotherRoot, "A project with the same name under a second workspace root must not share the first one's containers.");
	}

	[Fact]
	public void Container_EachRole_GetsANameOfItsOwnWithTheKeyAndCard()
	{
		string key = SandboxNames.ProjectKey(Path.Combine(s_projectsFolder, "shop"));

		string[] names = [.. Enum.GetValues<SandboxRole>().Select(role => SandboxNames.Container(key, 7, role))];

		Assert.Equal(names.Length, names.Distinct(StringComparer.Ordinal).Count());
		foreach (SandboxRole role in Enum.GetValues<SandboxRole>())
		{
			string name = SandboxNames.Container(key, 7, role);
			Assert.Equal($"pyxie-{key}-card-7-{SandboxNames.RoleName(role)}", name);
			Assert.Matches(DockerNamePattern, name);
		}
	}

	[Fact]
	public void Container_DifferentCardsOrProjects_GetDifferentNames()
	{
		string shop = SandboxNames.ProjectKey(Path.Combine(s_projectsFolder, "shop"));
		string blog = SandboxNames.ProjectKey(Path.Combine(s_projectsFolder, "blog"));

		string[] names =
		[
			SandboxNames.Container(shop, 1, SandboxRole.Worker),
			SandboxNames.Container(shop, 2, SandboxRole.Worker),
			SandboxNames.Container(shop, 12, SandboxRole.Worker),
			SandboxNames.Container(blog, 1, SandboxRole.Worker),
		];

		Assert.Equal(names.Length, names.Distinct(StringComparer.Ordinal).Count());
	}

	[Fact]
	public void Volume_EachPurpose_GetsANameOfItsOwnWithTheKeyAndCard()
	{
		string key = SandboxNames.ProjectKey(Path.Combine(s_projectsFolder, "shop"));

		string[] names = [SandboxNames.Volume(key, 7, "work"), SandboxNames.Volume(key, 7, "home"), SandboxNames.Volume(key, 7, "gates-home"), SandboxNames.Volume(key, 8, "work")];

		Assert.Equal(names.Length, names.Distinct(StringComparer.Ordinal).Count());
		Assert.All(names, name => Assert.StartsWith($"pyxie-{key}-card-", name, StringComparison.Ordinal));
		Assert.All(names, name => Assert.Matches(DockerNamePattern, name));
	}

	[Theory]
	[InlineData(SandboxRole.Worker, "work")]
	[InlineData(SandboxRole.Gates, "gates")]
	[InlineData(SandboxRole.Review, "review")]
	[InlineData(SandboxRole.Resolve, "resolve")]
	public void RoleName_EachRole_IsTheWordItIsLabelledWith(SandboxRole role, string expected)
	{
		Assert.Equal(expected, SandboxNames.RoleName(role));
	}

	[Fact]
	public void RoleName_UnknownRole_Throws()
	{
		Assert.Throws<ArgumentOutOfRangeException>(() => SandboxNames.RoleName((SandboxRole)99));
	}

	[Fact]
	public void ProjectImage_ProjectKey_IsALowercaseRepositoryNameWithTheKey()
	{
		string key = SandboxNames.ProjectKey(Path.Combine(s_projectsFolder, "Shop"));

		string image = SandboxNames.ProjectImage(key);

		Assert.Equal($"pyxie-project-{key}", image);
		Assert.Matches(RepositoryNamePattern, image);
		Assert.Matches(RepositoryNamePattern, SandboxNames.BaseImage);
	}

	[Fact]
	public void Hash_KnownText_IsItsLowercaseSha256()
	{
		Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", SandboxNames.Hash("abc"));
	}
}
