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
        private string connectionString = null!;
        private Dictionary<string, (string TableName, string ColumnName)> columnToTableMap =
            new(StringComparer.OrdinalIgnoreCase);
        private HashSet<string> userIgnoredLabels = new(StringComparer.OrdinalIgnoreCase);
        private AccessRepository _repo = null!;
        private readonly PdfImportService _pdfService = null!;

        public Form1()
        {
            InitializeComponent();

            // Resolve DB path
            dbPath = Properties.Settings.Default.DatabasePath;

            // Ensure a DB is selected
            if (string.IsNullOrEmpty(dbPath) || !File.Exists(dbPath))
                dbPath = PromptForDatabasePath();

            _pdfService = new PdfImportService();

            ConnectToDatabase();
        }

        /// <summary>
        /// (Re)builds everything that depends on the current dbPath. Called at startup and
        /// whenever the user picks a different database, so imports always go to the
        /// database shown in the title bar.
        /// </summary>
        private void ConnectToDatabase()
        {
            //
            // Requires Microsoft Access Database Engine 2016 x64
            //
            connectionString = $@"Provider=Microsoft.ACE.OLEDB.16.0;Data Source={dbPath};Persist Security Info=False;";

            ReloadMappings();

            UpdateWindowTitle();
            LoadGrid();
        }

        /// <summary>
        /// Reloads the mapping from the database and replaces the repository, which also drops
        /// its cached schema and RouteInfo names.
        /// </summary>
        private void ReloadMappings()
        {
            LoadColumnMap();
            _repo = new AccessRepository(connectionString, columnToTableMap, userIgnoredLabels);
        }

        /// <summary>
        /// Lets the user map or ignore the labels that blocked an order. Returns true when
        /// choices were saved and the order should be retried.
        /// </summary>
        private bool ResolveMappings(InsertOutcome outcome, string displayFile)
        {
            if (outcome.UnresolvedLabels.Count == 0)
                return false;

            string orderNumber = string.IsNullOrWhiteSpace(outcome.OrderNumber) ? "(Unknown)" : outcome.OrderNumber!;
            return ResolveLabels(outcome.UnresolvedLabels, orderNumber, Path.GetFileName(displayFile), introText: null);
        }

        /// <summary>
        /// Shows the Unrecognized Items dialog for these labels and saves the choices. Returns true
        /// when choices were saved (mappings are reloaded). introText replaces the dialog's
        /// opening sentence; null uses the import wording.
        /// </summary>
        private bool ResolveLabels(IReadOnlyList<UnresolvedLabel> items, string orderNumber, string fileName, string? introText)
        {
            Dictionary<string, List<string>> schema;
            try
            {
                schema = _repo.GetSchema();
            }
            catch (Exception ex)
            {
                ImportLogger.Log($"Could not read the database schema to resolve mappings: {ex.Message}");
                MessageBox.Show(this, $"The database tables could not be read.\n\n{ex.Message}",
                    "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }

            using var dlg = new ResolveMappingsForm(
                orderNumber,
                fileName,
                items,
                MappingRules.GetSelectableTargets(schema),
                columnToTableMap,
                introText);

            if (dlg.ShowDialog(this) != DialogResult.OK)
                return false;

            try
            {
                new ColumnMapRepository(connectionString).SaveChoices(dlg.Choices);
            }
            catch (Exception ex)
            {
                ImportLogger.Log($"Saving mappings for Order {orderNumber} failed: {ex.Message}");
                MessageBox.Show(this, $"Your choices could not be saved.\n\n{ex.Message}",
                    "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }

            foreach (var choice in dlg.Choices)
            {
                ImportLogger.Log(choice.Ignore
                    ? $"Mapping saved (Order {orderNumber}): \"{choice.PdfLabel}\" → ignored"
                    : $"Mapping saved (Order {orderNumber}): \"{choice.PdfLabel}\" → {choice.TableName}.{choice.ColumnName}");
            }

            ReloadMappings();
            return true;
        }

        /// <summary>
        /// Loads the PDF label → column mapping from the database's ColumnMap table, creating the
        /// table and seeding it from ColumnToTableMap.json the first time a database is opened.
        /// If anything fails the map is left empty, so every order reports missing mappings and
        /// nothing is inserted.
        /// </summary>
        private void LoadColumnMap()
        {
            columnToTableMap = new(StringComparer.OrdinalIgnoreCase);
            userIgnoredLabels = new(StringComparer.OrdinalIgnoreCase);

            try
            {
                var mapRepo = new ColumnMapRepository(connectionString);
                mapRepo.EnsureTable();

                if (mapRepo.IsEmpty())
                    SeedColumnMap(mapRepo);

                columnToTableMap = mapRepo.LoadActiveMap();
                userIgnoredLabels = mapRepo.LoadIgnoredLabels();
            }
            catch (Exception ex)
            {
                ImportLogger.Log($"Failed to load column mappings from {dbPath}: {ex.Message}");
                MessageBox.Show(
                    $"The column mappings could not be loaded from the database.\n\n{ex.Message}\n\n" +
                    "Orders cannot be imported until this is fixed.",
                    "Column Mapping Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void SeedColumnMap(ColumnMapRepository mapRepo)
        {
            string jsonPath = ColumnMapLoader.DefaultPath;

            if (!File.Exists(jsonPath))
            {
                ImportLogger.Log($"ColumnMap table is empty and {jsonPath} was not found; nothing to seed.");
                MessageBox.Show(
                    "This database has no column mappings yet, and the starter mapping file was not found:\n\n" +
                    jsonPath,
                    "No Column Mappings", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var result = mapRepo.Seed(ColumnMapLoader.LoadEntries(jsonPath));

            ImportLogger.Log($"Seeded ColumnMap in {dbPath} with {result.Inserted} mapping(s) from {jsonPath}.");
            foreach (var dup in result.Duplicates)
                ImportLogger.Log($"Seed skipped duplicate label \"{dup.PdfLabel}\" → {dup.TableName}.{dup.ColumnName}");

            string message = $"Column mappings were copied into this database ({result.Inserted} mapping(s)).";
            if (result.Duplicates.Count > 0)
                message += $"\n\n{result.Duplicates.Count} duplicate label(s) were skipped. See the import log for details.";

            MessageBox.Show(message, "Column Mappings Set Up", MessageBoxButtons.OK, MessageBoxIcon.Information);
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

        /// <summary>
        /// Returns the PDFs to process: order forms as they are, and each picklist split into one
        /// temporary PDF per order. Temporary files are added to tempFiles (the caller purges them)
        /// and mapped back to their picklist in sourceFileMap.
        /// </summary>
        private List<string> ExpandPicklists(
            IEnumerable<string> files,
            List<string> tempFiles,
            Dictionary<string, string> sourceFileMap)
        {
            var allFiles = new List<string>();

            foreach (var file in files)
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

            return allFiles;
        }

        private void ImportPdfFiles(IEnumerable<string>? files)
        {
            var fileList = files?.ToList() ?? new List<string>();
            var allFiles = new List<string>();
            var tempFiles = new List<string>();
            var duplicates = new List<string>();
            var skippedForMappings = new List<string>();
            var sourceFileMap = new Dictionary<string, string>(); // temp file → original source file

            ImportOrderForm.Enabled = false;

            try
            {
                allFiles.AddRange(ExpandPicklists(fileList, tempFiles, sourceFileMap));

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

                    // Let the user fix unrecognized items, then retry the same order. Each attempt
                    // runs in its own transaction, so a failed attempt leaves nothing behind.
                    while (outcome.Status == InsertStatus.MissingColumnMappings &&
                           ResolveMappings(outcome, displayFile))
                    {
                        outcome = _repo.InsertDataIntoDatabase(processedLines.ToArray(), displayFile);
                    }

                    switch (outcome.Status)
                    {
                        case InsertStatus.DuplicateOrderNumber:
                            duplicates.Add(outcome.OrderNumber ?? "(Unknown)");
                            break;

                        case InsertStatus.MissingColumnMappings:
                            string label = string.IsNullOrWhiteSpace(outcome.OrderNumber)
                                ? "(Unknown)" : outcome.OrderNumber!;
                            skippedForMappings.Add($"Order {label}: {string.Join("; ", outcome.MissingColumns)}");
                            ImportLogger.Log($"Skipped Order {label} with unresolved items.");
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

                if (skippedForMappings.Count > 0)
                {
                    using var dlg = new SelectableMessageForm(
                        "Orders Skipped",
                        $"{skippedForMappings.Count} order(s) were skipped because some items were not set up:"
                        + Environment.NewLine + Environment.NewLine
                        + string.Join(Environment.NewLine, skippedForMappings));
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
                    ConnectToDatabase();
                    MessageBox.Show($"Database path saved: {dbPath}");
                }
            }
        }

        private void columnMappingsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            MappingManagerForm dlg;
            try
            {
                dlg = new MappingManagerForm(connectionString, _repo.GetSchema());
            }
            catch (Exception ex)
            {
                ImportLogger.Log($"Could not open the mapping manager: {ex.Message}");
                MessageBox.Show(this, $"The column mappings could not be opened.\n\n{ex.Message}",
                    "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            using (dlg)
            {
                dlg.ShowDialog(this);

                if (dlg.MappingsChanged)
                    ReloadMappings();
            }
        }

        private void testPdfToolStripMenuItem_Click(object sender, EventArgs e)
        {
            using var open = new OpenFileDialog
            {
                Filter = "PDF Files|*.pdf",
                Title = "Select Order Form(s) or Picklist(s) to Test",
                Multiselect = true
            };

            if (open.ShowDialog(this) != DialogResult.OK) return;

            var tempFiles = new List<string>();
            var sourceFileMap = new Dictionary<string, string>();
            var parsed = new List<(string DisplayFile, List<ProcessedLine> Lines)>();

            try
            {
                Cursor = Cursors.WaitCursor;

                foreach (var file in ExpandPicklists(open.FileNames, tempFiles, sourceFileMap))
                {
                    string displayFile = Path.GetFileName(sourceFileMap.TryGetValue(file, out var source) ? source : file);

                    try
                    {
                        parsed.Add((displayFile, _pdfService.ParsePdfFile(file)));
                    }
                    catch (Exception ex)
                    {
                        Cursor = Cursors.Default;
                        MessageBox.Show(this, $"{displayFile}\n\n{ex.Message}", "PDF Parse Error",
                            MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"The PDF could not be read.\n\n{ex.Message}", "Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            finally
            {
                Cursor = Cursors.Default;
                PdfSplitService.PurgeGeneratedFiles(tempFiles);
            }

            if (parsed.Count == 0) return;

            // Re-run against the current mappings each time, so the window can refresh
            // after the user sets up unrecognized items.
            List<OrderCheck> RunCheck()
            {
                var schema = _repo.GetSchema();
                return parsed.Select(p =>
                {
                    string orderNumber = PdfLabelChecker.FindOrderNumber(p.Lines);
                    bool exists = orderNumber.Length > 0 && _repo.OrderExists(orderNumber);
                    var labels = PdfLabelChecker.Check(p.Lines, columnToTableMap, userIgnoredLabels, schema);
                    return new OrderCheck(p.DisplayFile, orderNumber, exists, labels);
                }).ToList();
            }

            bool SetUpItems(IReadOnlyList<UnresolvedLabel> items, string orderNumbers, string source) =>
                ResolveLabels(items, orderNumbers, source,
                    $"Testing {source} found {items.Count} item(s) that are not set up. Nothing has been imported.");

            PdfTestForm dlg;
            try
            {
                dlg = new PdfTestForm(RunCheck, SetUpItems);
            }
            catch (Exception ex)
            {
                ImportLogger.Log($"Testing PDF(s) failed: {ex.Message}");
                MessageBox.Show(this, $"The PDF could not be checked.\n\n{ex.Message}",
                    "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            using (dlg)
                dlg.ShowDialog(this);
        }

        private void btnShowIgnoredColumns_Click(object sender, EventArgs e)
        {
            var ignored = ImportConstants.IgnoredColumns.Union(userIgnoredLabels, StringComparer.OrdinalIgnoreCase).ToList();

            if (ignored.Count == 0)
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
                string.Join("\n", ignored.OrderBy(c => c)),
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