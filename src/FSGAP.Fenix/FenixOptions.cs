namespace FSGAP.Fenix;

/// <summary>
/// Fenix-specific settings: where the Fenix EFB listens and how long FSGAP waits for it. Generic host settings stay
/// in <c>FsgapOptions</c>.
/// </summary>
/// <remarks>
/// <para>
/// The Fenix EFB backend is a local, unofficial HTTP service that exists only while a Fenix aircraft is loaded. It
/// has no authentication. The default address is the IPv4 loopback, not <c>localhost</c>: the backend listens on
/// IPv4 only, and resolving <c>localhost</c> to <c>::1</c> first cost about two seconds per call in the audited
/// applications. Pointing <see cref="EfbBaseAddress"/> anywhere other than the local machine is the integrator's
/// responsibility; FSGAP adds no transport security of its own.
/// </para>
/// </remarks>
public sealed record FenixOptions
{
    /// <summary>Default EFB address: the IPv4 loopback, port 8083.</summary>
    public static Uri DefaultEfbBaseAddress { get; } = new("http://127.0.0.1:8083/");

    /// <summary>Base address of the Fenix EFB backend. Default <see cref="DefaultEfbBaseAddress"/>.</summary>
    public Uri EfbBaseAddress { get; init; } = DefaultEfbBaseAddress;

    /// <summary>
    /// Maximum time for one EFB request (3 s by default, as in the audited applications). A command never takes
    /// longer than this plus the confirmation read-back.
    /// </summary>
    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(3);

    /// <summary>
    /// Read-backs of the failure list when the EFB's answer to a command does not confirm the requested state
    /// (3 by default). Some failures echo a stale state and only appear correctly in the list shortly after.
    /// </summary>
    public int ConfirmationReadbacks { get; init; } = 3;

    /// <summary>Delay between two confirmation read-backs (400 ms by default).</summary>
    public TimeSpan ConfirmationDelay { get; init; } = TimeSpan.FromMilliseconds(400);

    /// <summary>Validates the options.</summary>
    /// <exception cref="ArgumentException">A value is out of range.</exception>
    public void Validate()
    {
        ArgumentNullException.ThrowIfNull(EfbBaseAddress, nameof(EfbBaseAddress));
        if (!EfbBaseAddress.IsAbsoluteUri || (EfbBaseAddress.Scheme != Uri.UriSchemeHttp && EfbBaseAddress.Scheme != Uri.UriSchemeHttps))
        {
            throw new ArgumentException("The EFB base address must be an absolute http(s) URI.", nameof(EfbBaseAddress));
        }

        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(RequestTimeout, TimeSpan.Zero, nameof(RequestTimeout));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(RequestTimeout, TimeSpan.FromSeconds(30), nameof(RequestTimeout));
        ArgumentOutOfRangeException.ThrowIfNegative(ConfirmationReadbacks, nameof(ConfirmationReadbacks));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(ConfirmationReadbacks, 10, nameof(ConfirmationReadbacks));
        ArgumentOutOfRangeException.ThrowIfLessThan(ConfirmationDelay, TimeSpan.Zero, nameof(ConfirmationDelay));
    }
}
