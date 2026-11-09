namespace EntitleX.Api.Tests;

/// <summary>Skips the test when Docker is not available for Testcontainers.</summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class DockerFactAttribute : FactAttribute
{
    public DockerFactAttribute()
    {
        if (!DockerAvailability.IsAvailable)
        {
            Skip = "Docker is not available; WebApplicationFactory API tests were skipped.";
        }
    }
}
