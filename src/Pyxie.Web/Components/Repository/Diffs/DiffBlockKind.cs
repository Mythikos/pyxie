namespace Pyxie.Web.Components.Repository.Diffs;

/// <summary>What a run of diff lines is.</summary>
public enum DiffBlockKind
{
	/// <summary>The lines that open one file's diff: <c>diff --git</c>, <c>index</c>, modes, renames, the <c>---</c> and <c>+++</c> names, and <c>Binary files differ</c>.</summary>
	Header = 0,

	/// <summary>A hunk header, <c>@@ -1,3 +1,4 @@</c>.</summary>
	Hunk = 1,

	/// <summary>Lines the commit added.</summary>
	Added = 2,

	/// <summary>Lines the commit removed.</summary>
	Removed = 3,

	/// <summary>Lines around the change that it left as they were.</summary>
	Context = 4,

	/// <summary>A remark about the diff itself: <c>\ No newline at end of file</c>, or the marker that says part of a long diff was cut.</summary>
	Note = 5,
}
