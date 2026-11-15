using System.Data.Common;

using Microsoft.EntityFrameworkCore.Diagnostics;

using Xunit;

namespace Pyxie.Tests.Support;

/// <summary>Runs a whole service call, once, just before the first query whose SQL contains a fragment. It simulates another caller winning a race against the service between two of its reads, which <see cref="ConcurrentWriteInterceptor"/> cannot do because it only fires before the service's writes. The operation runs to completion first, so the interrupted query then reads what the other caller left.</summary>
public sealed class ConcurrentReadInterceptor : DbCommandInterceptor
{
	private readonly string _fragment;
	private Func<Task>? _action;
	private bool _fired;

	/// <summary>Initializes a new instance of the <see cref="ConcurrentReadInterceptor"/> class.</summary>
	/// <param name="fragment">Text the interrupted query's SQL contains, such as <c>FROM "Cards"</c>.</param>
	public ConcurrentReadInterceptor(string fragment)
	{
		_fragment = fragment;
	}

	/// <summary>Arms the interceptor so the next matching query runs the operation once.</summary>
	/// <param name="action">The operation the concurrent caller runs, such as another service call on the same database.</param>
	public void Arm(Func<Task> action)
	{
		_action = action;
	}

	/// <summary>Asserts that the operation ran, so a test that never reached the intercepted query fails instead of passing without a race.</summary>
	public void AssertFired()
	{
		Assert.True(_fired, "The concurrent operation never ran, so the race this test describes did not happen.");
	}

	/// <inheritdoc/>
	public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
	{
		if (_action is not null && command.CommandText.Contains(_fragment, StringComparison.Ordinal))
		{
			Func<Task> action = _action;
			_action = null;
			_fired = true;
			await action();
		}

		return result;
	}
}
