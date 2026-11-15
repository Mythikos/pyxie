namespace Pyxie.Data.Services.Results;

/// <summary>The outcome of a service operation that returns no value.</summary>
/// <param name="Succeeded">Whether the operation succeeded.</param>
/// <param name="Error">A user-presentable message when the operation failed; otherwise <see langword="null"/>.</param>
public sealed record ServiceResult(bool Succeeded, string? Error)
{
	/// <summary>Creates a successful result.</summary>
	/// <returns>A result with <see cref="Succeeded"/> set.</returns>
	public static ServiceResult Success() => new(true, null);

	/// <summary>Creates a failed result.</summary>
	/// <param name="error">A user-presentable explanation.</param>
	/// <returns>A result with <see cref="Succeeded"/> cleared.</returns>
	public static ServiceResult Failure(string error) => new(false, error);
}
