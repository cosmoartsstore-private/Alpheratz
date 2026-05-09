using Alpheratz.Core;
using CommunityToolkit.Mvvm.ComponentModel;
using Alpheratz.Features.Gallery;
using Alpheratz.Models;

namespace Alpheratz.Features.PhotoModal;

public partial class SimilarWorldCandidateItem : UiThreadSafeObservableObject
{
    [ObservableProperty] private PhotoThumbnailItem photo = new();
    [ObservableProperty] private int distance;
    [ObservableProperty] private double similarity;

    public static SimilarWorldCandidateItem FromDto(SimilarWorldCandidateDto dto) => new()
    {
        Photo = PhotoThumbnailItem.FromDto(dto.photo),
        distance = dto.distance,
        similarity = dto.similarity,
    };
}
