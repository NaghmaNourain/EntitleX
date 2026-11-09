namespace EntitleX.Infrastructure.IntegrationTests;

/// <summary>
/// Skips when neither a CI Postgres env var nor a local (non-CI) Docker daemon is available.
/// Shared with CloudShield.Infrastructure.IntegrationTests via linked compile.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class DockerFactAttribute : FactAttribute
{
    public DockerFactAttribute()
    {
        if (!DockerAvailability.CanRunIntegrationTests)
        {
            Skip =
                "Postgres integration tests require ENTITLEX_TEST_POSTGRES / CLOUDSHIELD_TEST_POSTGRES " +
                "(preferred in CI) or a local Docker daemon for Testcontainers.";
        }
    }
}
