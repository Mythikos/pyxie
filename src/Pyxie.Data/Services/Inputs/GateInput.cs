namespace Pyxie.Data.Services.Inputs;

/// <summary>The editable fields of a gate. Its position among the gates is not an input: new gates join the end and <see cref="IGateService.MoveAsync"/> reorders them.</summary>
/// <param name="Name">The unique display name.</param>
/// <param name="Command">The executable the gate runs in a card's workspace.</param>
/// <param name="Arguments">The command-line arguments; empty when there are none.</param>
/// <param name="TimeoutMinutes">How long the gate may run before it is stopped and counted as failed.</param>
/// <param name="IsEnabled">Whether gate rounds run this gate.</param>
public sealed record GateInput(string Name, string Command, string Arguments, int TimeoutMinutes, bool IsEnabled);
