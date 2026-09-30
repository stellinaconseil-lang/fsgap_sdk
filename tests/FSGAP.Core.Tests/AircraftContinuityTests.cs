using System.Runtime.CompilerServices;
using FSGAP.Abstractions.Aircraft;
using FSGAP.Abstractions.Capabilities;
using FSGAP.Abstractions.Simulator;
using FSGAP.Core.Failures;
using FSGAP.Core.Sessions;
using FSGAP.Core.Telemetry;

namespace FSGAP.Core.Tests;

/// <summary>Same loaded aircraft vs a different one, and a session identity that follows the same aircraft's metadata.</summary>
public class AircraftContinuityTests
{
    private static readonly AircraftDescriptor Loaded = new()
    {
        Title = "Airliner 300",
        Model = "M300",
        Manufacturer = "T300",
        IcaoType = "A300",
        Registration = "AB-CDE",
        Livery = "House",
        LiveryFolder = "HOUSE AB-CDE",
        PackagePath = @"C:\packages\airliner",
    };

    public static TheoryData<string?> VolatileRegistrations() => new() { null, "", "XY-ZZZ", "C-FFCO", "I-OVTU" };

    [Theory]
    [MemberData(nameof(VolatileRegistrations))]
    public void An_atc_id_change_alone_is_the_same_loaded_aircraft(string? registration)
    {
        var updated = Loaded with { Registration = registration };

        Assert.True(AircraftContinuity.IsSameLoadedAircraft(Loaded, updated));
        Assert.Equal(AircraftContinuity.Comparer.GetHashCode(Loaded), AircraftContinuity.Comparer.GetHashCode(updated));
        Assert.NotEqual(Loaded, updated); // full descriptor equality still sees the change (published as metadata)
    }

    public static TheoryData<string> StructuralChanges() => new() { "title", "model", "type", "icao", "livery", "folder", "package" };

    [Theory]
    [MemberData(nameof(StructuralChanges))]
    public void Any_other_field_change_is_a_different_aircraft(string field)
    {
        var changed = field switch
        {
            "title" => Loaded with { Title = "Airliner 200" },
            "model" => Loaded with { Model = "M200" },
            "type" => Loaded with { Manufacturer = "T200" },
            "icao" => Loaded with { IcaoType = "A200" },
            "livery" => Loaded with { Livery = "Other" },
            "folder" => Loaded with { LiveryFolder = "OTHER XY-ZZZ" },
            _ => Loaded with { PackagePath = @"C:\packages\other" },
        };

        Assert.False(AircraftContinuity.IsSameLoadedAircraft(Loaded, changed));
    }

    [Fact]
    public void No_aircraft_is_only_the_same_as_no_aircraft()
    {
        Assert.True(AircraftContinuity.IsSameLoadedAircraft(null, null));
        Assert.False(AircraftContinuity.IsSameLoadedAircraft(null, Loaded));
        Assert.False(AircraftContinuity.IsSameLoadedAircraft(Loaded, null));
    }

    [Fact]
    public void The_tracker_resolves_again_only_for_new_metadata_of_the_same_aircraft()
    {
        var detector = new Detector(Loaded);
        var resolutions = new List<string?>();
        var tracker = new AircraftIdentityTracker(Loaded, detector, d =>
        {
            resolutions.Add(d.Registration);
            return new AircraftIdentity { Model = "M300", Registration = d.Registration };
        });

        Assert.Equal("AB-CDE", tracker.Get().Registration);
        Assert.Equal("AB-CDE", tracker.Get().Registration);

        detector.Current = Loaded with { Registration = "XY-ZZZ" };
        Assert.Equal("XY-ZZZ", tracker.Get().Registration);
        Assert.Equal("XY-ZZZ", tracker.Get().Registration);

        detector.Current = Loaded with { Registration = "XY-ZZZ" }; // equal content, new instance: no new resolution
        Assert.Equal("XY-ZZZ", tracker.Get().Registration);

        detector.Current = Loaded with { Title = "Airliner 200", Registration = "NEW" }; // a different aircraft
        Assert.Equal("XY-ZZZ", tracker.Get().Registration);

        detector.Current = null; // nothing loaded: keep the last identity
        Assert.Equal("XY-ZZZ", tracker.Get().Registration);

        Assert.Equal(["AB-CDE", "XY-ZZZ"], resolutions);
    }

    [Fact]
    public void Without_a_detector_the_identity_is_fixed()
    {
        var tracker = new AircraftIdentityTracker(Loaded, null, d => new AircraftIdentity { Registration = d.Registration });

        Assert.Equal("AB-CDE", tracker.Get().Registration);
    }

    [Fact]
    public void A_session_built_on_a_tracker_exposes_the_current_identity()
    {
        var detector = new Detector(Loaded);
        var tracker = new AircraftIdentityTracker(Loaded, detector, d => new AircraftIdentity { Registration = d.Registration });
        var session = new AircraftSession("test", tracker.Get, AircraftCapabilities.None, new UnavailableTelemetryProvider(), UnsupportedFailureProvider.Instance);

        Assert.Equal("AB-CDE", session.Identity.Registration);
        detector.Current = Loaded with { Registration = null };

        Assert.Null(session.Identity.Registration);
    }

    [Fact]
    public void A_session_identity_function_must_not_return_null()
    {
        var session = new AircraftSession("test", () => null!, AircraftCapabilities.None, new UnavailableTelemetryProvider(), UnsupportedFailureProvider.Instance);

        Assert.Throws<InvalidOperationException>(() => session.Identity);
    }

    private sealed class Detector(AircraftDescriptor? current) : IAircraftDetector
    {
        public AircraftDescriptor? Current { get; set; } = current;

        public async IAsyncEnumerable<AircraftDescriptor?> WatchAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            yield return Current;
        }
    }
}
