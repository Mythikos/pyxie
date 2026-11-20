using Pyxie.Runtime.Sandboxes;

using Xunit;

namespace Pyxie.Tests.Runtime.Sandboxes;

/// <summary>How <see cref="DockerImages"/> picks the build cache it prunes, from what <c>docker buildx du --format json</c> lists: Pyxie's entries are the steps run with <see cref="SandboxNames.BuildShell"/> or naming it, and every entry built on one of them, such as a <c>COPY</c> or a <c>WORKDIR</c>, which records no shell; only those nothing uses, neither a build nor an image that is still there, are pruned, each before the entries it was built on, because an entry cannot go while one built on it stays.</summary>
public sealed class DockerImagesBuildCacheTests
{
	private static readonly string s_marked = $"mount / from exec {SandboxNames.BuildShell} -c";

	[Fact]
	public void ParseBuildCache_JsonLines_ReadsEveryEntryAndSkipsWhatIsNotOne()
	{
		string output = string.Join('\n',
			"{\"ID\":\"a1\",\"Parents\":[\"p1\",\"p2\"],\"Reclaimable\":true,\"Shared\":false,\"Description\":\"mount / from exec /bin/sh -c echo \\tdone\",\"Size\":\"1kB\"}",
			"{\"ID\":\"a2\",\"Reclaimable\":false,\"Shared\":true}",
			"   ",
			"not json",
			"{\"Description\":\"no identifier\"}",
			"{\"ID\":\"a3\",\"Parents\":null,\"Reclaimable\":true,\"Description\":\"\"}",
			"{\"ID\":\"a4\",\"Parents\":[\"p3\"],\"Reclaimab");

		IReadOnlyList<BuildCacheEntry> entries = DockerImages.ParseBuildCache(output);

		Assert.Equal(["a1", "a2", "a3"], entries.Select(entry => entry.Id));
		Assert.Equal(["p1", "p2"], entries[0].Parents);
		Assert.Equal((true, false, "mount / from exec /bin/sh -c echo \tdone"), (entries[0].Reclaimable, entries[0].Shared, entries[0].Description));
		Assert.Equal((0, false, true, string.Empty), (entries[1].Parents.Count, entries[1].Reclaimable, entries[1].Shared, entries[1].Description));
		Assert.Equal((0, true, false, string.Empty), (entries[2].Parents.Count, entries[2].Reclaimable, entries[2].Shared, entries[2].Description));
	}

	[Fact]
	public void UnusedBuildCache_StepsOfPyxiesImages_PicksEveryOneBuiltOnAMarkedStepEachBeforeWhatItWasBuiltOn()
	{
		BuildCacheEntry[] entries =
		[
			Entry("project-workdir", "project-own-shell", "[4/4] WORKDIR /src"),
			Entry("base-from", null, "pulled from docker.io/library/node"),
			Entry("project-own-shell", "base-workdir", "mount / from exec /bin/bash -c make tools"),
			Entry("base-link", "base-from", $"mount / from exec /bin/sh -c ln -s /bin/sh {SandboxNames.BuildShell}"),
			Entry("base-apt", "base-link", $"{s_marked} apt-get install -y git"),
			Entry("base-copy", "base-apt", "[4/6] COPY pyxie-sandbox /usr/local/bin/"),
			Entry("base-chmod", "base-copy", $"{s_marked} chmod 755 /usr/local/bin/pyxie-sandbox"),
			Entry("base-workdir", "base-chmod", "[6/6] WORKDIR /work"),
		];

		IReadOnlyList<string> unused = DockerImages.UnusedBuildCache(entries);

		Assert.Equal(["base-apt", "base-chmod", "base-copy", "base-link", "base-workdir", "project-own-shell", "project-workdir"], unused.Order(StringComparer.Ordinal));
		AssertEachBeforeWhatItWasBuiltOn(entries, unused);
	}

