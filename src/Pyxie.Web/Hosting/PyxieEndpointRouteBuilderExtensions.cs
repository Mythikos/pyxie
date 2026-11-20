using Microsoft.AspNetCore.Mvc;

using Pyxie.Data.Entities;
using Pyxie.Data.Projects;
using Pyxie.Data.Services;
using Pyxie.Data.Services.Results;
using Pyxie.Data.Transfer;
using Pyxie.Data.Transfer.Files;
using Pyxie.Runtime.Repositories;

namespace Pyxie.Web.Hosting;

/// <summary>Maps the endpoints that serve files rather than pages.</summary>
internal static class PyxieEndpointRouteBuilderExtensions
{
	/// <summary>The path that serves a branch of the repository as a zip file; the branch goes in the <c>branch</c> query parameter.</summary>
	public const string RepositoryArchivePath = "repository/archive";

	/// <summary>The path that serves a project's settings as a settings file; the project goes in the <c>project</c> query parameter.</summary>
	public const string SettingsExportPath = "settings/export";

	/// <summary>Maps the download of a branch as a zip file: not found when no repository is connected or it has no such branch.</summary>
	/// <param name="endpoints">The endpoint route builder.</param>
	/// <returns>The same builder, for chaining.</returns>
	public static IEndpointRouteBuilder MapPyxieRepositoryArchive(this IEndpointRouteBuilder endpoints)
	{
		endpoints.MapGet(RepositoryArchivePath, async ([FromQuery] string branch, RepositoryBrowser browser, CancellationToken cancellationToken) => await browser.ArchiveAsync(branch, cancellationToken) is RepositoryArchive archive ? Results.File(archive.Content, "application/zip", archive.FileName) : Results.NotFound());

		return endpoints;
	}

	/// <summary>Maps the download of the open project's settings as a settings file, named after the project. The link names the project its page showed, so a page left open after another project was opened never downloads that project's settings: the download is not found then, as it is when no project is open.</summary>
	/// <param name="endpoints">The endpoint route builder.</param>
	/// <returns>The same builder, for chaining.</returns>
	public static IEndpointRouteBuilder MapPyxieSettingsExport(this IEndpointRouteBuilder endpoints)
	{
		endpoints.MapGet(SettingsExportPath, async ([FromQuery] int project, OpenProject openProject, ISettingsTransferService transfer, CancellationToken cancellationToken) =>
		{
			if (openProject.Current is not Project open || open.Id != project)
			{
				return Results.NotFound();
			}

			// The service checks again that the project is still open when it reads. Project names are folder names already (ProjectSetupService), so they make a file name as they are.
			ServiceResult<ProjectSettings> settings = await transfer.ExportAsync(project, cancellationToken);

			return settings is { Succeeded: true, Value: ProjectSettings exported } ? Results.File(SettingsFileWriter.Write(exported), SettingsFileFormat.MediaType, $"{open.Name}{SettingsFileFormat.FileNameSuffix}") : Results.NotFound();
		});

		return endpoints;
	}
}
