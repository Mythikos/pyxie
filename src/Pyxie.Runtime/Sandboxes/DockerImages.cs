using System.Text;
using System.Text.Json;

using Pyxie.Data.Sandboxes;
using Pyxie.Data.Services.Results;
using Pyxie.Runtime.Processes;

namespace Pyxie.Runtime.Sandboxes;

/// <summary>Builds Pyxie's base image and projects' images, and removes what their builds left that nothing needs. The base image is tagged with a hash of the files it is built from, and a project's image with a hash of the base tag and its Dockerfile, so a changed Dockerfile or a newer Pyxie gets a new image and an unchanged one is never built twice. Builds run one at a time, and removal never runs during a build. Every image is labelled as Pyxie's (<see cref="SandboxNames.ImageLabel"/>), and every image is built on steps run with <see cref="SandboxNames.BuildShell"/>, so what Pyxie removes is only ever what it built.</summary>
internal sealed class DockerImages
{
	// The engine lists a few hundred bytes per cache entry, and a busy machine's cache can hold tens of thousands.
	private const int BuildCacheListLength = 64_000_000;

	// Cache entries pruned per command, which keeps the filter far below the longest command line Windows takes.
	private const int PrunedPerCommand = 300;

	private static readonly TimeSpan s_buildTimeout = TimeSpan.FromMinutes(60);
	private static readonly TimeSpan s_inspectTimeout = TimeSpan.FromSeconds(30);
	private static readonly TimeSpan s_pruneTimeout = TimeSpan.FromMinutes(10);

	private readonly DockerCli _docker;
	private readonly SemaphoreSlim _builds = new(1, 1);

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

	/// <summary>Picks the base images nothing needs: every one but the current base image and those a remaining project image is built on. A project image is built on a base when its layers start with all of the base's layers. Removing a base a project image is built on would free nothing, because the project image holds those layers, and another Pyxie on the machine may still build on it.</summary>
	/// <param name="baseLayers">Each base image's layers, by image ID.</param>
	/// <param name="currentBaseId">The current base image's ID, or <see langword="null"/> when it is not built.</param>
	/// <param name="projectLayers">The layers of every remaining project image, of every project.</param>
	/// <returns>The IDs of the base images to remove.</returns>
	public static IReadOnlyList<string> UnusedBaseImages(IReadOnlyDictionary<string, IReadOnlyList<string>> baseLayers, string? currentBaseId, IReadOnlyList<IReadOnlyList<string>> projectLayers)
	{
		return [.. baseLayers.Where(entry => entry.Key != currentBaseId && !projectLayers.Any(layers => layers.Count >= entry.Value.Count && layers.Take(entry.Value.Count).SequenceEqual(entry.Value, StringComparer.Ordinal))).Select(entry => entry.Key).Order(StringComparer.Ordinal)];
	}

	/// <summary>Gets a project's image, building what is missing.</summary>
	/// <param name="request">The request.</param>
	/// <param name="cancellationToken">A token to cancel the build; the engine stops the build when its command is killed.</param>
	/// <returns>The tag, or why it could not be built.</returns>
	/// <exception cref="OperationCanceledException">The build was cancelled.</exception>
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

			request.BuildStarting?.Invoke();

			Directory.CreateDirectory(Path.GetDirectoryName(request.LogPath) ?? Path.GetTempPath());
			await File.WriteAllTextAsync(request.LogPath, string.Empty, CancellationToken.None);

			if (request.Rebuild || !await ExistsAsync(BaseTag, cancellationToken))
			{
				bool built = await BuildAsync(BaseTag, SandboxImageFiles.Files, [$"{SandboxNames.ImageLabel}={SandboxNames.BaseImageKind}"], buildArgument: null, fresh: request.Rebuild, request.LogPath, cancellationToken);
				if (!built)
				{
					return ServiceResult<string>.Failure($"Pyxie's base sandbox image could not be built. The build log is {request.LogPath}.");
				}
			}

			Dictionary<string, string> files = new(StringComparer.Ordinal) { ["Dockerfile"] = request.Dockerfile.ReplaceLineEndings("\n") };
			string[] labels = [$"{SandboxNames.ImageLabel}={SandboxNames.ProjectImageKind}", $"{SandboxNames.ProjectLabel}={request.ProjectKey}"];
			bool projectBuilt = await BuildAsync(projectTag, files, labels, $"{SandboxDefaults.BaseImageArgument}={BaseTag}", fresh: request.Rebuild, request.LogPath, cancellationToken);

