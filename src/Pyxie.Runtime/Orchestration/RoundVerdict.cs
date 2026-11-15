using Pyxie.Data.Enums;
using Pyxie.Data.Limits;
using Pyxie.Data.Services.Inputs;
using Pyxie.Data.Services.Pipeline;

namespace Pyxie.Runtime.Orchestration;

/// <summary>What running a round came to: the result to report and what to record with it.</summary>
/// <param name="Result">The result.</param>
/// <param name="Note">The short reason for history.</param>
/// <param name="Report">The details: the agent's report, gate output, or merge result.</param>
public sealed record RoundVerdict(RoundResult Result, string? Note, string? Report)
{
	// Keeps the start of long output, where the command line and first errors are, and more of its end, where summaries are.
	private const int ReportHeadLength = 20_000;

	/// <summary>Creates a verdict for a round that went well.</summary>
	/// <param name="note">The note, if any.</param>
	/// <param name="report">The report, if any.</param>
	/// <returns>The verdict.</returns>
	public static RoundVerdict Succeeded(string? note, string? report = null)
	{
		return new RoundVerdict(RoundResult.Succeeded, note, report);
	}

	/// <summary>Creates a verdict for a failed gate, a rejection, or a merge conflict.</summary>
	/// <param name="note">Why.</param>
	/// <param name="report">The details.</param>
	/// <returns>The verdict.</returns>
	public static RoundVerdict Failed(string note, string? report = null)
	{
		return new RoundVerdict(RoundResult.Failed, note, report);
	}

	/// <summary>Creates a verdict for a merge whose target branch had moved on.</summary>
	/// <param name="note">What happened.</param>
	/// <param name="report">The details.</param>
	/// <returns>The verdict.</returns>
	public static RoundVerdict Outdated(string note, string? report = null)
	{
		return new RoundVerdict(RoundResult.Outdated, note, report);
	}

	/// <summary>Creates a verdict for a round that broke but may succeed on another attempt.</summary>
	/// <param name="note">What broke.</param>
	/// <param name="report">The details.</param>
	/// <returns>The verdict.</returns>
	public static RoundVerdict Errored(string note, string? report = null)
	{
		return new RoundVerdict(RoundResult.Errored, note, report);
	}

	/// <summary>Creates a verdict for a round that cannot go on until a person fixes something.</summary>
	/// <param name="note">What needs fixing.</param>
	/// <param name="report">The details.</param>
	/// <returns>The verdict.</returns>
	public static RoundVerdict Blocked(string note, string? report = null)
	{
		return new RoundVerdict(RoundResult.Blocked, note, report);
	}

	/// <summary>Gets what to record, fitted to the limits the pipeline enforces: the note is shortened to one line's worth and the report keeps its beginning and end.</summary>
	/// <returns>The report for <see cref="ICardPipelineService.RecordResultAsync"/>.</returns>
	public RoundReport ToRoundReport()
	{
		string? note = string.IsNullOrWhiteSpace(Note) ? null : ReportText.Clip(Note.Trim(), PyxieLimits.MaxRoundNoteLength);
		if (note is null && Result is RoundResult.Failed or RoundResult.Errored or RoundResult.Blocked)
		{
			note = Result switch
			{
				RoundResult.Failed => "The round failed without saying why.",
				RoundResult.Errored => "The round broke without saying why.",
				_ => "The round needs a person without saying why.",
			};
		}

		string? report = string.IsNullOrWhiteSpace(Report) ? null : ReportText.Excerpt(Report, PyxieLimits.MaxRoundReportLength, ReportHeadLength);

		return new RoundReport(note, report);
	}
}
