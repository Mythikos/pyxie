using Microsoft.AspNetCore.Mvc;

using Pyxie.Runtime.Repositories;

namespace Pyxie.Web.Hosting;

/// <summary>Maps the endpoints that serve files rather than pages.</summary>
internal static class PyxieEndpointRouteBuilderExtensions
{
	/// <summary>The path that serves a branch of the repository as a zip file; the branch goes in the <c>branch</c> query parameter.</summary>
	public const string RepositoryArchivePath = "repository/archive";

	/// <summary>Maps the download of a branch as a zip file: not found when no repository is connected or it has no such branch.</summary>
	/// <param name="endpoints">The endpoint route builder.</param>
	/// <returns>The same builder, for chaining.</returns>
	public static IEndpointRouteBuilder MapPyxieRepositoryArchive(this IEndpointRouteBuilder endpoints)
	{
		endpoints.MapGet(RepositoryArchivePath, async ([FromQuery] string branch, RepositoryBrowser browser, CancellationToken cancellationToken) => await browser.ArchiveAsync(branch, cancellationToken) is RepositoryArchive archive ? Results.File(archive.Content, "application/zip", archive.FileName) : Results.NotFound());

		return endpoints;
	}
}
