using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace Servicefy.Package.Pipeline;

/// <summary>
/// Value-equatable snapshot of a source <see cref="Location"/>, safe to keep in incremental
/// pipeline models. A <see cref="Location"/> holds its <see cref="SyntaxTree"/>, which compares by
/// reference and keeps the whole tree alive across compilations; this record keeps only the data
/// needed to rebuild an equivalent location when a diagnostic is finally reported.
/// </summary>
internal sealed record LocationInfo(string FilePath, TextSpan TextSpan, LinePositionSpan LineSpan)
{
    public static LocationInfo? From(Location? location)
    {
        if (location is null || location == Location.None || !location.IsInSource)
            return null;

        return new LocationInfo(
            location.SourceTree?.FilePath ?? string.Empty,
            location.SourceSpan,
            location.GetLineSpan().Span);
    }

    public Location ToLocation() => Location.Create(FilePath, TextSpan, LineSpan);
}
