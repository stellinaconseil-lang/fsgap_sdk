using FSGAP.Abstractions;
using FSGAP.Abstractions.Aircraft;
using FSGAP.Abstractions.Capabilities;
using FSGAP.Abstractions.Configuration;
using FSGAP.Abstractions.Failures;
using FSGAP.Abstractions.Simulator;
using FSGAP.Abstractions.Telemetry;
using FSGAP.Core.Failures;
using FSGAP.Core.Sessions;
using FSGAP.Core.Telemetry;
using FSGAP.Fenix.Detection;
using FSGAP.Fenix.Failures;
using FSGAP.Fenix.Identity;
using FSGAP.Fenix.Telemetry;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace FSGAP.Fenix;

/// <summary>
/// Aircraft provider for the Fenix Simulations A319, A320 and A321: recognition, normalized identity, telemetry and
/// failures.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Match"/> recognizes a Fenix from the generic descriptor alone (the Fenix title rule on <c>TITLE</c>,
/// then <c>LIVERY FOLDER</c>). It answers with <see cref="MatchSpecificity.Dedicated"/> and a preliminary identity
/// whose registration is the simulator's ATC id.
/// </para>
/// <para>
/// <see cref="AttachAsync"/> resolves the full identity with the installed-livery catalog. Registration comes from
/// the installed livery matching <c>LIVERY FOLDER</c>, then from the ATC id, otherwise it is unknown.
/// </para>
/// <para>
/// Telemetry (all inputs optional, all from the one simulator connection):
/// </para>
/// <list type="bullet">
/// <item><description>
/// generic telemetry: exposed with <c>FenixGenericTelemetryPolicy</c> masking what is known to be wrong on Fenix;
/// </description></item>
/// <item><description>
/// a simulator variable reader: each session also polls the proven Fenix system variables and lays them over the
/// generic snapshot (<c>FenixTelemetryComposer</c>). Read-only;
/// </description></item>
/// <item><description>
/// neither: the session declares <see cref="AircraftCapabilities.None"/>, as in 0.4.0.
/// </description></item>
/// </list>
/// <para>
/// Failures (0.7.0): with <see cref="FenixOptions"/>, each session triggers, clears and reads Fenix failures through
/// the local EFB, in normalized <see cref="FailureKey"/> terms only (<c>FenixFailureProvider</c>). Without it, every
/// failure command returns <c>NotSupported</c>.
/// </para>
/// </remarks>
public sealed class FenixAircraftProvider : IAircraftProvider
{
    /// <summary>Value of <see cref="ProviderId"/>.</summary>
    public const string Id = "fenix";

    private readonly IInstalledAircraftCatalog? _installedAircraft;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger _logger;
    private readonly ITelemetryProvider? _genericTelemetry;
    private readonly ISimulatorVariableReader? _simulatorVariables;
    private readonly IAircraftDetector? _aircraftDetector;
    private readonly TimeSpan _staleAfter;
    private readonly FenixOptions? _fenixOptions;
    private readonly HttpClient? _efbHttpClient;
    private HttpClient? _ownedEfbHttpClient;

    /// <summary>Creates the provider.</summary>
    /// <param name="installedAircraft">
    /// Installed-livery catalog used to resolve registration and livery details, typically a
    /// <see cref="FenixInstalledAircraftCatalog"/>. Without it, the identity relies on the simulator's ATC id only.
    /// </param>
    /// <param name="timeProvider">Clock used by sessions; <see cref="TimeProvider.System"/> by default.</param>
    /// <param name="logger">Optional logger (identity conflicts, catalog lookup failures).</param>
    /// <param name="genericTelemetry">
    /// The simulator's generic telemetry, typically <c>SimConnectSimulator.Telemetry</c> of the connection that detected
    /// the aircraft. Sessions expose it with the Fenix policy applied.
    /// </param>
    /// <param name="simulatorVariables">
    /// Reader of simulator variables on the same connection, typically that <c>SimConnectSimulator</c> itself.
    /// With it, each session polls the Fenix-specific systems (inertial references, fuel pump switches, fire panel,
    /// hydraulic pressures) for as long as it lives. Without it, those sections stay unavailable.
    /// </param>
    /// <param name="aircraftDetector">
    /// The same connection's aircraft detector. With it, a session stops reading, and publishes nothing, as soon as
    /// the simulator reports a different aircraft from the one it was attached to (attach with the descriptor the
    /// detector published).
    /// </param>
    /// <param name="telemetryOptions">Freshness limit for the Fenix values; <see cref="TelemetryOptions"/> defaults otherwise.</param>
    /// <param name="fenixOptions">
    /// Enables Fenix failures through the local EFB (address, timeout). Without it, sessions support no failure, as
    /// before 0.7.0.
    /// </param>
    /// <param name="efbHttpClient">
    /// Optional HTTP client for the EFB (tests, or an application's own client). Not disposed by FSGAP. By default
    /// one client is created on first use and shared by all sessions.
    /// </param>
    /// <exception cref="ArgumentException">An option is invalid.</exception>
    public FenixAircraftProvider(
        IInstalledAircraftCatalog? installedAircraft = null,
        TimeProvider? timeProvider = null,
        ILogger<FenixAircraftProvider>? logger = null,
        ITelemetryProvider? genericTelemetry = null,
        ISimulatorVariableReader? simulatorVariables = null,
        IAircraftDetector? aircraftDetector = null,
        TelemetryOptions? telemetryOptions = null,
        FenixOptions? fenixOptions = null,
        HttpClient? efbHttpClient = null)
    {
        (telemetryOptions ?? new TelemetryOptions()).Validate();
        fenixOptions?.Validate();
        _fenixOptions = fenixOptions;
        _efbHttpClient = efbHttpClient;
        _installedAircraft = installedAircraft;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _logger = (ILogger?)logger ?? NullLogger.Instance;
        _genericTelemetry = genericTelemetry;
        _simulatorVariables = simulatorVariables;
        _aircraftDetector = aircraftDetector;
        _staleAfter = (telemetryOptions ?? new TelemetryOptions()).StaleAfter;
    }

