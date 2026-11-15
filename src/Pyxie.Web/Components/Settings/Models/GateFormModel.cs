using Pyxie.Data.Entities;
using Pyxie.Data.Limits;
using Pyxie.Data.Services.Inputs;

namespace Pyxie.Web.Components.Settings.Models;

/// <summary>The mutable state of the gate editor form. The field rules are <see cref="GateInputRules"/>, the same ones the gate service applies, so the form and the service report the same messages.</summary>
public sealed class GateFormModel
{
	/// <summary>Gets or sets the gate name.</summary>
	public string Name { get; set; } = string.Empty;

	/// <summary>Gets or sets the executable.</summary>
	public string Command { get; set; } = string.Empty;

	/// <summary>Gets or sets the command-line arguments.</summary>
	public string Arguments { get; set; } = string.Empty;

	/// <summary>Gets or sets the timeout in minutes.</summary>
	public int TimeoutMinutes { get; set; } = PyxieLimits.DefaultGateTimeoutMinutes;

	/// <summary>Gets or sets a value indicating whether gate rounds run the gate.</summary>
	public bool IsEnabled { get; set; } = true;

	/// <summary>Gets the message for an invalid name, or <see langword="null"/> when the name is valid.</summary>
	public string? NameError => GateInputRules.ValidateName(Name);

	/// <summary>Gets the message for an invalid command, or <see langword="null"/> when the command is valid.</summary>
	public string? CommandError => GateInputRules.ValidateCommand(Command);

	/// <summary>Gets the message for invalid arguments, or <see langword="null"/> when the arguments are valid.</summary>
	public string? ArgumentsError => GateInputRules.ValidateArguments(Arguments);

	/// <summary>Gets the message for an invalid timeout, or <see langword="null"/> when the timeout is valid.</summary>
	public string? TimeoutError => GateInputRules.ValidateTimeout(TimeoutMinutes);

	/// <summary>Gets a value indicating whether every field passes its rule.</summary>
	public bool IsValid => NameError is null && CommandError is null && ArgumentsError is null && TimeoutError is null;

	/// <summary>Creates the form state for a new gate: enabled, with the default timeout.</summary>
	/// <returns>A new form model.</returns>
	public static GateFormModel CreateNew() => new();

	/// <summary>Creates the form state for editing an existing gate.</summary>
	/// <param name="gate">The gate to edit.</param>
	/// <returns>A form model holding the gate's values.</returns>
	public static GateFormModel FromGate(Gate gate) => new()
	{
		Name = gate.Name,
		Command = gate.Command,
		Arguments = gate.Arguments,
		TimeoutMinutes = gate.TimeoutMinutes,
		IsEnabled = gate.IsEnabled,
	};

	/// <summary>Converts the form state into a service input with trimmed text.</summary>
	/// <returns>The gate input.</returns>
	public GateInput ToInput() => new(Name.Trim(), Command.Trim(), Arguments.Trim(), TimeoutMinutes, IsEnabled);
}
