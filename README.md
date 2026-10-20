# SqlSense for SSMS 22

An independent T-SQL productivity extension for **SQL Server Management Studio 22** (x64, VS 18 shell):
schema-aware suggestions, autocomplete generators, snippets and a safe formatter.

Its design follows the general architecture of Redgate SQL Prompt (an editor command filter in front of the SQL
language service, a background metadata cache, and a rewrite-style formatter), but none of SQL Prompt's code or
binaries are used. The parser is Microsoft's open-source `Microsoft.SqlServer.TransactSql.ScriptDom`.

## Features

**Suggestions** (open as you type, or with Ctrl+Space)
- Context-aware: tables/views/TVFs after `FROM`/`JOIN`/`APPLY`/`UPDATE`/`INSERT INTO`, procedures after `EXEC`,
  columns + functions + variables + keywords in expressions, data types after `DECLARE @x` and `CAST(… AS`,
  databases after `USE`, `@variables` declared in the batch.
- Dotted names: `alias.` → that table's columns, `schema.` → objects in the schema, `db.` → schemas.
- Knows CTEs, temp tables (`CREATE TABLE #t`, `SELECT … INTO #t`), table variables, derived tables, and `UPDATE a … FROM t a`.
- Fuzzy matching: `soh` finds `SalesOrderHeader`; prefix matches rank first.
- Metadata loads in the background per server/database/login and is cached (30 min by default); `USE db` switches the
  database used for suggestions.

**Autocomplete**
- Table aliases: picking `Customers` in `FROM` inserts `dbo.Customers AS c` (configurable, no collisions).
- `JOIN … ON` offers foreign-key conditions (`soh.CustomerID = c.CustomerID`).
- `INSERT INTO` + table inserts the column list (no identity/computed columns) and a typed `VALUES` template.
- `EXEC` + procedure inserts its parameters with typed defaults.
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

**Menu:** Tools › SqlSense (Format SQL, Expand Wildcard, Refresh Suggestions, Enable/Disable, Show Log).
**Options:** Tools › Options › SqlSense › General.

## Install

Close SSMS 22, then install into the SSMS instance (double-clicking the `.vsix` may pick Visual Studio instead):

```powershell
& "C:\Program Files\Microsoft SQL Server Management Studio 22\Release\Common7\IDE\VSIXInstaller.exe" /instanceIds:04cbd0c1 "dist\SqlSense.vsix"
```

`04cbd0c1` is this machine's SSMS 22 instance id (from `SSMS.isolation.ini`). To uninstall, use
Extensions › Manage Extensions in SSMS, or `VSIXInstaller.exe /instanceIds:04cbd0c1 /uninstall:SqlSense.Ssms22.6f2d0c55-3b1e-4f4e-9d7a-1c8a52b7e0a1`.

**Turn off SSMS's built-in IntelliSense** (Tools › Options › Text Editor › Transact-SQL › IntelliSense) so two lists
don't open at once. SQL Prompt does the equivalent automatically.

## Build

Requires Visual Studio 2026 (or 2022) with the *Visual Studio extension development* workload and the .NET SDK.

```powershell
& "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" src\SqlSense.Vsix\SqlSense.Vsix.csproj -restore -p:Configuration=Release
dotnet test tests\SqlSense.Core.Tests
```

When the checkout path is long (over 100 characters) `Directory.Build.props` sends bin/obj to `%TEMP%\ssb` to avoid
Windows path-length failures; otherwise output is in the usual bin/obj folders.

## Layout

| Path | What |
|---|---|
| `src/SqlSense.Core` | Host-independent engine (netstandard2.0): lexer wrapper, statement/scope analysis, completion engine and generators, snippets, formatter. Fully unit tested. |
| `src/SqlSense.Vsix` | SSMS 22 shell: package + options + menu, `IVsTextView` command filter, legacy completion source/set, schema loader, SSMS connection bridge. |
| `tests/SqlSense.Core.Tests` | 62 xUnit tests (context detection, completion, generators, formatter golden files, idempotence and token-safety). |

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
