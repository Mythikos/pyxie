using System.Text;

namespace Pyxie.FakeHarness;

/// <summary>Splits a script line into arguments: whitespace separates them and double quotes group them.</summary>
internal static class ScriptLine
{
	/// <summary>Splits a line.</summary>
	/// <param name="line">The line.</param>
	/// <returns>The arguments.</returns>
	public static IReadOnlyList<string> Split(string line)
	{
		List<string> arguments = [];
		StringBuilder current = new();
		bool quoted = false;
		bool started = false;

		foreach (char character in line)
		{
			if (character == '"')
			{
				quoted = !quoted;
				started = true;
			}
			else if (char.IsWhiteSpace(character) && !quoted)
			{
				if (started)
				{
					arguments.Add(current.ToString());
					current.Clear();
					started = false;
				}
			}
			else
			{
				current.Append(character);
				started = true;
			}
		}

		if (started)
		{
			arguments.Add(current.ToString());
		}

		return arguments;
	}
}
