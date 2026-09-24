using FSGAP.Abstractions;
using FSGAP.Abstractions.Aircraft;
using FSGAP.Abstractions.Capabilities;
using FSGAP.Core.Failures;
using FSGAP.Core.Sessions;
using FSGAP.Core.Telemetry;
using FSGAP.Fenix.Detection;
using FSGAP.Fenix.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace FSGAP.Fenix;

/// <summary>
/// Aircraft provider for the Fenix Simulations A319, A320 and A321: recognition and normalized identity.
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
/// Sessions still declare <see cref="AircraftCapabilities.None"/>: no Fenix telemetry or failures yet.
/// </para>
/// </remarks>
public sealed class FenixAircraftProvider : IAircraftProvider
{
    /// <summary>Value of <see cref="ProviderId"/>.</summary>
    public const string Id = "fenix";

    private readonly IInstalledAircraftCatalog? _installedAircraft;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger _logger;

    /// <summary>Creates the provider.</summary>
    /// <param name="installedAircraft">
    /// Installed-livery catalog used to resolve registration and livery details, typically a
    /// <see cref="FenixInstalledAircraftCatalog"/>. Without it, the identity relies on the simulator's ATC id only.
    /// </param>
    /// <param name="timeProvider">Clock used by sessions; <see cref="TimeProvider.System"/> by default.</param>
    /// <param name="logger">Optional logger (identity conflicts, catalog lookup failures).</param>
    public FenixAircraftProvider(
        IInstalledAircraftCatalog? installedAircraft = null,
        TimeProvider? timeProvider = null,
        ILogger<FenixAircraftProvider>? logger = null)
    {
        _installedAircraft = installedAircraft;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _logger = (ILogger?)logger ?? NullLogger.Instance;
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
        return new AircraftSession(
            ProviderId,
            FenixIdentityResolver.Resolve(variant, aircraft, installed, _logger),
            AircraftCapabilities.None,
            new UnavailableTelemetryProvider(_timeProvider),
            UnsupportedFailureProvider.Instance);
    }

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
