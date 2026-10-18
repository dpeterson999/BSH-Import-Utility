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
        /// Every row, active or not, for the Mapping Manager.
        /// </summary>
        public List<ColumnMapRow> LoadAll()
        {
            var rows = new List<ColumnMapRow>();

            using var connection = new OleDbConnection(_connectionString);
            connection.Open();

            using var command = new OleDbCommand(
                "SELECT [Id], [PdfLabel], [TableName], [ColumnName], [IsActive], [IsIgnored], [CreatedOn], [ChangedOn], [Notes] " +
                $"FROM [{ImportConstants.ColumnMapTableName}]",
                connection);

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                rows.Add(new ColumnMapRow(
                    Convert.ToInt32(reader[0]),
                    reader.GetString(1),
                    reader.IsDBNull(2) ? null : reader.GetString(2),
                    reader.IsDBNull(3) ? null : reader.GetString(3),
                    reader.GetBoolean(4),
                    reader.GetBoolean(5),
                    reader.GetDateTime(6),
                    reader.GetDateTime(7),
                    reader.IsDBNull(8) ? null : reader.GetString(8)));
            }

            return rows;
        }

        /// <summary>
        /// Deactivates or reactivates one row. A deactivated label is treated as unmapped on import.
        /// </summary>
        public void SetActive(int id, bool isActive)
        {
            using var connection = new OleDbConnection(_connectionString);
            connection.Open();

            using var command = new OleDbCommand(
                $"UPDATE [{ImportConstants.ColumnMapTableName}] SET [IsActive] = ?, [ChangedOn] = ? WHERE [Id] = ?",
                connection);

            command.Parameters.Add("?", OleDbType.Boolean).Value = isActive;
            command.Parameters.Add("?", OleDbType.Date).Value = DateTime.Now;
            command.Parameters.Add("?", OleDbType.Integer).Value = id;
            command.ExecuteNonQuery();
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
                        "([PdfLabel], [TableName], [ColumnName], [IsActive], [IsIgnored], [CreatedOn], [ChangedOn], [Notes]) " +
                        "VALUES (?, ?, ?, ?, ?, ?, ?, ?)",
                        connection,
                        transaction);

                    // An exported file lists ignored labels under a special section.
                    bool ignore = entry.TableName.Equals(ImportConstants.IgnoredJsonKey, StringComparison.OrdinalIgnoreCase);

                    command.Parameters.Add("?", OleDbType.VarWChar).Value = label;
                    command.Parameters.Add("?", OleDbType.VarWChar).Value = ignore ? DBNull.Value : entry.TableName;
                    command.Parameters.Add("?", OleDbType.VarWChar).Value = ignore ? DBNull.Value : entry.ColumnName;
                    command.Parameters.Add("?", OleDbType.Boolean).Value = entry.IsActive;
                    command.Parameters.Add("?", OleDbType.Boolean).Value = ignore;
                    command.Parameters.Add("?", OleDbType.Date).Value = now;
                    command.Parameters.Add("?", OleDbType.Date).Value = now;
                    command.Parameters.Add("?", OleDbType.VarWChar).Value = NotesValue(entry.Notes);

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

        /// <summary>
        /// Saves the user's choices in one transaction. An existing row for the label (active or
        /// not) is updated and given the choice's active state, which is active unless a mapping
        /// file said otherwise; otherwise a new row is added. Ignored labels are stored with no
        /// table or column. Callers validate targets against the live schema first; bracket
        /// checks are repeated here because these names reach INSERT statements.
        /// </summary>
        public void SaveChoices(IEnumerable<MappingChoice> choices)
        {
            var now = DateTime.Now;

            using var connection = new OleDbConnection(_connectionString);
            connection.Open();

            using var transaction = connection.BeginTransaction();
            try
            {
                foreach (var choice in choices)
                {
                    string label = choice.PdfLabel.Trim();
                    object table = choice.Ignore ? DBNull.Value : (object?)choice.TableName ?? DBNull.Value;
                    object column = choice.Ignore ? DBNull.Value : (object?)choice.ColumnName ?? DBNull.Value;

                    if (label.Length == 0)
                        throw new ArgumentException("A mapping has an empty PDF label.");

                    if (!choice.Ignore && (!MappingRules.IsSafeName(choice.TableName) || !MappingRules.IsSafeName(choice.ColumnName)))
                        throw new ArgumentException($"Invalid table or column for \"{label}\".");

                    object notes = NotesValue(choice.Notes);

                    // Notes is only updated when the choice carries notes. Parameters are
                    // positional, so they are added below in exactly the order of the SQL.
                    int updated;
                    using (var update = new OleDbCommand(
                               $"UPDATE [{ImportConstants.ColumnMapTableName}] " +
                               "SET [TableName] = ?, [ColumnName] = ?, [IsActive] = ?, [IsIgnored] = ?, [ChangedOn] = ?" +
                               (choice.Notes != null ? ", [Notes] = ?" : "") +
                               " WHERE [PdfLabel] = ?",
                               connection,
                               transaction))
                    {
                        update.Parameters.Add("?", OleDbType.VarWChar).Value = table;
                        update.Parameters.Add("?", OleDbType.VarWChar).Value = column;
                        update.Parameters.Add("?", OleDbType.Boolean).Value = choice.IsActive;
                        update.Parameters.Add("?", OleDbType.Boolean).Value = choice.Ignore;
                        update.Parameters.Add("?", OleDbType.Date).Value = now;
                        if (choice.Notes != null)
                            update.Parameters.Add("?", OleDbType.VarWChar).Value = notes;
                        update.Parameters.Add("?", OleDbType.VarWChar).Value = label;
                        updated = update.ExecuteNonQuery();
                    }

                    if (updated > 0)
                        continue;

                    using var insert = new OleDbCommand(
                        $"INSERT INTO [{ImportConstants.ColumnMapTableName}] " +
                        "([PdfLabel], [TableName], [ColumnName], [IsActive], [IsIgnored], [CreatedOn], [ChangedOn], [Notes]) " +
                        "VALUES (?, ?, ?, ?, ?, ?, ?, ?)",
                        connection,
                        transaction);

                    insert.Parameters.Add("?", OleDbType.VarWChar).Value = label;
                    insert.Parameters.Add("?", OleDbType.VarWChar).Value = table;
                    insert.Parameters.Add("?", OleDbType.VarWChar).Value = column;
                    insert.Parameters.Add("?", OleDbType.Boolean).Value = choice.IsActive;
                    insert.Parameters.Add("?", OleDbType.Boolean).Value = choice.Ignore;
                    insert.Parameters.Add("?", OleDbType.Date).Value = now;
                    insert.Parameters.Add("?", OleDbType.Date).Value = now;
                    insert.Parameters.Add("?", OleDbType.VarWChar).Value = notes;
                    insert.ExecuteNonQuery();
                }

                transaction.Commit();
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }

        /// <summary>
        /// Notes as stored: trimmed and cut to the 255-character column, with blank stored as NULL
        /// (which also clears notes on update).
        /// </summary>
        private static object NotesValue(string? notes) =>
            string.IsNullOrWhiteSpace(notes) ? DBNull.Value : MappingRules.NormalizeNotes(notes);
    }

    public record SeedResult(int Inserted, List<ColumnMapEntry> Duplicates);
}
