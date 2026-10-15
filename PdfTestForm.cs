using BSH_Import_Utility.Domain;
using BSH_Import_Utility.Infrastructure;
using System;
using System.Collections.Generic;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;

#nullable enable

namespace BSH_Import_Utility
{
    /// <summary>
    /// Shows what importing one or more PDFs would do, label by label, without saving anything.
    /// The caller supplies the check (so it can be re-run after mappings change) and the
    /// "set up unrecognized items" step, which is the same dialog an import uses.
    /// </summary>
    public sealed class PdfTestForm : Form
    {
        private const string ResultReady = "Ready";
        private const string ResultNotSetUp = "Not set up";
        private const string ResultMissing = "Column missing in database";
        private const string ResultIgnored = "Ignored";
        private const string ResultIgnoredBuiltIn = "Ignored (built in)";
        private const string ResultNoValue = "Skipped (no quantity)";

        private const string FilterProblems = "Items that need attention";
        private const string FilterImport = "Items that would be imported";
        private const string FilterIgnored = "Ignored labels";
        private const string FilterAll = "Every line in the PDF";

        private readonly Func<List<OrderCheck>> _runCheck;
        private readonly Func<IReadOnlyList<UnresolvedLabel>, string, string, bool> _setUpItems;
        private List<OrderCheck> _orders = new();

        private readonly TextBox _summary;
        private readonly ComboBox _filter;
        private readonly Label _count;
        private readonly DataGridView _grid;
        private readonly Button _setUp;

        /// <param name="runCheck">Checks every order against the current mappings and schema.</param>
        /// <param name="setUpItems">Shows the Unrecognized Items dialog for these labels (with their
        /// order numbers and where they came from); returns true when choices were saved.</param>
        public PdfTestForm(
            Func<List<OrderCheck>> runCheck,
            Func<IReadOnlyList<UnresolvedLabel>, string, string, bool> setUpItems)
        {
            _runCheck = runCheck;
            _setUpItems = setUpItems;

            Text = "Test a PDF";
            Font = new Font("Segoe UI", 9F);
            StartPosition = FormStartPosition.CenterParent;
            Size = new Size(1100, 700);
            MinimumSize = new Size(800, 450);
            MinimizeBox = false;
            ShowInTaskbar = false;

            // Top: summary (read-only, copyable)
            _summary = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                BackColor = SystemColors.Window,
                BorderStyle = BorderStyle.None
            };
            var summaryPanel = new Panel { Dock = DockStyle.Top, Height = 122, Padding = new Padding(12, 10, 12, 2) };
            summaryPanel.Controls.Add(_summary);

