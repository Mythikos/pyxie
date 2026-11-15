namespace Pyxie.Tests.Runtime.Processes;

/// <summary>Sets an environment variable of the test process and puts the previous value back when disposed. Environment variables belong to the whole process, so only tests in <see cref="ProcessEnvironmentCollection"/>, which never runs beside another test, may use it.</summary>
public sealed class EnvironmentVariableScope : IDisposable
{
	private readonly string _name;
	private readonly string? _original;

	/// <summary>Initializes a new instance of the <see cref="EnvironmentVariableScope"/> class, setting the variable.</summary>
	/// <param name="name">The variable name.</param>
	/// <param name="value">The new value, or <see langword="null"/> to remove the variable.</param>
	public EnvironmentVariableScope(string name, string? value)
	{
		_name = name;
		_original = Environment.GetEnvironmentVariable(name);
		Environment.SetEnvironmentVariable(name, value);
	}

	/// <inheritdoc/>
	public void Dispose()
	{
		Environment.SetEnvironmentVariable(_name, _original);
	}
}
