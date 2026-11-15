using Pyxie.Data.Services.Results;

namespace Pyxie.Runtime.Sandboxes;

/// <summary>Makes and removes the sandboxes rounds run in (see <c>docs/sandboxing.md</c>). Everything a project's sandboxes leave behind (containers and volumes) carries its key and the card's identifier, so it can be found and removed later, also after Pyxie stopped part-way.</summary>
public interface ISandboxes
{
	/// <summary>Checks that sandboxes can be made: the container engine answers and runs Linux containers.</summary>
	/// <param name="cancellationToken">A token to cancel the check.</param>
	/// <returns>Success, or what is wrong, in words for a person.</returns>
	Task<ServiceResult> CheckAvailableAsync(CancellationToken cancellationToken);

	/// <summary>Gets a project's image, building Pyxie's base image and the project's image when they are not there yet or a rebuild is asked for.</summary>
	/// <param name="request">The project's image.</param>
	/// <param name="cancellationToken">A token to cancel the build.</param>
	/// <returns>The image's tag, or why it could not be built; the log says more.</returns>
	/// <exception cref="SandboxUnavailableException">The container engine does not answer.</exception>
	Task<ServiceResult<string>> EnsureImageAsync(SandboxImageRequest request, CancellationToken cancellationToken);

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

	/// <summary>Lists the cards of a project that have containers or volumes.</summary>
	/// <param name="projectKey">The project's key.</param>
	/// <param name="cancellationToken">A token to cancel the operation.</param>
	/// <returns>The card identifiers.</returns>
	Task<IReadOnlySet<int>> ListCardsAsync(string projectKey, CancellationToken cancellationToken);
}
