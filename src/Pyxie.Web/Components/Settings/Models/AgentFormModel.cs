using Pyxie.Data.Entities;
using Pyxie.Data.Enums;
using Pyxie.Data.Services.Inputs;

namespace Pyxie.Web.Components.Settings.Models;

/// <summary>The mutable state of the agent editor form.</summary>
public sealed class AgentFormModel
{
	/// <summary>Gets or sets the agent name.</summary>
	public string Name { get; set; } = string.Empty;

	/// <summary>Gets or sets the harness kind. Use <see cref="ChangeHarness"/> to change it from the form, so the command and arguments follow.</summary>
	public HarnessKind Harness { get; set; }

	/// <summary>Gets or sets the selected authentication method.</summary>
	public AgentAuthentication Authentication { get; set; } = AgentAuthentication.Account;

	/// <summary>Gets or sets the executable.</summary>
	public string Command { get; set; } = string.Empty;

	/// <summary>Gets or sets the command-line arguments.</summary>
	public string Arguments { get; set; } = string.Empty;

	/// <summary>Gets or sets a value indicating whether the agent may work cards.</summary>
	public bool CanWork { get; set; } = true;

	/// <summary>Gets or sets a value indicating whether the agent may review cards.</summary>
	public bool CanReview { get; set; } = true;

	/// <summary>Gets or sets a value indicating whether the agent is the project's merge agent. A new agent is not: only one agent per project can be, and the service refuses a second.</summary>
	public bool CanMerge { get; set; }

	/// <summary>Creates the form state for a new agent using the default harness command and arguments.</summary>
	/// <returns>A new form model.</returns>
	public static AgentFormModel CreateNew()
	{
		HarnessKind harness = HarnessKind.ClaudeCode;
		return new AgentFormModel
		{
			Harness = harness,
			Command = HarnessInfo.DefaultCommand(harness),
			Arguments = HarnessInfo.DefaultArguments(harness),
		};
	}

	/// <summary>Creates the form state for editing an existing agent.</summary>
	/// <param name="agent">The agent to edit.</param>
	/// <returns>A form model holding the agent's values.</returns>
	public static AgentFormModel FromAgent(Agent agent) => new()
	{
		Name = agent.Name,
		Harness = agent.Harness,
		Authentication = agent.Authentication,
		Command = agent.Command,
		Arguments = agent.Arguments,
		CanWork = agent.CanWork,
		CanReview = agent.CanReview,
		CanMerge = agent.CanMerge,
	};

	/// <summary>
	/// Selects a harness. The command and the arguments each switch to the new harness's default unless a person has changed them:
	/// a value that is blank or still the previous harness's default is replaced, anything else is kept.
	/// </summary>
	/// <param name="harness">The harness that was selected.</param>
	public void ChangeHarness(HarnessKind harness)
	{
		HarnessKind previous = Harness;
		Harness = harness;

		if (harness == HarnessKind.Other)
		{
			Authentication = AgentAuthentication.None;
		}
		else if (previous == HarnessKind.Other)
		{
			Authentication = AgentAuthentication.Account;
		}

		Command = Prefill(Command, HarnessInfo.DefaultCommand(previous), HarnessInfo.DefaultCommand(harness));
		Arguments = Prefill(Arguments, HarnessInfo.DefaultArguments(previous), HarnessInfo.DefaultArguments(harness));
	}

	/// <summary>Converts the form state into a service input with trimmed text.</summary>
	/// <returns>The agent input.</returns>
	public AgentInput ToInput() => new AgentInput(Name.Trim(), Harness, Command.Trim(), Arguments.Trim(), CanWork, CanReview, CanMerge) { Authentication = Authentication };

	private static string Prefill(string current, string previousDefault, string newDefault)
	{
		return string.IsNullOrWhiteSpace(current) || current == previousDefault ? newDefault : current;
	}
}
