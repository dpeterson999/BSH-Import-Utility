using BSH_Import_Utility.Domain;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas.Parser;
using iText.Kernel.Pdf.Canvas.Parser.Listener;
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

#nullable enable

namespace BSH_Import_Utility.Services
{
    public class PdfImportService
    {
        public List<ProcessedLine> ParsePdfFile(string filePath)
        {
            var processedLines = new List<ProcessedLine>();
            bool firstOrderFound = false;

            using (var pdfDocument = new PdfDocument(new PdfReader(filePath)))
            {
                for (int page = 1; page <= pdfDocument.GetNumberOfPages(); page++)
                {
                    var linesArray =
                        PdfTextExtractor.GetTextFromPage(
                            pdfDocument.GetPage(page),
                            new SimpleTextExtractionStrategy()
                        )
                        .Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);

                    for (int i = 0; i < linesArray.Length; i++)
                    {
                        ProcessLine(
                            linesArray[i],
                            i + 1 < linesArray.Length ? linesArray[i + 1] : null,
                            ref i,
                            ref firstOrderFound,
                            processedLines
                        );
                    }
                }
            }

            return processedLines;
        }

        private string NormalizeLabel(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return "";

            raw = raw.Trim();

            if (raw.StartsWith(ImportConstants.EmailLabel, StringComparison.OrdinalIgnoreCase)) return ImportConstants.EmailLabel;
            if (raw.StartsWith(ImportConstants.StorehouseLabel, StringComparison.OrdinalIgnoreCase)) return ImportConstants.StorehouseLabel;
            if (raw.StartsWith(ImportConstants.TextLabel, StringComparison.OrdinalIgnoreCase)) return ImportConstants.TextLabel;

            if (raw.StartsWith("Recipient", StringComparison.OrdinalIgnoreCase)) return "Recipient";
            if (raw.StartsWith("Ward", StringComparison.OrdinalIgnoreCase)) return "Ward";

            return raw;
        }

        private void ProcessLine(string line, string? nextLine, ref int index, ref bool firstOrderFound, List<ProcessedLine> processedLines)
        {
            // iText sometimes merges two adjacent field labels onto one extracted line,
            // especially when the email value is blank.
            // Example: "Notify via Email (address below) Storehouse | Pickup location"
            if (!string.IsNullOrWhiteSpace(line) &&
                line.IndexOf(ImportConstants.EmailLabel, StringComparison.OrdinalIgnoreCase) >= 0 &&
                line.IndexOf(ImportConstants.StorehouseLabel, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                // Treat email as blank (merged label indicates email field had no value)
                processedLines.Add(new ProcessedLine(ImportConstants.EmailLabel, ""));

                // Treat storehouse value as the next line (typical PDF layout)
                processedLines.Add(new ProcessedLine(ImportConstants.StorehouseLabel, nextLine?.Trim() ?? ""));

                // Consume nextLine (since we used it as the storehouse value)
                index++;

                return;
            }

            // Detect if the line contains Ward information
            if (IsWardLine(line))
            {
                processedLines.Add(new ProcessedLine("Ward", line.Trim()));
                return;
            }

            if (line.StartsWith("Order #") && !firstOrderFound)
            {
                processedLines.Add(new ProcessedLine("Order Number", line.Substring(6).TrimStart('#')));
                firstOrderFound = true;
            }
            else if (line.StartsWith("Notify via Text (# below) Notify via Email (address below)"))
            {
                // Split the line into two parts manually
                processedLines.Add(new ProcessedLine(ImportConstants.TextLabel, "")); // Always blank

                // Only add the email notify if the nextLine looks like an email
                if (!string.IsNullOrEmpty(nextLine) && IsValidEmail(nextLine!))
                {
                    processedLines.Add(new ProcessedLine(ImportConstants.EmailLabel, nextLine!.Trim()));
                    index++;
                }
                else
                {
                    processedLines.Add(new ProcessedLine(ImportConstants.EmailLabel, ""));
                }
            }
            else if (line.StartsWith("Recipient") ||
                     line.StartsWith(ImportConstants.StorehouseLabel) ||
                     line.StartsWith(ImportConstants.TextLabel) ||
                     line.StartsWith(ImportConstants.EmailLabel))
            {
                string value;

                if (line.StartsWith("Project Number") && nextLine?.StartsWith("Recipient") == true)
                {
                    value = ""; // Assume "Project Number" is empty
                }
                else
                {
                    value = nextLine?.Trim() ?? "";
                    index++;
                }

                processedLines.Add(new ProcessedLine(NormalizeLabel(line), value));
            }
            else if (int.TryParse(nextLine?.Trim(), out int nextValue) && nextValue != 0)
            {
                processedLines.Add(new ProcessedLine(line, nextValue));
                index++;
            }
            else
            {
                processedLines.Add(new ProcessedLine(line, 0));
            }
        }

        private bool IsValidEmail(string input)
        {
            return Regex.IsMatch(input, @"^[^@\s]+@[^@\s]+\.[^@\s]+$");
        }

        private bool IsWardLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
                return false;

            return
                (
                    (
                        line.IndexOf("Ward", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        line.IndexOf("Branch", StringComparison.OrdinalIgnoreCase) >= 0
                    )
                    &&
                    line.IndexOf("Stake", StringComparison.OrdinalIgnoreCase) >= 0
                )
                ||
                line.IndexOf("Community Service Organization", StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}