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
        // Initializations
        private string? dbPath = Properties.Settings.Default.DatabasePath;
        private readonly string connectionString = null!;
        private readonly Dictionary<string, (string TableName, string ColumnName)> columnToTableMap = null!;
        private readonly AccessRepository _repo = null!;
        private readonly PdfImportService _pdfService = null!;

        public Form1()
        {
            InitializeComponent();

            // Load mapping config file.  If this file does not load, tables cannot be populated with records
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

            // Ensure a DB is selected
            if (string.IsNullOrEmpty(dbPath) || !File.Exists(dbPath))
                dbPath = PromptForDatabasePath();

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

        private string PromptForDatabasePath()
        {
            while (true)
            {
                using var openFileDialog = new OpenFileDialog
                {
                    Filter = "Access Database (*.mdb;*.accdb)|*.mdb;*.accdb",
                    Title = "Select Access Database"
                };

                if (openFileDialog.ShowDialog() == DialogResult.OK && File.Exists(openFileDialog.FileName))
                {
                    string path = openFileDialog.FileName;
                    Properties.Settings.Default.DatabasePath = path;
                    Properties.Settings.Default.Save();
                    return path;
                }

                MessageBox.Show(
                    "A valid Access Database is required to run this application. Please select a file.",
                    "Database Required",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
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
            var sourceFileMap = new Dictionary<string, string>(); // temp file → original source file

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

                        foreach (var s in split)
                            sourceFileMap[s] = file;
                    }
                    else
                    {
                        allFiles.Add(file);
                    }
                }

                int totalFiles = allFiles.Count;
                int successfulImports = 0;

                if (totalFiles == 0) return;

                ImportLogger.BeginSession(allFiles.Count);

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

                    // Resolve the display name — use original picklist name if this is a temp file
                    string displayFile = sourceFileMap.TryGetValue(file, out var sourceFile)
                        ? sourceFile
                        : file;

                    var outcome = _repo.InsertDataIntoDatabase(processedLines.ToArray(), displayFile);

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
                            string invalidFileLabel = string.IsNullOrWhiteSpace(outcome.FileName)
                                ? Path.GetFileName(file)
                                : Path.GetFileName(outcome.FileName);

                            string invalidOrderLabel = string.IsNullOrWhiteSpace(outcome.OrderNumber)
                                ? ""
                                : $"Order {outcome.OrderNumber}\n";

                            MessageBox.Show(
                                $"{invalidOrderLabel}An order in the following file could not be processed:\n\n{invalidFileLabel}",
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

        private void exitToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Application.Exit();
        }
    }
}