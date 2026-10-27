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
        public const int EditConnectionColors = 0x0108;
        public const int RefreshAllSuggestions = 0x0109;
        public const int TabEditConnectionColors = 0x010D;

        // Tools › SqlSense › Folding: Fold Level 1…FoldLevels from FoldLevelBase, then the other commands.
        public const int FoldLevelBase = 0x0110;
        public const int FoldLevels = 7;
        public const int FoldAll = 0x0117;
        public const int UnfoldAll = 0x0118;
        public const int FoldRecursively = 0x0119;
        public const int UnfoldRecursively = 0x011A;
        public const int FoldAllRegions = 0x011B;
        public const int FoldAllComments = 0x011C;

        // Document tab › Connection Color › Database / Server lists (SqlSense.vsct): a slot per palette
        // colour from the base id, then No Color, Custom... and Remove Rule at fixed offsets.
        public const int TabDbMenu = 0x1051;
        public const int TabServerMenu = 0x1052;
        public const int TabDbColorBase = 0x0200;
        public const int TabServerColorBase = 0x0240;
        public const int TabPaletteSlots = 18;
        public const int TabNoColorOffset = 0x20;
        public const int TabCustomOffset = 0x21;
        public const int TabRemoveOffset = 0x22;
    }
}
