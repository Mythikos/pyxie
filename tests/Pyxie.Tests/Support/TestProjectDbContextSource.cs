using Microsoft.EntityFrameworkCore;

using Pyxie.Data.Entities;
using Pyxie.Data.Persistence;

namespace Pyxie.Tests.Support;

/// <summary>A test database as a source of contexts for a given project: every project's context is on the one test database, so a service's own check of the open project decides what it works on.</summary>
public sealed class TestProjectDbContextSource : IProjectDbContextSource
{
	private readonly IDbContextFactory<ProjectDbContext> _factory;

	/// <summary>Initializes a new instance of the <see cref="TestProjectDbContextSource"/> class.</summary>
	/// <param name="factory">The test database's context factory.</param>
	public TestProjectDbContextSource(IDbContextFactory<ProjectDbContext> factory)
	{
		_factory = factory;
	}

	/// <summary>Gets the projects contexts were asked for, in order.</summary>
	public List<Project> Requested { get; } = [];

	/// <summary>Gets or sets what happens before each context is made, such as a test closing the project the service read.</summary>
	public Action<Project>? BeforeCreate { get; set; }

	/// <inheritdoc/>
	public ProjectDbContext CreateFor(Project project)
	{
		Requested.Add(project);
		BeforeCreate?.Invoke(project);

		return _factory.CreateDbContext();
	}
}
