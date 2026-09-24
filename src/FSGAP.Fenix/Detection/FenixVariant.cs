namespace FSGAP.Fenix.Detection;

/// <summary>Engine families offered by the Fenix A319/A320/A321.</summary>
internal enum FenixEngine
{
    Cfm,
    Iae,
}

/// <summary>Wingtip devices offered by the Fenix A319/A320/A321.</summary>
internal enum FenixWingtip
{
    Sharklets,
    WingtipFence,
}

/// <summary>What could be recognized about a Fenix variant. Every part except the model may be unknown.</summary>
internal sealed record FenixVariant(FenixModel Model, FenixEngine? Engine, FenixWingtip? Wingtip);
