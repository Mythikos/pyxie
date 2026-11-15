namespace Pyxie.Tests.Runtime.Sandboxes;

/// <summary>The condition for the <c>SkipUnless</c> property of the tests that drive a real container engine. They build images, start containers, and take minutes the first time, so they run only when <c>PYXIE_DOCKER_TESTS</c> is <c>1</c> and are reported as skipped otherwise.</summary>
public static class DockerTestSwitch
{
	/// <summary>The environment variable that turns the tests on.</summary>
	public const string VariableName = "PYXIE_DOCKER_TESTS";

	/// <summary>The skip reason for a test that needs a real container engine.</summary>
	public const string NeedsDocker = "Needs Docker: set PYXIE_DOCKER_TESTS=1 to run the tests that drive a real container engine.";

	/// <summary>Gets a value indicating whether the tests that need Docker run.</summary>
	public static bool IsOn => Environment.GetEnvironmentVariable(VariableName) == "1";
}
