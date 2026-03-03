namespace BSH_Import_Utility.Domain
{
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