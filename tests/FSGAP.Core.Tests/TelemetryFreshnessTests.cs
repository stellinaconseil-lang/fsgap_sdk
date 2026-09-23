using System.Collections;
using System.Reflection;
using FSGAP.Abstractions.Telemetry;
using FSGAP.Core.Telemetry;

namespace FSGAP.Core.Tests;

public class TelemetryFreshnessTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan StaleAfter = TimeSpan.FromSeconds(15);

    [Fact]
    public void Stale_values_become_unknown_and_fresh_values_stay_known()
    {
        var telemetry = AircraftTelemetry.Unavailable(Now) with
        {
            Flight = new FlightStateTelemetry
            {
                OnGround = TelemetryValue<bool>.Known(true, Now.AddSeconds(-1)),
                LatitudeDegrees = TelemetryValue<double>.Known(48.7, Now.AddSeconds(-20)), // feed stopped
            },
            Engines = [new EngineTelemetry { Index = 1, N1Percent = TelemetryValue<double>.Known(85, Now.AddMinutes(-2)) }],
        };

        var checkedTelemetry = TelemetryFreshness.ExpireStaleValues(telemetry, StaleAfter);

        Assert.True(checkedTelemetry.Flight.OnGround.Value);
        Assert.Equal(ValueState.Unknown, checkedTelemetry.Flight.LatitudeDegrees.State);
        Assert.Equal(Now.AddSeconds(-20), checkedTelemetry.Flight.LatitudeDegrees.ObservedAt);
        Assert.Equal(ValueState.Unknown, checkedTelemetry.Engines[0].N1Percent.State);
        Assert.Equal(ValueState.Unavailable, checkedTelemetry.Flight.AltitudeFeet.State);
    }

    [Fact]
    public void Original_snapshot_is_not_modified()
    {
        var telemetry = AircraftTelemetry.Unavailable(Now) with
        {
            Flight = new FlightStateTelemetry { AltitudeFeet = TelemetryValue<double>.Known(3000, Now.AddHours(-1)) },
        };

        _ = TelemetryFreshness.ExpireStaleValues(telemetry, StaleAfter);

        Assert.True(telemetry.Flight.AltitudeFeet.IsKnown);
    }

    [Fact]
    public void A_value_last_fed_long_ago_is_never_reported_as_known()
    {
        // Simulates a provider that keeps rebuilding snapshots from its last known reading after the feed stopped.
        var lastReading = TelemetryValue<double>.Known(250, Now);

        var states = Enumerable.Range(0, 60).Select(second =>
        {
            var snapshot = AircraftTelemetry.Unavailable(Now.AddSeconds(second)) with
            {
                Flight = new FlightStateTelemetry { IndicatedAirspeedKnots = lastReading },
            };
            return TelemetryFreshness.ExpireStaleValues(snapshot, StaleAfter).Flight.IndicatedAirspeedKnots.State;
        }).ToArray();

        Assert.All(states.Take(16), state => Assert.Equal(ValueState.Known, state));
        Assert.All(states.Skip(16), state => Assert.Equal(ValueState.Unknown, state));
    }

    [Fact]
    public void Every_telemetry_value_of_the_model_is_covered()
    {
        // Guard: a value added to the model but forgotten in TelemetryFreshness would stay "known" forever.
        var old = Now.AddHours(-1);
        var telemetry = (AircraftTelemetry)Populate(typeof(AircraftTelemetry), old);
        telemetry = telemetry with { Timestamp = Now };
        var before = TelemetryValues(telemetry).ToArray();

        var expired = TelemetryFreshness.ExpireStaleValues(telemetry, StaleAfter);
        var after = TelemetryValues(expired).ToArray();

        Assert.True(before.Length > 40, $"Only {before.Length} values were generated.");
        Assert.All(before, v => Assert.Equal(ValueState.Known, v.State));
        Assert.Equal(before.Length, after.Length);
        Assert.All(after, v => Assert.True(v.State == ValueState.Unknown, $"{v.Path} stayed {v.State}."));
    }

    private static object Populate(Type type, DateTimeOffset observedAt)
    {
        var instance = Activator.CreateInstance(type)!;
        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.CanWrite))
        {
            var propertyType = property.PropertyType;
            object? value = propertyType switch
            {
                _ when IsTelemetryValue(propertyType) => KnownOf(propertyType, observedAt),
                _ when propertyType == typeof(string) => "x",
                _ when propertyType == typeof(int) => 1,
                _ when propertyType == typeof(DateTimeOffset) => observedAt,
                _ when propertyType.IsGenericType && propertyType.GetGenericTypeDefinition() == typeof(IReadOnlyList<>) =>
                    CreateSingleItemList(propertyType.GetGenericArguments()[0], observedAt),
                _ when propertyType.IsClass && propertyType.Namespace == typeof(AircraftTelemetry).Namespace => Populate(propertyType, observedAt),
                _ => throw new InvalidOperationException($"Unhandled property {type.Name}.{property.Name} of type {propertyType}."),
            };
            property.SetValue(instance, value);
        }

        return instance;
    }

    private static object CreateSingleItemList(Type itemType, DateTimeOffset observedAt)
    {
        var array = Array.CreateInstance(itemType, 1);
        array.SetValue(Populate(itemType, observedAt), 0);
        return array;
    }

    private static object KnownOf(Type telemetryValueType, DateTimeOffset observedAt)
    {
        var valueType = telemetryValueType.GetGenericArguments()[0];
        var known = telemetryValueType.GetMethod(nameof(TelemetryValue<bool>.Known))!;
        return known.Invoke(null, [Activator.CreateInstance(valueType), observedAt])!;
    }

    private static IEnumerable<(string Path, ValueState State)> TelemetryValues(object node, string path = "")
    {
        foreach (var property in node.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            var value = property.GetValue(node);
            var childPath = $"{path}.{property.Name}";
            if (value is null || property.PropertyType == typeof(string))
            {
                continue;
            }

            if (IsTelemetryValue(property.PropertyType))
            {
                yield return (childPath, (ValueState)property.PropertyType.GetProperty(nameof(TelemetryValue<bool>.State))!.GetValue(value)!);
            }
            else if (value is IEnumerable items)
            {
                var index = 0;
                foreach (var item in items)
                {
                    foreach (var nested in TelemetryValues(item, $"{childPath}[{index++}]"))
                    {
                        yield return nested;
                    }
                }
            }
            else if (property.PropertyType.IsClass && property.PropertyType.Namespace == typeof(AircraftTelemetry).Namespace)
            {
                foreach (var nested in TelemetryValues(value, childPath))
                {
                    yield return nested;
                }
            }
        }
    }

    private static bool IsTelemetryValue(Type type) =>
        type.IsGenericType && type.GetGenericTypeDefinition() == typeof(TelemetryValue<>);
}
