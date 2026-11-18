# Docker Guidelines

## Images and Dockerfiles
- Multi-stage builds: SDK/build stage separate from a minimal runtime stage (for .NET: `mcr.microsoft.com/dotnet/aspnet`, not `sdk`). No build tools, compilers, or debug utilities in the runtime image.
- Trusted, maintained, official base images. Pin versions (digest when strict immutability is needed). Never rely on `latest` for reproducible production deployments. Rebuild regularly for security updates; use `--pull` to check for newer bases.
- `.dockerignore` excludes build output, local dependencies, `.git`, `.env`, and secrets. Keep the build context small; never copy the whole directory without reviewing it.
- Absolute `WORKDIR`. `COPY` over `ADD` (use `--chown` when needed). Exec form for `CMD` and `ENTRYPOINT` so signals reach the process.
- Cache-friendly order: copy dependency manifests and lockfiles (`.csproj`, `packages.lock.json`) and restore before copying source. Combine install and cleanup in one layer. Use BuildKit cache mounts where useful. `--no-cache` only when a clean rebuild is needed.
- Meaningful immutable tags (version or build); OCI labels for distributed images.
- Install only needed packages.

## Secrets and configuration
- Never put secrets in image layers, `ARG`, `ENV`, or copied `.env` files. Deleting a secret in a later layer does not remove it from history.
- Build-time credentials: BuildKit secret mounts. Runtime secrets: Docker/Compose secrets or a secret manager (environment variables are not equivalent to dedicated secret storage).
- Externalize environment-specific configuration. Document required variables; provide safe defaults for non-sensitive values. Separate dev, test, and production configuration.
- A leaked secret is compromised: rotate it and rebuild; removing it from the latest image is not enough.

## Runtime
- One primary responsibility per container; containers are replaceable, not VMs. Run as non-root with explicit UID/GID when ownership matters; verify volume permissions.
- Foreground main process that handles `SIGTERM` and shuts down gracefully. Set `STOPSIGNAL` only if needed. If multiple processes are truly required, ensure supervision and signal handling.
- Health checks with sensible interval, timeout, retries, and start period. A health check does not restart a container by itself; a running container is not necessarily ready. Restart policies do not fix application failures.
- CPU and memory limits where isolation matters. Read-only root filesystem when possible, with explicit writable temp or data locations.
- Drop unneeded Linux capabilities, disable privilege escalation. No `--privileged` as a shortcut for permission problems. Never mount the Docker socket into application containers. Never expose the Docker daemon without authentication. Do not disable security mechanisms to make a container start.
- Running in Docker does not make an application secure; protect the host too.

## Volumes and data
- Persistent data goes in named volumes or external storage, never in the writable layer or anonymous volumes. Bind mounts mainly for development.
- Back up important data and test restores. Treat `docker compose down -v`, `docker system prune --volumes`, and volume deletion as destructive: verify impact and get permission first.

## Networking
- Use user-defined networks and service names; never container IPs. Containers talk on the internal service port; the published port is for host access only.
- Publish only what must be reachable. Keep databases and internal services private. Segment networks by communication need. Avoid host networking unless required.

## Compose
- `compose.yaml` with clear service names, named volumes for stateful services, explicit networks, and health checks where readiness matters.
- `depends_on` with `condition: service_healthy`, but still implement retry and reconnection in the application; startup order does not guarantee availability later.
- Profiles for optional dev tools; separate override files for dev and production; no dev-only settings in production.
- Validate with `docker compose config`. Troubleshoot with `ps`, `logs`, `exec`, health status, and restart counts before making changes. Do not hot-edit running containers; change the image definition and rebuild.

## CI and supply chain
- Build, test, scan, tag, and publish through a controlled pipeline. Test the final runtime image as non-root with production-like configuration, not only the build stage.
- Scan images and dependencies; define severity gates and document accepted exceptions. Pin CI actions, build tools, and critical external images to trusted versions or digests.
- Generate and keep an SBOM; sign images or publish verifiable provenance where supported. Define retention and revocation for vulnerable or superseded images.
- Keep Dockerfiles and Compose files in version control. Verify the app works without files or tools that exist only on the developer machine. Choose image size optimizations with compatibility, updates, and debuggability in mind.
