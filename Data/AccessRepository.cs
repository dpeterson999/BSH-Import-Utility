using ADOX;
using BSH_Import_Utility.Domain;
using BSH_Import_Utility.Infrastructure;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.OleDb;
using System.IO;
using System.Linq;

#nullable enable

namespace BSH_Import_Utility.Data
{
    public class AccessRepository
    {
        private readonly string _connectionString;
        private readonly Dictionary<string, (string TableName, string ColumnName)> _columnToTableMap;
        private Dictionary<string, List<string>>? _cachedTableColumns;

        public AccessRepository(string connectionString,
            Dictionary<string, (string TableName, string ColumnName)> columnToTableMap)
        {
            _connectionString = connectionString;
            _columnToTableMap = columnToTableMap;
        }

        public DataTable LoadBshGrid()
        {
            using (var con = new OleDbConnection(_connectionString))
            using (var cmd = new OleDbCommand(
                       "SELECT [Order Number],[Recipient],[Ward],[Storehouse | Pickup location] FROM [BSH]",
                       con))
            {
                var da = new OleDbDataAdapter(cmd);
                var ds = new DataSet();
                da.Fill(ds);
                return ds.Tables[0];
            }
        }

        public InsertOutcome InsertDataIntoDatabase(ProcessedLine[] processedLines, string fileName)
        {
            var tableColumns = _cachedTableColumns ??= GetTableColumns();
            var columnsByTable = new Dictionary<string, List<string>>();
            var valuesByTable = new Dictionary<string, List<object>>();
            var missingColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            using (var connection = new OleDbConnection(_connectionString))
            {
                connection.Open();

                using (var transaction = connection.BeginTransaction())
                {
                    string orderNumber = string.Empty;

                    try
                    {
                        foreach (var line in processedLines)
                        {
                            string columnName = line.ColumnName?.ToString() ?? string.Empty;
                            object columnValue = line.ColumnValue;

                            if (string.IsNullOrWhiteSpace(columnName) ||
                                ImportConstants.IgnoredColumns.Contains(columnName.Trim()))
                            {
                                // ImportLogger.Log($"Ignored column: {columnName}");
                                continue;
                            }

                            if (columnName.Equals("Order Number", StringComparison.OrdinalIgnoreCase))
                            {
                                orderNumber = columnValue?.ToString() ?? string.Empty;
                            }

                            if (columnValue is int intValue && intValue == 0) continue;

                            var match = FindBestMatchForColumn(columnName, tableColumns);

                            if (match == null)
                            {
                                if (!_columnToTableMap.ContainsKey(columnName))
                                {
                                    missingColumns.Add(columnName);
                                }
                                else
                                {
                                    var mapped = _columnToTableMap[columnName];
                                    missingColumns.Add($"{columnName} → {mapped.TableName}.{mapped.ColumnName} (missing in DB)");
                                }

                                continue;
                            }

                            string matchedTable = match.Value.TableName!;
                            string matchedColumn = match.Value.ColumnName!;

                            if (!columnsByTable.ContainsKey(matchedTable))
                            {
                                columnsByTable[matchedTable] = new List<string>();
                                valuesByTable[matchedTable] = new List<object>();
                            }

                            if (!columnsByTable[matchedTable].Contains(matchedColumn, StringComparer.OrdinalIgnoreCase))
                            {
                                columnsByTable[matchedTable].Add(matchedColumn);
                                valuesByTable[matchedTable].Add(columnValue ?? DBNull.Value);
                            }
                            else
                            {
                                ImportLogger.Log($"Order {orderNumber} — duplicate column skipped: {matchedTable}.{matchedColumn}");
                            }
                        }

                        // Wrong file? - No order number found
                        if (string.IsNullOrEmpty(orderNumber))
                        {
                            transaction.Rollback();
                            ImportLogger.Log($"Invalid file (no order number found): {Path.GetFileName(fileName)}");
                            return new InsertOutcome
                            {
                                Status = InsertStatus.InvalidFile,
                                OrderNumber = orderNumber,
                                FileName = fileName
                            };
                        }

                        // Duplicate check
                        if (DoesOrderNumberExist(orderNumber, connection, transaction))
                        {
                            transaction.Rollback();
                            ImportLogger.Log($"Skipped duplicate: Order {orderNumber}");
                            return new InsertOutcome
                            {
                                Status = InsertStatus.DuplicateOrderNumber,
                                OrderNumber = orderNumber
                            };
                        }

                        // Missing mappings/schema
                        if (missingColumns.Count > 0)
                        {
                            transaction.Rollback();
                            ImportLogger.Log($"Missing mappings for Order {orderNumber}: {string.Join(", ", missingColumns)}");
                            return new InsertOutcome
                            {
                                Status = InsertStatus.MissingColumnMappings,
                                OrderNumber = orderNumber,
                                MissingColumns = missingColumns.ToList()
                            };
                        }

                        InsertTables(columnsByTable, valuesByTable, orderNumber!, connection, transaction);

                        transaction.Commit();

                        return new InsertOutcome
                        {
                            Status = InsertStatus.Inserted,
                            OrderNumber = orderNumber
                        };
                    }
                    catch (Exception ex)
                    {
                        try { transaction.Rollback(); }
                        catch (InvalidOperationException rollbackEx)
                        {
                            ImportLogger.Log($"Rollback failed for Order {orderNumber}: {rollbackEx.Message}");
                        }

                        ImportLogger.Log($"Error inserting Order {orderNumber} from {Path.GetFileName(fileName)}: {ex.Message}");

                        return new InsertOutcome
                        {
                            Status = InsertStatus.Error,
                            OrderNumber = orderNumber,
                            FileName = fileName,
                            Exception = ex
                        };
                    }
                }
            }
        }

