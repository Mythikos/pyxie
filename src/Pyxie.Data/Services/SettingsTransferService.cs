using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

using Pyxie.Data.Entities;
using Pyxie.Data.Persistence;
using Pyxie.Data.Projects;
using Pyxie.Data.Services.Inputs;
using Pyxie.Data.Services.Internal;
using Pyxie.Data.Services.Results;
using Pyxie.Data.Transfer;
using Pyxie.Data.Transfer.Planning;

namespace Pyxie.Data.Services;

/// <summary>Default <see cref="ISettingsTransferService"/> backed by <see cref="ProjectDbContext"/>. It writes agents, gates, categories, and sandbox settings through the same stores and rules as their services (<see cref="AgentStore"/>, <see cref="GateStore"/>, <see cref="CategoryStore"/>, <see cref="SandboxSettingsStore"/>), so an import is exactly as careful as the editors.</summary>
public sealed class SettingsTransferService : ISettingsTransferService
{
	private const string NotOpenMessage = "The project is not open.";
	private const string RefusedLead = "Nothing was imported.";
	private const string StaleMessage = "The project's settings changed after the file was read, so nothing was imported. Check what the import will do, then import again.";
	private const string ChangedWhileImportingMessage = "The project's settings changed while they were imported, so nothing was imported. Import again.";

	private readonly IProjectDbContextSource _contexts;
	private readonly OpenProject _openProject;
	private readonly TimeProvider _timeProvider;

	/// <summary>Initializes a new instance of the <see cref="SettingsTransferService"/> class.</summary>
	/// <param name="contexts">Creates a short-lived context per operation, on the project the operation is for.</param>
	/// <param name="openProject">The open project, which every operation must be for when it starts.</param>
	/// <param name="timeProvider">The clock that stamps the cards an import releases and the sandbox settings it saves.</param>
	public SettingsTransferService(IProjectDbContextSource contexts, OpenProject openProject, TimeProvider timeProvider)
	{
		_contexts = contexts;
		_openProject = openProject;
		_timeProvider = timeProvider;
	}

	/// <inheritdoc/>
	public async Task<ServiceResult<ProjectSettings>> ExportAsync(int projectId, CancellationToken cancellationToken)
	{
		await using ProjectDbContext? context = CreateContextFor(projectId, out _);
		if (context is null)
		{
			return ServiceResult<ProjectSettings>.Failure(NotOpenMessage);
		}

		// No transaction: each section is read whole, and every section on its own is a project's valid settings.
		SettingsSnapshot snapshot = await SettingsSnapshot.ReadAsync(context, cancellationToken);

		return ServiceResult<ProjectSettings>.Success(snapshot.ToSettings());
	}

	/// <inheritdoc/>
	public async Task<ServiceResult<SettingsImportPreview>> PreviewAsync(int projectId, ProjectSettings settings, CancellationToken cancellationToken)
	{
		await using ProjectDbContext? context = CreateContextFor(projectId, out _);
		if (context is null)
		{
			return ServiceResult<SettingsImportPreview>.Failure(NotOpenMessage);
		}

		ProjectSettings normalized = SettingsRules.Normalize(settings);
		SettingsSnapshot snapshot = await SettingsSnapshot.ReadAsync(context, cancellationToken);
		List<SectionPreview> sections = [];
		foreach (TransferSection section in settings.Sections)
		{
			IReadOnlyList<string> problems = SettingsRules.Check(normalized, section);
			if (problems.Count > 0)
			{
				sections.Add(new SectionPreview(SectionChanges.None(section), problems, snapshot.RevisionOf(section)));
				continue;
			}

			// Read-only: the counts of unfinished cards do not depend on releasing finished ones, so the preview names every blocker in the words the import would, without writing.
			SettingsImportPlan plan = SettingsImportPlanner.Plan(snapshot, normalized, new HashSet<TransferSection> { section });
			sections.Add(new SectionPreview(plan.ChangesOf(section), await BlockersAsync(context, plan, releasedAt: null, cancellationToken), snapshot.RevisionOf(section)));
		}

		return ServiceResult<SettingsImportPreview>.Success(new SettingsImportPreview(projectId, sections));
	}

