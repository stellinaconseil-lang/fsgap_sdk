namespace FSGAP.Abstractions.Tests;

/// <summary>Guards principle 1/2: the abstractions depend on nothing but the .NET base class library.</summary>
public class DependencyRulesTests
{
    [Fact]
    public void Abstractions_reference_only_the_base_class_library()
    {
        var references = typeof(IAircraftProvider).Assembly.GetReferencedAssemblies().Select(a => a.Name!);

        Assert.All(references, name => Assert.True(
            name.StartsWith("System", StringComparison.Ordinal) || name is "netstandard" or "mscorlib",
            $"FSGAP.Abstractions must not reference '{name}'."));
    }
}
