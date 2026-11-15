using Pyxie.Data.Limits;
using Pyxie.Data.Sandboxes;
using Pyxie.Data.Services.Inputs;

using Xunit;

namespace Pyxie.Tests.Services;

/// <summary>A project's Dockerfile must be there, fit the limit, and start a stage from Pyxie's base image, which holds what every sandbox needs.</summary>
public sealed class SandboxSettingsInputRulesTests
{
	private const string Blank = "Enter the project's Dockerfile.";
	private const string NotFromBase = "The Dockerfile must start FROM ${PYXIE_BASE}, Pyxie's base image.";
	private const string FromBase = "FROM ${PYXIE_BASE}\n";

	private static readonly string s_tooLong = $"The Dockerfile must be {PyxieLimits.MaxDockerfileLength} characters or fewer.";

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("   ")]
	[InlineData("\r\n\t\n")]
	public void ValidateDockerfile_WithNothingEntered_AsksForTheDockerfile(string? dockerfile)
	{
		Assert.Equal(Blank, SandboxSettingsInputRules.ValidateDockerfile(dockerfile));
	}

	[Fact]
	public void ValidateDockerfile_OfTheDefaultDockerfile_IsValid()
	{
		Assert.Null(SandboxSettingsInputRules.ValidateDockerfile(SandboxDefaults.Dockerfile));
	}

	[Fact]
	public void ValidateDockerfile_LongerThanTheLimitOnceItsFinalLineBreakIsAdded_SaysHowLongItMayBe()
	{
		Assert.Equal(s_tooLong, SandboxSettingsInputRules.ValidateDockerfile(OfLength(PyxieLimits.MaxDockerfileLength)));
	}

	[Fact]
	public void ValidateDockerfile_AtTheLimitWithItsFinalLineBreak_IsValid()
	{
		Assert.Null(SandboxSettingsInputRules.ValidateDockerfile(OfLength(PyxieLimits.MaxDockerfileLength - 1)));
		Assert.Equal(PyxieLimits.MaxDockerfileLength, SandboxSettingsInputRules.NormalizeDockerfile(OfLength(PyxieLimits.MaxDockerfileLength - 1)).Length);
	}

	[Fact]
	public void ValidateDockerfile_AtTheLimitWithSurroundingWhitespace_IsValidBecauseTheWhitespaceIsNotKept()
	{
		Assert.Null(SandboxSettingsInputRules.ValidateDockerfile("\n\n  " + OfLength(PyxieLimits.MaxDockerfileLength - 1) + "  \n\n"));
	}

	[Theory]
	[InlineData("FROM node:22-bookworm-slim\nRUN apt-get update\n")]
	[InlineData("FROM pyxie-base\n")]
	[InlineData("ARG PYXIE_BASE\nRUN echo ${PYXIE_BASE}\n")]
	[InlineData("# FROM ${PYXIE_BASE}\nFROM node:22\n")]
	[InlineData("FROM node:22\nRUN echo FROM ${PYXIE_BASE}\n")]
	[InlineData("ARG PYXIE_BASE_IMAGE\nFROM ${PYXIE_BASE_IMAGE}\n")]
	public void ValidateDockerfile_WithNoStageFromTheBaseImage_SaysToStartFromIt(string dockerfile)
	{
		Assert.Equal(NotFromBase, SandboxSettingsInputRules.ValidateDockerfile(dockerfile));
	}

	[Theory]
	[InlineData("ARG PYXIE_BASE\nFROM node:22 AS build\nRUN npm ci\nFROM ${PYXIE_BASE}\nCOPY --from=build /app /opt/app\n")]
	[InlineData("ARG PYXIE_BASE\nFROM golang:1.23 AS tools\nRUN go install example.com/tool@latest\nFROM ${PYXIE_BASE} AS final\nCOPY --from=tools /go/bin/tool /usr/local/bin/\n")]
	public void ValidateDockerfile_MultiStageWithAStageFromTheBaseImage_IsValid(string dockerfile)
	{
		Assert.Null(SandboxSettingsInputRules.ValidateDockerfile(dockerfile));
	}

	[Theory]
	[InlineData("ARG PYXIE_BASE\nfrom ${PYXIE_BASE}\n")]
	[InlineData("ARG PYXIE_BASE\nFrom ${PYXIE_BASE}\n")]
	[InlineData("arg PYXIE_BASE\nfRoM ${PYXIE_BASE}\n")]
	public void ValidateDockerfile_WithTheFromInstructionInAnyCase_IsValidAsDockerReadsInstructionsCaseInsensitively(string dockerfile)
	{
		Assert.Null(SandboxSettingsInputRules.ValidateDockerfile(dockerfile));
	}

	[Theory]
	[InlineData("ARG PYXIE_BASE\nFROM $PYXIE_BASE\n")]
	[InlineData("ARG PYXIE_BASE\n    FROM\t${PYXIE_BASE}\n")]
	[InlineData("ARG PYXIE_BASE\nFROM ${PYXIE_BASE} AS base\n")]
	public void ValidateDockerfile_WithTheBaseImageWrittenAnotherWayDockerAccepts_IsValid(string dockerfile)
	{
		Assert.Null(SandboxSettingsInputRules.ValidateDockerfile(dockerfile));
	}

	// A Dockerfile from the base image padded with a comment to an exact length.
	private static string OfLength(int length)
	{
		return FromBase + new string('#', length - FromBase.Length);
	}
}
