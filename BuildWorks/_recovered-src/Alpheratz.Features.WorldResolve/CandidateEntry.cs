namespace Alpheratz.Features.WorldResolve;

public sealed record CandidateEntry(string PhotoPath, string PhotoFilename, string WorldName, string? WorldId, int Distance, long SourceSlot);
