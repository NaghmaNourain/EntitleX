namespace EntitleX.Api.Tests;

/// <summary>
/// Single shared <see cref="EntitleXApiFactory"/> for all API integration tests.
/// WebApplicationFactory + Serilog bootstrap is not safe across parallel hosts.
/// </summary>
[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<EntitleXApiFactory>
{
    public const string Name = "EntitleX.Api";
}
