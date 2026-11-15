using Microsoft.AspNetCore.Components;

namespace Pyxie.Tests.Web.Rendering;

/// <summary>A navigation manager that records where it was asked to go and raises <see cref="NavigationManager.LocationChanged"/> for it, so a test can navigate.</summary>
internal sealed class TestNavigationManager : NavigationManager
{
	/// <summary>Initializes a new instance of the <see cref="TestNavigationManager"/> class at the root of a fake host.</summary>
	public TestNavigationManager()
	{
		Initialize("http://localhost/", "http://localhost/");
	}

	/// <summary>Moves to a location below the host, as a link click would.</summary>
	/// <param name="relativeUri">The location, relative to the host.</param>
	public void MoveTo(string relativeUri)
	{
		Uri = ToAbsoluteUri(relativeUri).ToString();
		NotifyLocationChanged(isInterceptedLink: false);
	}

	/// <inheritdoc/>
	protected override void NavigateToCore(string uri, NavigationOptions options)
	{
		MoveTo(uri);
	}
}
