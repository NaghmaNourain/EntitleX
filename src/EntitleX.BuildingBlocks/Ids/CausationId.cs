namespace EntitleX.BuildingBlocks.Ids;

/// <summary>Identity of the message that caused this one (parent MessageId).</summary>
public readonly record struct CausationId(Guid Value)
{
    public static CausationId From(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("CausationId cannot be empty.", nameof(value));
        }

        return new CausationId(value);
    }

    public static CausationId FromMessageId(MessageId messageId) => new(messageId.Value);

    public static CausationId? FromOptional(Guid? value) =>
        value is null || value == Guid.Empty ? null : From(value.Value);

    public override string ToString() => Value.ToString("D");
}
