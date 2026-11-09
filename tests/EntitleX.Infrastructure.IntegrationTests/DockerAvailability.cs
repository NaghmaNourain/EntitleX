using System.Diagnostics;

namespace EntitleX.Infrastructure.IntegrationTests;

internal static class DockerAvailability
{
    private static readonly Lazy<bool> IsAvailableLazy = new(Detect);

    public static bool IsAvailable => IsAvailableLazy.Value;

    /// <summary>
    /// True when CI provides a Postgres connection string, or when a local (non-CI) Docker daemon exists.
    /// Under CI we intentionally ignore Docker so Testcontainers is not attempted on runners
    /// that advertise Docker but cannot accept nested Postgres connections.
    /// </summary>
    public static bool CanRunIntegrationTests
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ENTITLEX_TEST_POSTGRES"))
                || !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("CLOUDSHIELD_TEST_POSTGRES")))
            {
                return true;
            }

            if (IsCiEnvironment)
            {
                return false;
            }

            return IsAvailable;
        }
    }

    private static bool IsCiEnvironment =>
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("CI"))
        || !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("GITHUB_ACTIONS"));

    private static bool Detect()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "docker",
                Arguments = "info",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });

            if (process is null)
            {
                return false;
            }

            process.WaitForExit(TimeSpan.FromSeconds(10));
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }
}
