using System;
using System.Collections.Generic;

namespace BSH_Import_Utility.Data
{
    public enum InsertStatus
    {
        Inserted,
        DuplicateOrderNumber,
        InvalidFile,
        MissingColumnMappings,
        Error
    }

    public sealed class InsertOutcome
    {
        public InsertStatus Status { get; init; }
        public string? OrderNumber { get; init; }
        public List<string> MissingColumns { get; init; } = new();
        public Exception? Exception { get; init; }
        public string? FileName { get; init; }
    }
}