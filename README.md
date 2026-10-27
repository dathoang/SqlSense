# SqlSense for SSMS 22

An independent T-SQL productivity extension for **SQL Server Management Studio 22** (x64, VS 18 shell):
schema-aware suggestions, autocomplete generators, snippets and a safe formatter.

**Website and user guide (Vietnamese): https://dathoang.github.io/SqlSense/** ·
**Download: [latest release](https://github.com/dathoang/SqlSense/releases/latest)**

Its design follows the general architecture of Redgate SQL Prompt (an editor command filter in front of the SQL
language service, a background metadata cache, and a rewrite-style formatter), but none of SQL Prompt's code or
binaries are used. The parser is Microsoft's open-source `Microsoft.SqlServer.TransactSql.ScriptDom`.

## Features

**Suggestions** (open as you type, or with Ctrl+Space)
- Context-aware: tables/views/TVFs after `FROM`/`JOIN`/`APPLY`/`UPDATE`/`INSERT INTO`, procedures after `EXEC`,
  columns + functions + variables + keywords in expressions, data types after `DECLARE @x` and `CAST(… AS`,
  databases after `USE`, `@variables` declared in the batch.
- Dotted names: `alias.` → that table's columns, `schema.` → objects in the schema, `db.` → schemas.
- Three- and four-part names: `FROM`/`EXEC` also list databases and linked servers; `OtherDb.` → its schemas,
  `OtherDb.schema.` (or `OtherDb..`) → its objects, `LinkedServer.` → its databases, `LinkedServer.db.schema.` → its
  tables; aliases of such tables get their columns, and hover works on every part. Another database's or a linked
  server's metadata loads in the background the first time it's named; the list reopens when it arrives.
- Knows CTEs, temp tables (`CREATE TABLE #t`, `SELECT … INTO #t`), table variables, derived tables, and `UPDATE a … FROM t a`.
- Fuzzy matching: `soh` finds `SalesOrderHeader`; prefix matches rank first.
- Metadata loads in the background per server/database/login and is cached (30 min by default); `USE db` switches the
  database used for suggestions. The status bar shows an animated "loading metadata for SERVER/Db…" while a first load
  runs, then the object count and time, or the error (option: General › Show metadata loading in the status bar).
- Refresh: Refresh Suggestions (Ctrl+Shift+D) reloads the window's database right away while suggestions keep the old
  metadata; Refresh All Suggestions marks every cached database for reload. Executing a script that creates, alters or
  drops objects (or `SELECT … INTO`, `sp_rename`) reloads the affected databases automatically (option: General ›
  Refresh metadata after running CREATE/ALTER/DROP).

**Autocomplete**
- Table aliases: picking `Customers` in `FROM` inserts `dbo.Customers AS c` (configurable, no collisions).
- `JOIN … ON` offers foreign-key conditions (`soh.CustomerID = c.CustomerID`), then conditions on same-named columns
  (key-like names first, combined with `AND` when there are several; also for CTEs and temp tables).
- `INSERT INTO` + table inserts the column list (no identity/computed columns) and a typed `VALUES` template.
- `EXEC` + procedure inserts its parameters with typed defaults.
- `ALTER PROCEDURE` / `FUNCTION` / `VIEW` / `TRIGGER` lists objects of that kind (also after `DROP … [IF EXISTS]` and
  `CREATE OR ALTER`); in an empty batch, picking one after `ALTER` loads its definition in the background and replaces
  the statement with it (CREATE → ALTER). `CREATE TRIGGER t ON ` lists tables and views.
- Security statements list what exists: `ALTER|DROP ROLE|USER|LOGIN|SCHEMA|DATABASE`, `ALTER [SERVER] ROLE … ADD MEMBER`,
  `GRANT … ON` (securables), `GRANT|DENY … TO` / `REVOKE … FROM` (users and roles, or logins for server permissions),
  `SCHEMA::` / `ROLE::` / `OBJECT::`, `FOR LOGIN`, `DEFAULT_SCHEMA =`, `AUTHORIZATION`.
- Maintenance statements: `BACKUP|RESTORE DATABASE|LOG` (databases, `TO DISK = N'<db>_<date>.bak'`, recent backup files
  from msdb after `RESTORE … FROM`, `WITH` options), `DBCC` commands and their arguments (databases, tables, files),
  `ALTER DATABASE … SET` options and `MODIFY FILE`, `ALTER|DROP INDEX` (indexes with their table, `REBUILD`/`REORGANIZE`),
  `CREATE INDEX … ON t (` columns, `UPDATE STATISTICS`. Snippets `bakd`, `bakl`, `rsdb`, `chkdb`.

**Hover info**
- Resting the mouse on a name shows what it is: tables and views (columns, types, foreign keys), columns, procedure and
  function parameters, triggers, CTEs and temp tables, variables, built-in functions, schemas, users, roles (members),
  logins, indexes and databases (state, recovery model, files, last backups). Option: General › Show object info on hover.
- Tab after `*` (or `alias.*`) expands to the column list; Ctrl+B, Ctrl+W does the same from anywhere on the line.
- Auto-closes `(` `[` `'` and types over the closer.
- Snippets: `ssf`, `st100`, `scf`, `ii`, `uu`, `df`, `ij`, `lj`, `bt`, `tc`, `cte`, `cur`, `cp`, `cv`, `ct`, `tt`, … then Tab.
  Add your own as `<shortcut>.sql` files in `Documents\SqlSense\Snippets` (`$CURSOR$` marks the caret).

**Formatter** (Ctrl+K, Ctrl+Y, or Edit › Format Document / Format Selection)
- Clause layout, aligned select lists, JOINs under FROM, `AND`/`OR` breaks, CASE blocks, BEGIN/END, IF/ELSE,
  TRY/CATCH, subqueries, CTEs, MERGE, CREATE TABLE column lists; keyword/function/data type casing.
- Keeps comments and blank lines between statements.
- **Safe by construction:** after formatting, the token stream is compared with the original. If anything other than
  whitespace or word case would change, nothing is applied and the reason is shown in the status bar.

**Code folding** (VS Code style)
- Collapsible regions: `BEGIN … END` (TRY/CATCH), multi-line `CASE … END`, statements (`SELECT`, `INSERT`, `UPDATE`,
  `DELETE`, `MERGE`, `WITH`, `EXEC`, `DECLARE`, `CREATE`/`ALTER`), whole procedures/functions/views/triggers,
  multi-line `( … )`, `/* */` and consecutive `--` comments, `-- #region … -- #endregion`.
- Ctrl+K, Ctrl+1…7 fold that nesting level (except the region holding the caret), Ctrl+K, Ctrl+0 folds all,
  Ctrl+K, Ctrl+J unfolds all, Ctrl+K, Ctrl+[ / ] fold/unfold recursively, Ctrl+K, Ctrl+8 folds all `#region`s;
  also Tools › SqlSense › Folding. Options: General › Code folding, Fold statements.

**Results grid** (right-click)
- Open Results in Excel: the grid (or the selected cells) as a typed `.xlsx`.
- Script Results as INSERT: `INSERT … VALUES` statements in a new query window, target table guessed from the query.

**Connection colors**
- Each query window's tab, editor frame and status bar are colored by server/database, from rules in
  `Documents\SqlSense\ConnectionColors.txt` (`server[/database] = color`, wildcards). The tab's right-click menu
  › Connection Color sets a rule for the window's database or server.

**Menu:** Tools › SqlSense (Format SQL, Expand Wildcard, Folding, Refresh Suggestions, Refresh All Suggestions, Enable/Disable, Show Log,
Import SQL Prompt Style, Edit Connection Colors).
**Options:** Tools › Options › SqlSense (General, Suggestions, Formatting, Connection Colors).

User guide (Vietnamese), from install to the main use cases: [deploy/HUONG-DAN-SU-DUNG.md](deploy/HUONG-DAN-SU-DUNG.md).

## Install

Download `SqlSense-<version>.zip` from the [latest release](https://github.com/dathoang/SqlSense/releases/latest)
(currently [1.4.0](https://github.com/dathoang/SqlSense/releases/tag/v1.4.0)) or build it (below), unzip it,
close SSMS 22 and double-click `Install.cmd`. It finds every
SSMS 22 instance with vswhere and runs SSMS's own `VSIXInstaller.exe` (per user, no admin rights). Double-clicking
the `.vsix` itself may install into Visual Studio instead. `Uninstall.cmd` removes it.

Manual install: `VSIXInstaller.exe /instanceIds:<id> SqlSense.vsix` from SSMS's `Common7\IDE`, where `<id>` is
`InstallationID` in `SSMS.isolation.ini`; uninstall with `/uninstall:SqlSense.Ssms22.6f2d0c55-3b1e-4f4e-9d7a-1c8a52b7e0a1`.

**Turn off SSMS's built-in IntelliSense** so two lists don't open at once. SqlSense does this by default
(Options › SqlSense › General); otherwise Tools › Options › Text Editor › Transact-SQL › IntelliSense.

## Build

Requires Visual Studio 2026 (or 2022) with the *Visual Studio extension development* workload, the .NET SDK and
Node.js 22+ (renders the user guide to .docx via `deploy\tools`; `npm ci` runs there on first use).

```powershell
.\deploy\Build-Package.ps1 -Version 1.4.0           # tests, Rebuild Release, zip + separate .vsix and .docx in dist\
.\deploy\Build-Package.ps1 -Version 1.4.0 -Install  # ... and install into this machine's SSMS 22
```

Raise the version for every release: VSIXInstaller won't install the same version over itself. By hand:

```powershell
dotnet test tests\SqlSense.Core.Tests
& "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" src\SqlSense.Vsix\SqlSense.Vsix.csproj -restore -t:Rebuild -p:Configuration=Release
```

When the checkout path is long (over 100 characters) `Directory.Build.props` sends bin/obj to `%TEMP%\ssb` to avoid
Windows path-length failures; otherwise output is in the usual bin/obj folders.

## Layout

| Path | What |
|---|---|
| `src/SqlSense.Core` | Host-independent engine (netstandard2.0): lexer wrapper, statement/scope analysis, completion engine and generators, snippets, formatter. Fully unit tested. |
| `src/SqlSense.Vsix` | SSMS 22 shell: package + options + menu, `IVsTextView` command filter, legacy completion source/set, schema loader, SSMS connection bridge. |
| `deploy` | `Build-Package.ps1` (release build + zip), `package/` (Install/Uninstall scripts shipped to users), the user guide. |
| `tests/SqlSense.Core.Tests` | xUnit tests (context detection, completion, generators, ALTER scripting, formatter golden files, idempotence and token-safety, connection color rules, export). |

### How it maps to SQL Prompt's design

| SQL Prompt 10 (observed with REA) | SqlSense |
|---|---|
| `TextViewMonitor` → `IVsTextView.AddCommandFilter`, routes TYPECHAR/TAB/RETURN/arrows to the engine | `Editor/SqlCommandFilter.cs` |
| `TextLinesMonitor.DetectLanguageService` | `Editor/SqlDocument.IsSql` (lazy content-type check) |
| `PopupController` + WPF popup, `KeyInputRouter` | VS completion broker + `IntelliSense/SqlCompletionSet` (custom filter/ranking) |
| `SuggestionCalculator`, `PartialParseCalculator`, `ScriptCursorScope` | `Core/Analysis/ContextAnalyzer`, `StatementAnalysis` |
| `RankedCandidatePrioritizer` | `Core/Completion/CompletionMatcher` + priorities |
| `AliasGenerator`, `ExecStatementGenerator`, `InsertBodyGenerator`, `ExpandWildcardRefactoring` | `Core/Completion/Generators.cs`, `CompletionEngine` |
| `Snippets.*` with `$CURSOR$` placeholders | `Core/Completion/Snippets.cs` |
| `Format.IO.Rewrite` minimal-diff formatter | `Core/Formatting/SqlFormatter` (token stream + verification) |
| `ConnectionInfoRetriever`, `Cache`/`Metadata` | `Schema/SsmsConnection` (reflection on `SqlPackageBase.ServiceCache`), `SchemaLoader`, `SchemaService` |
| pkgdef autoload on `{e80ef1cb-…}` (ShellInitialized) | Same UI context, via `ProvideAutoLoad` |

## Known limitations

- Early version: the engine is unit-tested and the VSIX installs into SSMS 22, but editor integration has only had
  manual testing so far (see *Verify* below).
- Schema suggestions need Windows or SQL authentication. Microsoft Entra ID logins get keyword/function/snippet
  suggestions only (logged in Tools › SqlSense › Show Log). If SSMS doesn't expose a SQL login's password, the same
  applies.
- The current database is read from the window's connection (and `USE` statements in the script); switching the
  database dropdown is picked up when SSMS updates the connection info.
- Formatting style is fixed apart from the options listed; there is no line-length wrapping.

## Verify after installing

1. Open SSMS 22 and a query window connected to a database, then check Tools › SqlSense › Show Log for
   `SqlSense … loaded` and `Content type 'SQL…' → SQL`.
2. Type `SELECT * FROM ` and confirm the table list appears; pick one and check the alias.
3. Type `ssf` + Tab, `JOIN … ON ` (FK suggestion), `EXEC ` + a procedure, and `SELECT *` + Tab.
4. Paste a messy query and press Ctrl+K, Ctrl+Y.

## License

[MIT](LICENSE) © 2026 Dat Hoang. Bundles Microsoft.SqlServer.TransactSql.ScriptDom (MIT).
