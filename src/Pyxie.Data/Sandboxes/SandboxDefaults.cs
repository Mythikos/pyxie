namespace Pyxie.Data.Sandboxes;

/// <summary>The defaults of a project's sandbox settings, and the build argument that names Pyxie's base image to a project's Dockerfile.</summary>
public static class SandboxDefaults
{
	/// <summary>The build argument Pyxie passes the base image's tag in; a project's Dockerfile starts <c>FROM ${PYXIE_BASE}</c>.</summary>
	public const string BaseImageArgument = "PYXIE_BASE";

	/// <summary>The Dockerfile a project starts with: Pyxie's base image and nothing else.</summary>
	public const string Dockerfile = """
		ARG PYXIE_BASE
		FROM ${PYXIE_BASE}

		# Add what this project needs to build and test. These commands run as root; agents and gates run as the node user.
		# For example, the .NET SDK:
		# RUN curl -fsSL https://dot.net/v1/dotnet-install.sh | bash -s -- --channel 10.0 --install-dir /usr/share/dotnet \
		#     && ln -s /usr/share/dotnet/dotnet /usr/local/bin/dotnet

		# To select harness versions for this project, uncomment the relevant installs.
		# RUN npm install -g @anthropic-ai/claude-code@latest && npm cache clean --force
		# RUN npm install -g @openai/codex@latest && npm cache clean --force
		# RUN npm install -g @deepseek-ai/dsh@0.2.2-rc.5 && npm cache clean --force

		""";
}
