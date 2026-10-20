using System;

namespace SqlSense
{
    internal static class PackageGuids
    {
        public const string PackageString = "b6a1f3d2-7c4e-4a5b-9f10-2e8d6c4b3a91";
        public const string CommandSetString = "d9c4e7a1-5b2f-4c3d-8e6a-0f1b2c3d4e5f";

        public static readonly Guid CommandSet = new Guid(CommandSetString);
    }

    internal static class PackageIds
    {
        public const int MenuGroup = 0x1020;
        public const int FormatSql = 0x0100;
        public const int RefreshSuggestions = 0x0101;
        public const int ToggleEnabled = 0x0102;
        public const int ExpandWildcards = 0x0103;
        public const int ShowLog = 0x0104;
        public const int OpenResultsInExcel = 0x0105;
        public const int ScriptResultsAsInsert = 0x0106;
        public const int ImportSqlPromptStyle = 0x0107;
    }
}
