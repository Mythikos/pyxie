using Pyxie.Data.Entities;
using Pyxie.Data.Services.Inputs;

using Xunit;

namespace Pyxie.Tests.Services;

/// <summary><see cref="GateInput.From"/> gives every field a person sets on a gate, each in its own place; the gate's place in the order is not one of them, as the list of gates holds it.</summary>
public sealed class GateInputTests
{
	[Theory]
	[InlineData(1, true)]
	[InlineData(45, false)]
	public void From_GivesEveryFieldOfTheGate(int timeoutMinutes, bool isEnabled)
	{
		Gate gate = new() { Id = 3, Name = "Tests", Command = "dotnet", Arguments = "test --no-build", TimeoutMinutes = timeoutMinutes, SortOrder = 2, IsEnabled = isEnabled };

		GateInput input = GateInput.From(gate);

		Assert.Equal(new GateInput("Tests", "dotnet", "test --no-build", timeoutMinutes, isEnabled), input);
	}
}
