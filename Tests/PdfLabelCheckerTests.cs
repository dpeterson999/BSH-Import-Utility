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

        [Fact]
        public void Check_FlagsUnorderedFormItemsThatAreNotSetUp_ButNotPageText()
        {
            // Shaped like a real order form: header, item list across a page break, sign-off area.
            var lines = new List<ProcessedLine>
            {
                new("Bishop's Order for Food and Supplies", 0),
                new("Order Number", "123456"),
                new("2572 Stone Rd Ann Arbor MI 48105", 0),  // header text, before the item list
                new("Qty", 0), new("Ordered", 0), new("Qty", 0), new("Filled", 0),
                new("CANNED MEATS", 0),
                new("Chili 14.4 oz", 2),
                new("Tuna 5 oz", 0),                         // mapped, not ordered
                new("Old soap", 0),                          // mapped to a missing column, not ordered
                new("Printed on 24 Feb 2026 c 2026 by Intellectual Reserve, Inc. All rights reserved. Page 1 of 2", 0),
                new("Bishop’s Order for Food and Supplies", 0),
                new("Order #123456", 0),
                new("Qty", 0), new("Ordered", 0), new("Qty", 0), new("Filled", 0),
                new("BREADS", 0),
                new("(continued)", 0),
                new("Tortillas (flour) pkg of 12", 0),       // new on the form, not set up
                new("FOR INFORMATION ONLY", 0),
                new("Initiated", 0),
                new("19 Feb 2026", 0),
                new("Linzey, Morgan Mary", 0),
            };

            var byLabel = PdfLabelChecker.Check(lines, Map, UserIgnored, Schema).ToDictionary(c => c.PdfLabel);

            var tortillas = byLabel["Tortillas (flour) pkg of 12"];
            Assert.True(tortillas.IsFormItem);
            Assert.True(tortillas.NotSetUpNotOrdered);
            Assert.True(tortillas.NeedsSetUp);
            Assert.False(tortillas.BlocksImport);
            Assert.Equal("BREADS", tortillas.Section);
            Assert.Equal("CANNED MEATS", byLabel["Chili 14.4 oz"].Section);
            Assert.Null(byLabel["2572 Stone Rd Ann Arbor MI 48105"].Section);
            Assert.Null(byLabel["Linzey, Morgan Mary"].Section);

            var sections = PdfLabelChecker.FindSections(lines);
            Assert.Equal("BREADS", sections["tortillas (FLOUR) pkg of 12"]);
            Assert.False(sections.ContainsKey("(continued)"));
            Assert.False(sections.ContainsKey("19 Feb 2026"));

            Assert.True(byLabel["Old soap"].NotSetUpNotOrdered);
            Assert.False(byLabel["Tuna 5 oz"].NeedsSetUp);
            Assert.False(byLabel["Chili 14.4 oz"].NotSetUpNotOrdered);

            var flagged = byLabel.Values.Where(c => c.NotSetUpNotOrdered).Select(c => c.PdfLabel).OrderBy(l => l);
            Assert.Equal(new[] { "Old soap", "Tortillas (flour) pkg of 12" }, flagged);

            var order = new OrderCheck("order.pdf", "123456", false, byLabel.Values.ToList());
            Assert.Equal(0, order.BlockingCount);
            Assert.Equal(2, order.NotSetUpNotOrderedCount);
        }

        [Fact]
        public void Check_UserIgnoredFormItemIsNotFlagged()
        {
            var lines = new List<ProcessedLine>
            {
                new("Filled", 0),
                new("Page footer", 0),
                new("FOR INFORMATION ONLY", 0),
            };

            var check = PdfLabelChecker.Check(lines, Map, UserIgnored, Schema).Single(c => c.PdfLabel == "Page footer");
            Assert.True(check.IsFormItem);
            Assert.False(check.NeedsSetUp);
        }

        [Fact]
        public void Check_RepeatedLabelKeepsFormItemFromAnyOccurrence()
        {
            var lines = new List<ProcessedLine>
            {
                new("Tortillas (flour) pkg of 12", 0),       // outside the item list
                new("Filled", 0),
                new("Tortillas (flour) pkg of 12", 0),
            };

            var check = Assert.Single(PdfLabelChecker.Check(lines, Map, UserIgnored, Schema), c => c.PdfLabel.StartsWith("Tortillas"));
            Assert.True(check.NotSetUpNotOrdered);
        }
    }
}
