namespace Pyxie.Data.Services.Results;

/// <summary>The outcome of a service operation that returns a value on success.</summary>
/// <typeparam name="T">The type of the returned value.</typeparam>
/// <param name="Succeeded">Whether the operation succeeded.</param>
/// <param name="Value">The returned value when the operation succeeded; otherwise the default.</param>
/// <param name="Error">A user-presentable message when the operation failed; otherwise <see langword="null"/>.</param>
public sealed record ServiceResult<T>(bool Succeeded, T? Value, string? Error)
{
	/// <summary>Creates a successful result.</summary>
	/// <param name="value">The value to return.</param>
	/// <returns>A result carrying <paramref name="value"/>.</returns>
	public static ServiceResult<T> Success(T value) => new(true, value, null);

	/// <summary>Creates a failed result.</summary>
	/// <param name="error">A user-presentable explanation.</param>
	/// <returns>A result carrying no value.</returns>
	public static ServiceResult<T> Failure(string error) => new(false, default, error);
}
