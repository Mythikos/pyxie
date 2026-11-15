using Pyxie.Data.Entities;
using Pyxie.Data.Enums;
using Pyxie.Data.Services;
using Pyxie.Data.Services.Inputs;
using Pyxie.Data.Services.Results;
using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Web;

/// <summary>Stores agents for the settings component tests the way the editor does, through <see cref="IAgentService"/>, so a merge agent is stored under the same rule as in the application.</summary>
internal static class AgentTestData
{
	/// <summary>Stores an agent with a Claude Code harness through the agent service.</summary>
	/// <param name="database">The database to write to.</param>
	/// <param name="name">The agent name.</param>
	/// <param name="canWork">Whether the agent may work.</param>
	/// <param name="canReview">Whether the agent may review.</param>
	/// <param name="canMerge">Whether the agent is the merge agent.</param>
	/// <returns>The stored agent.</returns>
	public static async Task<Agent> AddAsync(TestDatabase database, string name, bool canWork, bool canReview, bool canMerge)
	{
		AgentService service = new(database.ContextFactory, database.Clock);
		ServiceResult<Agent> result = await service.CreateAsync(new AgentInput(name, HarnessKind.ClaudeCode, "claude", string.Empty, canWork, canReview, canMerge), TestContext.Current.CancellationToken);
		Assert.True(result.Succeeded, $"The test agent '{name}' should have been stored: {result.Error}");

		return Assert.IsType<Agent>(result.Value);
	}
}
