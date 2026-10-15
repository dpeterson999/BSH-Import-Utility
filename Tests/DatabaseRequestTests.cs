using BSH_Import_Utility.Domain;
using Xunit;

namespace BSH_Import_Utility.Tests
{
    public class DatabaseRequestTests
    {
        [Fact]
        public void Build_ListsEachItemWithItsSectionQuantityAndMissingColumn()
        {
            var text = DatabaseRequest.Build("123456", "BSH-DB01.accdb", new[]
            {
                new UnresolvedLabel("Tortillas (flour) pkg of 12", 2, null, null, "BREADS"),
                new UnresolvedLabel("Granola bars box", null, null, null),
                new UnresolvedLabel("Old soap", 0, "HouseholdProducts", "Bar soap", "HOUSEHOLD PRODUCTS"),
            });

            Assert.Contains("Order 123456 can't be imported until these items are added to the database (BSH-DB01.accdb):", text);
            Assert.Contains("- \"Tortillas (flour) pkg of 12\" (form section BREADS, quantity 2)", text);
            Assert.Contains("- \"Granola bars box\"\n", text.Replace("\r\n", "\n"));
            Assert.Contains("- \"Old soap\" (form section HOUSEHOLD PRODUCTS): set up to go in HouseholdProducts / Bar soap, which the database doesn't have", text);
            Assert.Contains("Number (Long Integer), Default Value 0", text);
            Assert.Contains("primary key, related to BSH.[Order Number]", text);
        }
    }
}
