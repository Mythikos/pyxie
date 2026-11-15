using System.Collections.Concurrent;
using System.Text;

using Pyxie.Data.Sandboxes;
using Pyxie.Data.Services.Results;

namespace Pyxie.Runtime.Sandboxes;

/// <summary>Builds Pyxie's base image and projects' images. The base image is tagged with a hash of the files it is built from, and a project's image with a hash of the base tag and its Dockerfile, so a changed Dockerfile or a newer Pyxie gets a new image and an unchanged one is never built twice. Builds run one at a time.</summary>
internal sealed class DockerImages
{
	private static readonly TimeSpan s_buildTimeout = TimeSpan.FromMinutes(60);
	private static readonly TimeSpan s_inspectTimeout = TimeSpan.FromSeconds(30);

	private readonly DockerCli _docker;
	private readonly SemaphoreSlim _builds = new(1, 1);
	private readonly ConcurrentDictionary<string, bool> _present = new(StringComparer.Ordinal);

	/// <summary>Initializes a new instance of the <see cref="DockerImages"/> class.</summary>
	/// <param name="docker">The docker command line.</param>
	public DockerImages(DockerCli docker)
	{
		_docker = docker;
	}

	/// <summary>Gets the base image's tag.</summary>
	public static string BaseTag => $"{SandboxNames.BaseImage}:{SandboxImageFiles.Hash}";

	/// <summary>Gets a project's image tag for a Dockerfile.</summary>
	/// <param name="projectKey">The project's key.</param>
	/// <param name="dockerfile">The project's Dockerfile.</param>
	/// <returns>The tag.</returns>
	public static string ProjectTag(string projectKey, string dockerfile)
	{
		return $"{SandboxNames.ProjectImage(projectKey)}:{SandboxNames.Hash($"{BaseTag}\n{dockerfile.ReplaceLineEndings("\n")}")[..16]}";
	}

	/// <summary>Gets a project's image, building what is missing.</summary>
	/// <param name="request">The request.</param>
	/// <param name="cancellationToken">A token to cancel the build.</param>
	/// <returns>The tag, or why it could not be built.</returns>
	public async Task<ServiceResult<string>> EnsureAsync(SandboxImageRequest request, CancellationToken cancellationToken)
	{
		string projectTag = ProjectTag(request.ProjectKey, request.Dockerfile);
		if (!request.Rebuild && await ExistsAsync(projectTag, cancellationToken))
		{
			return ServiceResult<string>.Success(projectTag);
		}

		await _builds.WaitAsync(cancellationToken);
		try
		{
			// Another round may have built it while this one waited.
			if (!request.Rebuild && await ExistsAsync(projectTag, cancellationToken))
			{
				return ServiceResult<string>.Success(projectTag);
			}

			Directory.CreateDirectory(Path.GetDirectoryName(request.LogPath) ?? Path.GetTempPath());
			await File.WriteAllTextAsync(request.LogPath, string.Empty, CancellationToken.None);

			if (request.Rebuild || !await ExistsAsync(BaseTag, cancellationToken))
			{
				bool built = await BuildAsync(BaseTag, SandboxImageFiles.Files, buildArgument: null, fresh: request.Rebuild, request.LogPath, cancellationToken);
				if (!built)
				{
					return ServiceResult<string>.Failure($"Pyxie's base sandbox image could not be built. The build log is {request.LogPath}.");
				}
			}

			Dictionary<string, string> files = new(StringComparer.Ordinal) { ["Dockerfile"] = request.Dockerfile.ReplaceLineEndings("\n") };
			bool projectBuilt = await BuildAsync(projectTag, files, $"{SandboxDefaults.BaseImageArgument}={BaseTag}", fresh: request.Rebuild, request.LogPath, cancellationToken);

			return projectBuilt
				? ServiceResult<string>.Success(projectTag)
				: ServiceResult<string>.Failure($"The project's sandbox image could not be built. The build log is {request.LogPath}.");
		}
		finally
		{
			_builds.Release();
		}
	}

	private async Task<bool> ExistsAsync(string tag, CancellationToken cancellationToken)
	{
		if (_present.ContainsKey(tag))
		{
			return true;
		}

		bool exists = (await _docker.RunAsync(["image", "inspect", "--format", "{{.Id}}", tag], cancellationToken, s_inspectTimeout)).Succeeded;
		if (exists)
		{
			_present[tag] = true;
		}

		return exists;
	}

	// Builds from a folder of its own, so nothing else on the machine is in the build context.
	private async Task<bool> BuildAsync(string tag, IReadOnlyDictionary<string, string> files, string? buildArgument, bool fresh, string logPath, CancellationToken cancellationToken)
	{
		string context = Path.Combine(Path.GetTempPath(), $"pyxie-build-{Guid.NewGuid():N}");
		Directory.CreateDirectory(context);

		try
		{
			foreach ((string name, string text) in files)
			{
				await File.WriteAllTextAsync(Path.Combine(context, name), text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), cancellationToken);
			}

			List<string> arguments = ["build", "--progress=plain", "--tag", tag];
			if (fresh)
			{
				arguments.Add("--no-cache");
			}

			// The base image refreshes its own base when rebuilt; a project's image starts from the local base image, which no registry has.
			if (fresh && buildArgument is null)
			{
				arguments.Add("--pull");
			}

			if (buildArgument is not null)
			{
				arguments.Add("--build-arg");
				arguments.Add(buildArgument);
			}

			arguments.Add(context);
			_present.TryRemove(tag, out _);

			bool built = (await _docker.RunAsync(arguments, cancellationToken, s_buildTimeout, logPath: logPath, maxOutputLength: 20_000)).Succeeded;
			if (built)
			{
				_present[tag] = true;
			}

			return built;
		}
		finally
		{
			try
			{
				Directory.Delete(context, recursive: true);
			}
			catch (IOException)
			{
			}
		}
	}
}
