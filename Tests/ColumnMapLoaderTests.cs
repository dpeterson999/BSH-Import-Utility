using BSH_Import_Utility.Config;
using BSH_Import_Utility.Domain;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace BSH_Import_Utility.Tests
{
    public class ColumnMapLoaderTests
    {
        private static readonly DateTime When = new(2026, 1, 1);

        [Fact]
        public void Save_ThenLoad_KeepsInactiveIgnoredAndNotes()
        {
            var rows = new List<ColumnMapRow>
            {
                new(1, "Order Number", "BSH", "Order Number", true, false, When, When, null),
                new(2, "Chili 14.4 oz", "CannedMeats", "Chili", true, false, When, When, "Was \"Chili con carne\""),
                new(3, "Stray text", null, null, true, true, When, When, null),
                new(4, "TEST", "Sugars", "Powdered sugar", false, false, When, When, "TEST"),
                new(5, "Old footer", null, null, false, true, When, When, null),
            };

            string path = Path.Combine(Path.GetTempPath(), $"ColumnMapLoaderTests-{Guid.NewGuid():N}.json");
            try
            {
                ColumnMapLoader.Save(path, rows);
                var entries = ColumnMapLoader.LoadEntries(path);

                Assert.Equal(5, entries.Count);
                Assert.Contains(new ColumnMapEntry("Chili 14.4 oz", "CannedMeats", "Chili", true, "Was \"Chili con carne\""), entries);
                Assert.Contains(new ColumnMapEntry("Stray text", ImportConstants.IgnoredJsonKey, "", true, ""), entries);
                Assert.Contains(new ColumnMapEntry("TEST", "Sugars", "Powdered sugar", false, "TEST"), entries);
                Assert.Contains(new ColumnMapEntry("Old footer", ImportConstants.IgnoredJsonKey, "", false, ""), entries);

                // Re-importing the export of the same rows changes nothing.
                var targets = MappingRules.GetSelectableTargets(new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
                {
                    ["BSH"] = new() { "Order Number" },
                    ["CannedMeats"] = new() { "Order Number", "Chili" },
                    ["Sugars"] = new() { "Order Number", "Powdered sugar" },
                });
                var plan = MappingImportPlanner.Plan(entries, rows, targets);
                Assert.False(plan.HasWork);
                Assert.Equal(5, plan.Unchanged);
                Assert.Empty(plan.Problems);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void LoadEntries_ReadsOriginalSeedFileAsActive()
        {
            var entries = ColumnMapLoader.LoadEntries(Path.Combine(AppContext.BaseDirectory, "ColumnToTableMap.json"));

            Assert.NotEmpty(entries);
            Assert.All(entries, e => Assert.True(e.IsActive));
            Assert.All(entries, e => Assert.Null(e.Notes));
        }
    }
}
