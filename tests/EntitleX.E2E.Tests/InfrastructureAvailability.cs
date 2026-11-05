namespace EntitleX.E2E.Tests;

/// <summary>
/// E2E scenarios that need Compose + running hosts opt in via ENTITLEX_E2E=1.
/// Without that flag (or without Docker), tests skip so CI stays green offline.
/// </summary>
internal static class InfrastructureAvailability
{
    public static bool IsE2EEnabled =>
        string.Equals(
            Environment.GetEnvironmentVariable("ENTITLEX_E2E"),
            "1",
            StringComparison.Ordinal);

    public static bool LooksLikeDockerAvailable()
    {
        try
        {
            var dockerHost = Environment.GetEnvironmentVariable("DOCKER_HOST");
            if (!string.IsNullOrWhiteSpace(dockerHost))
            {
                return true;
            }

            // Best-effort: presence of docker CLI does not guarantee a running engine.
            return File.Exists("/var/run/docker.sock")
                   || File.Exists("/run/docker.sock");
        }
        catch
        {
            return false;
        }
    }

    public static bool CanRunFullScenario => IsE2EEnabled && LooksLikeDockerAvailable();
}
