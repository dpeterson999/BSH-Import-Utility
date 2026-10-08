using System;
using System.Collections.Generic;
using System.Linq;

#nullable enable

namespace BSH_Import_Utility.Domain
{
    public enum LabelStatus
    {
        /// <summary>Mapped to a column the database has.</summary>
        Mapped,
        /// <summary>Ignored by the app (built in) or by the user (ColumnMap.IsIgnored).</summary>
        Ignored,
        /// <summary>No active mapping.</summary>
        Unmapped,
        /// <summary>Mapped to a table or column the database does not have.</summary>
        MissingColumn
    }

    /// <summary>
    /// What an import would do with one PDF label. HasValue is false for lines whose value is 0,
    /// which the import skips (items not ordered, and most stray text).
    /// </summary>
    public record LabelCheck(
        string PdfLabel,
        object? Value,
        bool HasValue,
        LabelStatus Status,
        string? TableName,
        string? ColumnName,
        bool IsBuiltInIgnore)
    {
        /// <summary>True when this label would stop the import and ask the user about it.</summary>
        public bool BlocksImport => HasValue && (Status == LabelStatus.Unmapped || Status == LabelStatus.MissingColumn);

        public bool WouldImport => HasValue && Status == LabelStatus.Mapped;
    }

    /// <summary>One order's check: the labels in PDF order, plus what the import would do overall.</summary>
    public record OrderCheck(string FileName, string OrderNumber, bool AlreadyImported, List<LabelCheck> Labels)
    {
        public bool HasOrderNumber => !string.IsNullOrWhiteSpace(OrderNumber);
        public int ImportCount => Labels.Count(l => l.WouldImport);
        public int BlockingCount => Labels.Count(l => l.BlocksImport);
    }

    public static class PdfLabelChecker
    {
        /// <summary>
        /// Classifies each parsed line the same way AccessRepository.InsertDataIntoDatabase does,
        /// without touching the database. A label that appears more than once is listed once,
        /// keeping the first occurrence that has a value.
        /// </summary>
        public static List<LabelCheck> Check(
            IEnumerable<ProcessedLine> lines,
            IReadOnlyDictionary<string, (string TableName, string ColumnName)> columnMap,
            ISet<string> userIgnoredLabels,
            IReadOnlyDictionary<string, List<string>> schema)
        {
            var results = new List<LabelCheck>();
            var indexByLabel = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            foreach (var line in lines)
            {
                string label = line.ColumnName?.Trim() ?? "";
                if (label.Length == 0)
                    continue;

                object? value = line.ColumnValue;
                bool hasValue = !(value is int number && number == 0);
                var check = Classify(label, value, hasValue, columnMap, userIgnoredLabels, schema);

                if (indexByLabel.TryGetValue(label, out int index))
                {
                    if (!results[index].HasValue && hasValue)
                        results[index] = check;
                    continue;
                }

                indexByLabel[label] = results.Count;
                results.Add(check);
            }

            return results;
        }

        /// <summary>The order number the import would use: the "Order Number" line's value.</summary>
        public static string FindOrderNumber(IEnumerable<ProcessedLine> lines) =>
            lines.FirstOrDefault(l => string.Equals(l.ColumnName?.Trim(), ImportConstants.OrderNumberColumn, StringComparison.OrdinalIgnoreCase))
                 ?.ColumnValue?.ToString()?.Trim() ?? "";

        private static LabelCheck Classify(
            string label,
            object? value,
            bool hasValue,
            IReadOnlyDictionary<string, (string TableName, string ColumnName)> columnMap,
            ISet<string> userIgnoredLabels,
            IReadOnlyDictionary<string, List<string>> schema)
        {
            if (ImportConstants.IgnoredColumns.Contains(label))
                return new LabelCheck(label, value, hasValue, LabelStatus.Ignored, null, null, IsBuiltInIgnore: true);

            if (userIgnoredLabels.Contains(label))
                return new LabelCheck(label, value, hasValue, LabelStatus.Ignored, null, null, IsBuiltInIgnore: false);

            if (!columnMap.TryGetValue(label, out var mapped))
                return new LabelCheck(label, value, hasValue, LabelStatus.Unmapped, null, null, IsBuiltInIgnore: false);

            bool exists = schema.TryGetValue(mapped.TableName, out var columns) &&
                          columns.Any(c => c.Equals(mapped.ColumnName, StringComparison.OrdinalIgnoreCase));

            return new LabelCheck(label, value, hasValue, exists ? LabelStatus.Mapped : LabelStatus.MissingColumn,
                mapped.TableName, mapped.ColumnName, IsBuiltInIgnore: false);
        }
    }
}
