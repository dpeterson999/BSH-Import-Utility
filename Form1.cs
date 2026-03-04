using BSH_Import_Utility.Config;
using BSH_Import_Utility.Data;
using BSH_Import_Utility.Domain;
using BSH_Import_Utility.Infrastructure;
using BSH_Import_Utility.Services;
using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows.Forms;

#nullable enable

namespace BSH_Import_Utility
{
    public partial class Form1 : Form
    {
        private string dbPath = Properties.Settings.Default.DatabasePath;
        private readonly string connectionString = null!;

        // Loaded from JSON config at startup
        private readonly Dictionary<string, (string TableName, string ColumnName)> columnToTableMap = null!;

        // Services
        private readonly AccessRepository _repo = null!;
        private readonly PdfImportService _pdfService = null!;

        public Form1()
        {
            InitializeComponent();

            // Load mapping config
            try
            {
                columnToTableMap = ColumnMapLoader.Load("columnToTableMap.json");
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Config Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                Application.Exit();
                return;
            }

            // Resolve DB path
            dbPath = Properties.Settings.Default.DatabasePath;

            // Check if the path is null, empty, or not reachable
            if (string.IsNullOrEmpty(dbPath) || !File.Exists(dbPath))
            {
                // Prompt the user to select the database
                using (OpenFileDialog openFileDialog = new())
                {
                    openFileDialog.Filter = "Access Database (*.mdb;*.accdb)|*.mdb;*.accdb";
                    if (openFileDialog.ShowDialog() == DialogResult.OK)
                    {
                        dbPath = openFileDialog.FileName;
                        Properties.Settings.Default.DatabasePath = dbPath;
                        Properties.Settings.Default.Save();
                        MessageBox.Show($"Database path saved: {dbPath}");
                    }
                    else
                    {
                        MessageBox.Show("Database path is required. Application will now exit.");
                        Application.Exit();
                        return;
                    }
                }
            }

            // Check if the selected path is reachable, if not prompt again
            if (!File.Exists(dbPath))
            {
                MessageBox.Show("The selected database path is not accessible. Please select a valid database.");

                using (OpenFileDialog openFileDialog = new())
                {
                    openFileDialog.Filter = "Access Database (*.mdb;*.accdb)|*.mdb;*.accdb";
                    if (openFileDialog.ShowDialog() == DialogResult.OK)
                    {
                        dbPath = openFileDialog.FileName;
                        Properties.Settings.Default.DatabasePath = dbPath;
                        Properties.Settings.Default.Save();
                        MessageBox.Show($"Database path saved: {dbPath}");
                    }
                    else
                    {
                        MessageBox.Show("Database path is required. Application will now exit.");
                        Application.Exit();
                        return;
                    }
                }
            }
            
            //
            // Requires Microsoft Access Database Engine 2016 x64
            //
            connectionString = $@"Provider=Microsoft.ACE.OLEDB.16.0;Data Source={dbPath};Persist Security Info=False;";

            // Wire services
            _repo = new AccessRepository(connectionString, columnToTableMap);
            _pdfService = new PdfImportService();

            UpdateWindowTitle();
            LoadGrid();
        }

        private void UpdateWindowTitle()
        {
            Text = $"BSH PDF Importer — {dbPath}";
        }

        private void LoadGrid()
        {
            dataGridView1.DataSource = _repo.LoadBshGrid();
        }

        private void GetDataFromPDF_Click(object sender, EventArgs e)
        {
            using var dlg = new OpenFileDialog
            {
                Filter = "PDF Files|*.pdf",
                Title = "Select Order Form(s) or Picklist(s)",
                Multiselect = true
            };

            if (dlg.ShowDialog() != DialogResult.OK) return;

            ImportPdfFiles(dlg.FileNames);
        }

