using System;

#nullable enable

namespace BSH_Import_Utility.Domain
{
    /// <summary>
    /// One row of the ColumnMap table, as shown in the Mapping Manager.
    /// TableName/ColumnName are null for ignored labels.
    /// </summary>
    public record ColumnMapRow(
        int Id,
        string PdfLabel,
        string? TableName,
        string? ColumnName,
        bool IsActive,
        bool IsIgnored,
        DateTime CreatedOn,
        DateTime ChangedOn,
        string? Notes);
}