    /// <inheritdoc />
    public string ProviderId => Id;

    /// <inheritdoc />
    public AircraftMatch Match(AircraftDescriptor aircraft)
    {
        ArgumentNullException.ThrowIfNull(aircraft);
        return FenixAircraftRecognizer.Recognize(aircraft) is { } variant
            ? AircraftMatch.Supported(FenixIdentityResolver.Resolve(variant, aircraft, installed: null, _logger), MatchSpecificity.Dedicated)
            : AircraftMatch.NotSupported;
    }

    /// <inheritdoc />
    public async Task<IAircraftSession> AttachAsync(AircraftDescriptor aircraft, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(aircraft);
        cancellationToken.ThrowIfCancellationRequested();
        var variant = FenixAircraftRecognizer.Recognize(aircraft)
            ?? throw new NotSupportedException($"'{aircraft.Title}' is not a Fenix A319/A320/A321.");

        var installed = await FindInstalledAsync(aircraft.LiveryFolder, cancellationToken).ConfigureAwait(false);
        var identity = FenixIdentityResolver.Resolve(variant, aircraft, installed, _logger);
        bool AircraftReplaced() => _aircraftDetector?.Current is { } loaded && !loaded.Equals(aircraft);

        // Failures: only for a recognized Fenix (this method refused anything else above) and only when configured.
        // The provider is created here, so no EFB request can exist without a Fenix session.
        var (failures, failureCapabilities) = _fenixOptions is null
            ? ((IFailureProvider)UnsupportedFailureProvider.Instance, FailureCapabilities.None)
            : (new FenixFailureProvider(
                    new FenixEfbClient(EfbHttpClient, _fenixOptions, _timeProvider),
                    FenixFailureCatalogData.Default,
                    AircraftReplaced,
                    _fenixOptions,
                    _timeProvider,
                    _logger),
                new FailureCapabilities { CanReadActiveFailures = true, Catalog = FenixFailureCatalogData.Default.Catalog });

        if (_genericTelemetry is null && _simulatorVariables is null)
        {
            return new AircraftSession(
                ProviderId,
                identity,
                _fenixOptions is null ? AircraftCapabilities.None : new AircraftCapabilities { Failures = failureCapabilities },
                new UnavailableTelemetryProvider(_timeProvider),
                failures);
        }

        var sections = TelemetryCapabilities.None;
        if (_genericTelemetry is not null)
        {
            sections = FenixGenericTelemetryPolicy.Union(sections, FenixGenericTelemetryPolicy.GenericSections);
        }

        FenixSystemTelemetrySource? systems = null;
        if (_simulatorVariables is not null)
        {
            // Only reached for an aircraft the Fenix recognizer accepted: no Fenix variable is ever read otherwise.
            systems = new FenixSystemTelemetrySource(_simulatorVariables, _aircraftDetector, aircraft, _timeProvider, _logger);
            systems.Start();
            sections = FenixGenericTelemetryPolicy.Union(sections, FenixGenericTelemetryPolicy.SystemSections);
        }

        var staleAfter = _staleAfter;
        var telemetry = new TransformedTelemetryProvider(
            _genericTelemetry ?? new UnavailableTelemetryProvider(_timeProvider),
            generic => FenixTelemetryComposer.Compose(
                generic,
                systems?.Current ?? FenixSystemState.Empty,
                systems?.AircraftReplaced ?? AircraftReplaced(),
                staleAfter),
            systems);
        return new AircraftSession(
            ProviderId,
            identity,
            new AircraftCapabilities { Telemetry = sections, Failures = failureCapabilities },
            telemetry,
            failures);
    }

    /// <summary>
    /// The HTTP client for the EFB: the injected one, or one created on first use and shared by every session of
    /// this provider for its whole lifetime (no client per call, no socket exhaustion). Its own timeout is disabled:
    /// each request has <see cref="FenixOptions.RequestTimeout"/>.
    /// </summary>
    private HttpClient EfbHttpClient => _efbHttpClient ?? LazyInitializer.EnsureInitialized(
        ref _ownedEfbHttpClient,
        () => new HttpClient(new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(2) })
        {
            Timeout = Timeout.InfiniteTimeSpan,
        });

    private async Task<InstalledAircraft?> FindInstalledAsync(string? liveryFolder, CancellationToken cancellationToken)
    {
        if (_installedAircraft is null || string.IsNullOrWhiteSpace(liveryFolder))
        {
            return null;
        }

        try
        {
            return await _installedAircraft.FindByLiveryFolderAsync(liveryFolder, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Installed livery lookup failed for '{LiveryFolder}'; continuing without it", liveryFolder);
            return null;
        }
    }
}
