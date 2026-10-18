using BSH_Import_Utility.Domain;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
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
        /// can report them rather than fail. Entries under <see cref="ImportConstants.InactiveJsonKey"/>
        /// come back with IsActive = false. When the file has a <see cref="ImportConstants.NotesJsonKey"/>
        /// section, every entry gets its note from it ("" for none); otherwise Notes is null.
        /// </summary>
        public static List<ColumnMapEntry> LoadEntries(string jsonFilePath)
        {
            if (!File.Exists(jsonFilePath))
                throw new FileNotFoundException("Config file not found.", jsonFilePath);

            string jsonContent = File.ReadAllText(jsonFilePath);

            var result = JsonConvert.DeserializeObject<JObject>(jsonContent);

            if (result == null)
                throw new InvalidDataException($"Failed to parse {Path.GetFileName(jsonFilePath)}.");

            var entries = new List<ColumnMapEntry>();
            Dictionary<string, string>? notes = null;

            foreach (var category in result.Properties())
            {
                if (category.Name.Equals(ImportConstants.NotesJsonKey, StringComparison.OrdinalIgnoreCase))
                {
                    notes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var note in AsObject(category).Properties())
                        notes[note.Name.Trim()] = note.Value.ToString();
                }
                else if (category.Name.Equals(ImportConstants.InactiveJsonKey, StringComparison.OrdinalIgnoreCase))
                {
                    foreach (var inactiveCategory in AsObject(category).Properties())
                        AddEntries(entries, inactiveCategory, isActive: false);
                }
                else
                {
                    AddEntries(entries, category, isActive: true);
                }
            }

            if (notes == null)
                return entries;

            return entries
                .Select(e => e with { Notes = notes.TryGetValue(e.PdfLabel.Trim(), out var note) ? note : "" })
                .ToList();
        }

        private static void AddEntries(List<ColumnMapEntry> entries, JProperty category, bool isActive)
        {
            foreach (var item in AsObject(category).Properties())
            {
                entries.Add(new ColumnMapEntry(item.Name, category.Name, item.Value.ToString(), isActive));
            }
        }

        private static JObject AsObject(JProperty category) =>
            category.Value as JObject
            ?? throw new InvalidDataException($"\"{category.Name}\" should contain a list of labels.");

        /// <summary>
        /// Writes every row in the same shape as ColumnToTableMap.json (table → label → column),
        /// BSH first, then tables and labels alphabetically. Ignored labels go under
        /// <see cref="ImportConstants.IgnoredJsonKey"/>. Inactive rows go under
        /// <see cref="ImportConstants.InactiveJsonKey"/>, laid out the same way. Notes go under
        /// <see cref="ImportConstants.NotesJsonKey"/>, which is always written so that importing
        /// the file also clears notes that were removed.
        /// </summary>
        public static void Save(string jsonFilePath, IEnumerable<ColumnMapRow> rows)
        {
            var all = rows.ToList();
            var output = BuildSection(all.Where(r => r.IsActive));

            var inactive = BuildSection(all.Where(r => !r.IsActive));
            if (inactive.Count > 0)
                output[ImportConstants.InactiveJsonKey] = inactive;

            output[ImportConstants.NotesJsonKey] = new JObject(all
                .Where(r => !string.IsNullOrWhiteSpace(r.Notes))
                .OrderBy(r => r.PdfLabel, StringComparer.OrdinalIgnoreCase)
                .Select(r => new JProperty(r.PdfLabel, r.Notes!.Trim())));

            File.WriteAllText(jsonFilePath, output.ToString(Formatting.Indented));
        }

        private static JObject BuildSection(IEnumerable<ColumnMapRow> rows)
        {
            var list = rows.ToList();

            var tables = list
                .Where(r => !r.IsIgnored && r.TableName != null && r.ColumnName != null)
                .GroupBy(r => r.TableName!, StringComparer.OrdinalIgnoreCase)
                .OrderBy(g => g.Key.Equals(ImportConstants.BshTableName, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase);

            var section = new JObject();

            foreach (var table in tables)
            {
                section[table.Key] = new JObject(table
                    .OrderBy(r => r.PdfLabel, StringComparer.OrdinalIgnoreCase)
                    .Select(r => new JProperty(r.PdfLabel, r.ColumnName!)));
            }

            var ignored = list.Where(r => r.IsIgnored).OrderBy(r => r.PdfLabel, StringComparer.OrdinalIgnoreCase).ToList();
            if (ignored.Count > 0)
                section[ImportConstants.IgnoredJsonKey] = new JObject(ignored.Select(r => new JProperty(r.PdfLabel, "")));

            return section;
        }
    }
}
