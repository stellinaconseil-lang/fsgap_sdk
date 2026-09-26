using System.Reflection;
using FSGAP.Abstractions;
using FSGAP.Core.Observation;
using FSGAP.SimConnect.Native;

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
        // BLOCK 10A: the Synaptic A220 audit added no production detection, overlay or variable to the transport.
        string[] forbidden = ["Fenix", "Fnx", "Efb", "8083", "Lvar", "A319", "A320", "A321", "Cfm", "Iae", "RequiredTags", "Synaptic", "A22X", "A220"];
        var names = Transport.GetTypes()
            .SelectMany(type => type.GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .Select(member => $"{type.FullName}.{member.Name}")
                .Prepend(type.FullName!));

        Assert.DoesNotContain(names, name => forbidden.Any(f => name.Contains(f, StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public void Only_the_session_factory_creates_native_connections()
    {
        // BLOCK 5: telemetry reuses the lifecycle's connection. One factory implementation, one factory field on the
        // transport, and nothing outside the native seam holds a SimConnect.NET client or a factory of its own.
        var client = typeof(global::SimConnect.NET.SimConnectClient);
        var factories = Transport.GetTypes().Where(t => !t.IsInterface && typeof(ISimConnectSessionFactory).IsAssignableFrom(t));
        var holdersOfClient = Transport.GetTypes().Where(t => InstanceFields(t).Any(f => f.FieldType == client)).Select(t => Outermost(t).Name).Distinct().ToArray();
        var holdersOfFactory = Transport.GetTypes().Where(t => InstanceFields(t).Any(f => f.FieldType == typeof(ISimConnectSessionFactory))).Select(t => Outermost(t).Name).Distinct();

        Assert.Equal([typeof(SimConnectNetSessionFactory)], factories);
        // BLOCK 6: VariableSetStructs reads a runtime variable list through the session's client (a parameter captured by
        // its async state machine), inside the native seam; it creates no client.
        // Compiler-generated state machines only keep a local in a field in some builds (Debug hoists it, Release may
        // not), so the rule is an inclusion: every holder belongs to the native seam, and the session always holds one.
        Assert.Subset(
            new HashSet<string> { nameof(SimConnectNetSession), nameof(SimConnectNetSessionFactory), nameof(VariableSetStructs) },
            holdersOfClient.ToHashSet());
        Assert.Contains(nameof(SimConnectNetSession), holdersOfClient);
        Assert.Equal([nameof(SimConnectSimulator)], holdersOfFactory);
    }

    [Fact]
    public void Telemetry_source_holds_no_native_session()
    {
        Assert.DoesNotContain(
            InstanceFields(typeof(Telemetry.TelemetrySource)),
            f => f.FieldType == typeof(ISimConnectSession) || f.FieldType == typeof(ISimConnectSessionFactory));
    }

    /// <summary>Compiler-generated state machines and closures count as the type that declares them.</summary>
    private static Type Outermost(Type type) => type.DeclaringType is { } outer ? Outermost(outer) : type;

    [Fact]
    public void No_fenix_variable_name_exists_in_the_transport_core_or_abstractions()
    {
        // BLOCK 6: Fenix variable names live in FSGAP.Fenix only. Checked on the compiled binaries (string literals and
        // metadata), so no name can slip in through a constant.
        // BLOCK 7 adds the Fenix failure and EFB details: raw failure ids, endpoints, port.
        // BLOCK 10A adds the Synaptic A220 variable prefix: the discovery harness lives in the sample, never here.
        string[] fenixMarkers =
        [
            "S_OH_", "I_OH_", "S_MIP_", "I_MIP_", "I_ENG_FIRE", "L:S_", "L:I_",
            "F_PNEUMATIC", "F_ELEC_", "F_HYD_", "B_INT_SFCDC", "saveManual", "fenix/failures", "8083",
            "A22X", "INI_GPU",
        ];
        Assembly[] assemblies = [Transport, typeof(IAircraftProvider).Assembly, typeof(ObservableState<>).Assembly];

        foreach (var assembly in assemblies)
        {
            var bytes = File.ReadAllBytes(assembly.Location);
            foreach (var marker in fenixMarkers)
            {
                Assert.True(
                    bytes.AsSpan().IndexOf(System.Text.Encoding.Unicode.GetBytes(marker)) < 0
                    && bytes.AsSpan().IndexOf(System.Text.Encoding.UTF8.GetBytes(marker)) < 0,
                    $"{assembly.GetName().Name} contains '{marker}'.");
            }
        }
    }

    [Fact]
    public void The_transport_reads_simulator_variables_without_a_second_client()
    {
        // The variable reader is the transport itself, not a separate connection object.
        Assert.True(typeof(Abstractions.Simulator.ISimulatorVariableReader).IsAssignableFrom(typeof(SimConnectSimulator)));
        Assert.DoesNotContain(
            Transport.GetTypes(),
            t => t != typeof(SimConnectSimulator) && !t.IsInterface && typeof(Abstractions.Simulator.ISimulatorVariableReader).IsAssignableFrom(t));
    }

    private static IEnumerable<FieldInfo> InstanceFields(Type type) =>
        type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

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
