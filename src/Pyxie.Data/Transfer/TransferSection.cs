namespace Pyxie.Data.Transfer;

/// <summary>A part of a project's settings that a settings file can hold and an import can replace on its own.</summary>
public enum TransferSection
{
	/// <summary>The agents.</summary>
	Agents = 0,

	/// <summary>The gates, in the order they run.</summary>
	Gates = 1,

	/// <summary>The categories.</summary>
	Categories = 2,

	/// <summary>The sandbox settings: the project image's Dockerfile.</summary>
	Sandbox = 3,
}
