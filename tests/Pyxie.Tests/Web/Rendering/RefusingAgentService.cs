using Pyxie.Data.Entities;
using Pyxie.Data.Services;
using Pyxie.Data.Services.Inputs;
using Pyxie.Data.Services.Results;

namespace Pyxie.Tests.Web.Rendering;

/// <summary>An agent service that refuses every create and update with one message and records what it was asked to save, for tests of how the editor shows a service's refusal.</summary>
internal sealed class RefusingAgentService : IAgentService
{
	private readonly string _message;

	/// <summary>Initializes a new instance of the <see cref="RefusingAgentService"/> class.</summary>
	/// <param name="message">The refusal every create and update returns.</param>
	public RefusingAgentService(string message)
	{
		_message = message;
	}

	/// <summary>Gets the inputs the editor asked to save, in order.</summary>
	public List<AgentInput> Requests { get; } = [];

	/// <inheritdoc/>
	public Task<IReadOnlyList<Agent>> ListAsync(CancellationToken cancellationToken)
	{
		return Task.FromResult<IReadOnlyList<Agent>>([]);
	}

	/// <inheritdoc/>
	public Task<ServiceResult<Agent>> CreateAsync(AgentInput input, CancellationToken cancellationToken)
	{
		Requests.Add(input);

		return Task.FromResult(ServiceResult<Agent>.Failure(_message));
	}

	/// <inheritdoc/>
	public Task<ServiceResult<Agent>> UpdateAsync(int id, AgentInput input, CancellationToken cancellationToken)
	{
		Requests.Add(input);

		return Task.FromResult(ServiceResult<Agent>.Failure(_message));
	}

	/// <inheritdoc/>
	public Task<ServiceResult> SetMergeAgentAsync(int? agentId, CancellationToken cancellationToken)
	{
		return Task.FromResult(ServiceResult.Failure(_message));
	}

	/// <inheritdoc/>
	public Task<ServiceResult> DeleteAsync(int id, CancellationToken cancellationToken)
	{
		return Task.FromResult(ServiceResult.Failure(_message));
	}
}
