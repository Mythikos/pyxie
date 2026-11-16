using Pyxie.Data.Services.Results;
using Pyxie.Runtime.Sandboxes;
using Pyxie.Tests.Support;

namespace Pyxie.Tests.Runtime.Orchestration;

/// <summary>Sandboxes a test can make fail. Every call goes to the <see cref="LocalSandboxes"/> it wraps until the test sets a failure, which stands for the container engine not running (<see cref="UnavailableReason"/>), a project image that does not build (<see cref="ImageError"/>), or a sandbox that cannot start (<see cref="StartFailure"/>). Clearing the failure makes the sandboxes work again. The failures may be set and cleared while the pipeline runs.</summary>
public sealed class SwitchableSandboxes : ISandboxes
{
	private volatile string? _unavailableReason;
	private volatile string? _imageError;
	private volatile Exception? _startFailure;

	/// <summary>Initializes a new instance of the <see cref="SwitchableSandboxes"/> class.</summary>
	/// <param name="local">The stand-in sandboxes every call goes to while no failure is set.</param>
	public SwitchableSandboxes(LocalSandboxes local)
	{
		Local = local;
	}

	/// <summary>Gets the stand-in sandboxes, which record every sandbox that started and name the folders they work in.</summary>
	public LocalSandboxes Local { get; }

	/// <summary>Gets or sets why sandboxes cannot be made, which <see cref="CheckAvailableAsync"/> then reports; <see langword="null"/> while they can.</summary>
	public string? UnavailableReason
	{
		get => _unavailableReason;
		set => _unavailableReason = value;
	}

	/// <summary>Gets or sets why the project's image does not build, which <see cref="EnsureImageAsync"/> then reports; <see langword="null"/> while it builds.</summary>
	public string? ImageError
	{
		get => _imageError;
		set => _imageError = value;
	}

	/// <summary>Gets or sets what <see cref="StartAsync"/> throws, such as a <see cref="SandboxException"/> or a <see cref="SandboxUnavailableException"/>; <see langword="null"/> while sandboxes start.</summary>
	public Exception? StartFailure
	{
		get => _startFailure;
		set => _startFailure = value;
	}

	/// <inheritdoc/>
	public Task<ServiceResult> CheckAvailableAsync(CancellationToken cancellationToken)
	{
		return _unavailableReason is string reason ? Task.FromResult(ServiceResult.Failure(reason)) : Local.CheckAvailableAsync(cancellationToken);
	}

	/// <inheritdoc/>
	public Task<ServiceResult<string>> EnsureImageAsync(SandboxImageRequest request, CancellationToken cancellationToken)
	{
		return _imageError is string error ? Task.FromResult(ServiceResult<string>.Failure(error)) : Local.EnsureImageAsync(request, cancellationToken);
	}

	/// <inheritdoc/>
	public Task<string?> FindImageAsync(string projectKey, string dockerfile, CancellationToken cancellationToken)
	{
		return Local.FindImageAsync(projectKey, dockerfile, cancellationToken);
	}

	/// <inheritdoc/>
	public Task<bool> RemoveUnusedImagesAsync(string projectKey, string dockerfile, CancellationToken cancellationToken)
	{
		return Local.RemoveUnusedImagesAsync(projectKey, dockerfile, cancellationToken);
	}

	/// <inheritdoc/>
	public Task<ISandbox> StartAsync(SandboxSpec spec, CancellationToken cancellationToken)
	{
		return _startFailure is Exception failure ? Task.FromException<ISandbox>(failure) : Local.StartAsync(spec, cancellationToken);
	}

	/// <inheritdoc/>
	public Task RemoveCardAsync(string projectKey, int cardId, CancellationToken cancellationToken)
	{
		return Local.RemoveCardAsync(projectKey, cardId, cancellationToken);
	}

	/// <inheritdoc/>
	public Task RemoveLeftoverRoundsAsync(string projectKey, CancellationToken cancellationToken)
	{
		return Local.RemoveLeftoverRoundsAsync(projectKey, cancellationToken);
	}

	/// <inheritdoc/>
	public Task<IReadOnlySet<int>> ListCardsAsync(string projectKey, CancellationToken cancellationToken)
	{
		return Local.ListCardsAsync(projectKey, cancellationToken);
	}
}
