namespace EntitleX.SharedKernel.Ids;

/// <summary>Unique identity for a single integration message (envelope MessageId).</summary>
public readonly record struct MessageId(Guid Value)
{
    public static MessageId New() => new(Guid.NewGuid());

    public static MessageId From(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("MessageId cannot be empty.", nameof(value));
        }

        return new MessageId(value);
    }

    public override string ToString() => Value.ToString("D");
}
