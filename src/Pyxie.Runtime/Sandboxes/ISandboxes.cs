using Pyxie.Data.Services.Results;

namespace Pyxie.Runtime.Sandboxes;

/// <summary>Makes and removes the sandboxes rounds run in (see <c>docs/sandboxing.md</c>). Everything a project's sandboxes leave behind (containers and volumes) carries its key and the card's identifier, so it can be found and removed later, also after Pyxie stopped part-way.</summary>
public interface ISandboxes
{
	/// <summary>Stops a card's containers before recovering its host output, preserving its workspace volumes.</summary>
	/// <param name="projectKey">The owning project.</param>
	/// <param name="cardId">The card.</param>
	/// <param name="cancellationToken">A token to cancel stopping.</param>
	/// <returns>A task completing once no container can write output.</returns>
	Task QuiesceCardAsync(string projectKey, int cardId, CancellationToken cancellationToken);

	/// <summary>Checks that sandboxes can be made: the container engine answers and runs Linux containers.</summary>
	/// <param name="cancellationToken">A token to cancel the check.</param>
	/// <returns>Success, or what is wrong, in words for a person.</returns>
	Task<ServiceResult> CheckAvailableAsync(CancellationToken cancellationToken);

	/// <summary>Gets a project's image, building Pyxie's base image and the project's image when they are not there yet or a rebuild is asked for. Builds run one at a time; a request waits for the one ahead of it, and calls <see cref="SandboxImageRequest.BuildStarting"/> when its own build begins.</summary>
	/// <param name="request">The project's image.</param>
	/// <param name="cancellationToken">A token to cancel the build, which stops it in the engine and leaves the images as they were.</param>
	/// <returns>The image's tag, or why it could not be built; the log says more.</returns>
	/// <exception cref="SandboxUnavailableException">The container engine does not answer.</exception>
	/// <exception cref="OperationCanceledException">The build was cancelled.</exception>
	Task<ServiceResult<string>> EnsureImageAsync(SandboxImageRequest request, CancellationToken cancellationToken);

	/// <summary>Finds a project's image for a Dockerfile on Pyxie's current base image, which is the image a round would use. It is asked of the engine every time, because an image can be removed by Pyxie or by anyone else at any moment.</summary>
	/// <param name="projectKey">The project's key.</param>
	/// <param name="dockerfile">The project's Dockerfile.</param>
	/// <param name="cancellationToken">A token to cancel the operation.</param>
	/// <returns>The image's tag, or <see langword="null"/> when the engine does not have it.</returns>
	/// <exception cref="SandboxUnavailableException">The container engine does not answer.</exception>
	Task<string?> FindImageAsync(string projectKey, string dockerfile, CancellationToken cancellationToken);

	/// <summary>Removes what Pyxie's image builds left that nothing needs any more: the project's images other than the one for its Dockerfile, Pyxie's base images other than the current one that no remaining project image is built on, and the build cache of Pyxie's images that no image uses. An image a container still uses stays, and is removed by a later call once that container is gone. Nothing is forced, and only what Pyxie built is touched.</summary>
	/// <remarks>Call it only while no round can be starting a sandbox: a round that already found its image would otherwise find it gone when it starts the container. It does not wait for a build that is running; it does nothing then and says so.</remarks>
	/// <param name="projectKey">The project's key.</param>
	/// <param name="dockerfile">The project's Dockerfile, whose image stays.</param>
	/// <param name="cancellationToken">A token to cancel the operation.</param>
	/// <returns><see langword="true"/> when it ran; <see langword="false"/> when a build was running, so nothing was removed.</returns>
	/// <exception cref="SandboxUnavailableException">The container engine does not answer.</exception>
	Task<bool> RemoveUnusedImagesAsync(string projectKey, string dockerfile, CancellationToken cancellationToken);

	/// <summary>Starts a sandbox, or picks up a kept one that is still running from the same image.</summary>
	/// <param name="spec">What the sandbox is made from and given.</param>
	/// <param name="cancellationToken">A token to cancel the start.</param>
	/// <returns>The running sandbox, with its firewall in place.</returns>
	/// <exception cref="SandboxException">The sandbox could not start, for example because its image is broken.</exception>
	/// <exception cref="SandboxUnavailableException">The container engine does not answer.</exception>
	Task<ISandbox> StartAsync(SandboxSpec spec, CancellationToken cancellationToken);

	/// <summary>Removes everything a card's sandboxes left: its containers and its volumes.</summary>
	/// <param name="projectKey">The project's key.</param>
	/// <param name="cardId">The card.</param>
	/// <param name="cancellationToken">A token to cancel the operation.</param>
	/// <returns>A task that completes when they are gone.</returns>
	Task RemoveCardAsync(string projectKey, int cardId, CancellationToken cancellationToken);

	/// <summary>Removes the gate, review, and resolve sandboxes of a project that are still there, which only happens when Pyxie stopped part-way through a round.</summary>
	/// <param name="projectKey">The project's key.</param>
	/// <param name="cancellationToken">A token to cancel the operation.</param>
	/// <returns>A task that completes when they are gone.</returns>
	Task RemoveLeftoverRoundsAsync(string projectKey, CancellationToken cancellationToken);

	/// <summary>Removes everything a project's sandboxes and image builds left: its containers, its volumes, and its images, tagged or not. Pyxie's base image and build cache are shared by every project, so they are left to <see cref="RemoveUnusedImagesAsync"/>, which removes what of them no image needs. A project's key is a hash of its folder, so a project imported later into the same folder gets the same key and would otherwise find what this one left.</summary>
	/// <remarks>Call it only while none of the project's rounds or image builds runs.</remarks>
	/// <param name="projectKey">The project's key.</param>
	/// <param name="cancellationToken">A token to cancel the operation.</param>
	/// <returns>A task that completes when they are gone.</returns>
	/// <exception cref="SandboxUnavailableException">The container engine does not answer.</exception>
	Task RemoveProjectAsync(string projectKey, CancellationToken cancellationToken);

	/// <summary>Lists the cards of a project that have containers or volumes.</summary>
	/// <param name="projectKey">The project's key.</param>
	/// <param name="cancellationToken">A token to cancel the operation.</param>
	/// <returns>The card identifiers.</returns>
	Task<IReadOnlySet<int>> ListCardsAsync(string projectKey, CancellationToken cancellationToken);
}
