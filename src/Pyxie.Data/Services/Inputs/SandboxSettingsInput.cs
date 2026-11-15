namespace Pyxie.Data.Services.Inputs;

/// <summary>A project's sandbox settings as a person entered them.</summary>
/// <param name="Dockerfile">The project image's Dockerfile.</param>
public sealed record SandboxSettingsInput(string? Dockerfile);
