using System;
using System.Collections.Generic;
using System.Linq;

#nullable enable

namespace BSH_Import_Utility.Domain
{
    /// <summary>
    /// What importing a mapping JSON file would do. Import only adds and updates; it never
    /// deactivates or deletes rows that are missing from the file.
    /// </summary>
    public sealed class MappingImportPlan
    {
        public List<MappingChoice> Adds { get; } = new();
        public List<MappingChoice> Changes { get; } = new();
        public int Unchanged { get; set; }
        public List<string> Problems { get; } = new();

        public bool HasWork => Adds.Count > 0 || Changes.Count > 0;
    }

    public static class MappingImportPlanner
    {
        /// <summary>
        /// Compares file entries with the current rows. Entries under the
        /// <see cref="ImportConstants.IgnoredJsonKey"/> table are ignored labels. Invalid targets,
        /// repeated labels and attempts to change required fields are reported and skipped.
        /// </summary>
        public static MappingImportPlan Plan(
            IEnumerable<ColumnMapEntry> entries,
            IEnumerable<ColumnMapRow> existingRows,
            IReadOnlyDictionary<string, List<string>> selectableTargets)
        {
            var plan = new MappingImportPlan();
            var existing = existingRows.ToDictionary(r => r.PdfLabel.Trim(), StringComparer.OrdinalIgnoreCase);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var entry in entries)
            {
                string label = entry.PdfLabel.Trim();
                if (label.Length == 0)
                    continue;

                if (!seen.Add(label))
                {
                    plan.Problems.Add($"\"{label}\" is listed more than once; only the first one was used.");
                    continue;
                }

                bool ignore = entry.TableName.Equals(ImportConstants.IgnoredJsonKey, StringComparison.OrdinalIgnoreCase);
                existing.TryGetValue(label, out var row);

                bool same = row != null && row.IsActive && row.IsIgnored == ignore &&
                            (ignore ||
                             (string.Equals(row.TableName, entry.TableName, StringComparison.OrdinalIgnoreCase) &&
                              string.Equals(row.ColumnName, entry.ColumnName, StringComparison.OrdinalIgnoreCase)));

                if (same)
                {
                    plan.Unchanged++;
                    continue;
                }

                if (MappingRules.IsStructuralLabel(label))
                {
                    plan.Problems.Add($"\"{label}\" is a required order field and was not changed.");
                    continue;
                }

                if (!ignore)
                {
                    string? error = MappingRules.Validate(label, entry.TableName, entry.ColumnName, selectableTargets);
                    if (error != null)
                    {
                        plan.Problems.Add($"\"{label}\" was skipped: {error}");
                        continue;
                    }
                }

                var choice = ignore
                    ? new MappingChoice(label, null, null, Ignore: true)
                    : new MappingChoice(label, entry.TableName, entry.ColumnName, Ignore: false);

                (row == null ? plan.Adds : plan.Changes).Add(choice);
            }

            return plan;
        }
    }
}
