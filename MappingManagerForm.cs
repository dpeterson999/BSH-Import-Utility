using BSH_Import_Utility.Config;
using BSH_Import_Utility.Data;
using BSH_Import_Utility.Domain;
using BSH_Import_Utility.Infrastructure;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

#nullable enable

namespace BSH_Import_Utility
{
    /// <summary>
    /// Lists every ColumnMap row with search and filters, and lets the user add, edit,
    /// deactivate/reactivate, and export/import mappings as JSON.
    /// </summary>
    public sealed class MappingManagerForm : Form
    {
        private const string StatusActive = "Active";
        private const string StatusRequired = "Required";
        private const string StatusIgnored = "Ignored";
        private const string StatusInactive = "Inactive";
        private const string StatusMissing = "Missing column";

        private const string FilterEverything = "Everything";
        private const string FilterActive = "Active mappings";
        private const string FilterMissing = "Missing column";
        private const string FilterIgnored = "Ignored labels";
        private const string FilterInactive = "Inactive";
        private const string FilterRecent = "Changed in the last 30 days";

        private readonly ColumnMapRepository _mapRepo;
        private readonly Dictionary<string, List<string>> _schema;
        private readonly SortedDictionary<string, List<string>> _targets;
        private List<ColumnMapRow> _rows = new();

        private readonly TextBox _search;
        private readonly ComboBox _filter;
        private readonly Label _count;
        private readonly DataGridView _grid;
        private readonly Button _edit;
        private readonly Button _toggleActive;

        /// <summary>True when anything was saved, so the caller reloads its mapping.</summary>
        public bool MappingsChanged { get; private set; }

        public MappingManagerForm(string connectionString, Dictionary<string, List<string>> schema)
        {
            _mapRepo = new ColumnMapRepository(connectionString);
            _schema = schema;
            _targets = MappingRules.GetSelectableTargets(schema);

            Text = "Column Mappings";
            Font = new Font("Segoe UI", 9F);
            StartPosition = FormStartPosition.CenterParent;
            Size = new Size(1100, 650);
            MinimumSize = new Size(800, 400);
            MinimizeBox = false;
            ShowInTaskbar = false;

            // Top: search and filter
            var top = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 40, Padding = new Padding(8, 8, 8, 0), WrapContents = false };
            _search = new TextBox { Width = 280 };
            _filter = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200 };
            _filter.Items.AddRange(new object[] { FilterEverything, FilterActive, FilterMissing, FilterIgnored, FilterInactive, FilterRecent });
            _filter.SelectedIndex = 0;
            _count = new Label { AutoSize = true, ForeColor = SystemColors.GrayText, Margin = new Padding(12, 6, 3, 3) };

            top.Controls.Add(new Label { Text = "Search:", AutoSize = true, Margin = new Padding(3, 6, 3, 3) });
            top.Controls.Add(_search);
            top.Controls.Add(new Label { Text = "Show:", AutoSize = true, Margin = new Padding(12, 6, 3, 3) });
            top.Controls.Add(_filter);
            top.Controls.Add(_count);

