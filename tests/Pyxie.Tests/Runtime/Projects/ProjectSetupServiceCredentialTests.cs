using Pyxie.Data.Entities;
using Pyxie.Runtime.Git;
using Pyxie.Tests.Runtime.Git;
using Pyxie.Tests.Runtime.Repositories;
using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Runtime.Projects;

/// <summary>People put tokens and passwords in the URLs they clone. Every message that names the source shows the URL without them: a failed clone, and a clone that worked but has no branch to start cards from. The clone failures use ports nothing listens on; the others clone a bare repository served over loopback HTTP, which git accepts with credentials in the URL.</summary>
public sealed class ProjectSetupServiceCredentialTests
{
	private const string Token = "tok-3n-value";
	private const string Password = "s3cr3t-password";

	private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

	private static void AssertNoCredentials(string message)
	{
		Assert.DoesNotContain(Password, message, StringComparison.Ordinal);
		Assert.DoesNotContain(Token, message, StringComparison.Ordinal);
	}

	[Fact]
	public async Task CloneAsync_WithASignedUrlOmitsFreeFormGitDiagnostics()
	{
		await using RepositoryRig rig = new();
		const string address = "http://127.0.0.1:1/owner/repo.git";

		string message = ResultAssert.Failed(await rig.Setup.CloneAsync($"{address}?signature={Token}#private", Cancellation));

		Assert.Equal($"git could not clone {address}: Check the repository address, credentials and connectivity.", message);
		await rig.AssertNothingImportedAsync();
	}

	[Fact]
	public async Task CloneAsync_WhenAWebUrlWithCredentialsCannotBeCloned_NamesTheUrlWithoutThem()
	{
		await using RepositoryRig rig = new();

		string message = ResultAssert.Failed(await rig.Setup.CloneAsync($"http://{Token}:{Password}@127.0.0.1:1/owner/repo.git", Cancellation));

		Assert.StartsWith("git could not clone http://127.0.0.1:1/owner/repo.git: ", message, StringComparison.Ordinal);
		AssertNoCredentials(message);
		Assert.Null(await rig.GetStoredAsync());
		await rig.AssertNothingImportedAsync();
	}

	[Fact]
	public async Task CloneAsync_WhenATokenIsTheWholeUserPartOfAWebUrl_NamesTheUrlWithoutIt()
	{
		await using RepositoryRig rig = new();

		string message = ResultAssert.Failed(await rig.Setup.CloneAsync($"http://{Token}@127.0.0.1:1/owner/repo.git", Cancellation));

		Assert.StartsWith("git could not clone http://127.0.0.1:1/owner/repo.git: ", message, StringComparison.Ordinal);
		AssertNoCredentials(message);
	}

	[Fact]
	public async Task CloneAsync_WhenAnSshUrlWithAPasswordCannotBeCloned_KeepsTheUserNameAndDropsThePassword()
	{
		await using RepositoryRig rig = new();

		string message = ResultAssert.Failed(await rig.Setup.CloneAsync($"ssh://deploy:{Password}@127.0.0.1:1/owner/repo.git", Cancellation));

		Assert.StartsWith("git could not clone ssh://deploy@127.0.0.1:1/owner/repo.git: ", message, StringComparison.Ordinal);
		AssertNoCredentials(message);
	}

	[Fact]
	public async Task CloneAsync_WhenTheClonedRepositoryHasNoCommits_NamesTheUrlWithoutCredentialsInTheMessage()
	{
		await using ScratchFolder served = ScratchFolder.Create();
		await using TempGitRepository helper = await TempGitRepository.CreateAsync();
		string bare = served.Combine("empty.git");
		await helper.RunGitAsync("init", "--bare", "--quiet", bare);
		await helper.RunGitAsync("-C", bare, "update-server-info");
		await using StaticFileHttpServer server = await StaticFileHttpServer.StartAsync(served.Folder);
		await using RepositoryRig rig = new();

		string message = ResultAssert.Failed(await rig.Setup.CloneAsync($"http://{Token}:{Password}@127.0.0.1:{server.Port}/empty.git", Cancellation));

		Assert.Contains($" in {server.BaseAddress}/empty.git has no commits yet.", message, StringComparison.Ordinal);
		AssertNoCredentials(message);
		Assert.Null(await rig.GetStoredAsync());
		await rig.AssertNothingImportedAsync();
	}

	[Fact]
	public async Task CloneAsync_OfAUrlWithCredentials_KeepsThemOutOfTheMachineDatabaseAndListsTheProjectWithTheUrlWithoutThem()
	{
		await using ScratchFolder served = ScratchFolder.Create();
		await using TempGitRepository source = await TempGitRepository.CreateAsync();
		await source.RunGitAsync("clone", "--bare", "--quiet", ".", served.Combine("app.git"));
		await source.RunGitAsync("-C", served.Combine("app.git"), "update-server-info");
		await using StaticFileHttpServer server = await StaticFileHttpServer.StartAsync(served.Folder);
		await using RepositoryRig rig = new();

		Repository connected = ResultAssert.Succeeded(await rig.Setup.CloneAsync($"http://{Token}:{Password}@127.0.0.1:{server.Port}/app.git", Cancellation));

		Project listed = Assert.Single(await rig.Catalog.ListAsync(Cancellation));
		Assert.Equal($"{server.BaseAddress}/app.git", listed.Source);
		Assert.Equal(GitUrl.WithoutCredentials(connected.Origin), listed.Source);
		Assert.Equal(listed.Source, connected.Origin);
		AssertNoCredentials(listed.Source);
	}

	[Fact]
	public async Task CloneAsync_WhenTheClonedRepositoryHasNoBranchCheckedOut_NamesTheUrlWithoutCredentialsInTheMessage()
	{
		await using ScratchFolder served = ScratchFolder.Create();
		await using TempGitRepository source = await TempGitRepository.CreateAsync();
		string bare = served.Combine("detached.git");
		string firstCommit = await source.GetHashAsync();
		await source.CommitFileAsync("second.txt", "a commit no detached HEAD points at");
		await source.RunGitAsync("clone", "--bare", "--quiet", ".", bare);
		await source.RunGitAsync("-C", bare, "update-ref", "--no-deref", "HEAD", firstCommit);
		await source.RunGitAsync("-C", bare, "update-server-info");
		await using StaticFileHttpServer server = await StaticFileHttpServer.StartAsync(served.Folder);
		await using RepositoryRig rig = new();

		string message = ResultAssert.Failed(await rig.Setup.CloneAsync($"http://{Token}:{Password}@127.0.0.1:{server.Port}/detached.git", Cancellation));

		Assert.StartsWith($"No branch is checked out in {server.BaseAddress}/detached.git.", message, StringComparison.Ordinal);
		AssertNoCredentials(message);
		Assert.Null(await rig.GetStoredAsync());
		await rig.AssertNothingImportedAsync();
	}
}
