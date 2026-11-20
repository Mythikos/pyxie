using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;

using Pyxie.Data.Entities;
using Pyxie.Data.Persistence;
using Pyxie.Data.Projects;
using Pyxie.Data.Services.Results;
using Pyxie.Runtime.Media;

namespace Pyxie.Web.Hosting;

/// <summary>Bounded media uploads and project-scoped, range-enabled downloads.</summary>
internal static class PyxieMediaEndpoints
{
	/// <summary>Maps the media transport endpoints.</summary>
	/// <param name="endpoints">The application route builder.</param>
	/// <returns>The same route builder.</returns>
	public static IEndpointRouteBuilder MapPyxieMedia(this IEndpointRouteBuilder endpoints)
	{
		endpoints.MapGet("media/token", (HttpContext http, IAntiforgery antiforgery) =>
		{
			http.Response.Headers.CacheControl = "no-store";
			AntiforgeryTokenSet tokens = antiforgery.GetAndStoreTokens(http);
			return Results.Ok(new { token = tokens.RequestToken, header = tokens.HeaderName });
		});
		endpoints.MapPost("media/{projectId:int}/uploads/{uploadId:guid}", UploadAsync);
		endpoints.MapGet("media/{projectId:int}/files/{fileId:guid}", DownloadAsync);
		return endpoints;
	}

	private static async Task<IResult> UploadAsync(int projectId, Guid uploadId, HttpContext http, OpenProject openProject, MediaStorage storage, IAntiforgery antiforgery)
	{
		if (openProject.Current is not Project project || project.Id != projectId)
		{
			return Results.NotFound();
		}
		if (http.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } bodyLimit)
		{
			bodyLimit.MaxRequestBodySize = storage.Limits.MaxFileBytes;
		}
		if (!await antiforgery.IsRequestValidAsync(http))
		{
			return Results.BadRequest(new { error = "The upload session is no longer valid. Reload the page." });
		}
		if (http.Request.ContentLength > storage.Limits.MaxFileBytes)
		{
			return Results.BadRequest(new { error = "The file exceeds the configured media limit." });
		}
		try
		{
			ServiceResult<MediaFile> result = await storage.UploadAsync(project, uploadId, http.Request.Query["name"].ToString(), http.Request.Body, http.RequestAborted);
			return result.Value is MediaFile file ? Results.Ok(file) : Results.BadRequest(new { error = result.Error });
		}
		catch (OperationCanceledException)
		{
			return Results.BadRequest(new { error = "The upload was cancelled or its project closed." });
		}
		catch (IOException)
		{
			return Results.BadRequest(new { error = "The upload could not be read. Try the file again." });
		}
	}

	private static async Task<IResult> DownloadAsync(int projectId, Guid fileId, HttpContext http, OpenProject openProject, IProjectDbContextSource contexts, TimeProvider clock)
	{
		if (openProject.Current is not Project project || project.Id != projectId)
		{
			return Results.NotFound();
		}
		await using ProjectDbContext context = contexts.CreateFor(project);
		MediaFile? file = await context.MediaFiles.AsNoTracking().FirstOrDefaultAsync(item => item.Id == fileId, http.RequestAborted);
		if (file is null)
		{
			return Results.NotFound();
		}
		if (file.CardId is int cardId)
		{
			if (!int.TryParse(http.Request.Query["card"], out int requestedCard) || requestedCard != cardId)
			{
				return Results.NotFound();
			}
		}
		else if (!Guid.TryParse(http.Request.Query["upload"], out Guid uploadId) || file.UploadId != uploadId || !await context.MediaUploads.AnyAsync(upload => upload.Id == uploadId && upload.ExpiresUtc > clock.GetUtcNow().UtcDateTime, http.RequestAborted))
		{
			return Results.NotFound();
		}

		try
		{
			FileStream stream = new(MediaPaths.Object(project.Folder, file.Id), FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete, 64 * 1024, FileOptions.Asynchronous);
			http.Response.Headers.CacheControl = "private, no-store";
			http.Response.Headers.XContentTypeOptions = "nosniff";
			http.Response.Headers.ContentSecurityPolicy = "default-src 'none'; sandbox";
			http.Response.Headers["Cross-Origin-Resource-Policy"] = "same-origin";
			bool download = http.Request.Query["download"] == "1" || file.ContentType == "application/octet-stream";
			return Results.File(stream, file.ContentType, fileDownloadName: download ? file.Name : null, enableRangeProcessing: true);
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
		{
			return Results.NotFound();
		}
	}
}