        private void InsertTables(
            Dictionary<string, List<string>> columnsByTable,
            Dictionary<string, List<object>> valuesByTable,
            string orderNumber,
            OleDbConnection connection,
            OleDbTransaction transaction)
        {
            // Insert BSH table first (other tables have a FK dependency on it),
            // then insert all remaining tables. OrderBy ensures BSH sorts before
            // everything else without modifying the dictionaries.
            foreach (var table in columnsByTable.Keys.OrderBy(t => t == "BSH" ? 0 : 1))
            {
                ExecuteInsertQuery(
                    table,
                    columnsByTable[table],
                    valuesByTable[table],
                    orderNumber,
                    connection,
                    transaction);
            }
        }

        private void ExecuteInsertQuery(
            string tableName,
            List<string> columns,
            List<object> values,
            string orderNumber,
            OleDbConnection connection,
            OleDbTransaction transaction)
        {
            if (!columns.Contains("Order Number", StringComparer.OrdinalIgnoreCase))
            {
                columns.Add("Order Number");
                values.Add(orderNumber);
            }

            string colList = string.Join(", ", columns.Select(c => $"[{c}]"));
            string paramList = string.Join(", ", columns.Select(_ => "?"));
            string query = $"INSERT INTO [{tableName}] ({colList}) VALUES ({paramList})";

            using var command = new OleDbCommand(query, connection, transaction);

            foreach (var val in values)
                command.Parameters.AddWithValue("?", val ?? DBNull.Value);

            command.ExecuteNonQuery();
        }

        private (string TableName, string ColumnName)? FindBestMatchForColumn(
            string columnName,
            Dictionary<string, List<string>> tableColumns)
        {
            if (!_columnToTableMap.TryGetValue(columnName.Trim(), out var mapped))
                return null;

            if (tableColumns.TryGetValue(mapped.TableName, out var cols) &&
                cols.Any(c => c.Equals(mapped.ColumnName, StringComparison.OrdinalIgnoreCase)))
            {
                return (mapped.TableName, mapped.ColumnName);
            }

            // Mapped in JSON but column is missing from the live DB schema
            return null;
        }

        private bool DoesOrderNumberExist(string orderNumber, OleDbConnection connection, OleDbTransaction transaction)
        {
            string query = "SELECT COUNT(*) FROM BSH WHERE [Order Number] = ?";
            using (var command = new OleDbCommand(query, connection, transaction))
            {
                command.Parameters.AddWithValue("?", orderNumber);
                return Convert.ToInt32(command.ExecuteScalar()) > 0;
            }
        }

        private Dictionary<string, List<string>> GetTableColumns()
        {
            var tableColumns = new Dictionary<string, List<string>>();

            var cat = new Catalog();
            cat.let_ActiveConnection(_connectionString);

            foreach (Table table in cat.Tables)
            {
                if (table.Type != "TABLE" || table.Name.StartsWith("MSys"))
                    continue;

                var columns = new List<string>();

                foreach (Column column in table.Columns)
                {
                    bool isAutoNumber = column.Properties["AutoIncrement"] is { Value: true };

                    if (!isAutoNumber)
                        columns.Add(column.Name);
                }

                tableColumns[table.Name] = columns;
            }

            return tableColumns;
        }
    }
}