#nullable enable

namespace BSH_Import_Utility.Domain
{
    /// <summary>
    /// A PDF label that blocked an import. MappedTable/MappedColumn are null when the label
    /// has no mapping at all, and set when it is mapped to a column the database no longer has.
    /// Section is the form section it is listed under (e.g. "BREADS"), when known.
    /// </summary>
    public record UnresolvedLabel(string PdfLabel, object? Value, string? MappedTable, string? MappedColumn, string? Section = null)
    {
        public bool IsMappedToMissingColumn => !string.IsNullOrEmpty(MappedTable);
    }

    /// <summary>
    /// What the user chose for one label: map it to TableName/ColumnName, or ignore it.
    /// Notes, when not null, replaces the row's notes; null leaves existing notes alone.
    /// IsActive is false only when a mapping file says the row should be inactive.
    /// </summary>
    public record MappingChoice(string PdfLabel, string? TableName, string? ColumnName, bool Ignore, string? Notes = null, bool IsActive = true);
}
