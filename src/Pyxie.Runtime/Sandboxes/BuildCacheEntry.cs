namespace Pyxie.Runtime.Sandboxes;

/// <summary>One entry of the engine's build cache, as <c>docker buildx du --format json</c> lists it.</summary>
/// <param name="Id">The entry's identifier.</param>
/// <param name="Parents">The entries it was built on; empty for one built on nothing.</param>
/// <param name="Reclaimable">Whether no build uses it, so a prune can remove it once nothing built on it is left.</param>
/// <param name="Shared">Whether an image that is still there holds the same layer, so removing the entry frees nothing and only loses that image's cache.</param>
/// <param name="Description">What made it, such as the command of a <c>RUN</c> step; empty when the engine gives none.</param>
internal sealed record BuildCacheEntry(string Id, IReadOnlyList<string> Parents, bool Reclaimable, bool Shared, string Description);
