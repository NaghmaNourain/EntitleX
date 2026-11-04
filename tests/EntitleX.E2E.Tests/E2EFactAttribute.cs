namespace EntitleX.E2E.Tests;

/// <summary>Skips when ENTITLEX_E2E!=1 or Docker socket is absent.</summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class E2EFactAttribute : FactAttribute
{
    public E2EFactAttribute()
    {
        if (!InfrastructureAvailability.CanRunFullScenario)
        {
            Skip =
                "E2E requires ENTITLEX_E2E=1 and a reachable Docker engine/Compose stack. " +
                "See tests/EntitleX.E2E.Tests/Scenarios/thousand-item-mixed-outcome.md.";
        }
    }
}
