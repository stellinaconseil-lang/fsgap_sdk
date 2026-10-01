using FSGAP.Abstractions;
using FSGAP.Abstractions.Aircraft;
using FSGAP.Abstractions.Capabilities;
using FSGAP.Abstractions.Configuration;
using FSGAP.Abstractions.Degradations;
using FSGAP.Abstractions.Failures;
using FSGAP.Abstractions.Simulator;
using FSGAP.Abstractions.Telemetry;
using FSGAP.Core.Degradations;
using FSGAP.Core.Failures;
using FSGAP.Core.Sessions;
using FSGAP.Core.Telemetry;
using FSGAP.Synaptic.Degradations;
using FSGAP.Synaptic.Detection;
using FSGAP.Synaptic.Failures;
using FSGAP.Synaptic.Identity;
using FSGAP.Synaptic.Systems;
using FSGAP.Synaptic.Telemetry;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace FSGAP.Synaptic;

/// <summary>
/// Aircraft provider for the Synaptic Simulations A220-300: recognition, normalized identity and telemetry. One provider
/// among others: register it next to the Fenix provider (or any other) in an <c>AircraftProviderRegistry</c>; the
/// registry selects, per loaded aircraft, the provider whose <see cref="Match"/> supports it.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Match"/> recognizes the A220 from the generic descriptor alone (exact preset title + <c>ATC MODEL</c> +
/// <c>ATC TYPE</c>, see <c>SynapticA220Recognizer</c>) and answers <see cref="MatchSpecificity.Dedicated"/>.
/// </para>
/// <para>
/// <see cref="AttachAsync"/> opens a session whose telemetry is the simulator's generic telemetry with the A220 policy
/// applied (values proven wrong are hidden) and a small documented overlay laid over it: the two fuel boost pump modes,
/// the APU switch and APU bleed switch positions, and the engine fire pushbuttons. Read-only; every read goes through the
/// simulator's single connection.
/// </para>
/// <para>
/// <b>Failures (0.12).</b> Given a variable writer on the same connection, sessions expose the same 40 normalized failure
/// keys as the Fenix provider. The A220 documents no failure interface, so each executable key is realized by forcing
/// documented cockpit controls into a degraded configuration (17 keys; validated, assumed or approximated recipes); keys
/// with no documented control are listed with no operation. No native simulator failure event is used. Without a writer,
/// <see cref="FailureCapabilities.None"/>.
/// </para>
/// <para>
/// <b>Controlled degradations.</b> Given a variable writer on the same connection, sessions also offer four controlled
/// degradations qualified live (generator 1, hydraulic pump 3A, pack 1 and PFCC 1 forced off): documented cockpit controls
/// FSGAP forces into a degraded configuration, never component failures. See <see cref="DegradationCapabilities"/>; one
/// at a time, explicit set values, read back before success, restored on dispose only when this session applied them.
/// </para>
/// </remarks>
public sealed class SynapticAircraftProvider : IAircraftProvider
{
    /// <summary>Value of <see cref="ProviderId"/>.</summary>
    public const string Id = "synaptic";

    private readonly SynapticInstalledAircraftCatalog? _catalog;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger _logger;
    private readonly ITelemetryProvider? _genericTelemetry;
    private readonly ISimulatorVariableReader? _simulatorVariables;
    private readonly IAircraftDetector? _aircraftDetector;
    private readonly ISimulatorVariableWriter? _simulatorVariableWriter;
    private readonly TimeSpan _staleAfter;

    /// <summary>Creates the provider.</summary>
    /// <param name="installedAircraft">
    /// Catalog of the installed A220 liveries; the provider records each attached livery in it (folder, registration).
    /// Without it, the registration is resolved from the descriptor alone.
    /// </param>
    /// <param name="timeProvider">Clock used by sessions; <see cref="TimeProvider.System"/> by default.</param>
    /// <param name="logger">Optional logger.</param>
    /// <param name="genericTelemetry">The simulator's generic telemetry, typically <c>SimConnectSimulator.Telemetry</c>.</param>
    /// <param name="simulatorVariables">Reader of simulator variables on the same connection, typically the <c>SimConnectSimulator</c>.</param>
    /// <param name="aircraftDetector">The same connection's aircraft detector (stops a session's reads when another aircraft is loaded).</param>
    /// <param name="telemetryOptions">Freshness limit; <see cref="TelemetryOptions"/> defaults otherwise.</param>
    /// <param name="simulatorVariableWriter">
    /// Writer of local variables on the same connection, typically the <c>SimConnectSimulator</c>. With it (and
    /// <paramref name="simulatorVariables"/>), sessions offer the qualified controlled degradations; without it, they offer
    /// none.
    /// </param>
    /// <exception cref="ArgumentException">An option is invalid.</exception>
    public SynapticAircraftProvider(
        SynapticInstalledAircraftCatalog? installedAircraft = null,
        TimeProvider? timeProvider = null,
        ILogger<SynapticAircraftProvider>? logger = null,
        ITelemetryProvider? genericTelemetry = null,
        ISimulatorVariableReader? simulatorVariables = null,
        IAircraftDetector? aircraftDetector = null,
        TelemetryOptions? telemetryOptions = null,
        ISimulatorVariableWriter? simulatorVariableWriter = null)
    {
        var telemetry = telemetryOptions ?? new TelemetryOptions();
        telemetry.Validate();
        _catalog = installedAircraft;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _logger = (ILogger?)logger ?? NullLogger.Instance;
        _genericTelemetry = genericTelemetry;
        _simulatorVariables = simulatorVariables;
        _aircraftDetector = aircraftDetector;
        _simulatorVariableWriter = simulatorVariableWriter;
        _staleAfter = telemetry.StaleAfter;
    }

