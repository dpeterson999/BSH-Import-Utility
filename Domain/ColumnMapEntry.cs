#nullable enable

namespace BSH_Import_Utility.Domain
{
    /// <summary>
    /// One PDF label → database column pairing, as stored in the ColumnMap table
    /// or read from ColumnToTableMap.json.
    /// </summary>
    public record ColumnMapEntry(string PdfLabel, string TableName, string ColumnName);
}
