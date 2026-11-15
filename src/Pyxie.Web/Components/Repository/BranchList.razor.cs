using Microsoft.AspNetCore.Components;

using Pyxie.Data.Entities;
using Pyxie.Data.Services;
using Pyxie.Data.Services.Pipeline;
using Pyxie.Data.Services.Results;
using Pyxie.Runtime.Git;
using Pyxie.Runtime.Repositories;
using Pyxie.Web.Components.Repository.Navigation;

namespace Pyxie.Web.Components.Repository;

/// <summary>The Branches tab: the local branches with their last commit. A card's branch carries the card, and every branch opens its files.</summary>
public partial class BranchList : ComponentBase, IDisposable
{
	private readonly CancellationTokenSource _cts = new();

	// The names of the cards that branches belong to; null for a card that no longer exists, so it is asked about once.
	private readonly Dictionary<int, string?> _cardNames = [];

	/// <summary>Gets or sets the repository whose branches are listed.</summary>
	[Parameter]
	public required RepositoryOverview Overview { get; set; }

	/// <summary>Gets or sets the card service, supplied by the framework.</summary>
	[Inject]
	public required ICardService CardService { get; set; }

	/// <summary>Gets or sets the navigation manager, supplied by the framework.</summary>
	[Inject]
	public required NavigationManager Navigation { get; set; }

	/// <inheritdoc/>
	public void Dispose()
	{
		_cts.Cancel();
		_cts.Dispose();
	}

	/// <inheritdoc/>
	protected override async Task OnParametersSetAsync()
	{
		try
		{
			foreach (GitBranch branch in Overview.Branches)
			{
				if (CardIdOf(branch) is int cardId && !_cardNames.ContainsKey(cardId))
				{
					ServiceResult<WorkCard> card = await CardService.GetAsync(cardId, _cts.Token);
					_cardNames[cardId] = card.Value?.Name;
				}
			}
		}
		catch (OperationCanceledException) when (_cts.IsCancellationRequested)
		{
		}
	}

	private static int? CardIdOf(GitBranch branch)
	{
		return PipelineContract.TryParseBranchName(branch.Name, out int cardId) ? cardId : null;
	}

	private string Href(GitBranch branch)
	{
		return Navigation.RepositoryUri(new RepositoryLocation(RepositoryTab.Files, branch.Name, string.Empty, string.Empty, 0));
	}
}
