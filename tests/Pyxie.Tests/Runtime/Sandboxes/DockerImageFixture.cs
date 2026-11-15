using Pyxie.Data.Sandboxes;
using Pyxie.Data.Services.Results;
using Pyxie.Runtime.Processes;
using Pyxie.Runtime.Sandboxes;
using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Runtime.Sandboxes;

/// <summary>The image the Docker tests start their sandboxes from, built once for the whole <see cref="DockerSandboxCollection"/> with <see cref="DockerSandboxes.EnsureImageAsync"/> from the default Dockerfile (<see cref="SandboxDefaults.Dockerfile"/>). The first build on a machine also builds Pyxie's base image, which takes minutes; the base image stays afterwards, because the Pyxie installed on the machine uses the same one, and only the fixture's own project image is removed. When the Docker tests are off it does nothing, because xUnit makes collection fixtures even for tests it then skips.</summary>
public sealed class DockerImageFixture : IAsyncLifetime
{
	private string? _folder;
	private ServiceResult<string>? _image;
	private string _log = string.Empty;

	/// <summary>Gets the project key the fixture's image is built for.</summary>
	public string ProjectKey { get; private set; } = string.Empty;

	/// <summary>Gets the tag of the image, failing the test with the build's error and the end of its log when it could not be built.</summary>
	public string Image
	{
		get
		{
			Assert.True(_image is not null, $"The sandbox image was not built: {DockerTestSwitch.VariableName} was not 1 when the collection started.");
			Assert.True(_image.Succeeded && _image.Value is not null, $"{_image.Error}\n{Tail(_log)}");

			return _image.Value;
		}
	}

	/// <inheritdoc/>
	public async ValueTask InitializeAsync()
	{
		if (!DockerTestSwitch.IsOn)
		{
			return;
		}

		_folder = TempFolder.Create("pyxie-dimg");
		ProjectKey = SandboxNames.ProjectKey(_folder);
		string logPath = Path.Combine(_folder, "image.log");

		using ProcessRunner runner = new(TimeProvider.System);
		SandboxImageRequest request = new(ProjectKey, SandboxDefaults.Dockerfile, Rebuild: false, logPath);
		_image = await new DockerSandboxes(runner, TimeProvider.System).EnsureImageAsync(request, CancellationToken.None);
		_log = File.Exists(logPath) ? await File.ReadAllTextAsync(logPath, CancellationToken.None) : string.Empty;
	}

	/// <inheritdoc/>
	public async ValueTask DisposeAsync()
	{
		if (_folder is null)
		{
			return;
		}

		using (DockerProcess docker = new())
		{
			await docker.RemoveProjectAsync(ProjectKey);
		}

		await TempFolder.DeleteAsync(_folder);
	}

	private static string Tail(string log)
	{
		return log.Length <= 4000 ? log : log[^4000..];
	}
}
