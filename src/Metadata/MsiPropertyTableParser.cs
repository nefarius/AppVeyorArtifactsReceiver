namespace AppVeyorArtifactsReceiver.Metadata;

/// <summary>
///     Parses the tab-separated Property table written by <c>msiinfo export</c> and Windows Installer.
///     The first three rows are column names, column types, and the table name plus primary keys.
/// </summary>
internal static class MsiPropertyTableParser
{
    public static bool TryParse(string exportText, out Dictionary<string, string> properties, out string error)
    {
        properties = null;
        error = null;

        if (string.IsNullOrWhiteSpace(exportText))
        {
            error = "MSI property export was empty.";
            return false;
        }

        List<string> lines = ReadNonEmptyLines(exportText);
        if (lines.Count < 3)
        {
            error = "MSI property export is missing its header.";
            return false;
        }

        string[] columns = lines[0].Split('\t');
        int propertyIndex = Array.IndexOf(columns, "Property");
        int valueIndex = Array.IndexOf(columns, "Value");
        if (propertyIndex < 0 || valueIndex < 0)
        {
            error = "MSI property export is missing Property or Value columns.";
            return false;
        }

        if (lines[1].Split('\t').Length != columns.Length)
        {
            error = "MSI property export column types do not match the column names.";
            return false;
        }

        string[] tableHeader = lines[2].Split('\t');
        if (tableHeader.Length == 0 || !string.Equals(tableHeader[0], "Property", StringComparison.Ordinal))
        {
            error = "MSI property export is not a Property table.";
            return false;
        }

        int requiredFields = Math.Max(propertyIndex, valueIndex) + 1;
        Dictionary<string, string> parsed = new(StringComparer.Ordinal);
        for (int index = 3; index < lines.Count; index++)
        {
            string[] fields = lines[index].Split('\t');
            if (fields.Length < requiredFields)
            {
                error = $"MSI property export row {index + 1} is truncated.";
                properties = null;
                return false;
            }

            string name = fields[propertyIndex];
            if (string.IsNullOrEmpty(name))
            {
                continue;
            }

            parsed[name] = fields[valueIndex];
        }

        properties = parsed;
        return true;
    }

    private static List<string> ReadNonEmptyLines(string exportText)
    {
        List<string> lines = [];
        using StringReader reader = new(exportText.TrimStart('\uFEFF'));
        while (reader.ReadLine() is { } line)
        {
            if (line.Length > 0)
            {
                lines.Add(line);
            }
        }

        return lines;
    }
}
