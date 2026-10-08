using BSH_Import_Utility.Domain;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.OleDb;

#nullable enable

namespace BSH_Import_Utility.Data
{
    /// <summary>
    /// Reads and writes the ColumnMap table, which holds the PDF label → table/column
    /// pairings inside the Access database itself so they can be edited without a rebuild.
    /// </summary>
    public class ColumnMapRepository
    {
        private readonly string _connectionString;

        public ColumnMapRepository(string connectionString)
        {
            _connectionString = connectionString;
        }

        /// <summary>
        /// Creates the ColumnMap table and its unique PdfLabel index if the table does not exist.
        /// Access compares text case-insensitively, so the unique index also rejects labels that
        /// differ only by case.
        /// </summary>
        public void EnsureTable()
        {
            using var connection = new OleDbConnection(_connectionString);
            connection.Open();

            var existing = connection.GetOleDbSchemaTable(
                OleDbSchemaGuid.Tables,
                new object?[] { null, null, ImportConstants.ColumnMapTableName, "TABLE" });

            if (existing != null && existing.Rows.Count > 0)
                return;

            using (var create = new OleDbCommand(
                       $"CREATE TABLE [{ImportConstants.ColumnMapTableName}] (" +
                       "[Id] COUNTER CONSTRAINT [PK_ColumnMap] PRIMARY KEY, " +
                       "[PdfLabel] TEXT(255) NOT NULL, " +
                       "[TableName] TEXT(64), " +
                       "[ColumnName] TEXT(64), " +
                       "[IsActive] YESNO NOT NULL, " +
                       "[IsIgnored] YESNO NOT NULL, " +
                       "[CreatedOn] DATETIME NOT NULL, " +
                       "[ChangedOn] DATETIME NOT NULL, " +
                       "[Notes] TEXT(255))",
                       connection))
            {
                create.ExecuteNonQuery();
            }

            using (var index = new OleDbCommand(
                       $"CREATE UNIQUE INDEX [UX_ColumnMap_PdfLabel] ON [{ImportConstants.ColumnMapTableName}] ([PdfLabel])",
                       connection))
            {
                index.ExecuteNonQuery();
            }
        }

        public bool IsEmpty()
        {
            using var connection = new OleDbConnection(_connectionString);
            connection.Open();

            using var command = new OleDbCommand(
                $"SELECT COUNT(*) FROM [{ImportConstants.ColumnMapTableName}]", connection);

            return Convert.ToInt32(command.ExecuteScalar()) == 0;
        }

        /// <summary>
        /// Active, non-ignored mappings in the shape AccessRepository expects.
        /// </summary>
        public Dictionary<string, (string TableName, string ColumnName)> LoadActiveMap()
        {
            var map = new Dictionary<string, (string TableName, string ColumnName)>(StringComparer.OrdinalIgnoreCase);

            using var connection = new OleDbConnection(_connectionString);
            connection.Open();

            using var command = new OleDbCommand(
                $"SELECT [PdfLabel], [TableName], [ColumnName] FROM [{ImportConstants.ColumnMapTableName}] " +
                "WHERE [IsActive] = True AND [IsIgnored] = False",
                connection);

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                if (reader.IsDBNull(1) || reader.IsDBNull(2))
                    continue;

                map[reader.GetString(0)] = (reader.GetString(1), reader.GetString(2));
            }

            return map;
        }

        /// <summary>
        /// Labels the user has chosen to ignore. These are in addition to
        /// <see cref="ImportConstants.IgnoredColumns"/>, which stay in code.
        /// </summary>
        public HashSet<string> LoadIgnoredLabels()
        {
            var labels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            using var connection = new OleDbConnection(_connectionString);
            connection.Open();

            using var command = new OleDbCommand(
                $"SELECT [PdfLabel] FROM [{ImportConstants.ColumnMapTableName}] " +
                "WHERE [IsActive] = True AND [IsIgnored] = True",
                connection);

            using var reader = command.ExecuteReader();
            while (reader.Read())
                labels.Add(reader.GetString(0));

            return labels;
        }

        /// <summary>
        /// Inserts the given entries in one transaction. Labels are trimmed; a label that
        /// repeats (ignoring case) is skipped and returned instead of failing the whole seed.
        /// </summary>
        public SeedResult Seed(IEnumerable<ColumnMapEntry> entries)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var duplicates = new List<ColumnMapEntry>();
            int inserted = 0;
            var now = DateTime.Now;

            using var connection = new OleDbConnection(_connectionString);
            connection.Open();

            using var transaction = connection.BeginTransaction();
            try
            {
                foreach (var entry in entries)
                {
                    string label = entry.PdfLabel.Trim();

                    if (label.Length == 0)
                        continue;

                    if (!seen.Add(label))
                    {
                        duplicates.Add(entry);
                        continue;
                    }

                    using var command = new OleDbCommand(
                        $"INSERT INTO [{ImportConstants.ColumnMapTableName}] " +
                        "([PdfLabel], [TableName], [ColumnName], [IsActive], [IsIgnored], [CreatedOn], [ChangedOn]) " +
                        "VALUES (?, ?, ?, ?, ?, ?, ?)",
                        connection,
                        transaction);

                    command.Parameters.Add("?", OleDbType.VarWChar).Value = label;
                    command.Parameters.Add("?", OleDbType.VarWChar).Value = entry.TableName;
                    command.Parameters.Add("?", OleDbType.VarWChar).Value = entry.ColumnName;
                    command.Parameters.Add("?", OleDbType.Boolean).Value = true;
                    command.Parameters.Add("?", OleDbType.Boolean).Value = false;
                    command.Parameters.Add("?", OleDbType.Date).Value = now;
                    command.Parameters.Add("?", OleDbType.Date).Value = now;

                    command.ExecuteNonQuery();
                    inserted++;
                }

                transaction.Commit();
            }
            catch
            {
                transaction.Rollback();
                throw;
            }

            return new SeedResult(inserted, duplicates);
        }
    }

    public record SeedResult(int Inserted, List<ColumnMapEntry> Duplicates);
}
