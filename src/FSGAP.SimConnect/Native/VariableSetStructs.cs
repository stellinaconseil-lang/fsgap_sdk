using System.Collections.Concurrent;
using System.Reflection;
using System.Reflection.Emit;
using FSGAP.Abstractions.Simulator;
using SimConnect.NET;

namespace FSGAP.SimConnect.Native;

/// <summary>
/// Turns a runtime list of variables into the one thing SimConnect.NET can read in a single request: a struct
/// whose fields carry <see cref="SimConnectAttribute"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why emit a struct.</b> SimConnect.NET 0.2.2 batches only through <c>SimVars.GetAsync&lt;T&gt;()</c> over an
/// attributed struct. Its other read, <c>GetAsync&lt;T&gt;(name, unit)</c>, is one native request per variable. The
/// variable names belong to the aircraft providers (FSGAP.Fenix must never write them into this assembly), so the
/// struct cannot be declared at compile time here: it is emitted once per distinct list, with one
/// <see cref="double"/> field per variable, and read through the library's public generic API.
/// </para>
/// <para>
/// This uses no private member of SimConnect.NET, unlike the reflection kept out of scope by ADR 0004: the emitted
/// type is an ordinary public struct, and <c>GetAsync&lt;T&gt;</c> is called through its public signature. Values
/// are read back by field name (<c>V0</c>, <c>V1</c>...), so they never depend on the order in which the library
/// lays the fields out.
/// </para>
/// <para>
/// Emitted types are cached for the process lifetime, keyed by the list's content. Providers poll a handful of
/// fixed lists, so the cache stays tiny. SimConnect.NET registers the data definition per native connection, so
/// a reconnection simply registers it again on first read.
/// </para>
/// </remarks>
internal static class VariableSetStructs
{
    private static readonly ModuleBuilder Module = AssemblyBuilder
        .DefineDynamicAssembly(new AssemblyName("FSGAP.SimConnect.VariableSets"), AssemblyBuilderAccess.Run)
        .DefineDynamicModule("FSGAP.SimConnect.VariableSets");

    private static readonly ConstructorInfo AttributeConstructor = typeof(SimConnectAttribute).GetConstructor(
        [typeof(string), typeof(string), typeof(SimConnectDataType)])
        ?? throw new MissingMethodException(nameof(SimConnectAttribute), ".ctor(string, string, SimConnectDataType)");

    private static readonly MethodInfo ReadGeneric = typeof(VariableSetStructs)
        .GetMethod(nameof(ReadCoreAsync), BindingFlags.NonPublic | BindingFlags.Static)!;

    private static readonly ConcurrentDictionary<string, Lazy<VariableSetReader>> Readers = new();

    private static readonly object EmitGate = new();

    private static int _counter;

    /// <summary>Reads one batch through a SimConnect.NET client.</summary>
    internal delegate Task<double[]> VariableSetReader(SimConnectClient client, CancellationToken cancellationToken);

    /// <summary>The cached reader for <paramref name="variables"/>, emitting its struct on first use.</summary>
    internal static VariableSetReader For(IReadOnlyList<SimulatorVariable> variables) =>
        Readers.GetOrAdd(KeyOf(variables), _ => new Lazy<VariableSetReader>(() => Build(variables))).Value;

    /// <summary>The emitted struct type for <paramref name="variables"/> (exposed for tests).</summary>
    internal static Type StructTypeFor(IReadOnlyList<SimulatorVariable> variables) => Emit(variables);

    /// <summary>Validates a list: not empty, no duplicate name.</summary>
    internal static void Validate(IReadOnlyList<SimulatorVariable> variables)
    {
        ArgumentNullException.ThrowIfNull(variables);
        if (variables.Count == 0)
        {
            throw new ArgumentException("At least one variable is required.", nameof(variables));
        }

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var variable in variables)
        {
            ArgumentNullException.ThrowIfNull(variable, nameof(variables));
            if (!names.Add(variable.Name))
            {
                throw new ArgumentException($"Variable '{variable.Name}' appears twice.", nameof(variables));
            }
        }
    }

    private static string KeyOf(IReadOnlyList<SimulatorVariable> variables) =>
        string.Join('\u001F', variables.Select(v => $"{v.Name}\u001E{v.Unit}"));

    private static VariableSetReader Build(IReadOnlyList<SimulatorVariable> variables)
    {
        var type = Emit(variables);
        var fields = Enumerable.Range(0, variables.Count).Select(i => type.GetField(FieldName(i))!).ToArray();
        var read = ReadGeneric.MakeGenericMethod(type);
        return (client, ct) => (Task<double[]>)read.Invoke(null, [client, fields, ct])!;
    }

    private static Type Emit(IReadOnlyList<SimulatorVariable> variables)
    {
        lock (EmitGate)
        {
            return EmitLocked(variables);
        }
    }

    private static Type EmitLocked(IReadOnlyList<SimulatorVariable> variables)
    {
        var id = ++_counter;
        var builder = Module.DefineType(
            $"VariableSet{id}",
            TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.SequentialLayout,
            typeof(ValueType));
        for (var i = 0; i < variables.Count; i++)
        {
            var field = builder.DefineField(FieldName(i), typeof(double), FieldAttributes.Public);
            field.SetCustomAttribute(new CustomAttributeBuilder(
                AttributeConstructor,
                [variables[i].Name, variables[i].Unit, SimConnectDataType.FloatDouble]));
        }

        return builder.CreateType();
    }

    private static string FieldName(int index) => $"V{index}";

    private static async Task<double[]> ReadCoreAsync<T>(SimConnectClient client, FieldInfo[] fields, CancellationToken cancellationToken)
        where T : struct
    {
        object boxed = await client.SimVars.GetAsync<T>(0, cancellationToken).ConfigureAwait(false);
        var values = new double[fields.Length];
        for (var i = 0; i < fields.Length; i++)
        {
            values[i] = (double)fields[i].GetValue(boxed)!;
        }

        return values;
    }
}
