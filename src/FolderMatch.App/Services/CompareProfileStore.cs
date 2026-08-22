using System.Text.Json;
using System.Text.Json.Serialization;
using FolderMatch.App.Models;

namespace FolderMatch.App.Services;

public sealed class CompareProfileStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _filePath;

    public CompareProfileStore(string? appDataRoot = null)
    {
        var root = appDataRoot ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FolderMatch");
        _filePath = Path.Combine(root, "profiles.json");
    }

    public async Task<IReadOnlyList<CompareProfile>> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_filePath))
        {
            return Array.Empty<CompareProfile>();
        }

        await using var stream = File.OpenRead(_filePath);
        return await JsonSerializer.DeserializeAsync<List<CompareProfile>>(stream, JsonOptions, cancellationToken)
            ?? new List<CompareProfile>();
    }

    public async Task SaveAsync(CompareProfile profile, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (string.IsNullOrWhiteSpace(profile.Name))
        {
            throw new ArgumentException("Profile name is required.", nameof(profile));
        }

        var profiles = (await LoadAsync(cancellationToken)).ToList();
        profiles.RemoveAll(item => string.Equals(item.Name, profile.Name, StringComparison.OrdinalIgnoreCase));
        profiles.Add(profile with { Name = profile.Name.Trim() });
        profiles.Sort((left, right) => StringComparer.OrdinalIgnoreCase.Compare(left.Name, right.Name));

        var directory = Path.GetDirectoryName(_filePath)!;
        Directory.CreateDirectory(directory);
        var tempPath = _filePath + $".{Guid.NewGuid():N}.tmp";

        try
        {
            await using (var stream = File.Create(tempPath))
            {
                await JsonSerializer.SerializeAsync(stream, profiles, JsonOptions, cancellationToken);
            }

            File.Move(tempPath, _filePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    public async Task DeleteAsync(string name, CancellationToken cancellationToken = default)
    {
        var profiles = (await LoadAsync(cancellationToken))
            .Where(item => !string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var directory = Path.GetDirectoryName(_filePath)!;
        Directory.CreateDirectory(directory);
        await using var stream = File.Create(_filePath);
        await JsonSerializer.SerializeAsync(stream, profiles, JsonOptions, cancellationToken);
    }
}
