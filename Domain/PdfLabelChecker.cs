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
    /// which the import skips (items not ordered, and most stray text). IsFormItem is true for
    /// lines in the form's item list, ordered or not, as opposed to headings and page text;
    /// Section is the heading such an item is listed under (e.g. "BREADS").
    /// </summary>
    public record LabelCheck(
        string PdfLabel,
        object? Value,
        bool HasValue,
        LabelStatus Status,
        string? TableName,
        string? ColumnName,
        bool IsBuiltInIgnore,
        bool IsFormItem = false,
        string? Section = null)
    {
        private bool IsNotSetUp => Status == LabelStatus.Unmapped || Status == LabelStatus.MissingColumn;

        /// <summary>True when this label would stop the import and ask the user about it.</summary>
        public bool BlocksImport => HasValue && IsNotSetUp;

        /// <summary>
        /// True for an item on the form that is not set up but was not ordered this time (for example,
        /// a line newly added to the form). It won't stop this import, but it will stop the first one that orders it.
        /// </summary>
        public bool NotSetUpNotOrdered => !HasValue && IsFormItem && IsNotSetUp;

        /// <summary>True when the user should set this label up, now or before it is next ordered.</summary>
        public bool NeedsSetUp => BlocksImport || NotSetUpNotOrdered;

        public bool WouldImport => HasValue && Status == LabelStatus.Mapped;
    }

    /// <summary>One order's check: the labels in PDF order, plus what the import would do overall.</summary>
    public record OrderCheck(string FileName, string OrderNumber, bool AlreadyImported, List<LabelCheck> Labels)
    {
        public bool HasOrderNumber => !string.IsNullOrWhiteSpace(OrderNumber);
        public int ImportCount => Labels.Count(l => l.WouldImport);
        public int BlockingCount => Labels.Count(l => l.BlocksImport);
        public int NotSetUpNotOrderedCount => Labels.Count(l => l.NotSetUpNotOrdered);
    }

    public static class PdfLabelChecker
    {
        // The form's item list starts after the first "Qty Ordered / Qty Filled" column heading and ends
        // at "FOR INFORMATION ONLY" (the sign-off area). Inside it, everything except the lines below
        // (column headings, page titles and footers) and the ALL-CAPS section headings is an item.
        private const string ItemListStart = "Filled";
        private const string ItemListEnd = "FOR INFORMATION ONLY";

        private static readonly HashSet<string> ItemListFurniture = new(StringComparer.OrdinalIgnoreCase)
        {
            "Qty", "Ordered", "Filled", "(continued)"
        };

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

            foreach (var (label, value, section) in Walk(lines))
            {
                bool hasValue = !(value is int number && number == 0);
                var check = Classify(label, value, hasValue, columnMap, userIgnoredLabels, schema) with
                {
                    IsFormItem = section != null,
                    Section = section
                };

                if (indexByLabel.TryGetValue(label, out int index))
                {
                    var existing = results[index];
                    var kept = !existing.HasValue && hasValue ? check : existing;
                    results[index] = kept with
                    {
                        IsFormItem = existing.IsFormItem || check.IsFormItem,
                        Section = existing.Section ?? check.Section
                    };
                    continue;
                }

                indexByLabel[label] = results.Count;
                results.Add(check);
            }

            return results;
        }

        /// <summary>The form section (e.g. "BREADS") of each item label, for telling someone where a new item goes.</summary>
        public static Dictionary<string, string> FindSections(IEnumerable<ProcessedLine> lines)
        {
            var sections = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (label, _, section) in Walk(lines))
            {
                if (section != null && !sections.ContainsKey(label))
                    sections[label] = section;
            }
            return sections;
        }

        /// <summary>
        /// Yields each non-blank label with its value and, for items in the form's item list, the section
        /// heading it is under (null for everything else). Items before any heading get an empty section.
        /// </summary>
        private static IEnumerable<(string Label, object? Value, string? Section)> Walk(IEnumerable<ProcessedLine> lines)
        {
            bool inItemList = false;
            string section = "";

            foreach (var line in lines)
            {
                string label = line.ColumnName?.Trim() ?? "";
                if (label.Length == 0)
                    continue;

                bool isFormItem = inItemList && !IsItemListFurniture(label);

                if (label.Equals(ItemListStart, StringComparison.OrdinalIgnoreCase))
                    inItemList = true;
                else if (label.Equals(ItemListEnd, StringComparison.OrdinalIgnoreCase))
                    inItemList = false;
                else if (inItemList && IsSectionHeading(label))
                    section = label;

                yield return (label, line.ColumnValue, isFormItem ? section : null);
            }
        }

        private static bool IsItemListFurniture(string label) =>
            ItemListFurniture.Contains(label) ||
            label.StartsWith("Printed on ", StringComparison.OrdinalIgnoreCase) ||
            label.StartsWith("Order #", StringComparison.OrdinalIgnoreCase) ||
            label.EndsWith("Order for Food and Supplies", StringComparison.OrdinalIgnoreCase) || // page title; the apostrophe varies
            IsSectionHeading(label);

        /// <summary>Section headings such as "CANNED MEATS" are all capitals; item labels are not.</summary>
        private static bool IsSectionHeading(string label) =>
            label.Any(char.IsLetter) && !label.Any(char.IsLower);

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
