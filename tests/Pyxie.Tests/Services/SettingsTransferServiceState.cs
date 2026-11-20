using System.Globalization;

using Microsoft.EntityFrameworkCore;

using Pyxie.Data.Persistence;
using Pyxie.Data.Transfer;
using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Services;

/// <summary>Everything a settings import could touch, read straight from the tables, one line per row and every field a write could change: agents, gates with their positions, categories, the sandbox settings rows, and every card's references, version, and timestamp. A test reads it before and after an operation and compares, so a failure names the table and shows both sides.</summary>
/// <param name="Agents">The agents, by identifier.</param>
/// <param name="Gates">The gates, by identifier, with their positions.</param>
/// <param name="Categories">The categories, by identifier.</param>
/// <param name="Sandbox">The sandbox settings rows, by identifier.</param>
/// <param name="Cards">Every card, by identifier.</param>
public sealed record SettingsTransferServiceState(IReadOnlyList<string> Agents, IReadOnlyList<string> Gates, IReadOnlyList<string> Categories, IReadOnlyList<string> Sandbox, IReadOnlyList<string> Cards)
{
	/// <summary>Reads the state of a project database.</summary>
	/// <param name="database">The database.</param>
	/// <returns>The state.</returns>
	public static async Task<SettingsTransferServiceState> ReadAsync(TestDatabase database)
	{
		CancellationToken cancellationToken = TestContext.Current.CancellationToken;
		await using ProjectDbContext context = database.CreateContext();

		List<string> agents = [.. (await context.Agents.AsNoTracking().OrderBy(agent => agent.Id).ToListAsync(cancellationToken)).Select(agent => $"{agent.Id} '{agent.Name}' harness={agent.Harness} command='{agent.Command}' arguments='{agent.Arguments}' work={agent.CanWork} review={agent.CanReview} merge={agent.CanMerge}")];
		List<string> gates = [.. (await context.Gates.AsNoTracking().OrderBy(gate => gate.Id).ToListAsync(cancellationToken)).Select(gate => $"{gate.Id} '{gate.Name}' order={gate.SortOrder} command='{gate.Command}' arguments='{gate.Arguments}' timeout={gate.TimeoutMinutes} enabled={gate.IsEnabled}")];
		List<string> categories = [.. (await context.Categories.AsNoTracking().OrderBy(category => category.Id).ToListAsync(cancellationToken)).Select(category => $"{category.Id} '{category.Name}' color={category.Color}")];
		List<string> sandbox = [.. (await context.SandboxSettings.AsNoTracking().OrderBy(settings => settings.Id).ToListAsync(cancellationToken)).Select(settings => $"{settings.Id} version={settings.Version} updated={Stamp(settings.UpdatedUtc)} dockerfile='{settings.Dockerfile.ReplaceLineEndings("\\n")}'")];
		List<string> cards = [.. (await context.Cards.AsNoTracking().OrderBy(card => card.Id).ToListAsync(cancellationToken)).Select(card => $"{card.Id} '{card.Name}' lane={card.Lane} category={card.CategoryId} worker={card.WorkerAgentId?.ToString(CultureInfo.InvariantCulture) ?? "none"} reviewer={card.ReviewerAgentId?.ToString(CultureInfo.InvariantCulture) ?? "none"} version={card.Version} updated={Stamp(card.UpdatedUtc)}")];

		return new SettingsTransferServiceState(agents, gates, categories, sandbox, cards);
	}

	/// <summary>Asserts that nothing at all changed since this state was read.</summary>
	/// <param name="after">The state read afterwards.</param>
	public void AssertUnchanged(SettingsTransferServiceState after)
	{
		AssertSame("agents", Agents, after.Agents);
		AssertSame("gates", Gates, after.Gates);
		AssertSame("categories", Categories, after.Categories);
		AssertSame("sandbox settings", Sandbox, after.Sandbox);
		AssertSame("cards", Cards, after.Cards);
	}

	/// <summary>Asserts that the rows of one section did not change since this state was read. The cards are not part of any section.</summary>
	/// <param name="section">The section.</param>
	/// <param name="after">The state read afterwards.</param>
	public void AssertUnchanged(TransferSection section, SettingsTransferServiceState after)
	{
		switch (section)
		{
			case TransferSection.Agents:
				AssertSame("agents", Agents, after.Agents);
				break;

			case TransferSection.Gates:
				AssertSame("gates", Gates, after.Gates);
				break;

			case TransferSection.Categories:
				AssertSame("categories", Categories, after.Categories);
				break;

			default:
				AssertSame("sandbox settings", Sandbox, after.Sandbox);
				break;
		}
	}

	private static void AssertSame(string table, IReadOnlyList<string> before, IReadOnlyList<string> after)
	{
		Assert.True(before.SequenceEqual(after, StringComparer.Ordinal), $"The {table} changed.\nBefore:\n  {string.Join("\n  ", before)}\nAfter:\n  {string.Join("\n  ", after)}");
	}

	private static string Stamp(DateTime value)
	{
		return value.ToString("O", CultureInfo.InvariantCulture);
	}
}
