using FSGAP.Abstractions.Aircraft;
using FSGAP.Abstractions.Simulator;

namespace FSGAP.Core.Sessions;

/// <summary>
/// The current identity of a session's aircraft: resolved at attach, and resolved again, with the provider's own
/// rules, when the detector reports new metadata for the <i>same</i> loaded aircraft (<see cref="AircraftContinuity"/>),
/// such as a changed registration. Pass <see cref="Get"/> to <see cref="AircraftSession"/>.
/// </summary>
/// <remarks>
/// The tracker only decides <i>when</i> to resolve; <i>how</i> (which source wins for the registration, whether an
/// ATC ID is trusted) stays the provider's. Once the detector reports a different aircraft, or none, the last identity
/// is kept: the session is about to be replaced or is waiting for its aircraft. Thread-safe; resolution runs at most
/// once per new descriptor.
/// </remarks>
public sealed class AircraftIdentityTracker
{
    private readonly AircraftDescriptor _attached;
    private readonly IAircraftDetector? _detector;
    private readonly Func<AircraftDescriptor, AircraftIdentity> _resolve;
    private readonly object _gate = new();
    private AircraftDescriptor _resolvedFor;
    private AircraftIdentity _current;

    /// <summary>Creates the tracker and resolves the identity of <paramref name="attached"/> once.</summary>
    /// <param name="attached">Descriptor the session was attached with.</param>
    /// <param name="detector">The simulator's aircraft detector; without it the identity never changes.</param>
    /// <param name="resolve">The provider's identity resolution for a descriptor of this aircraft; never returns null.</param>
    public AircraftIdentityTracker(AircraftDescriptor attached, IAircraftDetector? detector, Func<AircraftDescriptor, AircraftIdentity> resolve)
    {
        _attached = attached ?? throw new ArgumentNullException(nameof(attached));
        _resolve = resolve ?? throw new ArgumentNullException(nameof(resolve));
        _detector = detector;
        _resolvedFor = attached;
        _current = Resolve(attached);
    }

    /// <summary>The identity for the descriptor the detector currently reports, if it is still the attached aircraft.</summary>
    public AircraftIdentity Get()
    {
        var loaded = _detector?.Current;
        lock (_gate)
        {
            if (loaded is null || ReferenceEquals(loaded, _resolvedFor) || !AircraftContinuity.IsSameLoadedAircraft(_attached, loaded))
            {
                return _current;
            }

            if (!loaded.Equals(_resolvedFor))
            {
                _current = Resolve(loaded);
            }

            _resolvedFor = loaded;
            return _current;
        }
    }

    private AircraftIdentity Resolve(AircraftDescriptor descriptor) =>
        _resolve(descriptor) ?? throw new InvalidOperationException("The identity resolution returned null.");
}
