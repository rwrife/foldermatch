namespace FolderMatch.Core;

public sealed class LocalDiffAiOptions
{
    public bool Enabled { get; init; }

    public string Endpoint { get; init; } = "http://localhost:11434/v1/chat/completions";

    public string Model { get; init; } = "llama3.2:3b";

    public int MaxMetadataItems { get; init; } = 500;

    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(15);
}
