using Pyxie.Runtime.Git;

using Xunit;

namespace Pyxie.Tests.Runtime.Git;

public sealed class GitUrlTests
{
	[Theory]
	[InlineData("https://github.com/owner/repo.git", "https://github.com/owner/repo.git")]
	[InlineData("https://TOKEN@github.com/owner/repo.git", "https://github.com/owner/repo.git")]
	[InlineData("https://user:secret@github.com/owner/repo.git", "https://github.com/owner/repo.git")]
	[InlineData("HTTP://user:secret@host:8080/repo", "HTTP://host:8080/repo")]
	[InlineData("https://user:p@ss@host/repo", "https://host/repo")]
	[InlineData("ssh://git@github.com/owner/repo.git", "ssh://git@github.com/owner/repo.git")]
	[InlineData("ssh://git:secret@github.com/owner/repo.git", "ssh://git@github.com/owner/repo.git")]
	[InlineData("git@github.com:owner/repo.git", "git@github.com:owner/repo.git")]
	[InlineData("C:\\work\\repo", "C:\\work\\repo")]
	[InlineData("/home/me/repo", "/home/me/repo")]
	[InlineData("file:///home/me/repo", "file:///home/me/repo")]
	[InlineData("", "")]
	public void WithoutCredentials_RemovesSecretsAndNothingElse(string url, string expected)
	{
		Assert.Equal(expected, GitUrl.WithoutCredentials(url));
	}

	[Theory]
	[InlineData("https://host/owner/repo@2.git")]
	[InlineData("https://host/repo?user=me@example.com")]
	[InlineData("https://host/repo#me@example.com")]
	public void WithoutCredentials_LeavesAnAtSignThatIsNotInTheUserPartAlone(string url)
	{
		Assert.Equal(url, GitUrl.WithoutCredentials(url));
	}
}
