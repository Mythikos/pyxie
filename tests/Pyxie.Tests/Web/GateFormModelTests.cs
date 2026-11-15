using Pyxie.Data.Entities;
using Pyxie.Data.Limits;
using Pyxie.Data.Services;
using Pyxie.Data.Services.Inputs;
using Pyxie.Data.Services.Results;
using Pyxie.Tests.Support;
using Pyxie.Web.Components.Settings.Models;

using Xunit;

namespace Pyxie.Tests.Web;

public sealed class GateFormModelTests
{
	[Fact]
	public void CreateNew_StartsEnabledWithTheDefaultTimeoutAndBlankText()
	{
		GateFormModel model = GateFormModel.CreateNew();

		Assert.True(model.IsEnabled, "A new gate should be enabled.");
		Assert.Equal(PyxieLimits.DefaultGateTimeoutMinutes, model.TimeoutMinutes);
		Assert.Equal(string.Empty, model.Name);
		Assert.Equal(string.Empty, model.Command);
		Assert.Equal(string.Empty, model.Arguments);
	}

	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public void FromGate_ReproducesEveryStoredField(bool isEnabled)
	{
		Gate gate = new()
		{
			Name = "Tests",
			Command = "dotnet",
			Arguments = "test --no-build",
			TimeoutMinutes = 12,
			IsEnabled = isEnabled,
		};

		GateFormModel model = GateFormModel.FromGate(gate);

		Assert.Equal("Tests", model.Name);
		Assert.Equal("dotnet", model.Command);
		Assert.Equal("test --no-build", model.Arguments);
		Assert.Equal(12, model.TimeoutMinutes);
		Assert.Equal(isEnabled, model.IsEnabled);
	}

	[Fact]
	public void ToInput_TrimsTextAndCarriesTheTimeoutAndEnabledFlagUnchanged()
	{
		GateFormModel model = new()
		{
			Name = "  Padded  ",
			Command = " run.exe ",
			Arguments = " -v ",
			TimeoutMinutes = 7,
			IsEnabled = false,
		};

		GateInput input = model.ToInput();

		Assert.Equal(new GateInput("Padded", "run.exe", "-v", 7, false), input);
	}

	[Fact]
	public void IsValid_IsTrueForAFilledInGateAndNeedsNoArguments()
	{
		GateFormModel model = GateFormModel.CreateNew();
		model.Name = "Tests";
		model.Command = "dotnet";

		Assert.True(model.IsValid, "A name and a command are enough.");
		Assert.Null(model.NameError);
		Assert.Null(model.CommandError);
		Assert.Null(model.ArgumentsError);
		Assert.Null(model.TimeoutError);
	}

	[Fact]
	public void IsValid_IsFalseForABlankNewGate()
	{
		GateFormModel model = GateFormModel.CreateNew();

		Assert.False(model.IsValid, "A blank gate has no name or command.");
		Assert.Equal("Gate name is required.", model.NameError);
		Assert.Equal("Command is required.", model.CommandError);
	}

	[Theory]
	[InlineData(0)]
	[InlineData(-5)]
	[InlineData(PyxieLimits.MaxGateTimeoutMinutes + 1)]
	public void IsValid_IsFalseWhenTheTimeoutIsOutsideTheAllowedRange(int timeoutMinutes)
	{
		GateFormModel model = GateFormModel.CreateNew();
		model.Name = "Tests";
		model.Command = "dotnet";
		model.TimeoutMinutes = timeoutMinutes;

		Assert.False(model.IsValid, "A timeout outside 1 to the maximum is invalid.");
		Assert.Equal("Timeout must be between 1 and 240 minutes.", model.TimeoutError);
	}

	[Fact]
	public void IsValid_IsFalseWhenTheArgumentsAreTooLong()
	{
		GateFormModel model = GateFormModel.CreateNew();
		model.Name = "Tests";
		model.Command = "dotnet";
		model.Arguments = new string('a', PyxieLimits.MaxArgumentsLength + 1);

		Assert.False(model.IsValid, "Arguments over the limit are invalid.");
		Assert.Equal("Arguments must be 2000 characters or fewer.", model.ArgumentsError);
	}

	[Fact]
	public async Task FlippingOnlyTheEnabledFlagAndSaving_ChangesNothingElseAboutTheStoredGate()
	{
		await using TestDatabase database = new();
		GateService service = new(database.ContextFactory);
		ServiceResult<Gate> created = await service.CreateAsync(GateTestInput.Create("Format", "dotnet", "format --verify-no-changes", 5), TestContext.Current.CancellationToken);
		Gate stored = created.Value!;

		GateFormModel model = GateFormModel.FromGate(stored);
		model.IsEnabled = false;
		ServiceResult<Gate> updated = await service.UpdateAsync(stored.Id, model.ToInput(), TestContext.Current.CancellationToken);

		Assert.True(updated.Succeeded, updated.Error);
		Gate reloaded = Assert.Single(await GateTestData.ReadAsync(database));
		Assert.False(reloaded.IsEnabled, "The gate should now be disabled.");
		Assert.Equal("Format", reloaded.Name);
		Assert.Equal("dotnet", reloaded.Command);
		Assert.Equal("format --verify-no-changes", reloaded.Arguments);
		Assert.Equal(5, reloaded.TimeoutMinutes);
		Assert.Equal(stored.SortOrder, reloaded.SortOrder);
	}
}
