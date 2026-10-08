using BSH_Import_Utility.Domain;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace BSH_Import_Utility.Tests
{
    public class MappingRulesTests
    {
        private static Dictionary<string, List<string>> Schema() => new(StringComparer.OrdinalIgnoreCase)
        {
            ["BSH"] = new() { "Order Number", "Recipient", "Ward", "Storehouse | Pickup location", "Delivered" },
            ["CannedSoups"] = new() { "Order Number", "Chicken rotini soup", "Tomato soup" },
            ["CannedMeats"] = new() { "Order Number", "Chili", "Tuna" },
            ["Seasonings"] = new() { "Order Number", "Salt" },
            ["RouteInfo"] = new() { "Route Letter", "StopName" },
            ["LoadingInfo"] = new() { "Order Number", "Bin" },
            ["ColumnMap"] = new() { "PdfLabel", "TableName", "ColumnName" },
            ["Copy of BSH"] = new() { "Order Number", "Recipient" },
            ["Paste Errors"] = new() { "Order Number", "Tuna" },
            ["MSysObjects"] = new() { "Order Number", "Name" },
            ["Bad[Table]"] = new() { "Order Number", "X" },
        };

        private static Dictionary<string, (string TableName, string ColumnName)> Map() => new(StringComparer.OrdinalIgnoreCase)
        {
            ["Chicken rotini soup 14.4 oz"] = ("CannedSoups", "Chicken rotini soup"),
            ["Chili 14.4 oz"] = ("CannedMeats", "Chili"),
            ["Salt (iodized) 26 oz"] = ("Seasonings", "Salt"),
        };

        [Fact]
        public void SelectableTargets_ExcludeNonItemTablesAndProtectedColumns()
        {
            var targets = MappingRules.GetSelectableTargets(Schema());

            Assert.Equal(new[] { "BSH", "CannedMeats", "CannedSoups", "Seasonings" }, targets.Keys);
            Assert.Equal(new[] { "Delivered" }, targets["BSH"]);
            Assert.DoesNotContain(targets.Values.SelectMany(c => c), c => c == "Order Number");
        }

        [Theory]
        [InlineData("Salt (iodized) 26 oz", "salt (iodized)")]
        [InlineData("Apples bag, abt 4 lb", "apples")]
        [InlineData("Hamburger buns pkg of 8", "hamburger buns")]
        [InlineData("Vanilla flavor 4 fl oz", "vanilla flavor")]
        [InlineData("Non-fat dry milk 27 servings", "non-fat dry milk")]
        [InlineData("2% milk gal", "2% milk")]
        [InlineData("Tampons, regular pkg of 32", "tampons, regular")]
        [InlineData("Great northern beans (can… 14.8 oz", "great northern beans (can...")]
        [InlineData("  Tuna   5 oz ", "tuna")]
        public void LabelStem_RemovesSizeAndPackaging(string label, string expected) =>
            Assert.Equal(expected, MappingRules.LabelStem(label));

        [Fact]
        public void Suggest_UsesExistingLabelWithSameStem()
        {
            var targets = MappingRules.GetSelectableTargets(Schema());
            Assert.Equal(("Seasonings", "Salt"), MappingRules.Suggest("Salt (iodized) 30 oz", Map(), targets));
        }

        [Fact]
        public void Suggest_FallsBackToColumnNamePrefix()
        {
            var targets = MappingRules.GetSelectableTargets(Schema());
            Assert.Equal(("CannedSoups", "Tomato soup"), MappingRules.Suggest("Tomato soup 10.75 oz", Map(), targets));
        }

        [Fact]
        public void Suggest_DoesNotMatchShortColumnThatIsOnlyTheFirstWord()
        {
            var schema = Schema();
            schema["MeatsOrderedByPKG"] = new() { "Order Number", "Chicken" };
            var targets = MappingRules.GetSelectableTargets(schema);
            Assert.Null(MappingRules.Suggest("Chicken noodle soup 10.5 oz", Map(), targets));
            Assert.Equal(("MeatsOrderedByPKG", "Chicken"), MappingRules.Suggest("Chicken 3 lb pkg", Map(), targets));
        }

        [Fact]
        public void Suggest_ReturnsNullWhenNothingMatches()
        {
            var targets = MappingRules.GetSelectableTargets(Schema());
            Assert.Null(MappingRules.Suggest("Peanut butter 18 oz", Map(), targets));
        }

        [Fact]
        public void Suggest_ReturnsNullWhenColumnNameIsInTwoTables()
        {
            var schema = Schema();
            schema["Drinks"] = new() { "Order Number", "Tuna" };
            var targets = MappingRules.GetSelectableTargets(schema);
            Assert.Null(MappingRules.Suggest("Tuna 5 oz", Map(), targets));
        }

        [Fact]
        public void Validate_RejectsStructuralExcludedUnknownAndBracketedNames()
        {
            var targets = MappingRules.GetSelectableTargets(Schema());

            Assert.Null(MappingRules.Validate("Tomato soup 10.75 oz", "CannedSoups", "Tomato soup", targets));
            Assert.NotNull(MappingRules.Validate("Ward", "BSH", "Delivered", targets));
            Assert.NotNull(MappingRules.Validate("Foo", "BSH", "Recipient", targets));
            Assert.NotNull(MappingRules.Validate("Foo", "RouteInfo", "StopName", targets));
            Assert.NotNull(MappingRules.Validate("Foo", "CannedSoups", "Chili", targets));
            Assert.NotNull(MappingRules.Validate("Foo", "CannedSoups", "Bad]Col", targets));
            Assert.NotNull(MappingRules.Validate("Foo", null, null, targets));
        }

        [Fact]
        public void CategoryWarnings_FlagSimilarItemInOtherTable()
        {
            var targets = MappingRules.GetSelectableTargets(Schema());

            Assert.NotEmpty(MappingRules.GetCategoryWarnings("Chili 15 oz", "CannedSoups", "Tomato soup", Map(), targets));
            Assert.Empty(MappingRules.GetCategoryWarnings("Chili 15 oz", "CannedMeats", "Chili", Map(), targets));
        }

        /// <summary>
        /// Replays every label in the real mapping as if it were new: a suggestion may be
        /// missing, but it must never point somewhere other than where the label really goes.
        /// </summary>
        [Fact]
        public void Suggest_IsNeverWrongForRealMappings()
        {
            var json = JsonConvert.DeserializeObject<Dictionary<string, Dictionary<string, string>>>(
                File.ReadAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ColumnToTableMap.json")))!;

            var schema = json.ToDictionary(
                t => t.Key,
                t => t.Value.Values.Append("Order Number").Distinct().ToList(),
                StringComparer.OrdinalIgnoreCase);
            var targets = MappingRules.GetSelectableTargets(schema);

            var map = json.SelectMany(t => t.Value.Select(kv => (Label: kv.Key, Target: (t.Key, kv.Value))))
                .ToDictionary(x => x.Label, x => x.Target, StringComparer.OrdinalIgnoreCase);

            var wrong = new List<string>();
            int suggested = 0;

            foreach (var entry in map.Where(kv => !MappingRules.IsStructuralLabel(kv.Key)))
            {
                var others = map.Where(kv => kv.Key != entry.Key).ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase);
                var guess = MappingRules.Suggest(entry.Key, others, targets);

                if (guess == null)
                    continue;

                suggested++;
                if (!string.Equals(guess.Value.TableName, entry.Value.Item1, StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(guess.Value.ColumnName, entry.Value.Item2, StringComparison.OrdinalIgnoreCase))
                {
                    wrong.Add($"{entry.Key}: suggested {guess.Value.TableName}.{guess.Value.ColumnName}, actual {entry.Value.Item1}.{entry.Value.Item2}");
                }
            }

            Assert.True(wrong.Count == 0, string.Join(Environment.NewLine, wrong));
            Assert.True(suggested > map.Count / 2, $"Only {suggested} of {map.Count} labels got a suggestion.");
        }
    }
}
