#nullable enable

namespace BSH_Import_Utility.Domain
{
    /// <summary>
    /// One PDF label → database column pairing, as stored in the ColumnMap table
    /// or read from ColumnToTableMap.json. IsActive is false for entries read from the
    /// <see cref="ImportConstants.InactiveJsonKey"/> section of an exported file. Notes is null
    /// when the file has no <see cref="ImportConstants.NotesJsonKey"/> section (leave notes alone)
    /// and "" when it has one but this label has no note (clear the note).
    /// </summary>
    public record ColumnMapEntry(string PdfLabel, string TableName, string ColumnName, bool IsActive = true, string? Notes = null);
}
