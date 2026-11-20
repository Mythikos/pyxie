using Pyxie.Data.Entities;
using Pyxie.Data.Services;
using Pyxie.Data.Services.Inputs;
using Pyxie.Data.Services.Results;

namespace Pyxie.Tests.Web.Rendering;

/// <summary>An agent service that does what the real one does, but counts its creates and lets a test hold every write after it has started, so the page can be looked at while a write runs.</summary>
internal sealed class HoldingAgentService : IAgentService
{
	private readonly IAgentService _inner;
	private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);

	/// <summary>Initializes a new instance of the <see cref="HoldingAgentService"/> class.</summary>
	/// <param name="inner">The service that does the work once a write is released.</param>
	public HoldingAgentService(IAgentService inner)
	{
		_inner = inner;
	}

	/// <summary>Gets or sets a task every write waits for before it is passed on, or <see langword="null"/> to pass writes on at once.</summary>
	public Task? Hold { get; set; }

	/// <summary>Gets a task that completes once the first write has started.</summary>
	public Task Started => _started.Task;

	/// <summary>Gets the probe that counts the creates of agents.</summary>
	public CallProbe Creates { get; } = new();

	/// <inheritdoc/>
	public Task<IReadOnlyList<Agent>> ListAsync(CancellationToken cancellationToken)
	{
		return _inner.ListAsync(cancellationToken);
	}

	/// <inheritdoc/>
	public async Task<ServiceResult<Agent>> CreateAsync(AgentInput input, CancellationToken cancellationToken)
	{
		await Creates.EnterAsync(cancellationToken);
		await WaitAsync(cancellationToken);

		return await _inner.CreateAsync(input, cancellationToken);
	}

	/// <inheritdoc/>
	public Task<ServiceResult<Agent>> UpdateAsync(int id, AgentInput input, CancellationToken cancellationToken) => UpdateIfCurrentAsync(id, input, null, cancellationToken);

	/// <inheritdoc/>
	public async Task<ServiceResult<Agent>> UpdateIfCurrentAsync(int id, AgentInput input, string? expectedRevision, CancellationToken cancellationToken)
	{
		await WaitAsync(cancellationToken);

		return await _inner.UpdateIfCurrentAsync(id, input, expectedRevision, cancellationToken);
	}

	/// <inheritdoc/>
	public async Task<ServiceResult> SetMergeAgentAsync(int? agentId, CancellationToken cancellationToken)
	{
		await WaitAsync(cancellationToken);

		return await _inner.SetMergeAgentAsync(agentId, cancellationToken);
	}

	/// <inheritdoc/>
	public Task<ServiceResult> DeleteAsync(int id, CancellationToken cancellationToken) => DeleteIfCurrentAsync(id, null, cancellationToken);

	/// <inheritdoc/>
	public async Task<ServiceResult> DeleteIfCurrentAsync(int id, string? expectedRevision, CancellationToken cancellationToken)
	{
		await WaitAsync(cancellationToken);

		return await _inner.DeleteIfCurrentAsync(id, expectedRevision, cancellationToken);
	}

	private async Task WaitAsync(CancellationToken cancellationToken)
	{
		_started.TrySetResult();

		if (Hold is Task hold)
		{
			await hold.WaitAsync(cancellationToken);
		}
	}
}
