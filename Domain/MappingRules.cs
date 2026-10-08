using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

#nullable enable

namespace BSH_Import_Utility.Domain
{
    /// <summary>
    /// Pure rules for pairing PDF labels with database columns: which targets may be chosen,
    /// whether a pairing is valid, what to suggest, and when to warn. No database or UI access,
    /// so it can be unit tested.
    /// </summary>
    public static class MappingRules
    {
        /// <summary>
        /// Tables that have an Order Number column but must never receive mapped PDF values.
        /// </summary>
        private static readonly HashSet<string> ExcludedTables = new(StringComparer.OrdinalIgnoreCase)
        {
            ImportConstants.RouteInfoTableName,
            "LoadingInfo",
            ImportConstants.ColumnMapTableName,
            "Copy of BSH",
            "Paste Errors"
        };

        /// <summary>
        /// BSH columns that the grid and the insert logic depend on. Their mappings cannot be
        /// changed, and no other label may be pointed at them.
        /// </summary>
        public static readonly IReadOnlyCollection<string> StructuralBshColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ImportConstants.OrderNumberColumn,
            "Recipient",
            "Ward",
            ImportConstants.StorehouseLabel
        };

        /// <summary>
        /// Table/column names end up inside [brackets] in INSERT statements, so they must not contain brackets.
        /// </summary>
        public static bool IsSafeName(string? name) =>
            !string.IsNullOrWhiteSpace(name) && name!.IndexOf('[') < 0 && name.IndexOf(']') < 0;

        public static bool IsStructuralLabel(string label) =>
            StructuralBshColumns.Contains(label.Trim());

