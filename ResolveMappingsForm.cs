using BSH_Import_Utility.Domain;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

#nullable enable

namespace BSH_Import_Utility
{
    /// <summary>
    /// Shown when an order contains PDF labels that cannot be saved: either not mapped at all,
    /// or mapped to a column the database no longer has. The user picks a table and column for
    /// each one, or ignores it. Rows are built from data, so the layout is done in code.
    /// </summary>
    public sealed class ResolveMappingsForm : Form
    {
        private readonly SortedDictionary<string, List<string>> _targets;
        private readonly IReadOnlyDictionary<string, (string TableName, string ColumnName)> _existingMap;
        private readonly List<Row> _rows = new();

        /// <summary>The user's choices, filled when the dialog closes with OK.</summary>
        public List<MappingChoice> Choices { get; } = new();

        private sealed class Row
        {
            public UnresolvedLabel Item = null!;
            public ComboBox Table = null!;
            public ComboBox Column = null!;
            public CheckBox Ignore = null!;
        }

        public ResolveMappingsForm(
            string orderNumber,
            string fileName,
            IReadOnlyList<UnresolvedLabel> items,
            SortedDictionary<string, List<string>> selectableTargets,
            IReadOnlyDictionary<string, (string TableName, string ColumnName)> existingMap,
            string? introText = null)
        {
            _targets = selectableTargets;
            _existingMap = existingMap;

            Text = $"Unrecognized Items — Order {orderNumber}";
            Font = new Font("Segoe UI", 9F);
            StartPosition = FormStartPosition.CenterParent;
            Size = new Size(1000, 520);
            MinimumSize = new Size(780, 320);
            MinimizeBox = false;
            ShowInTaskbar = false;

            var intro = new Label
            {
                Dock = DockStyle.Top,
                Height = 78,
                Padding = new Padding(12, 10, 12, 0),
                Text =
                    (introText ??
                     $"Order {orderNumber} ({fileName}) has {items.Count} item(s) that can't be saved yet. " +
                     "Nothing from this order has been saved.") + "\n\n" +
                    "For each item, choose the table and column it belongs in, or tick Ignore if it is not an order item " +
                    "(for example, stray text from the PDF). Your choices are remembered for future imports."
            };

            var grid = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                ColumnCount = 5,
                Padding = new Padding(12, 0, 12 + SystemInformation.VerticalScrollBarWidth, 0),
                GrowStyle = TableLayoutPanelGrowStyle.AddRows
            };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 200));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            var bold = new Font(Font, FontStyle.Bold);
            foreach (var header in new[] { "Item on the PDF", "Value", "Table", "Column", "" })
                grid.Controls.Add(new Label { Text = header, Font = bold, AutoSize = true, Margin = new Padding(3, 6, 3, 6) });

            foreach (var item in items)
                AddRow(grid, item);

            var buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                FlowDirection = FlowDirection.RightToLeft,
                Height = 48,
                Padding = new Padding(12, 8, 12, 8)
            };

            var save = new Button { Text = "Save and retry", AutoSize = true };
            save.Click += Save_Click;

            var skip = new Button { Text = "Skip this order", AutoSize = true, DialogResult = DialogResult.Cancel };

            buttons.Controls.Add(save);
            buttons.Controls.Add(skip);
            CancelButton = skip;

            // Edge-docked controls are added last so they claim their edges before the grid fills the rest.
            Controls.Add(grid);
            Controls.Add(buttons);
            Controls.Add(intro);
        }

        private void AddRow(TableLayoutPanel grid, UnresolvedLabel item)
        {
            bool structural = MappingRules.IsStructuralLabel(item.PdfLabel);
            var suggestion = structural ? null : MappingRules.Suggest(item.PdfLabel, _existingMap, _targets);

            string note;
            if (structural)
                note = "Required order field. It can't be changed here; check the BSH table in Access.";
            else if (item.IsMappedToMissingColumn)
                note = $"Was saved to {item.MappedTable} → {item.MappedColumn}, which no longer exists in the database.";
            else if (suggestion != null)
                note = "Suggested match filled in. Please check it before saving.";
            else
                note = "Not set up yet.";

            var labelPanel = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoSize = true,
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 4, 0, 4)
            };
            labelPanel.Controls.Add(new Label { Text = item.PdfLabel, AutoSize = true, MaximumSize = new Size(380, 0) });
            labelPanel.Controls.Add(new Label { Text = note, AutoSize = true, MaximumSize = new Size(380, 0), ForeColor = SystemColors.GrayText });

            var value = new Label
            {
                Text = item.Value?.ToString() ?? "",
                AutoSize = true,
                MaximumSize = new Size(160, 0),
                Margin = new Padding(3, 6, 3, 3)
            };

            var table = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill, Margin = new Padding(3, 4, 3, 3) };
            var column = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill, Margin = new Padding(3, 4, 3, 3) };
            var ignore = new CheckBox { Text = "Ignore", AutoSize = true, Margin = new Padding(6, 6, 3, 3) };

            table.Items.AddRange(_targets.Keys.Cast<object>().ToArray());
            table.SelectedIndexChanged += (_, _) =>
            {
                column.Items.Clear();
                if (table.SelectedItem is string t && _targets.TryGetValue(t, out var cols))
                    column.Items.AddRange(cols.Cast<object>().ToArray());
            };
            ignore.CheckedChanged += (_, _) =>
            {
                table.Enabled = !ignore.Checked;
                column.Enabled = !ignore.Checked;
            };

            if (suggestion != null)
            {
                table.SelectedItem = FindItem(table, suggestion.Value.TableName);
                column.SelectedItem = FindItem(column, suggestion.Value.ColumnName);
            }
            else if (item.IsMappedToMissingColumn)
            {
                table.SelectedItem = FindItem(table, item.MappedTable!);
            }

            if (structural)
            {
                table.Enabled = false;
                column.Enabled = false;
                ignore.Enabled = false;
            }

            grid.Controls.Add(labelPanel);
            grid.Controls.Add(value);
            grid.Controls.Add(table);
            grid.Controls.Add(column);
            grid.Controls.Add(ignore);

            _rows.Add(new Row { Item = item, Table = table, Column = column, Ignore = ignore });
        }

        private static object? FindItem(ComboBox box, string text) =>
            box.Items.Cast<string>().FirstOrDefault(i => i.Equals(text, StringComparison.OrdinalIgnoreCase));

        private void Save_Click(object? sender, EventArgs e)
        {
            var choices = new List<MappingChoice>();
            var warnings = new List<string>();

            foreach (var row in _rows)
            {
                string label = row.Item.PdfLabel;

                // Required BSH fields can't be changed here (the row is disabled), so leave them
                // out rather than blocking the user's choices for every other item.
                if (MappingRules.IsStructuralLabel(label))
                    continue;

                if (row.Ignore.Checked)
                {
                    choices.Add(new MappingChoice(label, null, null, Ignore: true));
                    continue;
                }

                string? tableName = row.Table.SelectedItem as string;
                string? columnName = row.Column.SelectedItem as string;

                string? error = MappingRules.Validate(label, tableName, columnName, _targets);
                if (error != null)
                {
                    MessageBox.Show(this, error, "Check This Item", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    (row.Table.Enabled ? row.Table : (Control)row.Ignore).Focus();
                    return;
                }

                choices.Add(new MappingChoice(label, tableName, columnName, Ignore: false));
                warnings.AddRange(MappingRules.GetCategoryWarnings(label, tableName!, columnName!, _existingMap, _targets));
            }

            if (choices.Count == 0)
            {
                MessageBox.Show(this,
                    "Only required order fields are listed, and they can't be changed here. " +
                    "Check the BSH table in Access.",
                    "Nothing to Save", MessageBoxButtons.OK, MessageBoxIcon.Information);
                DialogResult = DialogResult.Cancel;
                return;
            }

            if (warnings.Count > 0)
            {
                var answer = MessageBox.Show(
                    this,
                    "Please double-check these before saving:\n\n" +
                    string.Join("\n\n", warnings.Select(w => "• " + w)) +
                    "\n\nIf the table is wrong, the item will be saved under the wrong category. Save anyway?",
                    "Different Category Than Similar Items",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning,
                    MessageBoxDefaultButton.Button2);

                if (answer != DialogResult.Yes)
                    return;
            }

            Choices.Clear();
            Choices.AddRange(choices);
            DialogResult = DialogResult.OK;
        }
    }
}
