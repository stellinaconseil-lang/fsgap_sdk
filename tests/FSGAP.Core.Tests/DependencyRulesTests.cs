using FSGAP.Core.Resolution;

namespace FSGAP.Core.Tests;

/// <summary>Guards the dependency direction: Core builds on the abstractions only, never on a vendor provider.</summary>
public class DependencyRulesTests
{
    [Fact]
    public void Core_references_only_the_base_class_library_and_the_abstractions()
    {
        var references = typeof(AircraftProviderRegistry).Assembly.GetReferencedAssemblies().Select(a => a.Name!);

        Assert.All(references, name => Assert.True(
            name.StartsWith("System", StringComparison.Ordinal) || name is "netstandard" or "FSGAP.Abstractions",
            $"FSGAP.Core must not reference '{name}'."));
    }

    [Fact]
    public void Core_contains_no_vendor_specific_type_or_member()
    {
        string[] forbidden = ["Fenix", "Fnx", "Pmdg", "Lvar", "Efb", "SimConnect"];
        var names = typeof(AircraftProviderRegistry).Assembly.GetTypes()
            .SelectMany(t => t.GetMembers(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic
                    | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.DeclaredOnly)
                .Select(m => $"{t.FullName}.{m.Name}").Prepend(t.FullName!));

        Assert.DoesNotContain(names, name => forbidden.Any(f => name.Contains(f, StringComparison.OrdinalIgnoreCase)));
    }
}
