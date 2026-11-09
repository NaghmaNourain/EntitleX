namespace EntitleX.Api.Tests;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class DockerFactAttribute : FactAttribute
{
    public DockerFactAttribute()
    {
        if (!TestPostgres.IsConfigured)
        {
            Skip =
                $"API integration tests require {TestPostgres.EnvVar} (preferred in CI) " +
                "or a local Docker daemon for Testcontainers.";
        }
    }
}