	/// <inheritdoc/>
	public async Task<ServiceResult> ImportAsync(ProjectSettings settings, SettingsImportPreview preview, IReadOnlySet<TransferSection> sections, CancellationToken cancellationToken)
	{
		if (sections.Count == 0)
		{
			return ServiceResult.Failure("Choose what to import.");
		}

		foreach (TransferSection section in Ordered(sections))
		{
			if (!settings.Has(section))
			{
				return ServiceResult.Failure($"The file has no {WordFor(section)}.");
			}

			if (preview.Find(section) is null)
			{
				return ServiceResult.Failure($"What the import does to the {WordFor(section)} was not reviewed, so nothing was imported. Check what the import will do, then import again.");
			}
		}

		ProjectSettings normalized = SettingsRules.Normalize(settings);
		List<string> problems = [.. Ordered(sections).SelectMany(section => SettingsRules.Check(normalized, section))];
		if (problems.Count > 0)
		{
			return Refused(problems);
		}

		await using ProjectDbContext? context = CreateContextFor(preview.ProjectId, out Project? open);
		if (context is null)
		{
			return ServiceResult.Failure(open is null ? "No project is open, so nothing was imported." : "Another project is open, so nothing was imported.");
		}

		// Every return before the commit rolls back, the released finished cards included, and so does a write that fails. On SQLite the transaction takes the database's write lock at once, so nothing else writes until it ends; the check after the write below holds the import to the file even on a provider where only the first write takes the lock.
		await using IDbContextTransaction transaction = await context.Database.BeginTransactionAsync(cancellationToken);

		SettingsImportPlan plan = SettingsImportPlanner.Plan(await SettingsSnapshot.ReadAsync(context, cancellationToken), normalized, sections);
		if (!MatchesPreview(plan, preview, sections))
		{
			return ServiceResult.Failure(StaleMessage);
		}

		DateTime now = _timeProvider.GetUtcNow().UtcDateTime;
		IReadOnlyList<string> blockers = await BlockersAsync(context, plan, now, cancellationToken);
		if (blockers.Count > 0)
		{
			return Refused(blockers);
		}

		if (await ApplyAsync(context, plan, normalized, now, cancellationToken) is string refusal)
		{
			return ServiceResult.Failure(refusal);
		}

		// Checked again after the write, as the editors do: the project must now match the file in every imported section.
		context.ChangeTracker.Clear();
		SettingsImportPlan after = SettingsImportPlanner.Plan(await SettingsSnapshot.ReadAsync(context, cancellationToken), normalized, sections);
		if (sections.Any(section => after.ChangesOf(section).HasChanges))
		{
			return ServiceResult.Failure(ChangedWhileImportingMessage);
		}

		await transaction.CommitAsync(cancellationToken);

		return ServiceResult.Success();
	}

	private static IEnumerable<TransferSection> Ordered(IReadOnlySet<TransferSection> sections)
	{
		return Enum.GetValues<TransferSection>().Where(sections.Contains);
	}

	private static bool MatchesPreview(SettingsImportPlan plan, SettingsImportPreview preview, IReadOnlySet<TransferSection> sections)
	{
		return sections.All(section => preview.Find(section) is SectionPreview reviewed && plan.Snapshot.RevisionOf(section) == reviewed.Revision && plan.ChangesOf(section).SameAs(reviewed.Changes));
	}

	private static ServiceResult Refused(IEnumerable<string> reasons)
	{
		return ServiceResult.Failure(string.Join("\n", reasons.Prepend(RefusedLead)));
	}