            // Middle: grid
            _grid = new DataGridView
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                MultiSelect = false,
                RowHeadersVisible = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                BackgroundColor = SystemColors.Window
            };

            // Bottom: buttons
            var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 46, Padding = new Padding(8, 8, 8, 8) };
            var add = new Button { Text = "Add…", AutoSize = true };
            _edit = new Button { Text = "Edit…", AutoSize = true };
            _toggleActive = new Button { Text = "Deactivate", AutoSize = true };
            var export = new Button { Text = "Export to JSON…", AutoSize = true, Margin = new Padding(24, 3, 3, 3) };
            var import = new Button { Text = "Import from JSON…", AutoSize = true };
            var close = new Button { Text = "Close", AutoSize = true, DialogResult = DialogResult.Cancel, Margin = new Padding(24, 3, 3, 3) };

            bottom.Controls.AddRange(new Control[] { add, _edit, _toggleActive, export, import, close });
            CancelButton = close;

            Controls.Add(_grid);
            Controls.Add(bottom);
            Controls.Add(top);

            _search.TextChanged += (_, _) => RefreshGrid();
            _filter.SelectedIndexChanged += (_, _) => RefreshGrid();
            _grid.SelectionChanged += (_, _) => UpdateButtons();
            _grid.CellDoubleClick += (_, e) => { if (e.RowIndex >= 0) EditSelected(); };
            add.Click += (_, _) => AddMapping();
            _edit.Click += (_, _) => EditSelected();
            _toggleActive.Click += (_, _) => ToggleSelected();
            export.Click += (_, _) => ExportJson();
            import.Click += (_, _) => ImportJson();

            ReloadRows();
        }

        // ---------- Data ----------

        private void ReloadRows()
        {
            _rows = _mapRepo.LoadAll();
            RefreshGrid();
        }

        private string StatusOf(ColumnMapRow row)
        {
            if (!row.IsActive) return StatusInactive;
            if (row.IsIgnored) return StatusIgnored;
            if (MappingRules.IsStructuralLabel(row.PdfLabel)) return StatusRequired;

            bool exists = row.TableName != null && row.ColumnName != null &&
                          _schema.TryGetValue(row.TableName, out var cols) &&
                          cols.Contains(row.ColumnName, StringComparer.OrdinalIgnoreCase);

            return exists ? StatusActive : StatusMissing;
        }

        /// <summary>Active, non-ignored mappings, used for suggestions and category warnings.</summary>
        private Dictionary<string, (string TableName, string ColumnName)> ActiveMap() =>
            _rows.Where(r => r.IsActive && !r.IsIgnored && r.TableName != null && r.ColumnName != null)
                 .ToDictionary(r => r.PdfLabel, r => (r.TableName!, r.ColumnName!), StringComparer.OrdinalIgnoreCase);

        private void RefreshGrid()
        {
            int? selectedId = SelectedRow()?.Id;
            var sortColumn = _grid.SortedColumn?.Name;
            var sortOrder = _grid.SortOrder;

            string search = _search.Text.Trim();
            string filter = _filter.SelectedItem as string ?? FilterEverything;
            var since = DateTime.Now.AddDays(-30);

            var table = new DataTable();
            table.Columns.Add("Id", typeof(int));
            table.Columns.Add("PDF label", typeof(string));
            table.Columns.Add("Table", typeof(string));
            table.Columns.Add("Column", typeof(string));
            table.Columns.Add("Status", typeof(string));
            table.Columns.Add("Changed", typeof(DateTime));
            table.Columns.Add("Notes", typeof(string));

            foreach (var row in _rows)
            {
                string status = StatusOf(row);

                bool include = filter switch
                {
                    FilterActive => status == StatusActive || status == StatusRequired,
                    FilterMissing => status == StatusMissing,
                    FilterIgnored => status == StatusIgnored,
                    FilterInactive => status == StatusInactive,
                    FilterRecent => row.ChangedOn >= since,
                    _ => true
                };

                if (include && search.Length > 0)
                {
                    include = new[] { row.PdfLabel, row.TableName, row.ColumnName, row.Notes }
                        .Any(v => v != null && v.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0);
                }

                if (include)
                    table.Rows.Add(row.Id, row.PdfLabel, row.TableName ?? "", row.ColumnName ?? "", status, row.ChangedOn, row.Notes ?? "");
            }

            _grid.DataSource = table;
            _grid.Columns["Id"].Visible = false;
            _grid.Columns["PDF label"].FillWeight = 35;
            _grid.Columns["Table"].FillWeight = 15;
            _grid.Columns["Column"].FillWeight = 20;
            _grid.Columns["Status"].FillWeight = 10;
            _grid.Columns["Changed"].FillWeight = 10;
            _grid.Columns["Changed"].DefaultCellStyle.Format = "yyyy-MM-dd";
            _grid.Columns["Notes"].FillWeight = 15;

            if (sortColumn != null && sortOrder != SortOrder.None)
                _grid.Sort(_grid.Columns[sortColumn], sortOrder == SortOrder.Ascending ? ListSortDirection.Ascending : ListSortDirection.Descending);
            else
                _grid.Sort(_grid.Columns["PDF label"], ListSortDirection.Ascending);

            foreach (DataGridViewRow gridRow in _grid.Rows)
            {
                string status = (string)gridRow.Cells["Status"].Value;
                if (status == StatusMissing)
                    gridRow.DefaultCellStyle.ForeColor = Color.Firebrick;
                else if (status == StatusInactive || status == StatusIgnored)
                    gridRow.DefaultCellStyle.ForeColor = SystemColors.GrayText;

                if (selectedId != null && (int)gridRow.Cells["Id"].Value == selectedId)
                {
                    gridRow.Selected = true;
                    _grid.CurrentCell = gridRow.Cells["PDF label"];
                }
            }

            _count.Text = $"{table.Rows.Count} of {_rows.Count} shown";
            UpdateButtons();
        }

        private ColumnMapRow? SelectedRow()
        {
            if (_grid.SelectedRows.Count == 0)
                return null;

            int id = (int)_grid.SelectedRows[0].Cells["Id"].Value;
            return _rows.FirstOrDefault(r => r.Id == id);
        }

        private void UpdateButtons()
        {
            var row = SelectedRow();
            bool editable = row != null && !MappingRules.IsStructuralLabel(row.PdfLabel);

            _edit.Enabled = editable;
            _toggleActive.Enabled = editable;
            _toggleActive.Text = row != null && !row.IsActive ? "Reactivate" : "Deactivate";
        }

        // ---------- Actions ----------

        private void AddMapping()
        {
            var labels = new HashSet<string>(_rows.Select(r => r.PdfLabel.Trim()), StringComparer.OrdinalIgnoreCase);

            using var dlg = new MappingEditForm(null, _targets, ActiveMap(), labels);
            if (dlg.ShowDialog(this) == DialogResult.OK && dlg.Result != null)
                Save(new[] { dlg.Result }, "added");
        }

        private void EditSelected()
        {
            var row = SelectedRow();
            if (row == null || MappingRules.IsStructuralLabel(row.PdfLabel))
                return;

            var labels = new HashSet<string>(_rows.Select(r => r.PdfLabel.Trim()), StringComparer.OrdinalIgnoreCase);

            // Editing keeps the row's active state; Reactivate is a separate, deliberate step.
            using var dlg = new MappingEditForm(row, _targets, ActiveMap(), labels);
            if (dlg.ShowDialog(this) == DialogResult.OK && dlg.Result != null)
                Save(new[] { dlg.Result with { IsActive = row.IsActive } }, "updated");
        }

        private void ToggleSelected()
        {
            var row = SelectedRow();
            if (row == null || MappingRules.IsStructuralLabel(row.PdfLabel))
                return;

            if (row.IsActive)
            {
                var answer = MessageBox.Show(
                    this,
                    $"Deactivate \"{row.PdfLabel}\"?\n\nOrders containing this label will stop and ask where it belongs until it is set up again.",
                    "Deactivate Mapping",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question);

                if (answer != DialogResult.Yes)
                    return;
            }

            try
            {
                _mapRepo.SetActive(row.Id, !row.IsActive);
                ImportLogger.Log($"Mapping {(row.IsActive ? "deactivated" : "reactivated")}: \"{row.PdfLabel}\"");
                MappingsChanged = true;
                ReloadRows();
            }
            catch (Exception ex)
            {
                ShowError("The change could not be saved.", ex);
            }
        }

        private void Save(IReadOnlyCollection<MappingChoice> choices, string verb)
        {
            try
            {
                _mapRepo.SaveChoices(choices);
                foreach (var c in choices)
                {
                    ImportLogger.Log(c.Ignore
                        ? $"Mapping {verb}: \"{c.PdfLabel}\" → ignored"
                        : $"Mapping {verb}: \"{c.PdfLabel}\" → {c.TableName}.{c.ColumnName}");
                }

                MappingsChanged = true;
                ReloadRows();
            }
            catch (Exception ex)
            {
                ShowError("The mapping could not be saved.", ex);
            }
        }

        private void ExportJson()
        {
            using var dlg = new SaveFileDialog
            {
                Filter = "JSON files (*.json)|*.json",
                FileName = $"ColumnMap-{DateTime.Now:yyyy-MM-dd}.json",
                Title = "Export Column Mappings"
            };

            if (dlg.ShowDialog(this) != DialogResult.OK)
                return;

            try
            {
                ColumnMapLoader.Save(dlg.FileName, _rows);
                int inactive = _rows.Count(r => !r.IsActive);
                string counts = $"{_rows.Count} mapping(s) ({inactive} inactive)";
                ImportLogger.Log($"Exported {counts} to {dlg.FileName}");
                MessageBox.Show(this, $"Exported {counts} to:\n\n{dlg.FileName}",
                    "Export Complete", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                ShowError("The mappings could not be exported.", ex);
            }
        }

        private void ImportJson()
        {
            using var dlg = new OpenFileDialog
            {
                Filter = "JSON files (*.json)|*.json",
                Title = "Import Column Mappings"
            };

            if (dlg.ShowDialog(this) != DialogResult.OK)
                return;

            MappingImportPlan plan;
            try
            {
                plan = MappingImportPlanner.Plan(ColumnMapLoader.LoadEntries(dlg.FileName), _rows, _targets);
            }
            catch (Exception ex)
            {
                ShowError("The file could not be read.", ex);
                return;
            }

            var summary = new List<string>
            {
                $"File: {dlg.FileName}",
                "",
                $"{plan.Adds.Count} new mapping(s) will be added.",
                $"{plan.Changes.Count} existing mapping(s) will be changed.",
                $"{plan.Unchanged} mapping(s) already match and will be left alone.",
                "Mappings that are not in the file are not changed or removed."
            };

            if (plan.Adds.Count > 0)
                summary.AddRange(new[] { "", "ADD:" }.Concat(plan.Adds.Select(Describe)));
            if (plan.Changes.Count > 0)
                summary.AddRange(new[] { "", "CHANGE:" }.Concat(plan.Changes.Select(c => DescribeChange(c))));
            if (plan.Problems.Count > 0)
                summary.AddRange(new[] { "", "SKIPPED:" }.Concat(plan.Problems.Select(p => "  " + p)));

            if (!plan.HasWork)
            {
                using var info = new SelectableMessageForm("Nothing to Import", string.Join(Environment.NewLine, summary));
                info.ShowDialog(this);
                return;
            }

            summary.AddRange(new[] { "", "A backup of the current mappings is saved before importing." });

            using (var confirm = new ConfirmTextForm("Import Column Mappings", string.Join(Environment.NewLine, summary), "Import"))
            {
                if (confirm.ShowDialog(this) != DialogResult.OK)
                    return;
            }

            try
            {
                string backup = BackupMappings();
                ImportLogger.Log($"Mapping backup saved to {backup} before importing {dlg.FileName}");
            }
            catch (Exception ex)
            {
                ShowError("A backup of the current mappings could not be saved, so nothing was imported.", ex);
                return;
            }

            Save(plan.Adds.Concat(plan.Changes).ToList(), "imported");
        }

        private string Describe(MappingChoice c) =>
            (c.Ignore ? $"  {c.PdfLabel} → (ignored)" : $"  {c.PdfLabel} → {c.TableName} / {c.ColumnName}") +
            (c.IsActive ? "" : " (inactive)");

        private string DescribeChange(MappingChoice c)
        {
            var row = _rows.First(r => r.PdfLabel.Trim().Equals(c.PdfLabel, StringComparison.OrdinalIgnoreCase));
            string before = (row.IsIgnored ? "(ignored)" : $"{row.TableName} / {row.ColumnName}") +
                            (row.IsActive ? "" : " (inactive)");
            bool notesChanged = c.Notes != null && MappingRules.NormalizeNotes(c.Notes) != MappingRules.NormalizeNotes(row.Notes);
            return $"{Describe(c)}   (was {before})" + (notesChanged ? "   [note changed]" : "");
        }

        private string BackupMappings()
        {
            string folder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "BSH Import Tool",
                "Mapping Backups");
            Directory.CreateDirectory(folder);

            string path = Path.Combine(folder, $"ColumnMap-before-import-{DateTime.Now:yyyyMMdd-HHmmss}.json");
            ColumnMapLoader.Save(path, _rows);
            return path;
        }

        private void ShowError(string message, Exception ex)
        {
            ImportLogger.Log($"{message} {ex.Message}");
            MessageBox.Show(this, $"{message}\n\n{ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        /// <summary>Read-only, copyable text with confirm and cancel buttons.</summary>
        private sealed class ConfirmTextForm : Form
        {
            public ConfirmTextForm(string title, string text, string confirmText)
            {
                Text = title;
                Font = new Font("Segoe UI", 9F);
                StartPosition = FormStartPosition.CenterParent;
                Size = new Size(800, 520);
                MinimizeBox = false;
                ShowInTaskbar = false;

                var box = new TextBox
                {
                    Dock = DockStyle.Fill,
                    Multiline = true,
                    ReadOnly = true,
                    ScrollBars = ScrollBars.Both,
                    WordWrap = false,
                    Text = text,
                    BackColor = SystemColors.Window
                };

                var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, Height = 46, Padding = new Padding(8) };
                var cancel = new Button { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel };
                var ok = new Button { Text = confirmText, AutoSize = true, DialogResult = DialogResult.OK };
                buttons.Controls.Add(cancel);
                buttons.Controls.Add(ok);
                CancelButton = cancel;

                Controls.Add(box);
                Controls.Add(buttons);
                box.Select(0, 0);
            }
        }
    }
}
