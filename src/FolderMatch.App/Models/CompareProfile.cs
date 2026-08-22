using FolderMatch.Core;

namespace FolderMatch.App.Models;

public sealed record CompareProfile(
    string Name,
    string LeftPath,
    string RightPath,
    DiffCompareMode CompareMode,
    SyncDirection SyncDirection);
