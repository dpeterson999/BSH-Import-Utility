namespace BSH_Import_Utility.Domain
{
    /// <summary>
    /// One line read from an order PDF. ColumnName is the PDF label (for example
    /// "Chili 14.4 oz"), not a database column; the mapping decides where it goes.
    /// ColumnValue is an int quantity for items (0 when nothing was ordered) or a string
    /// for header fields such as Recipient.
    /// </summary>
    public class ProcessedLine
    {
        public string ColumnName { get; }
        public object ColumnValue { get; }

        public ProcessedLine(string columnName, object columnValue)
        {
            ColumnName = columnName;
            ColumnValue = columnValue;
        }
    }
}