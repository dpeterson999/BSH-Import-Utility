using BSH_Import_Utility.Domain;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace BSH_Import_Utility.Tests
{
    public class MappingImportPlannerTests
    {
        private static readonly DateTime When = new(2026, 1, 1);

        private static SortedDictionary<string, List<string>> Targets() =>
            MappingRules.GetSelectableTargets(new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["BSH"] = new() { "Order Number", "Recipient", "Ward", "Storehouse | Pickup location" },
                ["CannedMeats"] = new() { "Order Number", "Chili", "Tuna" },
                ["HouseholdProducts"] = new() { "Order Number", "Lysol spray cleaner" },
            });

        private static List<ColumnMapRow> Existing() => new()
        {
            new(1, "Order Number", "BSH", "Order Number", true, false, When, When, null),
            new(2, "Chili 14.4 oz", "CannedMeats", "Chili", true, false, When, When, null),
            new(3, "Tuna 5 oz", "CannedMeats", "Tuna", false, false, When, When, null),
            new(4, "Stray text", null, null, true, true, When, When, null),
        };

        [Fact]
        public void Plan_SortsEntriesIntoAddChangeUnchangedAndProblems()
        {
            var entries = new List<ColumnMapEntry>
            {
                new("Order Number", "BSH", "Order Number"),                  // unchanged (required, same)
                new("Chili 14.4 oz", "CannedMeats", "Chili"),                // unchanged
                new("Tuna 5 oz", "CannedMeats", "Tuna"),                     // change: reactivates
                new("Stray text", ImportConstants.IgnoredJsonKey, ""),       // unchanged (ignored)
                new("Purell Spray Disinfectant 32 oz", "HouseholdProducts", "Lysol spray cleaner"), // add
                new("Footer", ImportConstants.IgnoredJsonKey, ""),           // add (ignored)
                new("Ward", "CannedMeats", "Chili"),                         // problem: required field
                new("Beans 15 oz", "CannedMeats", "Beans"),                  // problem: no such column
                new("chili 14.4 OZ", "CannedMeats", "Tuna"),                 // problem: repeated label
            };

            var plan = MappingImportPlanner.Plan(entries, Existing(), Targets());

            Assert.Equal(new[] { "Purell Spray Disinfectant 32 oz", "Footer" }, plan.Adds.Select(a => a.PdfLabel));
            Assert.True(plan.Adds.Single(a => a.PdfLabel == "Footer").Ignore);
            Assert.Equal(new[] { "Tuna 5 oz" }, plan.Changes.Select(c => c.PdfLabel));
            Assert.Equal(3, plan.Unchanged);
            Assert.Equal(3, plan.Problems.Count);
            Assert.True(plan.HasWork);
        }

        [Fact]
        public void Plan_ChangesTargetAndIgnoreState()
        {
            var entries = new List<ColumnMapEntry>
            {
                new("Chili 14.4 oz", "CannedMeats", "Tuna"),
                new("Stray text", "CannedMeats", "Chili"),
            };

            var plan = MappingImportPlanner.Plan(entries, Existing(), Targets());

            Assert.Empty(plan.Adds);
            Assert.Equal(2, plan.Changes.Count);
            Assert.All(plan.Changes, c => Assert.False(c.Ignore));
        }

        [Fact]
        public void Plan_WithNothingNewHasNoWork()
        {
            var plan = MappingImportPlanner.Plan(
                new List<ColumnMapEntry> { new("Chili 14.4 oz", "CannedMeats", "Chili") }, Existing(), Targets());

            Assert.False(plan.HasWork);
            Assert.Equal(1, plan.Unchanged);
        }

        [Fact]
        public void Plan_HonorsActiveStateFromFile()
        {
            var entries = new List<ColumnMapEntry>
            {
                new("Chili 14.4 oz", "CannedMeats", "Chili", IsActive: false),   // change: deactivates
                new("Tuna 5 oz", "CannedMeats", "Tuna", IsActive: false),        // unchanged (already inactive)
                new("Stray text", ImportConstants.IgnoredJsonKey, "", IsActive: false), // change: deactivates ignore
                new("Old item", "CannedMeats", "Gone column", IsActive: false),   // add: inactive may point at a missing column
                new("Ward", "BSH", "Ward", IsActive: false),                      // problem: required field
            };

            var plan = MappingImportPlanner.Plan(entries, Existing(), Targets());

            Assert.Equal(new[] { "Old item" }, plan.Adds.Select(a => a.PdfLabel));
            Assert.Equal(new[] { "Chili 14.4 oz", "Stray text" }, plan.Changes.Select(c => c.PdfLabel));
            Assert.All(plan.Adds.Concat(plan.Changes), c => Assert.False(c.IsActive));
            Assert.True(plan.Changes.Single(c => c.PdfLabel == "Stray text").Ignore);
            Assert.Equal(1, plan.Unchanged);
            Assert.Single(plan.Problems);
        }

        [Fact]
        public void Plan_SetsNotesOnlyWhenFileHasThem()
        {
            var existing = Existing();
            existing[1] = existing[1] with { Notes = "keep me" };

            var withoutNotes = MappingImportPlanner.Plan(
                new List<ColumnMapEntry> { new("Chili 14.4 oz", "CannedMeats", "Chili") }, existing, Targets());
            Assert.False(withoutNotes.HasWork);

            var sameNotes = MappingImportPlanner.Plan(
                new List<ColumnMapEntry> { new("Chili 14.4 oz", "CannedMeats", "Chili", Notes: " keep me ") }, existing, Targets());
            Assert.False(sameNotes.HasWork);

            var cleared = MappingImportPlanner.Plan(
                new List<ColumnMapEntry> { new("Chili 14.4 oz", "CannedMeats", "Chili", Notes: "") }, existing, Targets());
            Assert.Equal("", Assert.Single(cleared.Changes).Notes);

            var changed = MappingImportPlanner.Plan(
                new List<ColumnMapEntry> { new("Chili 14.4 oz", "CannedMeats", "Chili", Notes: "new note") }, existing, Targets());
            Assert.Equal("new note", Assert.Single(changed.Changes).Notes);
        }
    }
}
