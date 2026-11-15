using Pyxie.Data.Services.Results;

using Xunit;

namespace Pyxie.Tests.Support;

/// <summary>Assertions over <see cref="ServiceResult"/> outcomes that report the service message on failure.</summary>
public static class ResultAssert
{
	/// <summary>Asserts that the result succeeded.</summary>
	/// <param name="result">The result to check.</param>
	public static void Succeeded(ServiceResult result)
	{
		Assert.True(result.Succeeded, $"Expected success but got: {result.Error}");
	}

	/// <summary>Asserts that the result succeeded and returns its value.</summary>
	/// <typeparam name="T">The value type.</typeparam>
	/// <param name="result">The result to check.</param>
	/// <returns>The returned value.</returns>
	public static T Succeeded<T>(ServiceResult<T> result)
	{
		Assert.True(result.Succeeded, $"Expected success but got: {result.Error}");
		Assert.NotNull(result.Value);

		return result.Value;
	}

	/// <summary>Asserts that the result succeeded and returns its value, which may legitimately be absent.</summary>
	/// <typeparam name="T">The value type.</typeparam>
	/// <param name="result">The result to check.</param>
	/// <returns>The returned value, or <see langword="null"/> when the operation succeeded without one.</returns>
	public static T? SucceededWithOptionalValue<T>(ServiceResult<T> result)
	{
		Assert.True(result.Succeeded, $"Expected success but got: {result.Error}");

		return result.Value;
	}

	/// <summary>Asserts that the result failed with a message and returns the message.</summary>
	/// <param name="result">The result to check.</param>
	/// <returns>The failure message.</returns>
	public static string Failed(ServiceResult result)
	{
		Assert.False(result.Succeeded, "Expected the operation to be refused.");
		Assert.False(string.IsNullOrWhiteSpace(result.Error), "A refusal must carry a user-presentable message.");

		return result.Error;
	}

	/// <summary>Asserts that the result failed with a message and no value, and returns the message.</summary>
	/// <typeparam name="T">The value type.</typeparam>
	/// <param name="result">The result to check.</param>
	/// <returns>The failure message.</returns>
	public static string Failed<T>(ServiceResult<T> result)
	{
		Assert.False(result.Succeeded, "Expected the operation to be refused.");
		Assert.Null(result.Value);
		Assert.False(string.IsNullOrWhiteSpace(result.Error), "A refusal must carry a user-presentable message.");

		return result.Error;
	}
}
