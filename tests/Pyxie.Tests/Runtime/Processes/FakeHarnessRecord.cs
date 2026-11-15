using System.Text.Json;

using Xunit;

namespace Pyxie.Tests.Runtime.Processes;

/// <summary>What one run of the fake harness wrote with <c>--record</c>: where it ran, the arguments it received, and everything it read from standard input.</summary>
/// <param name="Directory">The working directory the process started in.</param>
/// <param name="Arguments">The arguments the process received, after the harness dll.</param>
/// <param name="Input">The text read from standard input.</param>
public sealed record FakeHarnessRecord(string Directory, IReadOnlyList<string> Arguments, string Input)
{
	/// <summary>Reads every run recorded in a file, oldest first.</summary>
	/// <param name="path">The record file, which a test passed as <c>--record</c>.</param>
	/// <returns>One record per run.</returns>
	public static async Task<IReadOnlyList<FakeHarnessRecord>> ReadAllAsync(string path)
	{
		string[] lines = await File.ReadAllLinesAsync(path, TestContext.Current.CancellationToken);

		return [.. lines.Where(line => line.Length > 0).Select(Parse)];
	}

	/// <summary>Reads the only run recorded in a file.</summary>
	/// <param name="path">The record file.</param>
	/// <returns>The record. The test fails when the file holds no run or several.</returns>
	public static async Task<FakeHarnessRecord> ReadSingleAsync(string path)
	{
		IReadOnlyList<FakeHarnessRecord> records = await ReadAllAsync(path);
		Assert.True(records.Count == 1, $"Expected one recorded run in {path} but found {records.Count}.");

		return records[0];
	}

	private static FakeHarnessRecord Parse(string line)
	{
		using JsonDocument document = JsonDocument.Parse(line);
		JsonElement root = document.RootElement;
		string[] arguments = [.. root.GetProperty("arguments").EnumerateArray().Select(argument => argument.GetString() ?? string.Empty)];

		return new FakeHarnessRecord(root.GetProperty("directory").GetString() ?? string.Empty, arguments, root.GetProperty("input").GetString() ?? string.Empty);
	}
}
