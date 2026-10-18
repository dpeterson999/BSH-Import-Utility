using System;
using System.Collections.Generic;

namespace BSH_Import_Utility.Domain
{
    public static class ImportConstants
    {
        /// <summary>
        /// Household and contact fields on the form that are never stored. These are always
        /// ignored, on top of any labels ignored in the ColumnMap table.
        /// </summary>
        public static readonly HashSet<string> IgnoredColumns = new(StringComparer.OrdinalIgnoreCase)
        {
            "# in household",
            "Notify via Text (# below)",
            "Notify via Email (address below)",
            "Address"
        };

        public const string EmailLabel = "Notify via Email (address below)";
        public const string StorehouseLabel = "Storehouse | Pickup location";
        public const string TextLabel = "Notify via Text (# below)";

        public const string BshTableName = "BSH";
        public const string RouteInfoTableName = "RouteInfo";
        public const string RouteInfoStopNameColumn = "StopName";
        public const string ColumnMapTableName = "ColumnMap";
        public const string OrderNumberColumn = "Order Number";

        /// <summary>
        /// Section of an exported mapping JSON file that lists ignored labels (label → "").
        /// </summary>
        public const string IgnoredJsonKey = "_ignored";

        /// <summary>
        /// Section of an exported mapping JSON file that holds inactive rows, laid out like the
        /// rest of the file (table → label → column, plus its own <see cref="IgnoredJsonKey"/>).
        /// </summary>
        public const string InactiveJsonKey = "_inactive";

        /// <summary>
        /// Section of an exported mapping JSON file that holds row notes (label → note).
        /// </summary>
        public const string NotesJsonKey = "_notes";
    }
}