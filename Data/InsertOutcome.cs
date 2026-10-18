using BSH_Import_Utility.Domain;
using System;
using System.Collections.Generic;

namespace BSH_Import_Utility.Data
{
    public enum InsertStatus
    {
        /// <summary>The order was saved.</summary>
        Inserted,
        /// <summary>The order number is already in the BSH table; nothing was saved.</summary>
        DuplicateOrderNumber,
        /// <summary>No order number was found, so the file is not an order form.</summary>
        InvalidFile,
        /// <summary>Some labels are unmapped or mapped to a missing column; the Unrecognized Items dialog is shown.</summary>
        MissingColumnMappings,
        /// <summary>The save failed and was rolled back; Exception says why.</summary>
        Error
    }

    /// <summary>The result of saving one order, with the details the import screen reports.</summary>
    public sealed class InsertOutcome
    {
        public InsertStatus Status { get; init; }
        public string? OrderNumber { get; init; }
        public List<string> MissingColumns { get; init; } = new();
        public List<UnresolvedLabel> UnresolvedLabels { get; init; } = new();
        public Exception? Exception { get; init; }
        public string? FileName { get; init; }
    }
}