        private void ImportPdfFiles(IEnumerable<string>? files)
        {
            var fileList = files?.ToList() ?? new List<string>();
            var allFiles = new List<string>();
            var tempFiles = new List<string>();
            var duplicates = new List<string>();

            ImportLogger.BeginSession(allFiles.Count);

            ImportOrderForm.Enabled = false;

            try
            {
                foreach (var file in fileList)
                {
                    if (PdfSplitService.IsPicklist(file))
                    {
                        int startPage = PdfSplitService.FindFirstOrderPage(file);

                        if (startPage < 1)
                        {
                            MessageBox.Show(
                                $"No individual orders found in:\n{file}",
                                "No Orders Found",
                                MessageBoxButtons.OK,
                                MessageBoxIcon.Warning);
                            continue;
                        }

                        var split = PdfSplitService.FlattenAndSplitTwoPages(file, startPage);
                        allFiles.AddRange(split);
                        tempFiles.AddRange(split);
                    }
                    else
                    {
                        allFiles.Add(file);
                    }
                }

                int totalFiles = allFiles.Count;
                int successfulImports = 0;

                if (totalFiles == 0) return;

                progressBarImport.Visible = true;
                progressBarImport.Minimum = 0;
                progressBarImport.Maximum = totalFiles;
                progressBarImport.Value = 0;

                foreach (var file in allFiles)
                {
                    List<ProcessedLine> processedLines;

                    try
                    {
                        processedLines = _pdfService.ParsePdfFile(file);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show(ex.Message, "PDF Parse Error",
                            MessageBoxButtons.OK, MessageBoxIcon.Error);
                        progressBarImport.Value++;
                        continue;
                    }

                    var outcome = _repo.InsertDataIntoDatabase(processedLines.ToArray(), file);

                    switch (outcome.Status)
                    {
                        case InsertStatus.DuplicateOrderNumber:
                            duplicates.Add(outcome.OrderNumber ?? "(Unknown)");
                            break;

                        case InsertStatus.MissingColumnMappings:
                            string label = string.IsNullOrWhiteSpace(outcome.OrderNumber)
                                ? "(Unknown)" : outcome.OrderNumber!;
                            using (var dlg = new SelectableMessageForm(
                                $"Missing Mapping — {label}",
                                "Columns not mapped or missing in DB:"
                                + Environment.NewLine + Environment.NewLine
                                + string.Join(Environment.NewLine, outcome.MissingColumns)))
                            {
                                dlg.ShowDialog(this);
                            }
                            break;

                        case InsertStatus.Error:
                            string orderLabel = string.IsNullOrWhiteSpace(outcome.OrderNumber)
                                ? "(Unknown Order)"
                                : $"Order {outcome.OrderNumber}";

                            string fileLabel = string.IsNullOrWhiteSpace(outcome.FileName)
                                ? ""
                                : $"\nFile: {Path.GetFileName(outcome.FileName)}";

                            MessageBox.Show(
                                $"{orderLabel}{fileLabel}\n\n{outcome.Exception?.Message ?? "Unknown error."}",
                                "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                            break;

                        case InsertStatus.Inserted:
                            successfulImports++;
                            break;

                        case InsertStatus.InvalidFile:
                            MessageBox.Show(
                                $"The file does not appear to contain a valid order:\n{Path.GetFileName(file)}",
                                "Invalid File", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                            break;
                    }

                    progressBarImport.Value++;
                    Application.DoEvents();
                }

                foreach (var dup in duplicates)
                    ImportLogger.Log($"Skipped duplicate: Order {dup}");

                ImportLogger.EndSession(successfulImports, totalFiles);

                if (duplicates.Count > 0)
                {
                    using var dlg = new SelectableMessageForm(
                        "Duplicate Orders Skipped",
                        $"{duplicates.Count} order(s) already existed and were skipped:"
                        + Environment.NewLine + Environment.NewLine
                        + string.Join(Environment.NewLine, duplicates));
                    dlg.ShowDialog(this);
                }

                LoadGrid();

                MessageBox.Show(
                    $"Imported {successfulImports} of {totalFiles} order(s) successfully.",
                    "Import Complete", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            finally
            {
                progressBarImport.Visible = false;
                ImportOrderForm.Enabled = true;
                PdfSplitService.PurgeGeneratedFiles(tempFiles);
            }
        }

        private void accessDatabaseLocationToolStripMenuItem_Click(object sender, EventArgs e)
        {
            using (OpenFileDialog openFileDialog = new())
            {
                openFileDialog.Filter = "Access Database (*.mdb;*.accdb)|*.mdb;*.accdb";
                if (openFileDialog.ShowDialog() == DialogResult.OK)
                {
                    dbPath = openFileDialog.FileName;
                    Properties.Settings.Default.DatabasePath = dbPath;
                    Properties.Settings.Default.Save();
                    UpdateWindowTitle();
                    MessageBox.Show($"Database path saved: {dbPath}");
                }
            }
        }

        private void btnShowIgnoredColumns_Click(object sender, EventArgs e)
        {
            if (ImportConstants.IgnoredColumns.Count == 0)
            {
                MessageBox.Show(
                    "No columns are currently ignored.",
                    "Ignored Columns",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information
                );
                return;
            }

            MessageBox.Show(
                "The following columns are currently ignored:\n\n" +
                string.Join("\n", ImportConstants.IgnoredColumns.OrderBy(c => c)),
                "Ignored Columns",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information
            );
        }

        private void viewImportLogToolStripMenuItem_Click(object sender, EventArgs e)
        {
            string logPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "BSH Import Tool",
                "import_log.txt");

            if (!File.Exists(logPath))
            {
                MessageBox.Show(
                    "No import log found. The log is created after the first import.",
                    "Import Log",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            System.Diagnostics.Process.Start(logPath);
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.F5 && ImportOrderForm.Enabled)
            {
                LoadGrid();
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        private void refreshToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (ImportOrderForm.Enabled)
                LoadGrid();
        }

        private void exitToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Application.Exit();
        }
    }
}