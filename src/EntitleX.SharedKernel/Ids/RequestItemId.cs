namespace EntitleX.SharedKernel.Ids;

/// <summary>Strongly typed identity for a license-extension request line item.</summary>
public readonly record struct RequestItemId(Guid Value)
{
    public static RequestItemId New() => new(Guid.NewGuid());

    public static RequestItemId From(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("RequestItemId cannot be empty.", nameof(value));
        }

        return new RequestItemId(value);
    }

    public override string ToString() => Value.ToString("D");
}
