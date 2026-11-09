using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EntitleX.Application.Abstractions;
using EntitleX.Application.Common.Exceptions;

namespace EntitleX.Api.Infrastructure;

public static class IdempotencyKeyHelper
{
    public const string HeaderName = "Idempotency-Key";

    public static string? ReadKey(HttpRequest request, string? bodyKey = null)
    {
        var header = request.Headers[HeaderName].FirstOrDefault();
        var key = header ?? bodyKey;
        return string.IsNullOrWhiteSpace(key) ? null : key.Trim();
    }

    public static IResult MissingKeyResult() =>
        Results.ValidationProblem(new Dictionary<string, string[]>
        {
            [HeaderName] = ["Idempotency-Key header is required for this write operation."]
        });

    public static string Fingerprint(object payload)
    {
        var json = JsonSerializer.Serialize(payload);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(json));
        return Convert.ToHexString(hash);
    }

    public static void EnsureReplayMatches(
        ApiIdempotencyLookup existing,
        string operation,
        string actorUserId,
        string fingerprint)
    {
        if (!string.Equals(existing.Operation, operation, StringComparison.Ordinal)
            || !string.Equals(existing.ActorUserId, actorUserId, StringComparison.Ordinal)
            || !string.Equals(existing.RequestFingerprint, fingerprint, StringComparison.Ordinal))
        {
            throw new ConflictException(
                $"Idempotency key '{existing.IdempotencyKey}' is already bound to a different write.");
        }
    }
}
