using System.Data.Common;

using Microsoft.EntityFrameworkCore.Diagnostics;

using Xunit;

namespace Pyxie.Tests.Support;

/// <summary>Runs a SQL statement, or a whole service call, once at one moment of the service's work. It simulates an agent or another user winning a race against the service: either just before the service's write command, or just before the service begins the transaction that holds its writes. A service call armed this way runs to completion on the same connection first, so the interrupted service then meets the state the other caller left.</summary>
public sealed class ConcurrentWriteInterceptor : DbCommandInterceptor, IDbTransactionInterceptor
{
	private readonly string? _triggerKeyword;
	private string? _sql;
	private Func<Task>? _action;
	private bool _fired;

	private ConcurrentWriteInterceptor(string? triggerKeyword)
	{
		_triggerKeyword = triggerKeyword;
	}

	/// <summary>Creates an interceptor that fires just before the first write command that starts with a keyword, for services that write without a transaction.</summary>
	/// <param name="keyword">The first word of the write command, such as <c>UPDATE</c>.</param>
	/// <returns>The unarmed interceptor.</returns>
	public static ConcurrentWriteInterceptor BeforeCommand(string keyword)
	{
		return new ConcurrentWriteInterceptor(keyword);
	}

	/// <summary>Creates an interceptor that fires just before the service begins a transaction, so the statement commits on its own and the service's transaction then runs against the new state.</summary>
	/// <returns>The unarmed interceptor.</returns>
	public static ConcurrentWriteInterceptor BeforeTransaction()
	{
		return new ConcurrentWriteInterceptor(null);
	}

	/// <summary>Arms the interceptor so the next matching moment runs the statement once.</summary>
	/// <param name="sql">The statement the concurrent writer runs.</param>
	public void Arm(string sql)
	{
		_sql = sql;
	}

	/// <summary>Arms the interceptor so the next matching moment runs a concurrent operation once, such as another service call. The operation may use the same database and may start transactions of its own.</summary>
	/// <param name="action">The operation the concurrent writer runs.</param>
	public void Arm(Func<Task> action)
	{
		_action = action;
	}

	/// <summary>Asserts that the armed statement or operation ran, so a test that never reached the intercepted moment fails instead of passing without a race.</summary>
	public void AssertFired()
	{
		Assert.True(_fired, "The concurrent write never ran, so the race this test describes did not happen.");
	}

	/// <inheritdoc/>
	public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
	{
		if (_triggerKeyword is not null && command.CommandText.TrimStart().StartsWith(_triggerKeyword, StringComparison.OrdinalIgnoreCase))
		{
			await RunAsync(command.Connection, command.Transaction, cancellationToken);
		}

		return result;
	}

	/// <inheritdoc/>
	public async ValueTask<InterceptionResult<DbTransaction>> TransactionStartingAsync(DbConnection connection, TransactionStartingEventData eventData, InterceptionResult<DbTransaction> result, CancellationToken cancellationToken = default)
	{
		if (_triggerKeyword is null)
		{
			await RunAsync(connection, null, cancellationToken);
		}

		return result;
	}

	private async Task RunAsync(DbConnection? connection, DbTransaction? transaction, CancellationToken cancellationToken)
	{
		if (_action is not null)
		{
			Func<Task> action = _action;
			_action = null;
			_fired = true;
			await action();

			return;
		}

		if (_sql is null)
		{
			return;
		}

		string sql = _sql;
		_sql = null;
		_fired = true;

		await using DbCommand concurrent = (connection ?? throw new InvalidOperationException("The intercepted operation has no connection.")).CreateCommand();
		concurrent.Transaction = transaction;
		concurrent.CommandText = sql;
		await concurrent.ExecuteNonQueryAsync(cancellationToken);
	}
}
