using Pyxie.Data.Services.Results;
using Pyxie.Runtime.Processes;
using Pyxie.Runtime.Sandboxes;
using Pyxie.Runtime.Workspaces;
using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Runtime.Sandboxes;

/// <summary>One Docker test's own corner of the engine: a temporary folder that stands for a project's folder, the project key that hash gives (so every container and volume the test makes carries a key no other test or project has), and the real <see cref="DockerSandboxes"/>. Disposing it removes every container, volume, and project image with the key, whatever the test left, and deletes the folder. Create it inside the test method, never in a constructor, so a skipped test never touches the engine.</summary>
public sealed class DockerSandboxRig : IAsyncDisposable
{
	private readonly ProcessRunner _runner = new(TimeProvider.System);

	/// <summary>Initializes a new instance of the <see cref="DockerSandboxRig"/> class.</summary>
	/// <param name="image">The collection's image, which <see cref="Spec"/> starts sandboxes from.</param>
	public DockerSandboxRig(DockerImageFixture image)
	{
		Image = image.Image;
		Folder = TempFolder.Create("pyxie-dsbx");
		ProjectKey = SandboxNames.ProjectKey(Folder);
		Sandboxes = new DockerSandboxes(_runner, TimeProvider.System);
		Images = new DockerImages(new DockerCli(_runner));
	}

	/// <summary>Gets the folder that stands for the project's folder.</summary>
	public string Folder { get; }

	/// <summary>Gets the test's project key.</summary>
	public string ProjectKey { get; }

	/// <summary>Gets the tag of the image sandboxes start from.</summary>
	public string Image { get; }

	/// <summary>Gets the sandboxes under test.</summary>
	public DockerSandboxes Sandboxes { get; }

	/// <summary>Gets the image builder behind <see cref="Sandboxes"/>, for what it does beyond the public interface. It is a second instance over the same engine, with a build lock of its own.</summary>
	internal DockerImages Images { get; }

	/// <summary>Gets the docker command line, for looking at what the sandboxes left.</summary>
	public DockerProcess Docker { get; } = new();

	/// <summary>Gets a Dockerfile for a tiny image on Pyxie's base image, which builds in seconds because its one step writes a word: different words make different images, and the same word makes the same tag in every project.</summary>
	/// <param name="word">What the step writes.</param>
	/// <returns>The Dockerfile.</returns>
	public static string TinyDockerfile(string word)
	{
		return $"ARG PYXIE_BASE\nFROM ${{PYXIE_BASE}}\nRUN echo {word} > /pyxie-test-word\n";
	}

	/// <summary>Builds the project's image for a Dockerfile with <see cref="Sandboxes"/>, failing the test with the build's log when it cannot be built.</summary>
	/// <param name="dockerfile">The Dockerfile.</param>
	/// <param name="cancellationToken">A token to cancel the build.</param>
	/// <returns>The image's tag.</returns>
	public async Task<string> BuildImageAsync(string dockerfile, CancellationToken cancellationToken)
	{
		string log = Path.Combine(Folder, "image.log");
		ServiceResult<string> result = await Sandboxes.EnsureImageAsync(new SandboxImageRequest(ProjectKey, dockerfile, Rebuild: false, log), cancellationToken);
		Assert.True(result.Succeeded && result.Value is not null, $"{result.Error}\n{(File.Exists(log) ? await File.ReadAllTextAsync(log, cancellationToken) : string.Empty)}");

		return result.Value;
	}

	/// <summary>Gets a spec for one of a card's sandboxes, made the way <see cref="CardSandboxes"/> makes them: a worker has volumes for its work and its home, gates have a volume for their home, and review has none. The same card and role always get the same folders, so the same spec comes back each time.</summary>
	/// <param name="cardId">The card.</param>
	/// <param name="role">What the sandbox is for.</param>
	/// <returns>The spec.</returns>
	public SandboxSpec Spec(int cardId, SandboxRole role)
	{
		string folder = WorkspaceLayout.SandboxFolder(Folder, cardId, SandboxNames.RoleName(role));
		SandboxVolume[] volumes = role switch
		{
			SandboxRole.Worker => [new SandboxVolume(SandboxNames.Volume(ProjectKey, cardId, "work"), "/work"), new SandboxVolume(SandboxNames.Volume(ProjectKey, cardId, "home"), "/home/node")],
			SandboxRole.Gates => [new SandboxVolume(SandboxNames.Volume(ProjectKey, cardId, "gates-home"), "/home/node")],
			_ => [],
		};

		return new SandboxSpec(SandboxNames.Container(ProjectKey, cardId, role), Image, ProjectKey, cardId, role, Path.Combine(folder, "in"), Path.Combine(folder, "out")) { Volumes = volumes };
	}

	/// <inheritdoc/>
	public async ValueTask DisposeAsync()
	{
		await Docker.RemoveProjectAsync(ProjectKey);
		Docker.Dispose();
		_runner.Dispose();
		await TempFolder.DeleteAsync(Folder);
	}
}
