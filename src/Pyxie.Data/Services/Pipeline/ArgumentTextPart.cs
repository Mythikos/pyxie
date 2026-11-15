namespace Pyxie.Data.Services.Pipeline;

/// <summary>A run of text in an agent's arguments: either one token Pyxie fills in, such as <c>{CARD_NAME}</c>, or the plain text between tokens (see <see cref="AgentArgumentTokens.Split"/>).</summary>
/// <param name="Text">The text of the part.</param>
/// <param name="IsToken">Whether the part is a token.</param>
public sealed record ArgumentTextPart(string Text, bool IsToken);
