using Pyxie.Tests.Support;

namespace Pyxie.Tests.Runtime.Git;

/// <summary>A repository whose <c>main</c> and a feature branch both moved on from a common commit, with <c>main</c> checked out afterwards. The branches either change the same files differently (a merge conflicts) or different files (a merge succeeds).</summary>
/// <param name="BaseTip">The commit both branches started from.</param>
/// <param name="MainTip">The tip of <c>main</c>, which is checked out.</param>
/// <param name="FeatureTip">The tip of the feature branch.</param>
public sealed record Divergence(string BaseTip, string MainTip, string FeatureTip)
{
	/// <summary>Gets the content <c>main</c> gives a conflicting file.</summary>
	/// <param name="path">The file's path.</param>
	/// <returns>The text.</returns>
	public static string MainContent(string path)
	{
		return $"main version of {path}";
	}

	/// <summary>Gets the content the feature branch gives a conflicting file.</summary>
	/// <param name="path">The file's path.</param>
	/// <returns>The text.</returns>
	public static string FeatureContent(string path)
	{
		return $"feature version of {path}";
	}

	/// <summary>Makes both branches rewrite the same files differently, so merging one into the other conflicts in every file.</summary>
	/// <param name="repository">The repository, with <c>main</c> checked out.</param>
	/// <param name="branch">The feature branch to create.</param>
	/// <param name="paths">The conflicting files.</param>
	/// <returns>The divergence, with <c>main</c> checked out.</returns>
	public static async Task<Divergence> ConflictingAsync(TempGitRepository repository, string branch, params string[] paths)
	{
		foreach (string path in paths)
		{
			await repository.WriteFileAsync(path, $"base version of {path}");
		}

		string baseTip = await repository.CommitAsync("Add the files both sides will change");
		await repository.CreateBranchAsync(branch);

		await repository.CheckoutAsync(branch);
		foreach (string path in paths)
		{
			await repository.WriteFileAsync(path, FeatureContent(path));
		}

		string featureTip = await repository.CommitAsync("Change the files on the feature branch");

		await repository.CheckoutAsync(TempGitRepository.MainBranch);
		foreach (string path in paths)
		{
			await repository.WriteFileAsync(path, MainContent(path));
		}

		string mainTip = await repository.CommitAsync("Change the files on main");

		return new Divergence(baseTip, mainTip, featureTip);
	}

	/// <summary>Makes <c>main</c> add <c>main-only.txt</c> and the feature branch add <c>feature-only.txt</c>, so merging either into the other succeeds.</summary>
	/// <param name="repository">The repository, with <c>main</c> checked out.</param>
	/// <param name="branch">The feature branch to create.</param>
	/// <returns>The divergence, with <c>main</c> checked out.</returns>
	public static async Task<Divergence> CleanAsync(TempGitRepository repository, string branch)
	{
		string baseTip = await repository.GetHashAsync();
		await repository.CreateBranchAsync(branch);

		await repository.CheckoutAsync(branch);
		string featureTip = await repository.CommitFileAsync("feature-only.txt", "from the feature branch");

		await repository.CheckoutAsync(TempGitRepository.MainBranch);
		string mainTip = await repository.CommitFileAsync("main-only.txt", "from main");

		return new Divergence(baseTip, mainTip, featureTip);
	}
}
