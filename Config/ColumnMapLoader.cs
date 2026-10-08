using BSH_Import_Utility.Domain;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace BSH_Import_Utility.Config
{
    public static class ColumnMapLoader
    {
        /// <summary>
        /// ColumnToTableMap.json next to the executable, independent of the working directory.
        /// </summary>
        public static string DefaultPath =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ColumnToTableMap.json");

        /// <summary>
        /// Reads the JSON mapping as a flat list. Duplicate labels are kept so the caller
        /// can report them rather than fail.
        /// </summary>
        public static List<ColumnMapEntry> LoadEntries(string jsonFilePath)
        {
            if (!File.Exists(jsonFilePath))
                throw new FileNotFoundException("Config file not found.", jsonFilePath);

            string jsonContent = File.ReadAllText(jsonFilePath);

            var result = JsonConvert.DeserializeObject<Dictionary<string, Dictionary<string, string>>>(jsonContent);

            if (result == null)
                throw new InvalidDataException($"Failed to parse {Path.GetFileName(jsonFilePath)}.");

            var entries = new List<ColumnMapEntry>();

            foreach (var category in result)
            {
                foreach (var item in category.Value)
                {
                    entries.Add(new ColumnMapEntry(item.Key, category.Key, item.Value));
                }
            }

            return entries;
        }

        /// <summary>
        /// Writes active rows in the same shape as ColumnToTableMap.json (table → label → column),
        /// BSH first, then tables and labels alphabetically. Ignored labels go under
        /// <see cref="ImportConstants.IgnoredJsonKey"/>. Inactive rows are left out.
        /// </summary>
        public static void Save(string jsonFilePath, IEnumerable<ColumnMapRow> rows)
        {
            var active = rows.Where(r => r.IsActive).ToList();

            var tables = active
                .Where(r => !r.IsIgnored && r.TableName != null && r.ColumnName != null)
                .GroupBy(r => r.TableName!, StringComparer.OrdinalIgnoreCase)
                .OrderBy(g => g.Key.Equals(ImportConstants.BshTableName, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase);

            var output = new Dictionary<string, Dictionary<string, string>>();

            foreach (var table in tables)
            {
                output[table.Key] = table
                    .OrderBy(r => r.PdfLabel, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(r => r.PdfLabel, r => r.ColumnName!);
            }

            var ignored = active.Where(r => r.IsIgnored).OrderBy(r => r.PdfLabel, StringComparer.OrdinalIgnoreCase).ToList();
            if (ignored.Count > 0)
                output[ImportConstants.IgnoredJsonKey] = ignored.ToDictionary(r => r.PdfLabel, _ => "");

            File.WriteAllText(jsonFilePath, JsonConvert.SerializeObject(output, Formatting.Indented));
        }
    }
}
