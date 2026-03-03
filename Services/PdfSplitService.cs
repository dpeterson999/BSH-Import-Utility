using iText.Forms;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas.Parser;
using iText.Kernel.Pdf.Canvas.Parser.Listener;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace BSH_Import_Utility.Services
{
    public static class PdfSplitService
    {
        /// <summary>
        /// Returns true if this PDF is a picklist (contains summary pages before orders).
        /// Reliable marker: "Orders for Pickup or Delivery" appears only on picklist summary page 1.
        /// </summary>
        public static bool IsPicklist(string pdfPath)
        {
            using var reader = new PdfReader(pdfPath);
            using var pdf = new PdfDocument(reader);

            // Only need to check the first page — the summary header is always there
            string firstPage = PdfTextExtractor.GetTextFromPage(
                pdf.GetPage(1),
                new SimpleTextExtractionStrategy());

            return firstPage.IndexOf(
                "Orders for Pickup or Delivery",
                StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>
        /// Scans the PDF and returns the 1-based page number where the first
        /// "Bishop's Order for Food and Supplies" header appears.
        /// This is always the first page of the first individual order.
        /// Returns -1 if not found.
        /// </summary>
        public static int FindFirstOrderPage(string pdfPath)
        {
            using var reader = new PdfReader(pdfPath);
            using var pdf = new PdfDocument(reader);

            for (int page = 1; page <= pdf.GetNumberOfPages(); page++)
            {
                string text = PdfTextExtractor.GetTextFromPage(
                    pdf.GetPage(page),
                    new SimpleTextExtractionStrategy());

                if (text.IndexOf(
                        "s Order for Food and Supplies",
                        StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return page;
                }
            }

            return -1;
        }

        /// <summary>
        /// Flattens a PDF and splits it into 2-page PDFs starting at startPage.
        /// Uses the source PDF directory and returns generated files.
        /// </summary>
        public static List<string> FlattenAndSplitTwoPages(
            string sourcePdfPath,
            int startPage = 1)
        {
            if (!File.Exists(sourcePdfPath))
                throw new FileNotFoundException("Source PDF not found.", sourcePdfPath);

            string outputDir =
                Path.GetDirectoryName(sourcePdfPath)
                ?? throw new InvalidOperationException("Unable to determine PDF directory.");

            string flattenedPdfPath =
                Path.Combine(outputDir, "_flattened_temp.pdf");

            var outputFiles = new List<string>();

            try
            {
                FlattenPdf(sourcePdfPath, flattenedPdfPath);
                outputFiles = SplitIntoTwoPagePdfs(flattenedPdfPath, outputDir, startPage);
            }
            finally
            {
                if (File.Exists(flattenedPdfPath))
                    File.Delete(flattenedPdfPath);
            }

            return outputFiles;
        }

        private static void FlattenPdf(string source, string output)
        {
            using var reader = new PdfReader(source);
            using var writer = new PdfWriter(output);
            using var pdf = new PdfDocument(reader, writer);

            var form = PdfAcroForm.GetAcroForm(pdf, false);
            form?.FlattenFields();
        }

        private static List<string> SplitIntoTwoPagePdfs(
            string flattenedPdf,
            string outputDir,
            int startPage)
        {
            // Create a unique subdirectory for this split operation
            string uniqueDir = Path.Combine(outputDir,
                $"split_{Path.GetFileNameWithoutExtension(flattenedPdf)}_{Guid.NewGuid():N}");
            Directory.CreateDirectory(uniqueDir);

            var outputFiles = new List<string>();

            using var reader = new PdfReader(flattenedPdf);
            using var sourcePdf = new PdfDocument(reader);

            int totalPages = sourcePdf.GetNumberOfPages();

            if (startPage < 1 || startPage > totalPages)
                throw new ArgumentOutOfRangeException(nameof(startPage));

            int fileIndex = 1;

            for (int page = startPage; page <= totalPages; page += 2)
            {
                string outputPath = Path.Combine(uniqueDir, $"order_{fileIndex}.pdf");

                using (var writer = new PdfWriter(outputPath))
                using (var destPdf = new PdfDocument(writer))
                {
                    sourcePdf.CopyPagesTo(page, page, destPdf);
                    if (page + 1 <= totalPages)
                        sourcePdf.CopyPagesTo(page + 1, page + 1, destPdf);
                }

                outputFiles.Add(outputPath);
                fileIndex++;
            }

            return outputFiles;
        }

        /// <summary>
        /// Deletes generated output PDFs after import.
        /// </summary>
        public static void PurgeGeneratedFiles(IEnumerable<string> files)
        {
            var dirs = new HashSet<string>();

            foreach (var file in files)
            {
                try
                {
                    if (File.Exists(file))
                    {
                        dirs.Add(Path.GetDirectoryName(file)!);
                        File.Delete(file);
                    }
                }
                catch { }
            }

            foreach (var dir in dirs)
            {
                try
                {
                    if (Directory.Exists(dir) && !Directory.EnumerateFileSystemEntries(dir).Any())
                        Directory.Delete(dir);
                }
                catch { }
            }
        }
    }
}