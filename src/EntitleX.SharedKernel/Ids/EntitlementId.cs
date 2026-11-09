namespace EntitleX.SharedKernel.Ids;

/// <summary>Provider-scoped entitlement identifier (opaque string from vendor systems).</summary>
public readonly record struct EntitlementId(string Value)
{
    public static EntitlementId From(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("EntitlementId is required.", nameof(value));
        }

        return new EntitlementId(value.Trim());
    }

    public override string ToString() => Value;
}
