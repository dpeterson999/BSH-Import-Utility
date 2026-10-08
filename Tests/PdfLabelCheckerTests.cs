using BSH_Import_Utility.Domain;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace BSH_Import_Utility.Tests
{
    public class PdfLabelCheckerTests
    {
        private static readonly Dictionary<string, (string TableName, string ColumnName)> Map =
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["Order Number"] = ("BSH", "Order Number"),
                ["Chili 14.4 oz"] = ("CannedMeats", "Chili"),
                ["Tuna 5 oz"] = ("CannedMeats", "Tuna"),
                ["Old soap"] = ("HouseholdProducts", "Bar soap"),
            };

        private static readonly Dictionary<string, List<string>> Schema = new()
        {
            ["BSH"] = new() { "Order Number", "Recipient" },
            ["CannedMeats"] = new() { "Order Number", "Chili", "Tuna" },
            ["HouseholdProducts"] = new() { "Order Number" },
        };

        private static readonly HashSet<string> UserIgnored = new(StringComparer.OrdinalIgnoreCase) { "Page footer" };

        [Fact]
        public void Check_ClassifiesLikeTheImport()
        {
            var lines = new List<ProcessedLine>
            {
                new("Order Number", "123456"),
                new("Chili 14.4 oz", 2),
                new("Tuna 5 oz", 0),                 // not ordered
                new("Old soap", 1),                  // mapped to a column the database lacks
                new("Purell 32 oz", 3),              // not set up
                new("Some heading", 0),              // stray text, no value
                new("Address", "1 Main St"),         // built-in ignore
                new(" Page footer ", 7),             // user ignore, trimmed
                new("", 5),                          // blank labels are skipped
            };

            var checks = PdfLabelChecker.Check(lines, Map, UserIgnored, Schema);
            var byLabel = checks.ToDictionary(c => c.PdfLabel);

            Assert.Equal(8, checks.Count);
            Assert.True(byLabel["Order Number"].WouldImport);
            Assert.True(byLabel["Chili 14.4 oz"].WouldImport);
            Assert.Equal(("CannedMeats", "Chili"), (byLabel["Chili 14.4 oz"].TableName, byLabel["Chili 14.4 oz"].ColumnName));

            Assert.False(byLabel["Tuna 5 oz"].HasValue);
            Assert.False(byLabel["Tuna 5 oz"].WouldImport);
            Assert.False(byLabel["Tuna 5 oz"].BlocksImport);

            Assert.Equal(LabelStatus.MissingColumn, byLabel["Old soap"].Status);
            Assert.True(byLabel["Old soap"].BlocksImport);

            Assert.Equal(LabelStatus.Unmapped, byLabel["Purell 32 oz"].Status);
            Assert.True(byLabel["Purell 32 oz"].BlocksImport);

            Assert.False(byLabel["Some heading"].BlocksImport);

            Assert.Equal(LabelStatus.Ignored, byLabel["Address"].Status);
            Assert.True(byLabel["Address"].IsBuiltInIgnore);
            Assert.Equal(LabelStatus.Ignored, byLabel["Page footer"].Status);
            Assert.False(byLabel["Page footer"].IsBuiltInIgnore);
            Assert.False(byLabel["Page footer"].BlocksImport);
        }

        [Fact]
        public void Check_ListsRepeatedLabelsOncePreferringOneWithAValue()
        {
            var lines = new List<ProcessedLine>
            {
                new("Chili 14.4 oz", 0),
                new("chili 14.4 OZ", 4),
                new("Chili 14.4 oz", 9),
            };

            var check = Assert.Single(PdfLabelChecker.Check(lines, Map, UserIgnored, Schema));
            Assert.Equal(4, check.Value);
        }

        [Fact]
        public void FindOrderNumber_UsesTheOrderNumberLine()
        {
            Assert.Equal("123456", PdfLabelChecker.FindOrderNumber(new[] { new ProcessedLine("Recipient", "A"), new ProcessedLine("Order Number", " 123456 ") }));
            Assert.Equal("", PdfLabelChecker.FindOrderNumber(new[] { new ProcessedLine("Recipient", "A") }));
        }
    }
}
