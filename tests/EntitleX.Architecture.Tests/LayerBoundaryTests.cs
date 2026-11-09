using System.Reflection;
using Xunit;

namespace EntitleX.Architecture.Tests;

/// <summary>
/// Lightweight layer fitness gates (NetArchTest-equivalent via assembly metadata)
/// so CI does not need the NetArchTest package when NuGet is flaky.
/// </summary>
public sealed class LayerBoundaryTests
{
    [Fact]
    public void Domain_does_not_reference_BuildingBlocks_hosting()
    {
        var refs = GetReferencedAssemblyNames(typeof(Domain.LicenseExtension.LicenseExtensionRequest).Assembly);
        Assert.DoesNotContain(refs, r => r.Equals("EntitleX.BuildingBlocks", StringComparison.Ordinal));
        Assert.Contains(refs, r => r.Equals("EntitleX.SharedKernel", StringComparison.Ordinal));
    }

    [Fact]
    public void Application_does_not_reference_Messaging_adapters()
    {
        var refs = GetReferencedAssemblyNames(typeof(Application.DependencyInjection).Assembly);
        Assert.DoesNotContain(refs, r => r.Equals("EntitleX.Messaging", StringComparison.Ordinal));
        Assert.Contains(refs, r => r.Equals("EntitleX.Messaging.Contracts", StringComparison.Ordinal));
    }

    [Fact]
    public void Application_source_does_not_import_Messaging_Options()
    {
        // Compile-time: Application no longer references Messaging.Options types.
        var appAsm = typeof(Application.DependencyInjection).Assembly;
        var leaked = appAsm.GetTypes()
            .SelectMany(t => t.GetFields(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                .Select(f => f.FieldType)
                .Concat(t.GetProperties().Select(p => p.PropertyType))
                .Concat(t.GetConstructors().SelectMany(c => c.GetParameters().Select(p => p.ParameterType))))
            .Select(t => t.FullName ?? t.Name)
            .Where(n => n.StartsWith("EntitleX.Messaging.Options", StringComparison.Ordinal)
                        || n.StartsWith("EntitleX.Messaging.Kafka", StringComparison.Ordinal)
                        || n.StartsWith("EntitleX.Messaging.Sqs", StringComparison.Ordinal))
            .Distinct()
            .ToList();

        Assert.True(leaked.Count == 0, "Application leaked Messaging adapter types: " + string.Join(", ", leaked));
    }

    private static HashSet<string> GetReferencedAssemblyNames(Assembly assembly) =>
        assembly.GetReferencedAssemblies()
            .Select(a => a.Name ?? string.Empty)
            .Where(n => !string.IsNullOrEmpty(n))
            .ToHashSet(StringComparer.Ordinal);
}
