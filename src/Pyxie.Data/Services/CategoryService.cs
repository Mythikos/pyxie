using Microsoft.EntityFrameworkCore;

using Pyxie.Data.Entities;
using Pyxie.Data.Enums;
using Pyxie.Data.Limits;
using Pyxie.Data.Persistence;
using Pyxie.Data.Services.Internal;
using Pyxie.Data.Services.Results;

namespace Pyxie.Data.Services;

/// <summary>Default <see cref="ICategoryService"/> backed by <see cref="ProjectDbContext"/>.</summary>
public sealed class CategoryService : ICategoryService
{
	private const string NameLabel = "Category name";
	private const string NotFoundMessage = "Category not found.";

	private readonly IDbContextFactory<ProjectDbContext> _contextFactory;

	/// <summary>Initializes a new instance of the <see cref="CategoryService"/> class.</summary>
	/// <param name="contextFactory">The factory used to create a short-lived context per operation.</param>
	public CategoryService(IDbContextFactory<ProjectDbContext> contextFactory)
	{
		_contextFactory = contextFactory;
	}

	/// <inheritdoc/>
	public async Task<IReadOnlyList<Category>> ListAsync(CancellationToken cancellationToken)
	{
		await using ProjectDbContext context = await _contextFactory.CreateDbContextAsync(cancellationToken);

		return await context.Categories
			.AsNoTracking()
			.OrderBy(category => category.Name)
			.ThenBy(category => category.Id)
			.ToListAsync(cancellationToken);
	}

	/// <inheritdoc/>
	public async Task<ServiceResult<Category>> CreateAsync(string name, CancellationToken cancellationToken)
	{
		string normalized = InputRules.Normalize(name);
		string? inputError = InputRules.ValidateRequired(normalized, NameLabel, PyxieLimits.MaxNameLength);
		if (inputError is not null)
		{
			return ServiceResult<Category>.Failure(inputError);
		}

		await using ProjectDbContext context = await _contextFactory.CreateDbContextAsync(cancellationToken);

		if (await IsNameTakenAsync(context, normalized, null, cancellationToken))
		{
			return ServiceResult<Category>.Failure(DuplicateMessage(normalized));
		}

		Category category = new() { Name = normalized };
		context.Categories.Add(category);
		if (!await TrySaveAsync(context, normalized, null, cancellationToken))
		{
			return ServiceResult<Category>.Failure(DuplicateMessage(normalized));
		}

		return ServiceResult<Category>.Success(category);
	}

	/// <inheritdoc/>
	public async Task<ServiceResult<Category>> RenameAsync(int id, string name, CancellationToken cancellationToken)
	{
		string normalized = InputRules.Normalize(name);
		string? inputError = InputRules.ValidateRequired(normalized, NameLabel, PyxieLimits.MaxNameLength);
		if (inputError is not null)
		{
			return ServiceResult<Category>.Failure(inputError);
		}

		await using ProjectDbContext context = await _contextFactory.CreateDbContextAsync(cancellationToken);

		Category? category = await context.Categories.FirstOrDefaultAsync(existing => existing.Id == id, cancellationToken);
		if (category is null)
		{
			return ServiceResult<Category>.Failure(NotFoundMessage);
		}

		if (await IsNameTakenAsync(context, normalized, id, cancellationToken))
		{
			return ServiceResult<Category>.Failure(DuplicateMessage(normalized));
		}

		category.Name = normalized;
		if (!await TrySaveAsync(context, normalized, id, cancellationToken))
		{
			return ServiceResult<Category>.Failure(DuplicateMessage(normalized));
		}

		return ServiceResult<Category>.Success(category);
	}

	/// <inheritdoc/>
	public async Task<ServiceResult<Category>> SetColorAsync(int id, CategoryColor color, CancellationToken cancellationToken)
	{
		if (!Enum.IsDefined(color))
		{
			return ServiceResult<Category>.Failure("Category color is not recognized.");
		}

		await using ProjectDbContext context = await _contextFactory.CreateDbContextAsync(cancellationToken);

		// One conditional statement, so a category deleted by someone else mid-request is reported as missing rather than written back.
		int changed = await context.Categories.Where(category => category.Id == id).ExecuteUpdateAsync(changes => changes.SetProperty(category => category.Color, color), cancellationToken);
		Category? updated = changed == 0 ? null : await context.Categories.AsNoTracking().FirstOrDefaultAsync(category => category.Id == id, cancellationToken);

		return updated is null ? ServiceResult<Category>.Failure(NotFoundMessage) : ServiceResult<Category>.Success(updated);
	}

	/// <inheritdoc/>
	public async Task<ServiceResult> DeleteAsync(int id, CancellationToken cancellationToken)
	{
		await using ProjectDbContext context = await _contextFactory.CreateDbContextAsync(cancellationToken);

		Category? category = await context.Categories.FirstOrDefaultAsync(existing => existing.Id == id, cancellationToken);
		if (category is null)
		{
			return ServiceResult.Failure(NotFoundMessage);
		}

		int cardCount = await context.Cards.CountAsync(card => card.CategoryId == id, cancellationToken);
		if (cardCount > 0)
		{
			return ServiceResult.Failure($"Category '{category.Name}' is used by {cardCount} {(cardCount == 1 ? "card" : "cards")} and cannot be deleted.");
		}

		context.Categories.Remove(category);
		await context.SaveChangesAsync(cancellationToken);

		return ServiceResult.Success();
	}

	private static string DuplicateMessage(string name)
	{
		return $"A category named '{name}' already exists.";
	}

	// A concurrent writer can win between the name check and the save; the unique index then rejects this write, which is reported as a duplicate.
	private static async Task<bool> TrySaveAsync(ProjectDbContext context, string name, int? excludedId, CancellationToken cancellationToken)
	{
		try
		{
			await context.SaveChangesAsync(cancellationToken);

			return true;
		}
		catch (DbUpdateException)
		{
			if (await IsNameTakenAsync(context, name, excludedId, cancellationToken))
			{
				return false;
			}

			throw;
		}
	}

	private static async Task<bool> IsNameTakenAsync(ProjectDbContext context, string name, int? excludedId, CancellationToken cancellationToken)
	{
		List<string> otherNames = await context.Categories
			.AsNoTracking()
			.Where(category => category.Id != excludedId)
			.Select(category => category.Name)
			.ToListAsync(cancellationToken);

		return InputRules.ContainsName(otherNames, name);
	}
}
