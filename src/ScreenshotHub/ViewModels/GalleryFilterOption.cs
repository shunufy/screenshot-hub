using ScreenshotHub.Core;

namespace ScreenshotHub.ViewModels;

internal enum GalleryFilterKind
{
    All,
    Favorites,
    Tagged,
    Untagged,
    ExactDuplicates,
    SimilarImages
}

internal sealed record GalleryFilterOption(GalleryFilterKind Kind, string DisplayName)
{
    public override string ToString() => DisplayName;

    public static IReadOnlyList<GalleryFilterOption> CreateAll() =>
    [
        new(GalleryFilterKind.All, AppText.FilterAll),
        new(GalleryFilterKind.Favorites, AppText.FilterFavorites),
        new(GalleryFilterKind.Tagged, AppText.FilterTagged),
        new(GalleryFilterKind.Untagged, AppText.FilterUntagged),
        new(GalleryFilterKind.ExactDuplicates, AppText.FilterExactDuplicates),
        new(GalleryFilterKind.SimilarImages, AppText.FilterSimilarImages)
    ];
}

internal sealed record TagFilterOption(string? Tag, string DisplayName)
{
    public override string ToString() => DisplayName;
}
