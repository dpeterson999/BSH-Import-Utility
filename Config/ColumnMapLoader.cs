using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;

namespace BSH_Import_Utility.Config
{
    public static class ColumnMapLoader
    {
        public static Dictionary<string, (string TableName, string ColumnName)> Load(string jsonFilePath)
        {
            if (!File.Exists(jsonFilePath))
                throw new FileNotFoundException("Config file not found.", jsonFilePath);

            string jsonContent = File.ReadAllText(jsonFilePath);

            var result = JsonConvert.DeserializeObject<Dictionary<string, Dictionary<string, string>>>(jsonContent);

            if (result == null)
                throw new InvalidDataException("Failed to parse columnToTableMap.json.");

            var columnToTableMap = new Dictionary<string, (string, string)>(StringComparer.OrdinalIgnoreCase);

            foreach (var category in result)
            {
                foreach (var item in category.Value)
                {
                    columnToTableMap.Add(item.Key, (category.Key, item.Value));
                }
            }

            return columnToTableMap;
        }
    }
}