            // Filter row
            var filterRow = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 36, Padding = new Padding(8, 4, 8, 0), WrapContents = false };
            _filter = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 220 };
            _filter.Items.AddRange(new object[] { FilterProblems, FilterImport, FilterIgnored, FilterAll });
            _count = new Label { AutoSize = true, ForeColor = SystemColors.GrayText, Margin = new Padding(12, 6, 3, 3) };
            filterRow.Controls.Add(new Label { Text = "Show:", AutoSize = true, Margin = new Padding(3, 6, 3, 3) });
            filterRow.Controls.Add(_filter);
            filterRow.Controls.Add(_count);

            // Middle: grid
            _grid = new DataGridView
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                RowHeadersVisible = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                BackgroundColor = SystemColors.Window
            };

            // Bottom: buttons
            var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 46, Padding = new Padding(8, 8, 8, 8) };
            _setUp = new Button { Text = "Set Up Unrecognized Items…", AutoSize = true };
            var copy = new Button { Text = "Copy Results", AutoSize = true };
            var close = new Button { Text = "Close", AutoSize = true, DialogResult = DialogResult.Cancel, Margin = new Padding(24, 3, 3, 3) };
            bottom.Controls.AddRange(new Control[] { _setUp, copy, close });
            CancelButton = close;

            Controls.Add(_grid);
            Controls.Add(bottom);
            Controls.Add(filterRow);
            Controls.Add(summaryPanel);

            _filter.SelectedIndexChanged += (_, _) => RefreshGrid();
            _setUp.Click += (_, _) => SetUpItems();
            copy.Click += (_, _) => CopyResults();

            Recheck(pickFilter: true);
        }

        private void Recheck(bool pickFilter)
        {
            _orders = _runCheck();
            _summary.Text = BuildSummary();
            _setUp.Enabled = _orders.Any(o => o.BlockingCount > 0);

            if (pickFilter)
                _filter.SelectedItem = _orders.Any(o => o.BlockingCount > 0) ? FilterProblems : FilterImport;
            else
                RefreshGrid();
        }

        // ---------- Summary ----------

        private string BuildSummary()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"{_orders.Count} order(s) checked against the current mappings. Nothing was saved to the database.");

            foreach (var order in _orders)
            {
                sb.AppendLine();
                sb.Append(OrderHeading(order)).Append(": ");
                sb.Append(OrderVerdict(order));
            }

            return sb.ToString();
        }

        private static string OrderHeading(OrderCheck order) =>
            order.HasOrderNumber ? $"Order {order.OrderNumber} ({order.FileName})" : order.FileName;

        private static string OrderVerdict(OrderCheck order)
        {
            if (!order.HasOrderNumber)
                return "no order number was found, so the import would reject this file as not an order form.";

            string items = $"{order.ImportCount} item(s) would be imported";

            if (order.AlreadyImported)
                return $"already in the database, so the import would skip it. ({items} otherwise.)";

            if (order.BlockingCount > 0)
                return $"{items}, but {order.BlockingCount} item(s) are not set up. " +
                       "The import would stop and ask about them, or click Set Up Unrecognized Items now.";

            return $"ready to import. {items}.";
        }

        // ---------- Grid ----------

        private static string ResultOf(LabelCheck label)
        {
            if (label.Status == LabelStatus.Ignored)
                return label.IsBuiltInIgnore ? ResultIgnoredBuiltIn : ResultIgnored;
            if (!label.HasValue)
                return ResultNoValue;

            return label.Status switch
            {
                LabelStatus.Mapped => ResultReady,
                LabelStatus.MissingColumn => ResultMissing,
                _ => ResultNotSetUp
            };
        }

        private void RefreshGrid()
        {
            string filter = _filter.SelectedItem as string ?? FilterImport;
            bool showOrder = _orders.Count > 1;

            var table = new DataTable();
            table.Columns.Add("Order", typeof(string));
            table.Columns.Add("PDF label", typeof(string));
            table.Columns.Add("Value", typeof(string));
            table.Columns.Add("Result", typeof(string));
            table.Columns.Add("Saves to", typeof(string));

            int shown = 0, total = 0;

            foreach (var order in _orders)
            {
                foreach (var label in order.Labels)
                {
                    total++;

                    bool include = filter switch
                    {
                        FilterProblems => label.BlocksImport,
                        FilterImport => label.HasValue && label.Status != LabelStatus.Ignored,
                        FilterIgnored => label.Status == LabelStatus.Ignored,
                        _ => true
                    };

                    if (!include)
                        continue;

                    shown++;
                    string savesTo = label.TableName != null ? $"{label.TableName} / {label.ColumnName}" : "";
                    string value = label.HasValue ? label.Value?.ToString() ?? "" : "";
                    table.Rows.Add(order.HasOrderNumber ? order.OrderNumber : order.FileName, label.PdfLabel, value, ResultOf(label), savesTo);
                }
            }

            _grid.DataSource = table;
            _grid.Columns["Order"].Visible = showOrder;
            _grid.Columns["Order"].FillWeight = 10;
            _grid.Columns["PDF label"].FillWeight = 38;
            _grid.Columns["Value"].FillWeight = 14;
            _grid.Columns["Result"].FillWeight = 16;
            _grid.Columns["Saves to"].FillWeight = 26;

            foreach (DataGridViewRow gridRow in _grid.Rows)
            {
                string result = (string)gridRow.Cells["Result"].Value;
                if (result == ResultNotSetUp || result == ResultMissing)
                    gridRow.DefaultCellStyle.ForeColor = Color.Firebrick;
                else if (result != ResultReady)
                    gridRow.DefaultCellStyle.ForeColor = SystemColors.GrayText;
            }

            _count.Text = filter == FilterProblems && shown == 0
                ? "Nothing needs attention."
                : $"{shown} of {total} line(s) shown";
        }

        // ---------- Actions ----------

        private void SetUpItems()
        {
            var blocking = _orders.Where(o => o.BlockingCount > 0).ToList();

            var items = blocking
                .SelectMany(o => o.Labels.Where(l => l.BlocksImport))
                .GroupBy(l => l.PdfLabel, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .Select(l => new UnresolvedLabel(l.PdfLabel, l.Value,
                    l.Status == LabelStatus.MissingColumn ? l.TableName : null,
                    l.Status == LabelStatus.MissingColumn ? l.ColumnName : null))
                .ToList();

            if (items.Count == 0)
                return;

            string orderNumbers = string.Join(", ", blocking.Select(o => o.HasOrderNumber ? o.OrderNumber : "(Unknown)"));
            string source = string.Join(", ", blocking.Select(OrderHeading));

            if (!_setUpItems(items, orderNumbers, source))
                return;

            try
            {
                Recheck(pickFilter: false);
            }
            catch (Exception ex)
            {
                ImportLogger.Log($"Re-checking the PDF failed: {ex.Message}");
                MessageBox.Show(this, $"Your choices were saved, but the PDF could not be checked again.\n\n{ex.Message}",
                    "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void CopyResults()
        {
            var sb = new StringBuilder(_summary.Text);
            sb.AppendLine().AppendLine();
            sb.AppendLine(string.Join("\t", "Order", "PDF label", "Value", "Result", "Saves to"));

            foreach (var order in _orders)
            {
                foreach (var label in order.Labels)
                {
                    sb.AppendLine(string.Join("\t",
                        order.HasOrderNumber ? order.OrderNumber : order.FileName,
                        label.PdfLabel,
                        label.HasValue ? label.Value?.ToString() ?? "" : "",
                        ResultOf(label),
                        label.TableName != null ? $"{label.TableName} / {label.ColumnName}" : ""));
                }
            }

            Clipboard.SetText(sb.ToString());
            MessageBox.Show(this, "The summary and every line were copied to the clipboard.",
                "Copied", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }
}