        /// <summary>
        /// Filters the live schema down to what a user may pick: real item tables (those with an
        /// Order Number column, minus the excluded ones) and their columns, without Order Number
        /// and the structural BSH columns. Sorted for display.
        /// </summary>
        public static SortedDictionary<string, List<string>> GetSelectableTargets(
            IReadOnlyDictionary<string, List<string>> schema)
        {
            var result = new SortedDictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

            foreach (var table in schema)
            {
                if (table.Key.StartsWith("MSys", StringComparison.OrdinalIgnoreCase) ||
                    ExcludedTables.Contains(table.Key) ||
                    !IsSafeName(table.Key) ||
                    !table.Value.Contains(ImportConstants.OrderNumberColumn, StringComparer.OrdinalIgnoreCase))
                {
                    continue;
                }

                bool isBsh = table.Key.Equals(ImportConstants.BshTableName, StringComparison.OrdinalIgnoreCase);

                var columns = table.Value
                    .Where(IsSafeName)
                    .Where(c => !c.Equals(ImportConstants.OrderNumberColumn, StringComparison.OrdinalIgnoreCase))
                    .Where(c => !(isBsh && StructuralBshColumns.Contains(c)))
                    .OrderBy(c => c, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (columns.Count > 0)
                    result[table.Key] = columns;
            }

            return result;
        }

        /// <summary>
        /// Returns an error message a user can act on, or null when the pairing may be saved.
        /// </summary>
        public static string? Validate(
            string label,
            string? tableName,
            string? columnName,
            IReadOnlyDictionary<string, List<string>> selectableTargets)
        {
            if (string.IsNullOrWhiteSpace(label))
                return "The PDF label is empty.";

            if (IsStructuralLabel(label))
                return $"\"{label}\" is a required order field and its mapping cannot be changed here.";

            if (string.IsNullOrWhiteSpace(tableName) || string.IsNullOrWhiteSpace(columnName))
                return $"Choose a table and a column for \"{label}\", or tick Ignore.";

            if (!IsSafeName(tableName) || !IsSafeName(columnName))
                return $"Table and column names cannot contain [ or ].";

            if (!selectableTargets.TryGetValue(tableName!, out var columns))
                return $"\"{tableName}\" is not a table that order items can be saved to.";

            if (!columns.Contains(columnName!, StringComparer.OrdinalIgnoreCase))
                return $"\"{columnName}\" is not a column in {tableName}.";

            return null;
        }

        // Trailing size / packaging words seen on Bishop's Storehouse forms, e.g. "14.4 oz",
        // "pkg of 24", "bag, abt 4 lb", "ea", "doz", "27 servings", "4 fl oz".
        private static readonly Regex TrailingSizeToken = new(
            @"[\s,]+(\d+(\.\d+)?(-\d+(\.\d+)?)?|oz|fl|lbs?|pkg|of|bag|abt|ea|doz|gal|loaf|roll|head|servings?|ct|count)$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly Regex Whitespace = new(@"\s+");

        /// <summary>
        /// Lower-cased label with whitespace collapsed and Access's "…" turned back into "...".
        /// </summary>
        public static string NormalizeLabel(string label) =>
            Whitespace.Replace(label.Replace("…", "..."), " ").Trim().ToLowerInvariant();

        /// <summary>
        /// The label without its trailing size/packaging suffix, normalized:
        /// "Salt (iodized) 26 oz" → "salt (iodized)", "Apples bag, abt 4 lb" → "apples".
        /// </summary>
        public static string LabelStem(string label)
        {
            string stem = NormalizeLabel(label);

            while (true)
            {
                string shorter = TrailingSizeToken.Replace(stem, "");
                if (shorter.Length == 0 || shorter == stem)
                    break;
                stem = shorter;
            }

            return stem.TrimEnd(',', ' ');
        }

        /// <summary>
        /// Best guess for an unmapped label, or null when there is no single clear answer.
        /// First looks for existing labels with the same stem that all point at one target
        /// (this carries over deliberate renames), then for the longest column name the label
        /// starts with, provided only one table has that column.
        /// </summary>
        public static (string TableName, string ColumnName)? Suggest(
            string label,
            IReadOnlyDictionary<string, (string TableName, string ColumnName)> existingMap,
            IReadOnlyDictionary<string, List<string>> selectableTargets)
        {
            string stem = LabelStem(label);

            var byStem = existingMap
                .Where(kv => !kv.Key.Equals(label, StringComparison.OrdinalIgnoreCase) && LabelStem(kv.Key) == stem)
                .Select(kv => kv.Value)
                .Where(t => IsSelectable(t.TableName, t.ColumnName, selectableTargets))
                .Distinct(TargetComparer.Instance)
                .ToList();

            if (byStem.Count == 1)
                return byStem[0];
            if (byStem.Count > 1)
                return null;

            var byPrefix = PrefixColumnMatches(label, selectableTargets);
            if (byPrefix.Count == 0)
                return null;

            int longest = byPrefix.Max(t => t.ColumnName.Length);
            var best = byPrefix.Where(t => t.ColumnName.Length == longest).ToList();

            return best.Count == 1 ? best[0] : null;
        }

        /// <summary>
        /// Reasons to double-check a pairing before saving it. A wrong category imports
        /// "successfully" with the item under the wrong table, so this is worth a prompt.
        /// Many labels pointing at one column is normal and not warned about.
        /// </summary>
        public static List<string> GetCategoryWarnings(
            string label,
            string tableName,
            string columnName,
            IReadOnlyDictionary<string, (string TableName, string ColumnName)> existingMap,
            IReadOnlyDictionary<string, List<string>> selectableTargets)
        {
            var warnings = new List<string>();
            string stem = LabelStem(label);

            foreach (var kv in existingMap)
            {
                if (kv.Key.Equals(label, StringComparison.OrdinalIgnoreCase) ||
                    kv.Value.TableName.Equals(tableName, StringComparison.OrdinalIgnoreCase) ||
                    LabelStem(kv.Key) != stem)
                {
                    continue;
                }

                warnings.Add($"\"{label}\" → {tableName}: a similar item, \"{kv.Key}\", is saved under {kv.Value.TableName} ({kv.Value.ColumnName}).");
            }

            foreach (var match in PrefixColumnMatches(label, selectableTargets))
            {
                if (match.TableName.Equals(tableName, StringComparison.OrdinalIgnoreCase))
                    continue;

                warnings.Add($"\"{label}\" → {tableName}: its name starts with \"{match.ColumnName}\", which is a column in {match.TableName}.");
            }

            return warnings.Distinct().ToList();
        }

        private static bool IsSelectable(string table, string column, IReadOnlyDictionary<string, List<string>> selectableTargets) =>
            selectableTargets.TryGetValue(table, out var cols) &&
            cols.Contains(column, StringComparer.OrdinalIgnoreCase);

        private static List<(string TableName, string ColumnName)> PrefixColumnMatches(
            string label,
            IReadOnlyDictionary<string, List<string>> selectableTargets)
        {
            string normalized = NormalizeLabel(label);
            var matches = new List<(string, string)>();

            foreach (var table in selectableTargets)
            {
                foreach (var column in table.Value)
                {
                    // The label must be the column name plus only size/packaging words, so
                    // "Chicken flavored rotini soup 14.4 oz" does not match the column "Chicken".
                    string col = NormalizeLabel(column);
                    if (normalized == col ||
                        (normalized.StartsWith(col + " ", StringComparison.Ordinal) &&
                         LabelStem("x" + normalized.Substring(col.Length)) == "x"))
                    {
                        matches.Add((table.Key, column));
                    }
                }
            }

            return matches;
        }

        private sealed class TargetComparer : IEqualityComparer<(string TableName, string ColumnName)>
        {
            public static readonly TargetComparer Instance = new();

            public bool Equals((string TableName, string ColumnName) x, (string TableName, string ColumnName) y) =>
                StringComparer.OrdinalIgnoreCase.Equals(x.TableName, y.TableName) &&
                StringComparer.OrdinalIgnoreCase.Equals(x.ColumnName, y.ColumnName);

            public int GetHashCode((string TableName, string ColumnName) obj) =>
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.TableName) ^
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.ColumnName);
        }
    }
}
