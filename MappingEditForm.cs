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
    /// Add or edit one mapping from the Mapping Manager. The PDF label can only be typed when
    /// adding; editing changes where the label goes, whether it is ignored, and its notes.
    /// </summary>
    public sealed class MappingEditForm : Form
    {
        private readonly SortedDictionary<string, List<string>> _targets;
        private readonly IReadOnlyDictionary<string, (string TableName, string ColumnName)> _existingMap;
        private readonly ISet<string> _existingLabels;
        private readonly bool _isNew;

        private readonly TextBox _label;
        private readonly CheckBox _ignore;
        private readonly ComboBox _table;
        private readonly ComboBox _column;
        private readonly TextBox _notes;
        private readonly Label _hint;

        /// <summary>The saved choice, set when the dialog closes with OK.</summary>
        public MappingChoice? Result { get; private set; }

        public MappingEditForm(
            ColumnMapRow? existing,
            SortedDictionary<string, List<string>> selectableTargets,
            IReadOnlyDictionary<string, (string TableName, string ColumnName)> existingMap,
            ISet<string> existingLabels)
        {
            _targets = selectableTargets;
            _existingMap = existingMap;
            _existingLabels = existingLabels;
            _isNew = existing == null;

            Text = _isNew ? "Add Mapping" : "Edit Mapping";
            Font = new Font("Segoe UI", 9F);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;

            var layout = new TableLayoutPanel
            {
                ColumnCount = 2,
                AutoSize = true,
                Padding = new Padding(12),
                Dock = DockStyle.Fill
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 420));

            _label = new TextBox { Dock = DockStyle.Fill, ReadOnly = !_isNew, Text = existing?.PdfLabel ?? "" };
            _ignore = new CheckBox { Text = "Ignore this label (it is not an order item)", AutoSize = true, Checked = existing?.IsIgnored ?? false };
            _table = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
            _column = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
            _notes = new TextBox { Dock = DockStyle.Fill, MaxLength = 255, Text = existing?.Notes ?? "" };
            _hint = new Label { AutoSize = true, ForeColor = SystemColors.GrayText, MaximumSize = new Size(420, 0) };

            AddRow(layout, "PDF label:", _label);
            AddRow(layout, "", _ignore);
            AddRow(layout, "Table:", _table);
            AddRow(layout, "Column:", _column);
            AddRow(layout, "Notes:", _notes);
            AddRow(layout, "", _hint);

            var ok = new Button { Text = "Save", AutoSize = true };
            var cancel = new Button { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel };
            ok.Click += Save_Click;

            var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Dock = DockStyle.Fill };
            buttons.Controls.Add(cancel);
            buttons.Controls.Add(ok);
            AddRow(layout, "", buttons);

            AcceptButton = ok;
            CancelButton = cancel;
            Controls.Add(layout);

            _table.Items.AddRange(_targets.Keys.Cast<object>().ToArray());
            _table.SelectedIndexChanged += (_, _) => FillColumns();
            _ignore.CheckedChanged += (_, _) => UpdateEnabled();

            if (existing != null && !existing.IsIgnored && existing.TableName != null)
            {
                _table.SelectedItem = FindItem(_table, existing.TableName);
                _column.SelectedItem = existing.ColumnName == null ? null : FindItem(_column, existing.ColumnName);

                if (_column.SelectedItem == null)
                    _hint.Text = $"Currently saved to {existing.TableName} → {existing.ColumnName}, which no longer exists in the database.";
            }

            if (_isNew)
            {
                _hint.Text = "Type the label exactly as it appears on the PDF. A matching table and column will be suggested when there is a clear one.";
                _label.Leave += (_, _) => SuggestIfEmpty();
            }

            UpdateEnabled();
        }

        private static void AddRow(TableLayoutPanel layout, string caption, Control control)
        {
            layout.Controls.Add(new Label { Text = caption, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 6, 6, 3) });
            layout.Controls.Add(control);
        }

        private void FillColumns()
        {
            _column.Items.Clear();
            if (_table.SelectedItem is string t && _targets.TryGetValue(t, out var cols))
                _column.Items.AddRange(cols.Cast<object>().ToArray());
        }

        private void UpdateEnabled()
        {
            _table.Enabled = !_ignore.Checked;
            _column.Enabled = !_ignore.Checked;
        }

        private void SuggestIfEmpty()
        {
            if (_table.SelectedItem != null || string.IsNullOrWhiteSpace(_label.Text))
                return;

            var suggestion = MappingRules.Suggest(_label.Text.Trim(), _existingMap, _targets);
            if (suggestion == null)
                return;

            _table.SelectedItem = FindItem(_table, suggestion.Value.TableName);
            _column.SelectedItem = FindItem(_column, suggestion.Value.ColumnName);
            _hint.Text = "Suggested match filled in. Please check it before saving.";
        }

        private static object? FindItem(ComboBox box, string text) =>
            box.Items.Cast<string>().FirstOrDefault(i => i.Equals(text, StringComparison.OrdinalIgnoreCase));

        private void Save_Click(object? sender, EventArgs e)
        {
            string label = _label.Text.Trim();

            if (label.Length == 0)
            {
                Warn("Enter the PDF label.");
                _label.Focus();
                return;
            }

            if (_isNew && _existingLabels.Contains(label))
            {
                Warn($"\"{label}\" already has a mapping. Find it in the list (it may be inactive) and edit it instead.");
                _label.Focus();
                return;
            }

            if (MappingRules.IsStructuralLabel(label))
            {
                Warn($"\"{label}\" is a required order field and its mapping cannot be changed here.");
                return;
            }

            if (_ignore.Checked)
            {
                Result = new MappingChoice(label, null, null, Ignore: true, Notes: _notes.Text);
                DialogResult = DialogResult.OK;
                return;
            }

            string? tableName = _table.SelectedItem as string;
            string? columnName = _column.SelectedItem as string;

            string? error = MappingRules.Validate(label, tableName, columnName, _targets);
            if (error != null)
            {
                Warn(error);
                return;
            }

            var warnings = MappingRules.GetCategoryWarnings(label, tableName!, columnName!, _existingMap, _targets);
            if (warnings.Count > 0)
            {
                var answer = MessageBox.Show(
                    this,
                    "Please double-check this before saving:\n\n" +
                    string.Join("\n\n", warnings.Select(w => "• " + w)) +
                    "\n\nIf the table is wrong, the item will be saved under the wrong category. Save anyway?",
                    "Different Category Than Similar Items",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning,
                    MessageBoxDefaultButton.Button2);

                if (answer != DialogResult.Yes)
                    return;
            }

            Result = new MappingChoice(label, tableName, columnName, Ignore: false, Notes: _notes.Text);
            DialogResult = DialogResult.OK;
        }

        private void Warn(string message) =>
            MessageBox.Show(this, message, "Check This Mapping", MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }
}
