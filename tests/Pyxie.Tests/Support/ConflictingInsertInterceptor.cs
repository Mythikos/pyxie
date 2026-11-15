using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Pyxie.Tests.Support;

/// <summary>Runs a SQL statement once just before the first save after it is armed, simulating a concurrent writer that wins the race after a service's pre-check.</summary>
public sealed class ConflictingInsertInterceptor : SaveChangesInterceptor
{
	private readonly string _sql;
	private bool _armed;

	/// <summary>Initializes a new instance of the <see cref="ConflictingInsertInterceptor"/> class.</summary>
	/// <param name="sql">The statement to run on the context's connection.</param>
	public ConflictingInsertInterceptor(string sql)
	{
		_sql = sql;
	}

	/// <summary>Arms the interceptor so the next save triggers the statement once.</summary>
	public void Arm()
	{
		_armed = true;
	}

	/// <inheritdoc/>
	public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
	{
		if (_armed && eventData.Context is not null)
		{
			_armed = false;
			await eventData.Context.Database.ExecuteSqlRawAsync(_sql, cancellationToken);
		}

		return result;
	}
}
