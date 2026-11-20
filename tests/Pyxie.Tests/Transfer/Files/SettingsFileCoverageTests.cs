using System.Reflection;

using Pyxie.Data.Colors;
using Pyxie.Data.Entities;
using Pyxie.Data.Services.Inputs;
using Pyxie.Data.Transfer;
using Pyxie.Data.Transfer.Files;
using Pyxie.Data.Transfer.Planning;
using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Transfer.Files;

/// <summary>Every field a person sets on an agent, a gate, a category, or the sandbox settings travels in a settings file, unless it is listed here as staying out of it, with the reason. A field added to one of those entities fails these tests until someone decides which: carry it, through the entity's input, the export, a new file version, and the import, or list it here.</summary>
public sealed class SettingsFileCoverageTests
{
	private const BindingFlags PublicInstance = BindingFlags.Public | BindingFlags.Instance;

	// What the database keeps for itself, never what a person sets, so it stays out of a file that outlives the database.
	private static readonly IReadOnlyList<(Type Entity, string Field, string Reason)> s_excluded =
	[
		(typeof(Agent), nameof(Agent.Id), "Ids belong to one database; an import matches agents by name."),
		(typeof(Gate), nameof(Gate.Id), "Ids belong to one database; an import matches gates by name."),
		(typeof(Gate), nameof(Gate.SortOrder), "A gate's place in the file's list is its place in the order gates run."),
		(typeof(Category), nameof(Category.Id), "Ids belong to one database; an import matches categories by name."),
		(typeof(SandboxSettings), nameof(SandboxSettings.Id), "Ids belong to one database; a project has one sandbox settings row."),
		(typeof(SandboxSettings), nameof(SandboxSettings.UpdatedUtc), "When the settings were last saved is the database's record, not a setting."),
		(typeof(SandboxSettings), nameof(SandboxSettings.Version), "The version guards concurrent saves within one database."),
	];

	// The input that carries an entity's fields into and out of a settings file.
	private static readonly IReadOnlyList<(Type Entity, Type Input)> s_carriers =
	[
		(typeof(Agent), typeof(AgentInput)),
		(typeof(Gate), typeof(GateInput)),
		(typeof(Category), typeof(CategoryInput)),
		(typeof(SandboxSettings), typeof(SandboxSettingsInput)),
	];

	public static TheoryData<string> Entities { get; } = [.. s_carriers.Select(carrier => carrier.Entity.Name)];

	public static IEnumerable<TheoryDataRow<string, string>> CarriedFields { get; } =
	[
		.. s_carriers.SelectMany(carrier => SettableFields(carrier.Entity).Where(field => !IsExcluded(carrier.Entity, field.Name)).Select(field => new TheoryDataRow<string, string>(carrier.Entity.Name, field.Name) { TestDisplayName = $"{carrier.Entity.Name}.{field.Name}" })),
	];

	[Theory]
	[MemberData(nameof(Entities))]
	public void EveryFieldOfTheEntity_IsCarriedByTheFileOrExcludedFromIt(string entityName)
	{
		(Type entity, Type input) = CarrierOf(entityName);
		List<string> problems = [];

		foreach (PropertyInfo field in SettableFields(entity))
		{
			bool excluded = IsExcluded(entity, field.Name);
			PropertyInfo? carried = input.GetProperty(field.Name, PublicInstance);
			if (!excluded && carried is null)
			{
				problems.Add($"{entity.Name}.{field.Name} is neither carried by the settings file nor excluded from it. Decide which. To carry it, add it to {input.Name} and to what an export makes of a {entity.Name} (SettingsSnapshot.ToSettings), write and read it in a new settings file version (SettingsFileFormat), and import it. To leave it out of the file, add it to the excluded fields in {nameof(SettingsFileCoverageTests)} with the reason.");
			}
			else if (excluded && carried is not null)
			{
				problems.Add($"{entity.Name}.{field.Name} is carried by {input.Name} and also excluded from the settings file in {nameof(SettingsFileCoverageTests)}; it can only be one.");
			}
			else if (carried is not null && carried.PropertyType != field.PropertyType)
			{
				problems.Add($"{entity.Name}.{field.Name} is a {field.PropertyType.Name}, but {input.Name} carries it as a {carried.PropertyType.Name}.");
			}
		}

		Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
	}

