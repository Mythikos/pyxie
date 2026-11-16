using Pyxie.Runtime.Processes;
using Pyxie.Runtime.Sandboxes;

using Xunit;

namespace Pyxie.Tests.Runtime.Sandboxes;

/// <summary>Runs the <c>docker</c> command line directly, beside the code under test, so a test can look at what a sandbox left in the engine and clean up whatever happens to the code it tests. Cleanup only ever touches what carries a test's own project key, by name or by label, so Pyxie's shared base image and every other project's images are never removed.</summary>
public sealed class DockerProcess : IDisposable
{
	private static readonly TimeSpan s_timeout = TimeSpan.FromMinutes(2);

	private readonly ProcessRunner _runner = new(TimeProvider.System);

	/// <summary>Runs a docker command and returns how it ended, without judging it.</summary>
	/// <param name="arguments">The arguments after <c>docker</c>.</param>
	/// <returns>The result.</returns>
	public Task<ProcessResult> RunAsync(params string[] arguments)
	{
		return _runner.RunAsync(new ProcessRequest("docker", arguments, Path.GetTempPath()) { Timeout = s_timeout }, CancellationToken.None);
	}

	/// <summary>Runs a docker command and fails the test when it does not succeed.</summary>
	/// <param name="arguments">The arguments after <c>docker</c>.</param>
	/// <returns>What it printed on standard output, trimmed.</returns>
	public async Task<string> RunCheckedAsync(params string[] arguments)
	{
		ProcessResult result = await RunAsync(arguments);
		Assert.True(result.Succeeded, $"docker {string.Join(' ', arguments)} failed ({result.Outcome}, exit code {result.ExitCode}): {result.StartError ?? result.Output}");

		return result.StandardOutput.Trim();
	}

	/// <summary>Gets whether a container exists, running or not.</summary>
	/// <param name="name">The container's name.</param>
	/// <returns><see langword="true"/> when it exists.</returns>
	public async Task<bool> ContainerExistsAsync(string name)
	{
		return (await RunAsync("container", "inspect", "--format", "{{.Id}}", name)).Succeeded;
	}

	/// <summary>Gets a container's identifier, which changes when the container is made again under the same name.</summary>
	/// <param name="name">The container's name.</param>
	/// <returns>The identifier.</returns>
	public Task<string> ContainerIdAsync(string name)
	{
		return RunCheckedAsync("container", "inspect", "--format", "{{.Id}}", name);
	}

	/// <summary>Gets whether a volume exists.</summary>
	/// <param name="name">The volume's name.</param>
	/// <returns><see langword="true"/> when it exists.</returns>
	public async Task<bool> VolumeExistsAsync(string name)
	{
		return (await RunAsync("volume", "inspect", "--format", "{{.Name}}", name)).Succeeded;
	}

	/// <summary>Gets whether an image exists, by tag or by identifier.</summary>
	/// <param name="reference">The image's tag or identifier.</param>
	/// <returns><see langword="true"/> when it exists.</returns>
	public async Task<bool> ImageExistsAsync(string reference)
	{
		return (await RunAsync("image", "inspect", "--format", "{{.Id}}", reference)).Succeeded;
	}

	/// <summary>Gets an image's identifier, which changes when a tag is moved to another image.</summary>
	/// <param name="reference">The image's tag.</param>
	/// <returns>The identifier.</returns>
	public Task<string> ImageIdAsync(string reference)
	{
		return RunCheckedAsync("image", "inspect", "--format", "{{.Id}}", reference);
	}

	/// <summary>Gets what one of an image's labels says, or an empty text when it has none.</summary>
	/// <param name="reference">The image's tag.</param>
	/// <param name="label">The label's name.</param>
	/// <returns>The label's value.</returns>
	public Task<string> ImageLabelAsync(string reference, string label)
	{
		return RunCheckedAsync("image", "inspect", "--format", $"{{{{index .Config.Labels \"{label}\"}}}}", reference);
	}

	/// <summary>Gets the identifiers of the images that carry a test project's label, tagged or not, which is how an image a tag was moved away from is still found.</summary>
	/// <param name="projectKey">The test project's key.</param>
	/// <returns>The identifiers.</returns>
	public async Task<IReadOnlySet<string>> ProjectImageIdsAsync(string projectKey)
	{
		return Lines(await RunAsync("image", "ls", "--all", "--no-trunc", "--quiet", "--filter", $"label={SandboxNames.ProjectLabel}={projectKey}")).ToHashSet(StringComparer.Ordinal);
	}

	/// <summary>Builds a small image from a Dockerfile on standard input, so the test needs no build folder, and labels it as a test project's image the way Pyxie labels its own.</summary>
	/// <param name="tag">The tag the image gets, where building again under the same tag moves it to the new image; <see langword="null"/> leaves the image without a tag.</param>
	/// <param name="dockerfile">The Dockerfile.</param>
	/// <param name="projectKey">The test project's key, which labels the image so cleanup finds it even when it has no tag.</param>
	/// <returns>The image's identifier.</returns>
	public async Task<string> BuildAsync(string? tag, string dockerfile, string projectKey)
	{
		List<string> arguments = ["build", "--quiet", "--label", $"{SandboxNames.ImageLabel}={SandboxNames.ProjectImageKind}", "--label", $"{SandboxNames.ProjectLabel}={projectKey}"];
		if (tag is not null)
		{
			arguments.AddRange(["--tag", tag]);
		}

		arguments.Add("-");
		ProcessResult result = await _runner.RunAsync(new ProcessRequest("docker", arguments, Path.GetTempPath()) { Timeout = s_timeout, StandardInput = dockerfile }, CancellationToken.None);

		Assert.True(result.Succeeded, $"docker build of {tag ?? "an untagged image"} failed ({result.Outcome}, exit code {result.ExitCode}): {result.StartError ?? result.Output}");

		return result.StandardOutput.Trim();
	}

	/// <summary>Removes everything a test project left in the engine: its containers and volumes, found by its label and by its name prefix, and its project images, untagged by name and then removed by the label they carry, which also takes an image a tag was moved away from. Only what carries the test's own key is touched.</summary>
	/// <param name="projectKey">The test project's key.</param>
	/// <returns>A task that completes when they are gone.</returns>
	public async Task RemoveProjectAsync(string projectKey)
	{
		string label = $"label={SandboxNames.ProjectLabel}={projectKey}";
		string prefix = $"name=pyxie-{projectKey}-";

		foreach (string filter in new[] { label, prefix })
		{
			foreach (string container in Lines(await RunAsync("ps", "--all", "--quiet", "--filter", filter)))
			{
				await RunAsync("rm", "--force", container);
			}
		}

		foreach (string filter in new[] { label, prefix })
		{
			foreach (string volume in Lines(await RunAsync("volume", "ls", "--quiet", "--filter", filter)))
			{
				await RunAsync("volume", "rm", "--force", volume);
			}
		}

		ProcessResult images = await RunAsync("image", "ls", "--filter", $"reference={SandboxNames.ProjectImage(projectKey)}", "--format", "{{.Repository}}:{{.Tag}}");
		foreach (string image in Lines(images))
		{
			await RunAsync("image", "rm", image);
		}

		foreach (string image in await ProjectImageIdsAsync(projectKey))
		{
			await RunAsync("image", "rm", image);
		}
	}

	/// <inheritdoc/>
	public void Dispose()
	{
		_runner.Dispose();
	}

	private static IEnumerable<string> Lines(ProcessResult result)
	{
		return result.Succeeded ? result.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) : [];
	}
}
