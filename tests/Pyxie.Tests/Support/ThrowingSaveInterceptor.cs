using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Pyxie.Tests.Support;

/// <summary>Fails the first save after it is armed with a <see cref="DbUpdateException"/> that no unique index caused, simulating a write that fails for a reason a service cannot turn into a typed refusal.</summary>
public sealed class ThrowingSaveInterceptor : SaveChangesInterceptor
{
	private bool _armed;

	/// <summary>Arms the interceptor so the next save fails once.</summary>
	public void Arm()
	{
		_armed = true;
	}

	/// <inheritdoc/>
	public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
	{
		if (_armed)
		{
			_armed = false;

			throw new DbUpdateException("The save failed for a reason of its own.");
		}

		return ValueTask.FromResult(result);
	}
}
