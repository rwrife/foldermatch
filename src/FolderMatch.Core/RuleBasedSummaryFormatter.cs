using System.Globalization;
using System.Text;

namespace FolderMatch.Core;

internal static class RuleBasedSummaryFormatter
{
    public static string Build(DiffResult diffResult)
    {
        ArgumentNullException.ThrowIfNull(diffResult);

        var builder = new StringBuilder();

        builder.Append("Compared ");
        builder.Append(diffResult.Items.Count.ToString("N0", CultureInfo.InvariantCulture));
        builder.Append(" entries (");
        builder.Append(diffResult.CountsSummary);
        builder.Append(").");

        var changedItems = diffResult.Items
            .Where(static item => item.ChangeType != DiffChangeType.Identical)
            .ToArray();

        if (changedItems.Length == 0)
        {
            builder.Append(" No differences found.");
            return builder.ToString();
        }

        builder.Append(' ');
        builder.Append(BuildChangeBreakdownSentence(diffResult));

        var topAreas = changedItems
            .GroupBy(GetTopArea, StringComparer.OrdinalIgnoreCase)
            .Select(group => new
            {
                Area = group.Key,
                Count = group.Count()
            })
            .OrderByDescending(static group => group.Count)
            .ThenBy(static group => group.Area, StringComparer.OrdinalIgnoreCase)
            .Take(3)
            .ToArray();

        if (topAreas.Length > 0)
        {
            builder.Append(" Most changes are under ");
            builder.Append(string.Join(", ", topAreas.Select(area => $"'{area.Area}' ({area.Count})")));
            builder.Append('.');
        }

        return builder.ToString();
    }

    private static string BuildChangeBreakdownSentence(DiffResult diffResult)
    {
        var parts = new List<string>();

        if (diffResult.NewCount > 0)
        {
            parts.Add($"{diffResult.NewCount} new");
        }

        if (diffResult.UpdatedCount > 0)
        {
            parts.Add($"{diffResult.UpdatedCount} updated");
        }

        if (diffResult.DeletedCount > 0)
        {
            parts.Add($"{diffResult.DeletedCount} deleted");
        }

        if (diffResult.ConflictCount > 0)
        {
            parts.Add($"{diffResult.ConflictCount} conflicted");
        }

        if (parts.Count == 0)
        {
            return "No actionable changes were detected.";
        }

        return $"Detected {string.Join(", ", parts)} entries.";
    }

    private static string GetTopArea(DiffItem item)
    {
        var normalized = PathNormalization.NormalizeRelativePath(item.RelativePath);
        var separatorIndex = normalized.IndexOf('/');
        if (separatorIndex <= 0)
        {
            return "(root)";
        }

        return normalized[..separatorIndex];
    }
}
