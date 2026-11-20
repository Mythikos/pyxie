namespace Pyxie.Runtime.Git;

/// <summary>Removes the secrets people put in git URLs, before a URL is shown or written into a message.</summary>
public static class GitUrl
{
	private const string SchemeSeparator = "://";

	/// <summary>Gets the source address to retain after cloning. URL queries and fragments can carry temporary credentials.</summary>
	/// <param name="source">The clone URL or local path.</param>
	/// <returns>The address suitable for storage and display.</returns>
	public static string DisplaySource(string source)
	{
		string address = WithoutCredentials(source);
		int suffix = address.IndexOfAny(['?', '#']);
		return suffix >= 0 && address.Contains(SchemeSeparator, StringComparison.Ordinal) ? address[..suffix] : address;
	}

	/// <summary>Removes credentials from a URL before it is shown. A web URL loses its whole user part, because a token is often written as the user name (<c>https://TOKEN@host/repo.git</c>). Any other scheme keeps the user name and loses the password (<c>ssh://user:password@host/repo</c> becomes <c>ssh://user@host/repo</c>), because the user name there is part of the address.</summary>
	/// <param name="url">The URL, an scp-style address such as <c>git@host:owner/repo.git</c>, or a path.</param>
	/// <returns>The text to show; the input itself when it carries no credentials.</returns>
	public static string WithoutCredentials(string url)
	{
		int schemeEnd = url.IndexOf(SchemeSeparator, StringComparison.Ordinal);

		if (schemeEnd < 0)
		{
			return url;
		}

		int authorityStart = schemeEnd + SchemeSeparator.Length;
		int authorityEnd = url.IndexOfAny(['/', '?', '#'], authorityStart);
		authorityEnd = authorityEnd < 0 ? url.Length : authorityEnd;

		int at = url.AsSpan(authorityStart, authorityEnd - authorityStart).LastIndexOf('@');

		if (at < 0)
		{
			return url;
		}

		int userInfoEnd = authorityStart + at;
		string scheme = url[..schemeEnd];

		if (scheme.Equals("http", StringComparison.OrdinalIgnoreCase) || scheme.Equals("https", StringComparison.OrdinalIgnoreCase))
		{
			return url[..authorityStart] + url[(userInfoEnd + 1)..];
		}

		int colon = url.IndexOf(':', authorityStart, userInfoEnd - authorityStart);

		return colon < 0 ? url : url[..colon] + url[userInfoEnd..];
	}
}
