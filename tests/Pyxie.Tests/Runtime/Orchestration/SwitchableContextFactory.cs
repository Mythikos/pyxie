using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Pyxie.Data.Persistence;

namespace Pyxie.Tests.Runtime.Orchestration;

/// <summary>A context factory that fails on demand, as the database does when its file is locked or gone: while <see cref="Failing"/> is set, or for the next few calls after <see cref="FailNext"/>, creating a context throws a <see cref="SqliteException"/> (a database failure, not an I/O or git failure). Otherwise it hands out contexts of the factory it wraps.</summary>
public sealed class SwitchableContextFactory : IDbContextFactory<ProjectDbContext>
{
	private readonly IDbContextFactory<ProjectDbContext> _inner;
	private volatile bool _failing;
	private int _failuresLeft;
	private int _failures;

	/// <summary>Initializes a new instance of the <see cref="SwitchableContextFactory"/> class.</summary>
	/// <param name="inner">The factory that creates the contexts while nothing is failing.</param>
	public SwitchableContextFactory(IDbContextFactory<ProjectDbContext> inner)
	{
		_inner = inner;
	}

	/// <summary>Gets or sets a value indicating whether every call fails.</summary>
	public bool Failing
	{
		get => _failing;
		set => _failing = value;
	}

	/// <summary>Gets how many calls failed so far.</summary>
	public int Failures => Volatile.Read(ref _failures);

	/// <summary>Makes the next calls fail, whoever makes them.</summary>
	/// <param name="count">How many calls fail before the factory works again.</param>
	public void FailNext(int count)
	{
		Volatile.Write(ref _failuresLeft, count);
	}

	/// <inheritdoc/>
	public ProjectDbContext CreateDbContext()
	{
		ThrowWhenFailing();

		return _inner.CreateDbContext();
	}

	/// <inheritdoc/>
	public Task<ProjectDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
	{
		ThrowWhenFailing();

		return _inner.CreateDbContextAsync(cancellationToken);
	}

	private void ThrowWhenFailing()
	{
		bool fail = _failing;
		if (!fail)
		{
			int left = Volatile.Read(ref _failuresLeft);
			while (left > 0 && !fail)
			{
				fail = Interlocked.CompareExchange(ref _failuresLeft, left - 1, left) == left;
				left = Volatile.Read(ref _failuresLeft);
			}
		}

		if (fail)
		{
			Interlocked.Increment(ref _failures);

			throw new SqliteException("database is locked", 5);
		}
	}
}
