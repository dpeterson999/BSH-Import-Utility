# BSH Import Utility

A Windows desktop app that reads Bishop's Order for Food and Supplies PDFs and saves each order into the BSH Access database: the order details go into the `BSH` table, and each ordered item goes into its category table (`CannedMeats`, `DairyAndEggs`, and so on).

## Requirements

- Windows with .NET Framework 4.8
- Microsoft Access Database Engine 2016 (x64), for the `Microsoft.ACE.OLEDB.16.0` provider
- An Access database (`.accdb` or `.mdb`) with the BSH tables

The app installs and updates itself with ClickOnce from https://dpeterson999.github.io/BSH-Import-Utility/. A new version is picked up the next time the app starts.

## Importing orders

1. Click **Import Order Form** and pick one or more PDFs. Both single order forms and storehouse picklists work. A picklist is split into one order per two pages.
2. Each order is saved in one step: all of it is saved, or none of it is.
3. At the end, the app lists any orders that were skipped:
   - **Already imported:** the order number is already in the `BSH` table.
   - **Not an order form:** no order number was found in the file.
   - **Items not set up:** see *Unrecognized items* below.

Every import is written to a log. Open it with **Menu → View Import Log**. The log is at `%AppData%\BSH Import Tool\import_log.txt`.

## Column mappings

Every line on the order form, for example `Chili 14.4 oz`, needs a *mapping* that says which table and column it is saved to (`CannedMeats → Chili`). Mappings are stored in the database itself, in a table named `ColumnMap`, so they can be changed without a new version of the app.

The first time the app opens a database without a `ColumnMap` table, it creates the table and fills it from `ColumnToTableMap.json`, which ships with the app. After that, the JSON file is only used to set up new databases.

Several labels can map to the same column. That is how older and newer wording of the same item on different versions of the form (for example `Salt 26 oz` and `Salt (iodized) 26 oz`) both land in `Seasonings → Salt`.

### Unrecognized items

If an order contains a label with no mapping, or a mapping to a column that no longer exists, the import stops before saving that order and shows the **Unrecognized Items** dialog. For each item you can:

- choose the table and column it belongs in (a suggestion is filled in when there is a clear match), or
- tick **Ignore** if it is not an order item, such as stray text from the PDF, or
- tick **Not in database** if its table or column hasn't been added yet (a new item or a new section on the form).

Click **Save and retry** to save your choices and import the order again, or **Skip this order** to leave it out. If you pick a table that differs from similar items, the app asks you to double-check, because a wrong category still imports "successfully", just in the wrong place.

The app never adds tables or columns to the database. When any item is marked **Not in database**, your other choices are saved, the order is not imported, and the app copies a note to send to whoever manages the database. The note lists each item with its form section and says what to add in Access:

- a new item in an existing section: a new column in that section's table, Number (Long Integer), Default Value 0
- a new section: a new table with an `Order Number` Short Text primary key related to `BSH.[Order Number]`, plus one column per item
- any queries or reports that list that table's items (for example `BreadAndDairy`) need the new column added too

Once it has been added, import the order again (or use **Test a PDF**). If asked, choose the new table and column.

The four order fields the app depends on (Order Number, Recipient, Ward and Storehouse) are fixed and cannot be changed from the app.

### Column Mappings screen

**Menu → Column Mappings…** lists every mapping.

- **Search** and **Show** narrow the list: active mappings, mappings to a missing column, ignored labels, inactive mappings, or anything changed in the last 30 days.
- **Add… / Edit…** create or change a mapping, with the same suggestions and checks as the import dialog. Each mapping can have a note.
- **Deactivate / Reactivate.** Mappings are never deleted. A deactivated mapping is ignored by imports, so orders containing that label stop and ask again.
- **Export to JSON…** saves every mapping, including inactive ones and notes, to a file. This is the backup.
- **Import from JSON…** shows a preview of what would be added or changed, saves a backup of the current mappings first, then applies the file. Each mapping in the file keeps its active or inactive state and its note. Mappings that are not in the file are left alone.

Backups made before each import are kept in `%AppData%\BSH Import Tool\Mapping Backups`. To undo an import, import the backup file.

### Test a PDF

**Menu → Test a PDF…** reads one or more order forms or picklists and shows what an import *would* do, without saving anything:

- a summary for each order: ready to import, items not set up, already imported, or no order number found
- each line from the PDF with its value, its result, and the table and column it would be saved to
- items on the form that aren't set up even though this order didn't include them, marked **Not set up (not ordered)**. They don't stop this import, but the first order that includes one would stop.

Use it to check a new version of the order form before importing real orders. **Set Up Unrecognized Items…** opens the same dialog an import uses, and the results refresh after you save.

## For developers

- Open `BSH Import Utility.sln` in Visual Studio. The app project needs full-framework MSBuild (Visual Studio's), not `dotnet build`, because of its ADOX COM reference.
- Unit tests for the mapping logic: `dotnet test Tests`
- Main pieces:
  - `Services/PdfImportService.cs` reads the PDF text into label/value lines.
  - `Services/PdfSplitService.cs` splits picklists into orders.
  - `Data/AccessRepository.cs` saves an order.
  - `Data/ColumnMapRepository.cs` reads and writes the `ColumnMap` table.
  - `Domain/` holds the rules that don't touch the database or UI, and is unit tested.

### Publishing

Publish only from `main`, after the feature branch has been merged. Publishing writes the ClickOnce output to `docs\` and bumps the version in the project file. Pushing those changes to `main` runs the GitHub Pages workflow, which deploys the new version. The manifest signing certificate is not in the repository; it must be installed on the publishing machine.