	private static string WordFor(TransferSection section)
	{
		return section switch
		{
			TransferSection.Agents => "agents",
			TransferSection.Gates => "gates",
			TransferSection.Categories => "categories",
			_ => "sandbox settings",
		};
	}

	// What stops the plan, in the editors' words: an agent the file removes, or takes a role from, that unfinished cards still have in it, and a category the file removes that any card uses. Given a time, each agent's finished cards are released first, as the transaction's first writes, so each count follows a write (AgentStore.ReleaseAsync); only an agent that is removed or loses a role is released, so no other card changes. Without one, nothing is written.
	private static async Task<IReadOnlyList<string>> BlockersAsync(ProjectDbContext context, SettingsImportPlan plan, DateTime? releasedAt, CancellationToken cancellationToken)
	{
		List<string> blockers = [];
		if (plan.Agents is NamePlan<Agent, AgentInput> agents)
		{
			foreach (Agent removed in agents.Removed)
			{
				(int working, int reviewing, int cards) = await HeldAsync(context, removed.Id, worker: true, reviewer: true, releasedAt, cancellationToken);
				if (cards > 0)
				{
					blockers.Add(AgentStore.DeleteRefusal(removed.Name, working, reviewing, cards));
				}
			}

			foreach ((Agent existing, AgentInput incoming) in agents.Matched)
			{
				bool dropsWorker = existing.CanWork && !incoming.CanWork;
				bool dropsReviewer = existing.CanReview && !incoming.CanReview;
				if (!dropsWorker && !dropsReviewer)
				{
					continue;
				}

				(int working, int reviewing, _) = await HeldAsync(context, existing.Id, dropsWorker, dropsReviewer, releasedAt, cancellationToken);
				if (working > 0)
				{
					blockers.Add(AgentStore.KeepWorkerRefusal(existing.Name, working));
				}

				if (reviewing > 0)
				{
					blockers.Add(AgentStore.KeepReviewerRefusal(existing.Name, reviewing));
				}
			}
		}

		if (plan.Categories is NamePlan<Category, CategoryInput> categories)
		{
			foreach (Category removed in categories.Removed)
			{
				int cards = await CategoryStore.CountCardsAsync(context, removed.Id, cancellationToken);
				if (cards > 0)
				{
					blockers.Add(CategoryStore.InUseRefusal(removed.Name, cards));
				}
			}
		}

		return blockers;
	}

	private static Task<(int Working, int Reviewing, int Cards)> HeldAsync(ProjectDbContext context, int agentId, bool worker, bool reviewer, DateTime? releasedAt, CancellationToken cancellationToken)
	{
		return releasedAt is DateTime now ? AgentStore.ReleaseAsync(context, agentId, worker, reviewer, now, cancellationToken) : AgentStore.CountUnfinishedAsync(context, agentId, worker, reviewer, cancellationToken);
	}