	[Theory]
	[MemberData(nameof(Entities))]
	public void EveryFieldTheInputCarries_IsAFieldOfTheEntity(string entityName)
	{
		(Type entity, Type input) = CarrierOf(entityName);
		HashSet<string> fields = [.. SettableFields(entity).Select(field => field.Name)];

		List<string> strays = [.. input.GetProperties(PublicInstance).Select(property => property.Name).Where(name => !fields.Contains(name))];

		Assert.True(strays.Count == 0, $"{input.Name} carries {string.Join(", ", strays)}, which {entity.Name} does not have: a settings file would hold a value that nothing stores.");
	}

	[Theory]
	[MemberData(nameof(Entities))]
	public void EveryExcludedField_IsAFieldOfTheEntity(string entityName)
	{
		(Type entity, _) = CarrierOf(entityName);
		HashSet<string> fields = [.. SettableFields(entity).Select(field => field.Name)];

		List<string> stale = [.. s_excluded.Where(exclusion => exclusion.Entity == entity && !fields.Contains(exclusion.Field)).Select(exclusion => exclusion.Field)];

		Assert.True(stale.Count == 0, $"{nameof(SettingsFileCoverageTests)} excludes {string.Join(", ", stale)} of {entity.Name}, which it no longer has: remove the exclusion.");
	}

	[Theory]
	[MemberData(nameof(CarriedFields))]
	public void EveryCarriedField_SurvivesAnExportToAFileAndAnotherRead(string entityName, string fieldName)
	{
		Agent agent = new();
		Gate gate = new();
		Category category = new();
		SandboxSettings sandbox = new();
		object owner = entityName switch
		{
			nameof(Agent) => agent,
			nameof(Gate) => gate,
			nameof(Category) => category,
			_ => sandbox,
		};
		PropertyInfo changed = owner.GetType().GetProperty(fieldName, PublicInstance) ?? throw new InvalidOperationException($"{entityName} has no field {fieldName}.");
		changed.SetValue(owner, OtherValue(changed, changed.GetValue(owner)));

		ProjectSettings exported = new SettingsSnapshot([agent], [gate], [category], sandbox).ToSettings();
		ProjectSettings read = ResultAssert.Succeeded(SettingsFileReader.Read(SettingsFileWriter.Write(exported)));

		object carried = entityName switch
		{
			nameof(Agent) => Assert.Single(read.Agents ?? []),
			nameof(Gate) => Assert.Single(read.Gates ?? []),
			nameof(Category) => Assert.Single(read.Categories ?? []),
			_ => read.Sandbox ?? throw new InvalidOperationException("The export always holds the sandbox settings."),
		};
		foreach (PropertyInfo field in SettableFields(owner.GetType()).Where(field => !IsExcluded(owner.GetType(), field.Name)))
		{
			object? stored = field.GetValue(owner);
			object? imported = carried.GetType().GetProperty(field.Name, PublicInstance)?.GetValue(carried);
			Assert.True(Equals(stored, imported), $"With {entityName}.{fieldName} changed, {entityName}.{field.Name} was {stored} but read back from the exported file as {imported}: the export, the file, or the reader drops it or swaps it with another field.");
		}
	}

	private static (Type Entity, Type Input) CarrierOf(string entityName)
	{
		return s_carriers.Single(carrier => carrier.Entity.Name == entityName);
	}

	private static IEnumerable<PropertyInfo> SettableFields(Type entity)
	{
		return entity.GetProperties(PublicInstance).Where(property => property.GetSetMethod() is not null);
	}

	private static bool IsExcluded(Type entity, string field)
	{
		return s_excluded.Any(exclusion => exclusion.Entity == entity && exclusion.Field == field);
	}

	// A value of the field's type that differs from the one it has. Text ends with a line break, as a stored Dockerfile does, so the export's normalizing of the Dockerfile keeps it as it is.
	private static object OtherValue(PropertyInfo field, object? current)
	{
		return current switch
		{
			string => $"{field.DeclaringType?.Name}.{field.Name} value\n",
			bool flag => !flag,
			int number => number + 1,
			Enum value => Enum.GetValues(value.GetType()).Cast<object>().First(other => !other.Equals(value)),
			HexColor color => new HexColor((byte)~color.Red, (byte)~color.Green, (byte)~color.Blue),
			_ => throw new InvalidOperationException($"{nameof(SettingsFileCoverageTests)} has no other value for {field.DeclaringType?.Name}.{field.Name}, a {field.PropertyType.Name}; add one to {nameof(OtherValue)}."),
		};
	}
}
