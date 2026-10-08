using FSGAP.Abstractions.Aircraft;
using FSGAP.Abstractions.Simulator;
using FSGAP.Abstractions.Telemetry;
using FSGAP.Core.Resolution;
using FSGAP.Fenix;
using FSGAP.Synaptic;
using Microsoft.Extensions.Logging;

namespace FSGAP.Composition;

/// <summary>
/// The services of the one simulator connection that every provider shares. In production all of them are the same
/// <c>SimConnectSimulator</c> instance; tests substitute doubles.
/// </summary>
internal sealed record FsgapSimulatorServices(
    ISimulatorConnection Connection,
    ISimulatorStateProvider State,
    IAircraftDetector Detector,
    ITelemetryProvider GenericTelemetry,
    ISimulatorVariableReader Reader,
    ISimulatorVariableWriter Writer,
    IInstalledLiveryService Liveries,
    IAirportService Airports);

/// <summary>The built-in providers registered in the existing registry, and their installed-aircraft catalogs.</summary>
internal sealed record FsgapComposition(AircraftProviderRegistry Registry, IReadOnlyList<IInstalledAircraftCatalog> InstalledAircraft)
{
    /// <summary>Provider ids of the built-in providers, in registration order.</summary>
    internal static IReadOnlyList<string> BuiltInProviderIds { get; } = [FenixAircraftProvider.Id, SynapticAircraftProvider.Id];

    /// <summary>
    /// Composes the built-in aircraft providers on the shared simulator services. This is the only place that knows the
    /// provider types: adding an aircraft family (for example iniBuilds or PMDG) means one more registration here and no
    /// change in any application.
    /// </summary>
    /// <param name="options">Runtime options.</param>
    /// <param name="services">The one connection's services.</param>
    /// <param name="loggers">Optional logger factory.</param>
    /// <param name="time">Clock.</param>
    /// <param name="fenixPackageRoots">Fenix package folders; <see langword="null"/> locates the MSFS installation (tests pass their own).</param>
    /// <param name="fenixEfbHttpClient">EFB HTTP client; <see langword="null"/> lets the Fenix provider create its own (tests pass a stub).</param>
    /// <param name="synapticPackageRoots">Synaptic livery package folders; <see langword="null"/> locates the MSFS installation (tests pass their own).</param>
    internal static FsgapComposition CreateDefault(
        FsgapRuntimeOptions options,
        FsgapSimulatorServices services,
        ILoggerFactory? loggers,
        TimeProvider time,
        IReadOnlyList<string>? fenixPackageRoots = null,
        HttpClient? fenixEfbHttpClient = null,
        IReadOnlyList<string>? synapticPackageRoots = null)
    {
        var sdk = options.Sdk;
        var commands = options.EnableAircraftCommands;
        var fenixCatalog = new FenixInstalledAircraftCatalog(sdk, fenixPackageRoots, loggers?.CreateLogger<FenixInstalledAircraftCatalog>(), time);
        var synapticCatalog = new SynapticInstalledAircraftCatalog(sdk, services.Liveries, loggers?.CreateLogger<SynapticInstalledAircraftCatalog>(), time, synapticPackageRoots);

        // Deterministic built-in registration; the registry resolves each loaded aircraft to the provider that supports it.
        var registry = new AircraftProviderRegistry();
        registry.Register(new FenixAircraftProvider(
            fenixCatalog,
            time,
            loggers?.CreateLogger<FenixAircraftProvider>(),
            services.GenericTelemetry,
            services.Reader,
            services.Detector,
            sdk.Telemetry,
            fenixOptions: commands ? new FenixOptions() : null,
            efbHttpClient: fenixEfbHttpClient));
        registry.Register(new SynapticAircraftProvider(
            synapticCatalog,
            time,
            loggers?.CreateLogger<SynapticAircraftProvider>(),
            services.GenericTelemetry,
            services.Reader,
            services.Detector,
            sdk.Telemetry,
            simulatorVariableWriter: commands ? services.Writer : null));

        return new FsgapComposition(registry, [fenixCatalog, synapticCatalog]);
    }
}
