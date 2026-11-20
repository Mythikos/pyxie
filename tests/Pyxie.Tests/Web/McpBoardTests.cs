using System.Text.Json;

using ModelContextProtocol;

using Pyxie.Data.Entities;
using Pyxie.Data.Projects;
using Pyxie.Runtime.Orchestration;
using Pyxie.Tests.Support;
using Pyxie.Web.Mcp;

using Xunit;

namespace Pyxie.Tests.Web;

public sealed class McpBoardTests
{
	[Fact]
	public async Task BoardCursorAndInspectedVersionRemainUsableWhenCardsAreEditedOrDeleted()
	{
		CancellationToken token = TestContext.Current.CancellationToken;
		await using PipelineRig rig = new();
		OpenProject open = new();
		open.Set(new Project { Id = 1, Name = "Board" });
		CardTools tools = new(new PipelineController(open), new PipelineActivity(), rig.Cards);
		WorkCard first = await rig.CreateCardAsync("First");
		WorkCard second = await rig.CreateCardAsync("Second");

		JsonElement page = JsonSerializer.SerializeToElement(await tools.GetBoardAsync(1, token, count: 1));
		Assert.Equal(first.Id, page.GetProperty("NextAfterId").GetInt32());
		Assert.Equal(first.Id, Assert.Single(page.GetProperty("Cards").EnumerateArray()).GetProperty("Card").GetProperty("Id").GetInt32());
		WorkCard changed = ResultAssert.Succeeded(await rig.Cards.UpdateAsync(first.Id, await TestCardInput.CreateAsync(rig.Database, "Edited"), token));
		await Assert.ThrowsAsync<McpException>(() => tools.DeleteCardAsync(1, first.Id, first.Version, token));
		Assert.Equal("Edited", ResultAssert.Succeeded(await rig.Cards.GetAsync(first.Id, token)).Name);
		await tools.DeleteCardAsync(1, first.Id, changed.Version, token);

		JsonElement next = JsonSerializer.SerializeToElement(await tools.GetBoardAsync(1, token, first.Id, 1));
		Assert.Equal(second.Id, Assert.Single(next.GetProperty("Cards").EnumerateArray()).GetProperty("Card").GetProperty("Id").GetInt32());
		Assert.Equal(JsonValueKind.Null, next.GetProperty("NextAfterId").ValueKind);
	}
}
