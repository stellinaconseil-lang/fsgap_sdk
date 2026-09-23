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
}
