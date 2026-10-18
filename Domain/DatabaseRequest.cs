using System.Collections.Generic;
using System.Text;

#nullable enable

namespace BSH_Import_Utility.Domain
{
    /// <summary>
    /// Builds the note a user sends to whoever manages the database when order items have no table
    /// or column to go in yet. The app never changes the database's tables or columns itself.
    /// </summary>
    public static class DatabaseRequest
    {
        public static string Build(string orderNumbers, string databaseFile, IEnumerable<UnresolvedLabel> items)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Order {orderNumbers} can't be imported until these items are added to the database ({databaseFile}):");
            sb.AppendLine();

            foreach (var item in items)
            {
                var details = new List<string>();
                if (!string.IsNullOrEmpty(item.Section))
                    details.Add($"form section {item.Section}");
                if (item.Value != null && !(item.Value is int number && number == 0))
                    details.Add($"quantity {item.Value}");

                sb.Append($"- \"{item.PdfLabel}\"");
                if (details.Count > 0)
                    sb.Append($" ({string.Join(", ", details)})");
                if (item.IsMappedToMissingColumn)
                    sb.Append($": set up to go in {item.MappedTable} / {item.MappedColumn}, which the database doesn't have");
                sb.AppendLine();
            }

            sb.AppendLine();
            sb.AppendLine("How to add them in Access:");
            sb.AppendLine("- An item in an existing section is a new column in that section's table: Number (Long Integer), Default Value 0.");
            sb.AppendLine("- A new section needs a new table: an \"Order Number\" Short Text field as the primary key, related to BSH.[Order Number], " +
                          "plus one column per item as above.");
            sb.AppendLine("- Also add new columns to any queries or reports that list that table's items.");
            sb.AppendLine();
            sb.Append("Then import the order again (or use Test a PDF). If asked, choose the new table and column for each item.");

            return sb.ToString();
        }
    }
}
