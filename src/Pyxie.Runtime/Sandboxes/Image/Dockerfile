# Pyxie's base sandbox image. A project's image starts FROM it and adds the project's own build tools.
FROM node:22-bookworm-slim

# Every later RUN, here and in each project's image built on this one, runs through this link to /bin/sh. The build cache records its path with each step, which is how Pyxie finds and removes the cache its old builds left (SandboxNames.BuildShell).
RUN ln -s /bin/sh /usr/local/bin/pyxie-build-sh
SHELL ["/usr/local/bin/pyxie-build-sh", "-c"]

RUN apt-get update \
	&& apt-get install -y --no-install-recommends git ca-certificates curl procps iptables ipset dnsutils python3 \
	&& rm -rf /var/lib/apt/lists/*

RUN npm install -g @anthropic-ai/claude-code @openai/codex opencode-ai \
	&& npm cache clean --force

COPY pyxie-sandbox pyxie-firewall pyxie-exec /usr/local/bin/
RUN chmod 755 /usr/local/bin/pyxie-sandbox /usr/local/bin/pyxie-firewall /usr/local/bin/pyxie-exec \
	&& mkdir -p /work /pyxie \
	&& chown node:node /work

# The start script runs as root only to set the firewall; every command runs as the node user, which cannot change it.
ENV HOME=/home/node \
	DISABLE_AUTOUPDATER=1
WORKDIR /work
ENTRYPOINT ["/usr/local/bin/pyxie-sandbox"]