			return projectBuilt
				? ServiceResult<string>.Success(projectTag)
				: ServiceResult<string>.Failure($"The project's sandbox image could not be built. The build log is {request.LogPath}.");
		}
		finally
		{
			_builds.Release();
		}
	}

	/// <summary>Finds a project's image for a Dockerfile on the current base image (see <see cref="ISandboxes.FindImageAsync"/>).</summary>
	/// <param name="projectKey">The project's key.</param>
	/// <param name="dockerfile">The project's Dockerfile.</param>
	/// <param name="cancellationToken">A token to cancel the operation.</param>
	/// <returns>The image's tag, or <see langword="null"/> when the engine does not have it.</returns>
	public async Task<string?> FindAsync(string projectKey, string dockerfile, CancellationToken cancellationToken)
	{
		string tag = ProjectTag(projectKey, dockerfile);

		return await ExistsAsync(tag, cancellationToken) ? tag : null;
	}

	/// <summary>Removes what builds left that nothing needs: the project's other images, base images no remaining project image is built on, and the build cache of Pyxie's images that no image uses (see <see cref="ISandboxes.RemoveUnusedImagesAsync"/>). It never waits for a build: one that is running means it does nothing, because the image it is building is not listed yet.</summary>
	/// <param name="projectKey">The project's key.</param>
	/// <param name="dockerfile">The project's Dockerfile, whose image stays.</param>
	/// <param name="cancellationToken">A token to cancel the operation.</param>
	/// <returns><see langword="true"/> when it ran; <see langword="false"/> when a build was running.</returns>
	public async Task<bool> RemoveUnusedAsync(string projectKey, string dockerfile, CancellationToken cancellationToken)
	{
		if (!await _builds.WaitAsync(TimeSpan.Zero, cancellationToken))
		{
			return false;
		}

		try
		{
			// The project's images go first, so a base only they were built on is free to go next.
			await RemoveUnusedProjectImagesAsync(projectKey, ProjectTag(projectKey, dockerfile), cancellationToken);
			await RemoveUnusedBaseImagesAsync(cancellationToken);

			// Only entries no image uses are pruned, so the current images keep their cache for the next build.
			await RemoveUnusedBuildCacheAsync(cancellationToken);

			return true;
		}
		finally
		{
			_builds.Release();
		}
	}

	/// <summary>Reads the engine's build cache as <c>docker buildx du --format json</c> lists it, one entry per line. A line that is not an entry, such as the last one of output cut short, is skipped.</summary>
	/// <param name="output">What the command printed.</param>
	/// <returns>The entries, in the order listed.</returns>
	internal static IReadOnlyList<BuildCacheEntry> ParseBuildCache(string output)
	{
		List<BuildCacheEntry> entries = [];
		foreach (string line in Lines(output))
		{
			try
			{
				using JsonDocument document = JsonDocument.Parse(line);
				JsonElement entry = document.RootElement;
				if (entry.ValueKind != JsonValueKind.Object || !entry.TryGetProperty("ID", out JsonElement id) || id.ValueKind != JsonValueKind.String || id.GetString() is not string entryId)
				{
					continue;
				}

				IReadOnlyList<string> parents = entry.TryGetProperty("Parents", out JsonElement list) && list.ValueKind == JsonValueKind.Array ? [.. list.EnumerateArray().Select(parent => parent.ValueKind == JsonValueKind.String ? parent.GetString() : null).OfType<string>()] : [];
				bool reclaimable = entry.TryGetProperty("Reclaimable", out JsonElement unused) && unused.ValueKind == JsonValueKind.True;
				bool shared = entry.TryGetProperty("Shared", out JsonElement held) && held.ValueKind == JsonValueKind.True;
				string description = entry.TryGetProperty("Description", out JsonElement text) && text.ValueKind == JsonValueKind.String ? text.GetString() ?? string.Empty : string.Empty;
				entries.Add(new BuildCacheEntry(entryId, parents, reclaimable, shared, description));
			}
			catch (JsonException)
			{
				// Not an entry.
			}
		}

		return entries;
	}

	/// <summary>Picks the entries of the build cache that are Pyxie's and that nothing uses, neither a build nor an image that is still there, each before the entries it was built on, so a prune in that order never meets an entry that something built on it still holds. Pyxie's entries are the steps run with <see cref="SandboxNames.BuildShell"/> or naming it (the step that links it), and every entry built on one of them: a later step of a Pyxie image, such as a <c>COPY</c> or a <c>WORKDIR</c>, records no shell, and an entry cannot go while one built on it stays. What Pyxie's images start from belongs to everyone, and stays.</summary>
	/// <param name="entries">The engine's build cache.</param>
	/// <returns>The identifiers of the entries to prune.</returns>
	internal static IReadOnlyList<string> UnusedBuildCache(IReadOnlyList<BuildCacheEntry> entries)
	{
		Dictionary<string, List<BuildCacheEntry>> children = new(StringComparer.Ordinal);
		foreach (BuildCacheEntry entry in entries)
		{
			foreach (string parent in entry.Parents)
			{
				if (!children.TryGetValue(parent, out List<BuildCacheEntry>? built))
				{
					built = [];
					children[parent] = built;
				}

				built.Add(entry);
			}
		}

		HashSet<string> visited = new(StringComparer.Ordinal);
		List<string> unused = [];
		foreach (BuildCacheEntry entry in entries.Where(entry => entry.Description.Contains(SandboxNames.BuildShell, StringComparison.Ordinal)))
		{
			Visit(entry);
		}

		return unused;

		// What was built on an entry comes before it.
		void Visit(BuildCacheEntry entry)
		{
			if (!visited.Add(entry.Id))
			{
				return;
			}

			foreach (BuildCacheEntry child in children.GetValueOrDefault(entry.Id) ?? [])
			{
				Visit(child);
			}

			// An entry an image still holds frees nothing when it goes, and that image keeps its cache for the next build.
			if (entry.Reclaimable && !entry.Shared)
			{
				unused.Add(entry.Id);
			}
		}
	}

	/// <summary>Turns the entries to prune into prune filters, in their order and a bounded number per filter. docker turns <c>id=</c> into a match of the pattern after it, which names exactly these entries.</summary>
	/// <param name="ids">The identifiers of the entries, each before the entries it was built on.</param>
	/// <param name="perFilter">How many entries one filter names at most.</param>
	/// <returns>The filters, one per prune.</returns>
	internal static IReadOnlyList<string> PruneFilters(IReadOnlyList<string> ids, int perFilter)
	{
		return [.. ids.Chunk(perFilter).Select(chunk => $"id=^({string.Join('|', chunk)})$")];
	}

	/// <summary>Removes a project's images other than the one a tag names. The base image is shared by every project and every Pyxie on the machine, so it is left alone here.</summary>
	/// <param name="projectKey">The project's key.</param>
	/// <param name="keepTag">The tag of the image that stays.</param>
	/// <param name="cancellationToken">A token to cancel the operation.</param>
	/// <returns>A task that completes when the images that could be removed are gone.</returns>
	internal async Task RemoveUnusedProjectImagesAsync(string projectKey, string keepTag, CancellationToken cancellationToken)
	{
		IReadOnlySet<string> images = await ListImagesAsync([$"label={SandboxNames.ProjectLabel}={projectKey}", $"reference={SandboxNames.ProjectImage(projectKey)}:*"], cancellationToken);
		string? keep = await ImageIdAsync(keepTag, cancellationToken);

		await RemoveImagesAsync(images.Where(image => image != keep), cancellationToken);
	}

	/// <summary>Removes every image of a project (see <see cref="ISandboxes.RemoveProjectAsync"/>). The base image is shared by every project and every Pyxie on the machine, so it is left alone here. It does not wait for a build, because the caller makes sure none of the project's runs.</summary>
	/// <param name="projectKey">The project's key.</param>
	/// <param name="cancellationToken">A token to cancel the operation.</param>
	/// <returns>A task that completes when the images that could be removed are gone.</returns>
	internal async Task RemoveProjectImagesAsync(string projectKey, CancellationToken cancellationToken)
	{
		// An image can carry several of the project's tags, when two Dockerfiles built the same image; the engine refuses to remove such an image by its ID without force, so its tags are removed one by one, and the last one takes the image with it.
		string tags = await _docker.RunCheckedAsync(["image", "ls", "--filter", $"reference={SandboxNames.ProjectImage(projectKey)}:*", "--format", "{{.Repository}}:{{.Tag}}"], cancellationToken);
		foreach (string tag in Lines(tags))
		{
			await _docker.RunAsync(["image", "rm", tag], cancellationToken);
		}

		// What a rebuild left without a tag is found by its label.
		await RemoveImagesAsync(await ListImagesAsync([$"label={SandboxNames.ProjectLabel}={projectKey}"], cancellationToken), cancellationToken);
	}

	private async Task RemoveUnusedBaseImagesAsync(CancellationToken cancellationToken)
	{
		IReadOnlySet<string> bases = await ListImagesAsync([$"label={SandboxNames.ImageLabel}={SandboxNames.BaseImageKind}", $"reference={SandboxNames.BaseImage}:*"], cancellationToken);
		if (bases.Count == 0)
		{
			return;
		}

		IReadOnlySet<string> projects = await ListImagesAsync([$"label={SandboxNames.ImageLabel}={SandboxNames.ProjectImageKind}", $"reference={SandboxNames.ProjectImage("*")}:*"], cancellationToken);
		IReadOnlyDictionary<string, IReadOnlyList<string>> layers = await LayersAsync([.. bases, .. projects], cancellationToken);
		Dictionary<string, IReadOnlyList<string>> baseLayers = bases.Where(layers.ContainsKey).ToDictionary(image => image, image => layers[image], StringComparer.Ordinal);
		IReadOnlyList<IReadOnlyList<string>> projectLayers = [.. projects.Where(layers.ContainsKey).Select(image => layers[image])];

		await RemoveImagesAsync(UnusedBaseImages(baseLayers, await ImageIdAsync(BaseTag, cancellationToken), projectLayers), cancellationToken);
	}

	// A prune removes the entries it names that nothing uses, each once nothing built on it is left; what was built on an entry comes in the same prune or an earlier one, so every prune finds what the ones before it freed.
	private async Task RemoveUnusedBuildCacheAsync(CancellationToken cancellationToken)
	{
		ProcessResult listed = await _docker.RunAsync(["buildx", "du", "--format", "json"], cancellationToken, s_pruneTimeout, maxOutputLength: BuildCacheListLength);
		if (!listed.Succeeded)
		{
			throw new SandboxException($"docker buildx du failed: {DockerCli.LastLine(listed.Output)}");
		}

		foreach (string filter in PruneFilters(UnusedBuildCache(ParseBuildCache(listed.StandardOutput)), PrunedPerCommand))
		{
			await _docker.RunAsync(["builder", "prune", "--force", "--filter", filter], cancellationToken, s_pruneTimeout);
		}
	}

	// The union of the images each filter lists; a filter stands alone, because docker combines filters on one command as "all of them".
	private async Task<IReadOnlySet<string>> ListImagesAsync(IReadOnlyList<string> filters, CancellationToken cancellationToken)
	{
		HashSet<string> images = new(StringComparer.Ordinal);
		foreach (string filter in filters)
		{
			images.UnionWith(Lines(await _docker.RunCheckedAsync(["image", "ls", "--all", "--no-trunc", "--quiet", "--filter", filter], cancellationToken)));
		}

		return images;
	}

	// An image a container uses, or one tagged by someone else as well, is refused without force, and stays.
	private async Task RemoveImagesAsync(IEnumerable<string> images, CancellationToken cancellationToken)
	{
		foreach (string image in images)
		{
			await _docker.RunAsync(["image", "rm", image], cancellationToken);
		}
	}

	private async Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> LayersAsync(IReadOnlyList<string> images, CancellationToken cancellationToken)
	{
		Dictionary<string, IReadOnlyList<string>> layers = new(StringComparer.Ordinal);
		if (images.Count == 0)
		{
			return layers;
		}

		// An image removed meanwhile makes inspect fail for that one and still print the rest.
		string output = (await _docker.RunAsync(["image", "inspect", "--format", "{{.Id}} {{join .RootFS.Layers \",\"}}", .. images], cancellationToken, s_inspectTimeout)).StandardOutput;
		foreach (string line in Lines(output))
		{
			string[] fields = line.Split(' ', 2);
			layers[fields[0]] = fields.Length < 2 ? [] : fields[1].Split(',', StringSplitOptions.RemoveEmptyEntries);
		}

		return layers;
	}

	private async Task<bool> ExistsAsync(string tag, CancellationToken cancellationToken)
	{
		return await ImageIdAsync(tag, cancellationToken) is not null;
	}

	/// <summary>Gets the ID of the image a tag names. It is asked of the engine every time: an image can be removed by Pyxie or by anyone else at any moment, so nothing about it is remembered.</summary>
	/// <param name="tag">The tag.</param>
	/// <param name="cancellationToken">A token to cancel the operation.</param>
	/// <returns>The image's ID, or <see langword="null"/> when no image has the tag.</returns>
	internal async Task<string?> ImageIdAsync(string tag, CancellationToken cancellationToken)
	{
		ProcessResult result = await _docker.RunAsync(["image", "inspect", "--format", "{{.Id}}", tag], cancellationToken, s_inspectTimeout);
		string id = result.StandardOutput.Trim();

		return result.Succeeded && id.Length > 0 ? id : null;
	}

	private static IEnumerable<string> Lines(string output)
	{
		return output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
	}

	// Builds from a folder of its own, so nothing else on the machine is in the build context.
	private async Task<bool> BuildAsync(string tag, IReadOnlyDictionary<string, string> files, IReadOnlyList<string> labels, string? buildArgument, bool fresh, string logPath, CancellationToken cancellationToken)
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
			foreach (string label in labels)
			{
				arguments.AddRange(["--label", label]);
			}

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

			return (await _docker.RunAsync(arguments, cancellationToken, s_buildTimeout, logPath: logPath, maxOutputLength: 20_000)).Succeeded;
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
