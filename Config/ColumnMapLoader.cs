using BSH_Import_Utility.Domain;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;

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
    }
}
