using Pyxie.Data.Entities;
using Pyxie.Data.Services;
using Pyxie.Data.Services.Results;

using Xunit;

namespace Pyxie.Tests.Web.Rendering;

/// <summary>Answers which project is open from a value the test sets, and changes nothing. An import asks it once no other change can run; a test can hold that answer back, so the import stays running until the test gives it, and then fails because a project is open, which needs no git. A held question ends when the caller's token is cancelled, as a read of the database would.</summary>
internal sealed class StubProjectCatalog : IProjectCatalog
{
	private const string ChangesNothing = "The test catalog changes nothing.";

	private readonly Lock _gate = new();
	private readonly List<(Project? Asked, TaskCompletionSource<Project?> Answer)> _held = [];
	private TaskCompletionSource _askedAgain = new(TaskCreationOptions.RunContinuationsAsynchronously);

	/// <summary>Gets or sets the project the catalog says is open, or <see langword="null"/> for none.</summary>
	public Project? Open { get; set; }

	/// <summary>Gets or sets a value indicating whether questions asked from now on wait for <see cref="Answer"/>.</summary>
	public bool HoldAnswers { get; set; }

	/// <summary>Gives the answer to a held question.</summary>
	/// <param name="index">The zero-based position of the question among the held ones, in the order they were asked.</param>
	public void Answer(int index)
	{
		(Project? asked, TaskCompletionSource<Project?> answer) = Held(index);
		answer.TrySetResult(asked);
	}

	/// <summary>Waits until a number of questions have been held, for a question asked from a background task that the test cannot await.</summary>
	/// <param name="count">How many held questions to wait for.</param>
	/// <returns>A task that completes when that many questions are held.</returns>
	public async Task WaitForHeldAsync(int count)
	{
		while (true)
		{
			Task next;

			lock (_gate)
			{
				if (_held.Count >= count)
				{
					return;
				}

				next = _askedAgain.Task;
			}

			await next.WaitAsync(TestContext.Current.CancellationToken);
		}
	}

	/// <inheritdoc/>
	public Task<Project?> GetOpenAsync(CancellationToken cancellationToken)
	{
		Project? asked = Open;

		if (!HoldAnswers)
		{
			return Task.FromResult(asked);
		}

		TaskCompletionSource<Project?> answer = new();
		TaskCompletionSource announcement;

		lock (_gate)
		{
			_held.Add((asked, answer));
			announcement = _askedAgain;
			_askedAgain = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		}

		announcement.TrySetResult();
		cancellationToken.Register(() => answer.TrySetCanceled(cancellationToken));

		return answer.Task;
	}

	/// <inheritdoc/>
	public Task<IReadOnlyList<Project>> ListAsync(CancellationToken cancellationToken)
	{
		return Task.FromResult<IReadOnlyList<Project>>(Open is null ? [] : [Open]);
	}

	/// <inheritdoc/>
	public Task<Project?> RestoreOpenAsync(CancellationToken cancellationToken)
	{
		return Task.FromResult(Open);
	}

	/// <inheritdoc/>
	public Task<ServiceResult<Project>> AddAsync(string name, string folder, string source, CancellationToken cancellationToken)
	{
		return Task.FromResult(ServiceResult<Project>.Failure(ChangesNothing));
	}

	/// <inheritdoc/>
	public Task<ServiceResult<Project>> OpenAsync(int id, CancellationToken cancellationToken)
	{
		return Task.FromResult(ServiceResult<Project>.Failure(ChangesNothing));
	}

	/// <inheritdoc/>
	public Task<ServiceResult> CloseAsync(CancellationToken cancellationToken)
	{
		return Task.FromResult(ServiceResult.Failure(ChangesNothing));
	}

	/// <inheritdoc/>
	public Task<ServiceResult> RemoveAsync(int id, CancellationToken cancellationToken)
	{
		return Task.FromResult(ServiceResult.Failure(ChangesNothing));
	}

	private (Project? Asked, TaskCompletionSource<Project?> Answer) Held(int index)
	{
		lock (_gate)
		{
			return _held[index];
		}
	}
}
