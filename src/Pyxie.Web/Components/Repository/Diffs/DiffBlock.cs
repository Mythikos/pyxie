namespace Pyxie.Web.Components.Repository.Diffs;

/// <summary>Consecutive diff lines of one kind, shown together so a long diff costs a few elements and not one per line.</summary>
/// <param name="Kind">What the lines are.</param>
/// <param name="Text">The lines exactly as git wrote them, including the leading <c>+</c>, <c>-</c>, or space, joined by line feeds without a final one.</param>
public sealed record DiffBlock(DiffBlockKind Kind, string Text);