    /// <inheritdoc />
    public string ProviderId => Id;

    /// <inheritdoc />
    public AircraftMatch Match(AircraftDescriptor aircraft)
    {
        ArgumentNullException.ThrowIfNull(aircraft);
        if (!SynapticA220Recognizer.IsSynapticA220(aircraft))
        {
            return AircraftMatch.NotSupported;
        }

        var registration = RegistrationResolver.Resolve(null, aircraft.LiveryFolder, aircraft.Registration, cached: null);
        return AircraftMatch.Supported(SynapticIdentity.Create(registration?.Registration, registration?.Source, aircraft.Livery), MatchSpecificity.Dedicated);
    }

    /// <inheritdoc />
    public Task<IAircraftSession> AttachAsync(AircraftDescriptor aircraft, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(aircraft);
        cancellationToken.ThrowIfCancellationRequested();
        if (!SynapticA220Recognizer.IsSynapticA220(aircraft))
        {
            throw new NotSupportedException($"'{aircraft.Title}' is not the Synaptic A220-300.");
        }

        var identity = new AircraftIdentityTracker(aircraft, _aircraftDetector, ResolveIdentity);

        if (_genericTelemetry is null && _simulatorVariables is null)
        {
            return Task.FromResult<IAircraftSession>(new AircraftSession(
                ProviderId, identity.Get, AircraftCapabilities.None, new UnavailableTelemetryProvider(_timeProvider), UnsupportedFailureProvider.Instance));
        }

        var sections = TelemetryCapabilities.None;
        if (_genericTelemetry is not null)
        {
            sections = SynapticGenericTelemetryPolicy.Union(sections, SynapticGenericTelemetryPolicy.GenericSections);
        }

        SynapticSystemTelemetrySource? overlay = null;
        if (_simulatorVariables is not null)
        {
            // Only reached for an aircraft the recognizer accepted: no Synaptic variable is ever read otherwise.
            overlay = new SynapticSystemTelemetrySource(_simulatorVariables, _aircraftDetector, aircraft, _timeProvider, _logger);
            overlay.Start();
            sections = SynapticGenericTelemetryPolicy.Union(sections, SynapticGenericTelemetryPolicy.OverlaySections);
        }

        bool AircraftReplaced() => _aircraftDetector?.Current is { } loaded && !AircraftContinuity.IsSameLoadedAircraft(aircraft, loaded);
        var staleAfter = _staleAfter;
        var telemetry = new TransformedTelemetryProvider(
            _genericTelemetry ?? new UnavailableTelemetryProvider(_timeProvider),
            generic => SynapticTelemetryComposer.Compose(
                generic,
                overlay?.Current ?? SynapticSystemState.Empty,
                overlay?.AircraftReplaced ?? AircraftReplaced(),
                staleAfter),
            overlay);

        // Degradations and failures need both directions on the same connection; never a write without a read-back. They
        // share one board (one owner per control, conflicts detected across both); the failure provider, disposed last by
        // AircraftSession, disposes it.
        IDegradationProvider degradations = UnsupportedDegradationProvider.Instance;
        var degradationCapabilities = DegradationCapabilities.None;
        IFailureProvider failures = UnsupportedFailureProvider.Instance;
        var failureCapabilities = FailureCapabilities.None;
        if (_simulatorVariables is not null && _simulatorVariableWriter is not null)
        {
            var board = new SynapticControlBoard(_simulatorVariables, _simulatorVariableWriter, _aircraftDetector, aircraft, _logger);
            degradations = new SynapticDegradationProvider(board, ownsBoard: false);
            degradationCapabilities = SynapticDegradationProvider.Capabilities;
            failures = new SynapticFailureProvider(board, ownsBoard: true);
            failureCapabilities = SynapticFailureProvider.Capabilities;
        }

        return Task.FromResult<IAircraftSession>(new AircraftSession(
            ProviderId,
            identity.Get,
            new AircraftCapabilities { Telemetry = sections, Failures = failureCapabilities, Degradations = degradationCapabilities },
            telemetry,
            failures,
            degradations));
    }

    /// <summary>
    /// Identity for a descriptor of the attached aircraft. The registration follows the conservative resolver (and the
    /// catalog's learned value); an incoherent ATC ID is never promoted, whichever descriptor carries it.
    /// </summary>
    private AircraftIdentity ResolveIdentity(AircraftDescriptor aircraft)
    {
        var registration = _catalog is not null
            ? _catalog.Learn(aircraft)
            : RegistrationResolver.Resolve(null, aircraft.LiveryFolder, aircraft.Registration, cached: null);
        return SynapticIdentity.Create(registration?.Registration, registration?.Source, aircraft.Livery);
    }
}