	// Writes the plan: one save for agents, categories, and gates, then the sandbox settings. Returns why the sandbox settings could not be saved, when another save got there first.
	private static async Task<string?> ApplyAsync(ProjectDbContext context, SettingsImportPlan plan, ProjectSettings settings, DateTime now, CancellationToken cancellationToken)
	{
		// A section the import does not change is not written at all, so nothing in it moves, such as gate positions an older write left with gaps.
		if (plan.Agents is NamePlan<Agent, AgentInput> agents && plan.ChangesOf(TransferSection.Agents).HasChanges)
		{
			// The unique index on the merge role checks each row as it is written, in whatever order EF sends them, so the role is cleared from every agent but the one the file keeps it on before any tracked write; the agents are loaded after, so the tracker holds what the clear left. The tracked writes then give the role only to that agent, or to a new one when the file's merge agent is new.
			int? keptMergeAgentId = agents.Matched.Where(pair => pair.Incoming.CanMerge).Select(pair => (int?)pair.Existing.Id).FirstOrDefault();
			await AgentStore.ClearMergeRoleExceptAsync(context, keptMergeAgentId, cancellationToken);

			Dictionary<int, Agent> tracked = await context.Agents.ToDictionaryAsync(agent => agent.Id, cancellationToken);
			foreach ((Agent existing, AgentInput incoming) in agents.Matched)
			{
				AgentStore.Apply(tracked[existing.Id], incoming);
			}

			// Unfinished cards hold no removed agent, which BlockersAsync made sure of, and finished ones were released; the SET NULL foreign keys are the backstop.
			foreach (Agent removed in agents.Removed)
			{
				context.Agents.Remove(tracked[removed.Id]);
			}

			foreach (AgentInput added in agents.Added)
			{
				Agent agent = new();
				AgentStore.Apply(agent, added);
				context.Agents.Add(agent);
			}
		}

		if (plan.Categories is NamePlan<Category, CategoryInput> categories && plan.ChangesOf(TransferSection.Categories).HasChanges)
		{
			Dictionary<int, Category> tracked = await context.Categories.ToDictionaryAsync(category => category.Id, cancellationToken);
			foreach ((Category existing, CategoryInput incoming) in categories.Matched)
			{
				tracked[existing.Id].Name = incoming.Name;
				tracked[existing.Id].Color = incoming.Color;
			}

			// No card uses a removed category, which BlockersAsync made sure of; the Restrict foreign key is the backstop.
			foreach (Category removed in categories.Removed)
			{
				context.Categories.Remove(tracked[removed.Id]);
			}

			foreach (CategoryInput added in categories.Added)
			{
				context.Categories.Add(new Category { Name = added.Name, Color = added.Color });
			}
		}

		if (plan.Gates is NamePlan<Gate, GateInput> gates && settings.Gates is IReadOnlyList<GateInput> fileGates && plan.ChangesOf(TransferSection.Gates).HasChanges)
		{
			Dictionary<int, Gate> tracked = (await GateStore.LoadInOrderAsync(context, cancellationToken)).ToDictionary(gate => gate.Id);
			Dictionary<string, Gate> byName = new(InputRules.NameComparer);
			foreach ((Gate existing, GateInput incoming) in gates.Matched)
			{
				GateStore.Apply(tracked[existing.Id], incoming);
				byName[incoming.Name] = tracked[existing.Id];
			}

			foreach (Gate removed in gates.Removed)
			{
				context.Gates.Remove(tracked[removed.Id]);
			}

			foreach (GateInput added in gates.Added)
			{
				Gate gate = new();
				GateStore.Apply(gate, added);
				context.Gates.Add(gate);
				byName[added.Name] = gate;
			}

			// The file's order is the order gates run in; its names are unique ignoring case, so each names one gate.
			GateStore.Renumber([.. fileGates.Select(input => byName[input.Name])]);
		}

		await context.SaveChangesAsync(cancellationToken);

		if (plan.DockerfileChanges && plan.Dockerfile is string dockerfile)
		{
			// Only a changed Dockerfile is written, so an editor that read the settings before stays able to save when nothing changed. The write advances the version, so an editor that read them before cannot save over the import.
			bool saved = plan.Snapshot.Sandbox is SandboxSettings stored
				? await SandboxSettingsStore.UpdateAsync(context, stored.Id, stored.Version, dockerfile, now, cancellationToken) == 1
				: await SandboxSettingsStore.InsertFirstAsync(context, dockerfile, now, cancellationToken) is not null;
			if (!saved)
			{
				return StaleMessage;
			}
		}

		return null;
	}

	// The open project is read once, and the context is made for that project, so a project opened or closed meanwhile can neither turn the work to another project nor fail it with an exception. Work begun on a project that is closed after this goes on in its own database, as a write begun by any editor does.
	private ProjectDbContext? CreateContextFor(int projectId, out Project? open)
	{
		open = _openProject.Current;

		return open is not null && open.Id == projectId ? _contexts.CreateFor(open) : null;
	}
}
