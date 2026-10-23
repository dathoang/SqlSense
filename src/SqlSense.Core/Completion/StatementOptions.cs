using System;
using System.Collections.Generic;

namespace SqlSense.Core.Completion
{
    /// <summary>One entry of a fixed option list. "|" in <see cref="Text"/> marks the caret after commit.</summary>
    public sealed class StatementOption
    {
        public StatementOption(string text, string description)
        {
            Text = text;
            Description = description;
        }

        public string Text { get; }
        public string Description { get; }
    }

    /// <summary>Options offered inside BACKUP, RESTORE, DBCC, ALTER DATABASE, ALTER INDEX and UPDATE STATISTICS.</summary>
    public static class StatementOptions
    {
        private static StatementOption O(string text, string description) => new StatementOption(text, description);

        public static readonly Dictionary<string, StatementOption[]> Lists = new Dictionary<string, StatementOption[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["BACKUP"] = new[]
            {
                O("DATABASE", "full or differential backup of a database"),
                O("LOG", "transaction log backup (FULL or BULK_LOGGED recovery only)"),
            },
            ["RESTORE"] = new[]
            {
                O("DATABASE", "restore a database from a backup"),
                O("LOG", "restore a transaction log backup"),
                O("FILELISTONLY FROM", "list the files in a backup (logical names for MOVE)"),
                O("HEADERONLY FROM", "list the backup sets in a backup file"),
                O("VERIFYONLY FROM", "check that a backup is complete and readable"),
                O("LABELONLY FROM", "show the media header"),
            },
            ["BACKUP TO"] = new[]
            {
                O("DISK = N'|'", "backup file; a name without a folder goes to the server's default backup folder"),
                O("URL = N'|'", "Azure Blob Storage or S3-compatible URL"),
            },
            ["RESTORE FROM"] = new[]
            {
                O("DISK = N'|'", "backup file; a name without a folder is read from the server's default backup folder"),
                O("URL = N'|'", "Azure Blob Storage or S3-compatible URL"),
            },
            ["BACKUP WITH"] = new[]
            {
                O("COMPRESSION", "compress the backup"),
                O("COPY_ONLY", "backup outside the regular sequence; doesn't reset the differential base or truncate the log"),
                O("DIFFERENTIAL", "only extents changed since the last full backup"),
                O("CHECKSUM", "verify page checksums and add a backup checksum"),
                O("INIT", "overwrite the backup sets in the file"),
                O("NOINIT", "append to the file (default)"),
                O("FORMAT", "write a new media header (overwrites the whole media set)"),
                O("STATS = 10", "report progress every 10 percent"),
                O("NAME = N'|'", "name of the backup set"),
                O("DESCRIPTION = N'|'", "description of the backup set"),
                O("ENCRYPTION (ALGORITHM = AES_256, SERVER CERTIFICATE = |)", "encrypt the backup with a certificate"),
                O("NO_TRUNCATE", "log backup of a damaged database (tail-log)"),
                O("NORECOVERY", "tail-log backup: leaves the database RESTORING"),
                O("MAXTRANSFERSIZE = 4194304", "bytes per transfer (up to 4 MB)"),
                O("BUFFERCOUNT = 50", "number of I/O buffers"),
                O("BLOCKSIZE = 65536", "physical block size"),
                O("EXPIREDATE = '|'", "date after which the backup set can be overwritten"),
                O("RETAINDAYS = 7", "days before the backup set can be overwritten"),
            },
            ["RESTORE WITH"] = new[]
            {
                O("RECOVERY", "bring the database online (default)"),
                O("NORECOVERY", "leave the database RESTORING for more backups (differential, log)"),
                O("STANDBY = N'|'", "read-only between log restores; undo file path"),
                O("REPLACE", "overwrite an existing database with this name"),
                O("MOVE N'|' TO N''", "put a file (logical name) at a new path"),
                O("STATS = 10", "report progress every 10 percent"),
                O("FILE = 1", "which backup set in the file (see RESTORE HEADERONLY)"),
                O("CHECKSUM", "verify the backup checksum"),
                O("CONTINUE_AFTER_ERROR", "keep restoring past errors"),
                O("STOPAT = '|'", "point-in-time restore of a log backup"),
                O("KEEP_REPLICATION", "keep replication settings"),
                O("RESTRICTED_USER", "only db_owner, dbcreator and sysadmin may connect"),
            },
            ["DBCC"] = new[]
            {
                O("CHECKDB", "check the logical and physical integrity of a database"),
                O("CHECKTABLE", "check the integrity of one table or indexed view"),
                O("CHECKALLOC", "check disk space allocation"),
                O("CHECKCATALOG", "check catalog consistency"),
                O("CHECKCONSTRAINTS", "find rows that violate CHECK or foreign key constraints"),
                O("CHECKIDENT", "show or reseed the identity value of a table"),
                O("SHRINKFILE", "shrink a data or log file"),
                O("SHRINKDATABASE", "shrink all files of a database"),
                O("SHOW_STATISTICS", "show a statistics object's header, density and histogram"),
                O("UPDATEUSAGE", "correct page and row counts"),
                O("OPENTRAN", "oldest active transaction"),
                O("INPUTBUFFER", "last statement sent by a session"),
                O("SQLPERF(LOGSPACE)", "log space used by every database"),
                O("FREEPROCCACHE", "clear the plan cache"),
                O("DROPCLEANBUFFERS", "clear the buffer pool (test servers only)"),
                O("FREESYSTEMCACHE('ALL')", "clear unused cache entries"),
                O("TRACEON", "enable a trace flag"),
                O("TRACEOFF", "disable a trace flag"),
                O("TRACESTATUS", "status of trace flags"),
                O("USEROPTIONS", "SET options of this session"),
                O("CLONEDATABASE", "schema-only copy of a database, for diagnostics"),
            },
            ["DBCC WITH"] = new[]
            {
                O("NO_INFOMSGS", "only report errors"),
                O("ALL_ERRORMSGS", "every error for every object"),
                O("PHYSICAL_ONLY", "faster check of the physical structure only"),
                O("DATA_PURITY", "check column values are in range"),
                O("EXTENDED_LOGICAL_CHECKS", "logical checks of indexed views, XML and spatial indexes"),
                O("TABLOCK", "take locks instead of an internal snapshot"),
                O("ESTIMATEONLY", "estimate tempdb space needed"),
                O("MAXDOP = 4", "limit parallelism"),
            },
            ["ALTER DATABASE"] = new[]
            {
                O("SET", "change a database option"),
                O("SET SINGLE_USER WITH ROLLBACK IMMEDIATE", "disconnect everyone else and allow one connection"),
                O("SET MULTI_USER", "allow all connections again"),
                O("SET OFFLINE WITH ROLLBACK IMMEDIATE", "take the database offline"),
                O("SET ONLINE", "bring the database online"),
                O("SET READ_ONLY", "make the database read-only"),
                O("SET READ_WRITE", "make the database writable"),
                O("MODIFY FILE (NAME = |, SIZE = )", "change size, growth or path of a file"),
                O("ADD FILE (NAME = N'|', FILENAME = N'')", "add a data file"),
                O("ADD LOG FILE (NAME = N'|', FILENAME = N'')", "add a log file"),
                O("MODIFY NAME = |", "rename the database"),
                O("COLLATE |", "change the default collation"),
            },
            ["DATABASE SET"] = new[]
            {
                O("RECOVERY SIMPLE", "log truncates at checkpoint; no log backups"),
                O("RECOVERY FULL", "log kept until backed up; point-in-time restore"),
                O("RECOVERY BULK_LOGGED", "minimally logged bulk operations"),
                O("SINGLE_USER WITH ROLLBACK IMMEDIATE", "one connection; rolls back other sessions"),
                O("RESTRICTED_USER WITH ROLLBACK IMMEDIATE", "only db_owner, dbcreator and sysadmin"),
                O("MULTI_USER", "all connections allowed"),
                O("OFFLINE WITH ROLLBACK IMMEDIATE", "take the database offline"),
                O("ONLINE", "bring the database online"),
                O("READ_ONLY", "read-only"),
                O("READ_WRITE", "writable"),
                O("COMPATIBILITY_LEVEL = 160", "query processor behaviour (160 = SQL Server 2022, 170 = 2025)"),
                O("READ_COMMITTED_SNAPSHOT ON", "read committed uses row versions instead of shared locks"),
                O("ALLOW_SNAPSHOT_ISOLATION ON", "allow SNAPSHOT isolation"),
                O("AUTO_CLOSE OFF", "keep the database open"),
                O("AUTO_SHRINK OFF", "no automatic shrinking"),
                O("AUTO_CREATE_STATISTICS ON", "create statistics automatically"),
                O("AUTO_UPDATE_STATISTICS ON", "update statistics automatically"),
                O("AUTO_UPDATE_STATISTICS_ASYNC ON", "update statistics in the background"),
                O("PAGE_VERIFY CHECKSUM", "detect damaged pages"),
                O("QUERY_STORE = ON", "capture query plans and runtime statistics"),
                O("TRUSTWORTHY OFF", "trust modules impersonating across databases"),
                O("DELAYED_DURABILITY = DISABLED", "transaction log durability"),
                O("CHANGE_TRACKING = ON (CHANGE_RETENTION = 2 DAYS, AUTO_CLEANUP = ON)", "track changed rows"),
                O("ENCRYPTION ON", "transparent data encryption"),
                O("ACCELERATED_DATABASE_RECOVERY = ON", "fast recovery and instant rollback"),
                O("TARGET_RECOVERY_TIME = 60 SECONDS", "indirect checkpoints"),
                O("PARAMETERIZATION FORCED", "parameterize all queries"),
            },
            ["RECOVERY"] = new[]
            {
                O("SIMPLE", "log truncates at checkpoint; no log backups"),
                O("FULL", "log kept until backed up; point-in-time restore"),
                O("BULK_LOGGED", "minimally logged bulk operations"),
            },
            ["ALTER INDEX"] = new[]
            {
                O("REBUILD", "rebuild the index (removes fragmentation, updates its statistics)"),
                O("REBUILD WITH (ONLINE = ON)", "rebuild without blocking (Enterprise edition)"),
                O("REBUILD WITH (SORT_IN_TEMPDB = ON)", "sort in tempdb"),
                O("REBUILD WITH (DATA_COMPRESSION = PAGE)", "rebuild with page compression"),
                O("REBUILD PARTITION = |", "rebuild one partition"),
                O("REORGANIZE", "defragment the leaf level online"),
                O("REORGANIZE WITH (LOB_COMPACTION = ON)", "also compact LOB pages"),
                O("DISABLE", "disable the index"),
                O("SET (ALLOW_PAGE_LOCKS = ON)", "change an index option"),
            },
            ["UPDATE STATISTICS"] = new[]
            {
                O("WITH FULLSCAN", "read every row"),
                O("WITH SAMPLE 25 PERCENT", "read a sample"),
                O("WITH RESAMPLE", "reuse each statistic's last sample rate"),
                O("WITH FULLSCAN, PERSIST_SAMPLE_PERCENT = ON", "keep the full scan rate for automatic updates"),
                O("WITH NORECOMPUTE", "turn off automatic updates for these statistics"),
            },
        };
    }
}
