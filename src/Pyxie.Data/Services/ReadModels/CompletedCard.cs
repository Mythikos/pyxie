using Pyxie.Data.Enums;

namespace Pyxie.Data.Services.ReadModels;

/// <summary>A card and the UTC timestamp of its successful merge, including after it was closed.</summary>
/// <param name="Id">Card identifier.</param>
/// <param name="Name">Card title.</param>
/// <param name="Lane">Current lane.</param>
/// <param name="CompletedUtc">Time of the successful merge.</param>
public sealed record CompletedCard(int Id, string Name, CardLane Lane, DateTime CompletedUtc);
