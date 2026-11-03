using System.Diagnostics;

namespace EntitleX.Infrastructure.IntegrationTests;

internal static class DockerAvailability
{
    private static readonly Lazy<bool> IsAvailableLazy = new(Detect);

    public static bool IsAvailable => IsAvailableLazy.Value;

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