	[Fact]
	public void UnusedBuildCache_EntriesSomethingUses_AreLeftOutAndWhatWasBuiltOnThemIsNot()
	{
		BuildCacheEntry[] entries =
		[
			Entry("base-apt", null, $"{s_marked} apt-get install -y git", reclaimable: false),
			Entry("base-workdir", "base-apt", "[6/6] WORKDIR /work", reclaimable: false),
			Entry("old-project-step", "base-workdir", $"{s_marked} make"),
			Entry("old-project-workdir", "old-project-step", "[3/3] WORKDIR /src"),
			Entry("current-project-step", "base-workdir", $"{s_marked} make tools", reclaimable: false),
		];

		IReadOnlyList<string> unused = DockerImages.UnusedBuildCache(entries);

		Assert.Equal(["old-project-workdir", "old-project-step"], unused);
	}

	[Fact]
	public void UnusedBuildCache_EntriesAnImageStillHolds_AreLeftOutAndWhatIsBuiltOnThemIsNot()
	{
		BuildCacheEntry[] entries =
		[
			Entry("base-apt", null, $"{s_marked} apt-get install -y git", shared: true),
			Entry("base-workdir", "base-apt", "[6/6] WORKDIR /work", shared: true),
			Entry("removed-project-step", "base-workdir", $"{s_marked} make"),
			Entry("current-project-step", "base-workdir", $"{s_marked} make tools", shared: true),
		];

		Assert.Equal(["removed-project-step"], DockerImages.UnusedBuildCache(entries));
	}

	[Fact]
	public void UnusedBuildCache_EverythingElseBuiltOnTheMachine_IsLeftAlone()
	{
		BuildCacheEntry[] entries =
		[
			Entry("other-from", null, "pulled from docker.io/library/node"),
			Entry("other-npm", "other-from", "mount / from exec /bin/sh -c npm install -g @anthropic-ai/claude-code @openai/codex @deepseek-ai/dsh@0.2.2-rc.5"),
			Entry("other-workdir", "other-npm", "[3/3] WORKDIR /work"),
			Entry("other-context", null, "local source for context"),
		];

		Assert.Empty(DockerImages.UnusedBuildCache(entries));
	}

	[Fact]
	public void UnusedBuildCache_AnEntryBuiltOnTwoPyxieSteps_IsPickedOnceBeforeBoth()
	{
		BuildCacheEntry[] entries =
		[
			Entry("first", null, $"{s_marked} make first"),
			Entry("second", null, $"{s_marked} make second"),
			new BuildCacheEntry("merged", ["first", "second"], Reclaimable: true, Shared: false, "merge"),
		];

		IReadOnlyList<string> unused = DockerImages.UnusedBuildCache(entries);

		Assert.Equal(["first", "merged", "second"], unused.Order(StringComparer.Ordinal));
		AssertEachBeforeWhatItWasBuiltOn(entries, unused);
	}

	[Fact]
	public void PruneFilters_Entries_NamesEachExactlyInTheirOrderABoundedNumberPerFilter()
	{
		Assert.Equal(["id=^(a|b)$", "id=^(c|d)$", "id=^(e)$"], DockerImages.PruneFilters(["a", "b", "c", "d", "e"], 2));
		Assert.Empty(DockerImages.PruneFilters([], 2));
	}

	private static BuildCacheEntry Entry(string id, string? parent, string description, bool reclaimable = true, bool shared = false)
	{
		return new BuildCacheEntry(id, parent is null ? [] : [parent], reclaimable, shared, description);
	}

	// Pruning in this order removes an entry only after everything picked that was built on it.
	private static void AssertEachBeforeWhatItWasBuiltOn(IReadOnlyList<BuildCacheEntry> entries, IReadOnlyList<string> unused)
	{
		foreach (BuildCacheEntry entry in entries.Where(entry => unused.Contains(entry.Id)))
		{
			foreach (string parent in entry.Parents.Where(unused.Contains))
			{
				Assert.True(unused.ToList().IndexOf(entry.Id) < unused.ToList().IndexOf(parent), $"{entry.Id} was built on {parent}, so it must be pruned first.");
			}
		}
	}
}
