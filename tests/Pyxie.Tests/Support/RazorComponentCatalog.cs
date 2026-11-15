using System.Reflection;

using Blazorise;

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;

using Pyxie.Web.Components.Settings;

namespace Pyxie.Tests.Support;

/// <summary>Resolves the components used in Razor markup and checks their attributes against the parameters they really declare.</summary>
/// <remarks>Blazorise components accept arbitrary unmatched attributes, so a parameter that was renamed, moved, or removed between releases still compiles. It then does nothing at run time. This catalog turns that silent failure into a test failure.</remarks>
public static class RazorComponentCatalog
{
	private const string BindPrefix = "@bind-";
	private const string ChangedSuffix = "Changed";

	// Razor itself consumes this attribute on templated components; it is not a component parameter.
	private static readonly string[] s_razorAttributes = ["Context"];

	private static readonly Lazy<IReadOnlyDictionary<string, Type>> s_components = new(FindComponents);

	/// <summary>Finds the attributes of a component element that are not parameters of that component.</summary>
	/// <param name="usage">The component element.</param>
	/// <returns>One message per problem; empty when the element is valid or is not a known component.</returns>
	public static IReadOnlyList<string> FindParameterProblems(RazorComponentUsage usage)
	{
		if (!s_components.Value.TryGetValue(usage.Name, out Type? component))
		{
			return [];
		}

		List<string> problems = [];

		foreach (string attribute in usage.Attributes)
		{
			string? parameter = ParameterName(attribute);
			if (parameter is null || s_razorAttributes.Contains(parameter) || DeclaresTypeParameter(component, parameter))
			{
				continue;
			}

			string? problem = CheckParameter(component, parameter);
			if (problem is null && attribute.StartsWith(BindPrefix, StringComparison.Ordinal))
			{
				problem = CheckParameter(component, parameter + ChangedSuffix);
			}

			if (problem is not null)
			{
				problems.Add($"<{usage.Name}> attribute '{attribute}': {problem}");
			}
		}

		return problems;
	}

	// Attributes starting with an uppercase letter are meant as component parameters; lowercase ones are plain HTML attributes that cannot be validated.
	private static string? ParameterName(string attribute)
	{
		if (attribute.StartsWith(BindPrefix, StringComparison.Ordinal))
		{
			string name = attribute[BindPrefix.Length..];
			int modifier = name.IndexOf(':', StringComparison.Ordinal);

			return modifier < 0 ? name : name[..modifier];
		}

		return char.IsAsciiLetterUpper(attribute[0]) ? attribute : null;
	}

	private static bool DeclaresTypeParameter(Type component, string name)
	{
		return component.IsGenericTypeDefinition && component.GetGenericArguments().Any(argument => argument.Name == name);
	}

	private static string? CheckParameter(Type component, string name)
	{
		PropertyInfo[] parameters = component.GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(property => property.Name == name && Attribute.IsDefined(property, typeof(ParameterAttribute), inherit: true)).ToArray();

		if (parameters.Length == 0)
		{
			return $"{component.Name} declares no parameter named '{name}'; it is ignored at run time.";
		}

		return parameters.Any(parameter => Attribute.IsDefined(parameter, typeof(ObsoleteAttribute), inherit: true)) ? $"parameter '{name}' is obsolete." : null;
	}

	private static Dictionary<string, Type> FindComponents()
	{
		Assembly[] assemblies =
		[
			typeof(AgentEditor).Assembly,
			typeof(Button).Assembly,
			typeof(ComponentBase).Assembly,
			typeof(NavLink).Assembly,
		];

		Dictionary<string, Type> components = new(StringComparer.Ordinal);

		foreach (Type type in assemblies.Distinct().SelectMany(assembly => assembly.GetExportedTypes()))
		{
			if (type.IsClass && !type.IsAbstract && typeof(IComponent).IsAssignableFrom(type))
			{
				components.TryAdd(type.Name.Split('`')[0], type);
			}
		}

		return components;
	}
}
