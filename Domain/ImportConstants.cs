using System;
using System.Collections.Generic;

namespace BSH_Import_Utility.Domain
{
    public static class ImportConstants
    {
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
    }
}