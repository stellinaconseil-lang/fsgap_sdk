using System.Reflection;
using FSGAP.Abstractions;
using FSGAP.Core.Observation;

namespace FSGAP.SimConnect.Tests;

/// <summary>Guards ADR 0004: SimConnect.NET stays inside FSGAP.SimConnect, which stays vendor-neutral.</summary>
public class ArchitectureTests
{
    private static readonly Assembly Transport = typeof(SimConnectSimulator).Assembly;

    [Fact]
    public void Transport_references_abstractions_core_and_simconnect_but_no_provider()
    {
        var references = Transport.GetReferencedAssemblies().Select(a => a.Name!).ToArray();

        Assert.Contains("FSGAP.Abstractions", references);
        Assert.Contains("FSGAP.Core", references);
        Assert.Contains("SimConnect.NET", references);
        Assert.DoesNotContain(references, name => name.Contains("Fenix", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Abstractions_and_core_do_not_reference_simconnect()
    {
        foreach (var assembly in new[] { typeof(IAircraftProvider).Assembly, typeof(ObservableState<>).Assembly })
        {
            Assert.DoesNotContain(assembly.GetReferencedAssemblies(), a => a.Name!.Contains("SimConnect", StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public void Public_api_is_the_transport_class_only()
    {
        Assert.Equal([typeof(SimConnectSimulator)], Transport.GetExportedTypes());
    }

    [Fact]
    public void Public_api_exposes_no_simconnect_library_type()
    {
        var simConnectLibrary = typeof(global::SimConnect.NET.SimConnectClient).Assembly;
        var exposed = Transport.GetExportedTypes()
            .SelectMany(PublicSignatureTypes)
            .SelectMany(Flatten)
            .Where(type => type.Assembly == simConnectLibrary)
            .Select(type => type.FullName)
            .Distinct();

        Assert.Empty(exposed);
    }

    [Fact]
    public void Internal_request_structs_are_visible_to_simconnect_net()
    {
        // Regression guard for a defect found in live validation: SimConnect.NET reads request structs through
        // `dynamic`, which fails on internal structs unless SimConnect.NET can see this assembly's internals.
        var friends = Transport.GetCustomAttributes<System.Runtime.CompilerServices.InternalsVisibleToAttribute>()
            .Select(a => a.AssemblyName);
        var requestStructs = Transport.GetTypes()
            .Where(t => t.IsValueType && t.GetFields().Any(f => f.GetCustomAttributes().Any(a => a.GetType().Assembly.GetName().Name == "SimConnect.NET")))
            .ToArray();

        Assert.NotEmpty(requestStructs);
        Assert.All(requestStructs, t => Assert.False(t.IsVisible, $"{t.Name} should stay internal."));
        Assert.Contains("SimConnect.NET", friends);
    }

    [Fact]
    public void Transport_contains_no_vendor_specific_names()
    {
        string[] forbidden = ["Fenix", "Fnx", "Efb", "8083", "Lvar", "A319", "A320", "A321", "Cfm", "Iae", "RequiredTags"];
        var names = Transport.GetTypes()
            .SelectMany(type => type.GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .Select(member => $"{type.FullName}.{member.Name}")
                .Prepend(type.FullName!));

        Assert.DoesNotContain(names, name => forbidden.Any(f => name.Contains(f, StringComparison.OrdinalIgnoreCase)));
    }

    private static IEnumerable<Type> PublicSignatureTypes(Type type)
    {
        if (type.BaseType is { } baseType)
        {
            yield return baseType;
        }

        foreach (var implemented in type.GetInterfaces())
        {
            yield return implemented;
        }

        foreach (var member in type.GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
        {
            switch (member)
            {
                case PropertyInfo property:
                    yield return property.PropertyType;
                    break;
                case FieldInfo field:
                    yield return field.FieldType;
                    break;
                case EventInfo e when e.EventHandlerType is not null:
                    yield return e.EventHandlerType;
                    break;
                case MethodBase method:
                    if (method is MethodInfo info)
                    {
                        yield return info.ReturnType;
                    }

                    foreach (var parameter in method.GetParameters())
                    {
                        yield return parameter.ParameterType;
                    }

                    break;
            }
        }
    }

    private static IEnumerable<Type> Flatten(Type type)
    {
        yield return type;
        var element = type.HasElementType ? type.GetElementType() : null;
        var nested = type.IsGenericType ? type.GetGenericArguments() : [];
        foreach (var inner in (element is null ? nested : nested.Append(element)).SelectMany(Flatten))
        {
            yield return inner;
        }
    }
}
