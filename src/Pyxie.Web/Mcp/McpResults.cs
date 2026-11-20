using ModelContextProtocol;

using Pyxie.Data.Services.Results;
using Pyxie.Runtime.Orchestration;

namespace Pyxie.Web.Mcp;

internal static class McpResults
{
	internal static T Value<T>(this ServiceResult<T> result) where T : class
	{
		return result is { Succeeded: true, Value: not null } ? result.Value : throw new McpException(result.Error ?? "The operation failed.");
	}

	internal static string Done(this ServiceResult result)
	{
		return result.Succeeded ? "Done." : throw new McpException(result.Error ?? "The operation failed.");
	}

	internal static string Changed(this ServiceResult result, PipelineActivity activity)
	{
		string message = result.Done();
		activity.NotifyCardsChanged();
		return message;
	}
}
