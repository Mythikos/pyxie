using System.Globalization;
using System.Text.RegularExpressions;

using Pyxie.Data.Enums;

namespace Pyxie.Data.Services.Pipeline;

/// <summary>The tokens an agent's arguments can carry, which each round fills in with its card's values before the harness starts, such as <c>--name {CARD_NAME}</c> for the name of a Claude Code session. A token is one of these names in capitals between braces, standing alone or inside a longer argument (<c>--name=card-{CARD_ID}</c>). The arguments are split first and each one is filled in once, so a value with spaces or quotes stays inside its argument and a value that looks like a token is not filled in again; a value is not quoted for a shell. <c>${NAME}</c> is left for a shell to expand (an agent that runs <c>sh -c</c>), and braces that do not hold a name in capitals, such as JSON, are left as they are. Any other name in capitals between braces is refused, both when the agent is saved and when a round would run it.</summary>
public static partial class AgentArgumentTokens
{
	/// <summary>The card's identifier, such as <c>12</c>.</summary>
	public const string CardId = "{CARD_ID}";

	/// <summary>The card's name.</summary>
	public const string CardName = "{CARD_NAME}";

	/// <summary>The card's branch, such as <c>pyxie/card-12</c>.</summary>
	public const string Branch = "{BRANCH}";

	/// <summary>The stage the round runs, as the briefing names it: <c>Work</c>, <c>Review</c>, or <c>Resolve</c>.</summary>
	public const string Stage = "{STAGE}";

	/// <summary>The attempt of the card's run the round belongs to, such as <c>2</c>.</summary>
	public const string Attempt = "{ATTEMPT}";

	/// <summary>Gets every token, in the order messages list them.</summary>
	public static IReadOnlyList<string> All { get; } = [CardId, CardName, Branch, Stage, Attempt];

	/// <summary>Checks that arguments use no name in capitals between braces other than the tokens.</summary>
	/// <param name="arguments">The configured arguments string.</param>
	/// <returns>A user-presentable message naming what is not a token and listing the tokens, or <see langword="null"/> when there is nothing wrong.</returns>
	public static string? Validate(string arguments)
	{
		return Describe(Unknown([arguments]));
	}

	/// <summary>Fills in the tokens of each argument with the values of a round.</summary>
	/// <param name="arguments">The arguments, already split, each one argument.</param>
	/// <param name="work">The round, whose card and stage give the values.</param>
	/// <param name="expanded">The arguments with their tokens filled in, as many as were given; empty when there is a problem.</param>
	/// <returns><see langword="null"/> on success, otherwise the message of <see cref="Validate"/>: an argument uses a name in capitals between braces that is not a token.</returns>
	public static string? TryExpand(IReadOnlyList<string> arguments, PipelineWork work, out IReadOnlyList<string> expanded)
	{
		if (Describe(Unknown(arguments)) is string problem)
		{
			expanded = [];

			return problem;
		}

		expanded = [.. arguments.Select(argument => TokenShape().Replace(argument, match => ValueOf(match.Value, work)))];

		return null;
	}

	/// <summary>Splits text into the tokens it uses and the text between them, in order, so a view can mark the tokens without knowing their shape. Only the tokens Pyxie fills in count; <c>${HOME}</c> and braces around anything else stay plain text.</summary>
	/// <param name="text">The text, such as an agent's arguments or its whole command line.</param>
	/// <returns>The parts, which joined give back the text; empty for empty text.</returns>
	public static IReadOnlyList<ArgumentTextPart> Split(string text)
	{
		List<ArgumentTextPart> parts = [];
		int plainStart = 0;

		foreach (Match match in TokenShape().Matches(text))
		{
			if (!All.Contains(match.Value, StringComparer.Ordinal))
			{
				continue;
			}

			if (match.Index > plainStart)
			{
				parts.Add(new ArgumentTextPart(text[plainStart..match.Index], IsToken: false));
			}

			parts.Add(new ArgumentTextPart(match.Value, IsToken: true));
			plainStart = match.Index + match.Length;
		}

		if (plainStart < text.Length)
		{
			parts.Add(new ArgumentTextPart(text[plainStart..], IsToken: false));
		}

		return parts;
	}

	private static string ValueOf(string token, PipelineWork work)
	{
		return token switch
		{
			CardId => work.CardId.ToString(CultureInfo.InvariantCulture),
			CardName => work.CardName,
			Branch => PipelineContract.BranchName(work.CardId),
			Stage => work.Kind.DisplayName(),
			Attempt => work.Attempt.ToString(CultureInfo.InvariantCulture),
			_ => throw new ArgumentOutOfRangeException(nameof(token), token, "Only tokens are filled in; the arguments were checked first."),
		};
	}

	// Each name once, in the order the arguments first use it.
	private static List<string> Unknown(IEnumerable<string> arguments)
	{
		return [.. arguments.SelectMany(argument => TokenShape().Matches(argument)).Select(match => match.Value).Where(name => !All.Contains(name, StringComparer.Ordinal)).Distinct(StringComparer.Ordinal)];
	}

	private static string? Describe(List<string> unknown)
	{
		if (unknown.Count == 0)
		{
			return null;
		}

		string verb = unknown.Count == 1 ? "is not a token" : "are not tokens";

		return $"Arguments use {List(unknown)}, which {verb} Pyxie fills in. The tokens are {List(All)}.";
	}

	private static string List(IReadOnlyList<string> names)
	{
		return names.Count switch
		{
			1 => names[0],
			2 => $"{names[0]} and {names[1]}",
			_ => $"{string.Join(", ", names.Take(names.Count - 1))}, and {names[^1]}",
		};
	}

	// A name in capitals between braces, not preceded by $: ${HOME} is a shell's, and stays the shell's.
	[GeneratedRegex(@"(?<!\$)\{[A-Z][A-Z0-9_]*\}", RegexOptions.CultureInvariant)]
	private static partial Regex TokenShape();
}
