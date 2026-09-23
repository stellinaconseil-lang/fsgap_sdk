using System.Reflection;

namespace FSGAP.Abstractions.Tests;

/// <summary>
/// Guards principles 1, 2 and 6: the abstractions depend on nothing but the .NET base class library, and their
/// public surface names no vendor, simulator library or consuming application.
/// </summary>
public class DependencyRulesTests
{
    private static readonly string[] ForbiddenNames = ["SimConnect", "Fenix", "Fnx", "Pmdg", "Efb", "Lvar", "Hvar", "Hangar", "Flippp", "VendorId"];

    private static readonly Assembly Abstractions = typeof(IAircraftProvider).Assembly;

    [Fact]
    public void Abstractions_reference_only_the_base_class_library()
    {
        var references = Abstractions.GetReferencedAssemblies().Select(a => a.Name!);

        Assert.All(references, name => Assert.True(
            name.StartsWith("System", StringComparison.Ordinal) || name is "netstandard" or "mscorlib",
            $"FSGAP.Abstractions must not reference '{name}'."));
    }

    [Fact]
    public void Public_surface_names_no_vendor_simulator_library_or_application()
    {
        var publicNames = Abstractions.GetExportedTypes().SelectMany(PublicNamesOf);

        var offenders = publicNames
            .Where(name => ForbiddenNames.Any(forbidden => name.Contains(forbidden, StringComparison.OrdinalIgnoreCase)))
            .ToArray();

        Assert.Empty(offenders);
    }

    private static IEnumerable<string> PublicNamesOf(Type type)
    {
        yield return type.FullName!;
        foreach (var member in type.GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
        {
            yield return $"{type.Name}.{member.Name}";
            if (member is MethodBase method)
            {
                foreach (var parameter in method.GetParameters())
                {
                    yield return $"{type.Name}.{member.Name}({parameter.Name})";
                }
            }
        }
    }
}
