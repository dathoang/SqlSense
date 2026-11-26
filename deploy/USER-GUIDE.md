# SqlSense for SSMS 22 — Installation and User Guide

SqlSense is an extension for **SQL Server Management Studio 22** (x64): schema-aware T-SQL suggestions, code generation
(aliases, JOIN conditions, INSERT/EXEC, `SELECT *` expansion, snippets), safe SQL formatting, level-based code folding like
VS Code, exporting results to Excel / INSERT, and colouring query windows by server/database.

Online version of this guide (searchable): <https://dathoang.github.io/SqlSense/en/user-guide/>
(Vietnamese: <https://dathoang.github.io/SqlSense/huong-dan/>)

---

## 1. Installation

### 1.1 Requirements

| | |
|---|---|
| Operating system | Windows 10/11 x64 |
| SSMS | **SQL Server Management Studio 22** (x64). SSMS 20/21 and earlier are **not** supported |
| Permissions | No Administrator rights needed (the extension is installed per user) |
| SQL authentication | Windows Authentication or SQL Server Authentication for schema-based suggestions (see section 7) |

### 1.2 Install with the installer (recommended)

Download `SqlSense-<version>.zip` from the GitHub
[Releases](https://github.com/dathoang/SqlSense/releases/latest) page. That page also has the standalone
`SqlSense-<version>.vsix` (manual install, section 1.3) and the Vietnamese user guide as a Word document
(`HUONG-DAN-SU-DUNG.docx`; this English guide is on the website). The installer is the folder / zip file
`SqlSense-<version>` containing:

| File | Purpose |
|---|---|
| `SqlSense.vsix` | Extension package |
| `Install.cmd` / `Install.ps1` | Install or upgrade |
| `Uninstall.cmd` / `Uninstall.ps1` | Uninstall |
| `SsmsSetup.ps1` | Shared functions for the two scripts above |
| `HUONG-DAN-SU-DUNG.docx` | Vietnamese user guide (Word); the English guide is on the website |
| `LICENSE` | MIT license |

Steps:

1. Extract `SqlSense-<version>.zip` to any folder.
   If Windows blocks the downloaded file: right-click the zip file › **Properties** › tick **Unblock** before extracting.
2. **Close all SSMS windows.**
3. Double-click **`Install.cmd`**. The script finds every SSMS 22 installation on the machine and installs into it.
   On success you see the green line `Đã cài SqlSense x.y.z.` (SqlSense x.y.z installed.)
4. Open SSMS 22 — once the **Tools › SqlSense** menu appears, installation is complete.

Running from PowerShell (advanced options):

```powershell
.\Install.ps1                    # install into every SSMS 22
.\Install.ps1 -CloseSsms         # close running SSMS automatically (unsaved content is lost!)
.\Install.ps1 -InstanceId 04cbd0c1   # install into one instance only (InstallationID in SSMS.isolation.ini)
```

> **Do not double-click the `.vsix` file directly** if Visual Studio is installed: Windows may open it with Visual
> Studio's VSIX Installer and install it in the wrong place. Always use `Install.cmd`.

### 1.3 Manual install (when the script can't run)

```powershell
& "C:\Program Files\Microsoft SQL Server Management Studio 22\Release\Common7\IDE\VSIXInstaller.exe" /instanceIds:<InstanceId> "SqlSense.vsix"
```

`<InstanceId>` is the `InstallationID=` value in
`C:\Program Files\Microsoft SQL Server Management Studio 22\Release\Common7\IDE\SSMS.isolation.ini`.

### 1.4 First-time setup

1. **Turn off SSMS's built-in IntelliSense** so you don't get two suggestion lists at once. By default SqlSense turns it off
   (Tools › Options › SqlSense › General › *Turn off SSMS IntelliSense while SqlSense is enabled*). If you still see two
   lists, turn it off manually in Tools › Options › Text Editor › Transact-SQL › IntelliSense, then reopen the query window.
2. Open a query window connected to a database, go to **Tools › SqlSense › Show Log** and check for the line
   `SqlSense … loaded` and the line for the database's schema load.
3. (Optional) If your team uses a SQL Prompt style: **Tools › SqlSense › Import SQL Prompt Style...** and pick
   that style's `.json` file (see section 3.3).

### 1.5 Upgrade

Run `Install.cmd` from the new version. If it reports *"Phiên bản x.y.z đã được cài rồi"* (version x.y.z is already
installed), the version in the package is the same as the installed one: run `Uninstall.cmd` then `Install.cmd`, or ask
for a build with a higher version number.

### 1.6 Uninstall

Close SSMS and double-click **`Uninstall.cmd`**, or in SSMS: **Extensions › Manage Extensions** › SqlSense ›
Uninstall. Settings, your own snippets and the connection colour file (`Documents\SqlSense`) are kept.

---

## 2. Suggestions and auto-completion (main use case)

The suggestion list opens automatically as you type; **Ctrl+Space** opens it at any time. **Tab/Enter** selects, **Esc** closes.
Fuzzy search: typing `soh` finds `SalesOrderHeader`; word-start matches rank first.

### 2.1 Writing SELECT quickly

```sql
ssf⇥                     →  SELECT * FROM |
SELECT * FROM cus⇥       →  SELECT * FROM dbo.Customers
SELECT * FROM dbo.Customers AS ⇥  →  suggests alias c
```

* After `FROM` / `JOIN` / `APPLY` / `UPDATE` / `INSERT INTO`: suggests tables, views, table-valued functions.
* Picking a table adds the schema; by default **no** alias is added. Type `AS` after the table name and the list opens with
  a suggested alias that doesn't clash with existing aliases (`Customers` → `c`, `SalesOrderHeader` → `soh`; a second table with the same name → `c2`).
* To get an alias whenever you pick a table: turn on Tools › Options › SqlSense › Suggestions › *Add table aliases*
  (`dbo.Customers c`); also turn on *Add AS before table aliases* to get `dbo.Customers AS c`.
* In the column list / WHERE / ORDER BY: suggests columns of the tables in the statement, functions, variables, keywords.
* Type `c.` → only columns of the table aliased `c`; `dbo.` → objects in the schema; `TenDb.` → schemas.
* Typing a complete name (for example `sys`) selects that name ahead of tables starting with it. A `.` only inserts a schema,
  database, alias or exactly the name just typed, never a table with an alias: `FROM sys.` keeps `sys.` and then lists system views.
* Recognises CTEs, temp tables `#t` (`CREATE TABLE #t`, `SELECT … INTO #t`), table variables, derived tables.

**Fully qualified names in other databases and linked servers:**

```sql
SELECT * FROM ⇥                       -- after tables/views come databases and linked servers
SELECT * FROM Archive.⇥               -- schemas of the Archive database
SELECT * FROM Archive.hist.⇥          -- tables/views in Archive.hist (Archive.. = default schema dbo)
SELECT * FROM REMOTE.⇥                -- databases on linked server REMOTE
SELECT * FROM REMOTE.Sales2.dbo.⇥     -- tables/views of REMOTE.Sales2.dbo
EXEC Archive.dbo.⇥                    -- procedures of another database
```

* Aliases of these tables (`FROM Archive.hist.OldOrders AS o` then `o.`) also suggest columns; hovering over each part
  (server, database, schema, table, column) shows a tooltip.
* The first time a database/linked server is mentioned, its metadata loads in the background; the list reopens when loading finishes.
* You need access to that database; a linked server must be SQL Server and allow data access.

### 2.2 JOIN on foreign keys and same-named columns

```sql
SELECT *
FROM dbo.SalesOrderHeader AS soh
    JOIN ⇥                       →  tables with a foreign key to soh rank first (dbo.Customers …)
    JOIN cus⇥ AS ⇥               →  JOIN dbo.Customers AS c
    ON ⇥                         →  ON soh.CustomerID = c.CustomerID
```

After `JOIN … ON`, SqlSense suggests conditions taken from foreign keys between the tables, at the top of the list.

Next come conditions on **same-named columns** in both tables, even without a foreign key:

* Key-like columns (name ending in `ID`, `Code`, `Key`, `No`, `Num`, `Number`) come first; if there are two or more such
  columns, there is also a suggestion combining them with `AND` (for example `b.CustomerID = a.CustomerID AND b.RegionCode = a.RegionCode`).
* Works with CTEs, temp tables, table variables, derived tables, and when metadata hasn't been loaded.
* Skips columns with different data types, and identity columns in both tables (two unrelated auto-increment keys).
* Turn off in Tools › Options › SqlSense › Suggestions › *Suggest JOIN on same-named columns*.

**Context-based ranking:**

* After `JOIN`: tables with a foreign key to a table already in the statement come first, described as `related to c (FK_…)`.
* Typing after `ON` (for example `ON cus`) still filters JOIN conditions by column name or alias in the condition.
* `ON c.⇥`: JOIN conditions written from the `c` side (`c.CustomerID = soh.CustomerID`) come first; picking one inserts the rest.
* Right after `ON`: key-like columns (`…ID`, primary key, foreign key) of the table just joined, then of the other tables, come before the remaining columns.
* Right-hand side of a comparison (`ON soh.CustomerID = c.⇥`, `WHERE c.CustomerID = ⇥`): the matching column (same name or the other end of
  the foreign key) comes first, described as `matches soh.CustomerID`. A same-named variable (`@CustomerID`) ranks above even that column; then
  variables and columns of the **same data type group** (numbers with numbers, strings with strings, dates with dates), then the rest.
* Recently picked items (tables, columns, functions…) move ahead of equally good matches. The history is kept only for the
  current SSMS session. Turn off in Tools › Options › SqlSense › Suggestions › *Rank recently used suggestions first*.

### Reusing SELECT columns in WHERE / GROUP BY / HAVING / ORDER BY

```sql
SELECT o.Product, price * quantity AS amount, SUM(price) AS total
FROM sales.OrderLines AS o
WHERE ⇥                 →  suggests amount; picking it inserts  price * quantity
GROUP BY ⇥              →  (select columns)  inserts  o.Product, price * quantity
HAVING ⇥                →  total  inserts  SUM(price)
ORDER BY ⇥              →  amount, total (alias kept because ORDER BY can use aliases)
```

* SQL Server doesn't allow SELECT aliases in `WHERE` / `GROUP BY` / `HAVING`, so SqlSense suggests the alias name but inserts
  the original expression. The list opens automatically right after `WHERE `, `GROUP BY `, `ORDER BY `, `HAVING `.
* `WHERE` skips aggregate columns (`SUM`, `COUNT`…); `HAVING` includes them; window functions (`… OVER (…)`) don't count as aggregates.
* `GROUP BY`: the *(select columns)* item inserts all non-aggregate SELECT columns at once; columns already in `GROUP BY` are not
  suggested again.
* When inserted after an operator (`2 * ⇥`), an expression containing operators is wrapped in parentheses: `2 * (price + 1)`.
* Works in subqueries (using that subquery's own SELECT) and when metadata hasn't been loaded.

### 2.3 Expanding `SELECT *` into a column list

* Type `*` (or `c.*`) and press **Tab** right after the `*`.
* Or put the caret on the line with `*` and press **Ctrl+B, Ctrl+W** (Tools › SqlSense › Expand Wildcard).

### 2.4 INSERT and EXEC templates

```sql
INSERT INTO cus⇥
-- →
INSERT INTO dbo.Customers (Name, Email)
VALUES (N'', '')                    -- sample values by data type, identity/computed columns skipped

EXEC usp_get⇥
-- →
EXEC dbo.usp_GetOrders @CustomerID = 0, @From = SYSDATETIME()
```

Tables in other databases or on linked servers work too: in `INSERT INTO DbA.dbo.⇥`, picking a table gives the column list and
`VALUES`. Long lists (an `INSERT INTO … (…)` line over 80 characters) are wrapped the same way as when formatting (see 3.3):

```sql
INSERT INTO DbA.dbo.CustomerHistory (
    CustomerID, FirstName, LastName, Email, Phone, Address, City, Country,
    CreatedAt, CreatedBy
)
VALUES (
    0, N'', N'', '', '', N'', N'', N'',
    SYSDATETIME(), N''
)
```

When the caret is on a column in an INSERT column list, the corresponding value in `VALUES` is highlighted (and
vice versa) — handy for checking long INSERTs. Change the colour in Tools › Options › Environment › Fonts and Colors ›
*SqlSense INSERT column/value*.

### 2.5 Editing existing procedures / functions / views / triggers

Type `ALTER PROCEDURE ` (or `ALTER PROC`, `ALTER FUNCTION`, `ALTER VIEW`, `ALTER TRIGGER`) — the list shows only objects
of **that type** in the database. Pick an object in an **empty** query window (or an empty batch after `GO`):

```sql
ALTER PROCEDURE usp_get⇥
-- → after a moment, the whole statement is replaced by the procedure's current definition:
-- header comment
ALTER PROCEDURE dbo.usp_GetOrders @CustomerID int, @From date = NULL
AS
SELECT …
```

* Like Object Explorer's *Script as ALTER* but without leaving the keyboard: the header comment and body are kept character
  for character; only `CREATE` is changed to `ALTER`.
* `CREATE OR ALTER PROCEDURE ` + pick an object → script in `CREATE OR ALTER` form.
* The definition loads in the background so the editor doesn't freeze; the status bar shows *scripting…* / *scripted*.
  If you keep typing before it finishes, SqlSense doesn't overwrite your text. **Ctrl+Z** goes back to just the object name.
* If a body already follows the name (`AS …`), SqlSense only replaces the name and doesn't insert the definition.
* CLR objects, `WITH ENCRYPTION`, or missing `VIEW DEFINITION` permission → only the name is inserted; the status bar shows the reason.
* `DROP PROCEDURE ` / `DROP TRIGGER IF EXISTS ` … also suggest objects of the right type (name only).
* `CREATE TRIGGER trg ON ` suggests tables and views.
* Turn off in Tools › Options › SqlSense › Suggestions › *ALTER scripts the object's definition*.

### 2.6 Permissions: users, roles, logins, schemas

When writing security statements, the list suggests existing objects of the right type on the server/database:

```sql
ALTER ROLE ⇥                              -- database roles (your roles come before db_datareader, db_owner …)
ALTER ROLE app_readers ADD MEMBER ⇥       -- database users and roles
ALTER SERVER ROLE sysadmin ADD MEMBER ⇥   -- logins and server roles
GRANT SELECT ON ⇥                         -- tables, views, procedures, functions, schemas (no triggers)
GRANT SELECT ON dbo.Customers TO ⇥        -- users, roles, application roles (also after a comma)
REVOKE EXECUTE ON SCHEMA::sales FROM ⇥
```

* `ALTER` / `DROP [IF EXISTS]` + `ROLE`, `USER`, `LOGIN`, `APPLICATION ROLE`, `SERVER ROLE`, `SCHEMA`, `DATABASE`
  suggest existing objects of that type; `CREATE ROLE ` / `CREATE USER ` … take a new name, so nothing is suggested.
* `CREATE USER u FOR LOGIN `, `WITH LOGIN = ` → logins; `WITH DEFAULT_SCHEMA = ` → schemas;
  `WITH DEFAULT_DATABASE = ` → databases; `CREATE SCHEMA s AUTHORIZATION ` and `ALTER AUTHORIZATION ON … TO ` → users/roles.
* `ON SCHEMA::`, `ROLE::`, `USER::`, `LOGIN::`, `DATABASE::`, `OBJECT::` suggest the matching type after `::`.
* Server-level permissions (`GRANT VIEW SERVER STATE TO `, `ALTER ANY LOGIN`, `ON LOGIN::` …) suggest logins and server roles.
* The description next to each item shows its type (`SQL_USER`, `WINDOWS_GROUP` …), a role's members, and the roles a user belongs to.
  Names with special characters (`CORP\alice`) are inserted in `[ ]`.
* Logins and server roles are listed only if the connected account can see them (by default it sees itself and the
  server roles); without permission that part is empty and the other suggestions still work.

### 2.7 Hover information

Hover over a name in the script and a tooltip shows what it is:

| Hover over | Tooltip shows |
|---|---|
| Table, view, alias (`c` in `c.Name`) | Full name, columns with data type, null/not null, identity; the table's foreign keys |
| Column (`Name`, `c.Name`, `dbo.Customers.Name`) | Owning table, data type, null/not null |
| Procedure, function | Parameters (type, `OUTPUT`, has default value); returned columns of a table-valued function |
| Trigger | The table/view the trigger is attached to |
| CTE, temp table `#t`, table variable | Column list |
| Variable `@x` | Declared type; `@@ROWCOUNT` … and built-in functions (`ISNULL`, `LEFT` …) show syntax and description |
| Schema | Number of tables/views/procedures… in the schema |
| Database | State (if not ONLINE), recovery model, compatibility level, files of the current database, last full/differential/log backup |
| Index (`ALTER INDEX ix ON …`) | Index type, key columns, `INCLUDE` columns, table; the table's tooltip also lists its indexes |
| User, role, login | Type, mapped login, default schema/database, role members, roles a user belongs to |

* No connection is needed for variables, CTEs, temp tables and built-in functions; object information needs loaded metadata (as with suggestions).
* Turn off in Tools › Options › SqlSense › General › *Show object info on hover*. If SSMS IntelliSense is on, you may
  also see SSMS's own tooltip.

### 2.8 Backup, restore and maintenance

```sql
BACKUP DATABASE ⇥                         -- databases on the server
BACKUP DATABASE Shop TO ⇥                 -- DISK = N'Shop_20261023_1405.bak' (name with date and time), DISK = N'', URL = N''
BACKUP DATABASE Shop TO DISK = N'…' WITH ⇥  -- COMPRESSION, COPY_ONLY, DIFFERENTIAL, CHECKSUM, INIT, STATS = 10 …
RESTORE DATABASE Shop FROM ⇥              -- most recent backup files of Shop (history in msdb)
RESTORE DATABASE Shop FROM DISK = N'…' WITH ⇥  -- RECOVERY, NORECOVERY, REPLACE, MOVE N'' TO N'', STATS = 10 …
```

* `BACKUP ` / `RESTORE ` suggest `DATABASE`, `LOG`, `FILELISTONLY FROM`, `HEADERONLY FROM`, `VERIFYONLY FROM` …
* `BACKUP LOG Shop TO ` suggests a `.trn` file. For a file name without a folder, SQL Server writes to/reads from the server's
  default backup folder.
* `RESTORE DATABASE ` also suggests databases in `RESTORING` state (after `NORECOVERY`). Recent backup files are only available if
  the account can read `msdb`; the description shows the backup type and time.
* `DBCC ` suggests commands (`CHECKDB`, `CHECKTABLE`, `SHRINKFILE`, `SQLPERF(LOGSPACE)`, `FREEPROCCACHE` …);
  `DBCC CHECKDB (` → databases; `DBCC CHECKIDENT (` / `CHECKTABLE (` → tables, inserted as a string `'dbo.Customers'`;
  `DBCC SHRINKFILE (` → database files (`N'Shop_log'`); `… WITH ` → `NO_INFOMSGS`, `PHYSICAL_ONLY` …
* `ALTER DATABASE Shop ` suggests `SET …`, `MODIFY FILE`, `ADD FILE`, `MODIFY NAME`; `SET ` suggests `RECOVERY SIMPLE`,
  `SINGLE_USER WITH ROLLBACK IMMEDIATE`, `COMPATIBILITY_LEVEL = 160`, `READ_COMMITTED_SNAPSHOT ON`, `QUERY_STORE = ON` …;
  `MODIFY FILE (NAME = ` suggests files.
* Indexes: `ALTER INDEX ` suggests `ALL ON` and indexes (picking an index inserts `IX_… ON dbo.Bang` too); same for `DROP INDEX `;
  `ALTER INDEX … ON dbo.Bang ` → `REBUILD`, `REBUILD WITH (ONLINE = ON)`, `REORGANIZE`, `DISABLE` …;
  `CREATE INDEX ix ON ` → tables, `(` and `INCLUDE (` → columns of that table.
* `UPDATE STATISTICS ` → tables; after the table name → `WITH FULLSCAN`, `WITH SAMPLE 25 PERCENT` …
* Snippets: `bakd` (full backup), `bakl` (log backup), `rsdb` (restore), `chkdb` (DBCC CHECKDB) — see 2.10.

### 2.9 Creating and altering tables

```sql
CREATE TABLE dbo.Orders (
    OrderID int ⇥                      -- after the column name: data type (int, nvarchar, decimal …)
    OrderID int ⇥                      -- after the type: NOT NULL, NULL, IDENTITY(1, 1), PRIMARY KEY, DEFAULT, REFERENCES …
    CustomerID int REFERENCES ⇥        -- tables; REFERENCES dbo.Customers ( → columns of that table
    ⇥                                   -- start of line: CONSTRAINT, PRIMARY KEY (), UNIQUE (), FOREIGN KEY () REFERENCES, CHECK ()
    CONSTRAINT PK_Orders ⇥             -- PRIMARY KEY CLUSTERED (), UNIQUE (), FOREIGN KEY () REFERENCES …
    CONSTRAINT PK_Orders PRIMARY KEY (⇥ -- the columns declared above
)
ALTER TABLE dbo.Customers ⇥            -- ADD, ALTER COLUMN, DROP COLUMN, DROP CONSTRAINT, NOCHECK CONSTRAINT, ENABLE TRIGGER …
ALTER TABLE dbo.Customers ALTER COLUMN ⇥     -- the table's columns; after the column name → type; after the type → NULL / NOT NULL
ALTER TABLE dbo.Customers DROP CONSTRAINT ⇥  -- the table's primary key, unique and foreign key constraints
```

* Applies to `CREATE TABLE`, `DECLARE @t TABLE (…)`, `CREATE TYPE … AS TABLE (…)`, `RETURNS @t TABLE (…)` and
  `ALTER TABLE … ADD`.
* Foreign keys: `… REFERENCES dbo.Bang (Cot) ` suggests `ON DELETE CASCADE`, `ON DELETE SET NULL`, `ON UPDATE CASCADE` …;
  `ON ` → `DELETE` / `UPDATE`; `ON DELETE ` → `CASCADE`, `NO ACTION`, `SET NULL`, `SET DEFAULT`.
* `ALTER TABLE … ADD CONSTRAINT DF_x DEFAULT 0 ` suggests `FOR`, then the table's columns.
* `NOCHECK CONSTRAINT ` / `CHECK CONSTRAINT ` suggest `ALL` and foreign keys; `ENABLE TRIGGER ` / `DISABLE TRIGGER ` suggest `ALL`
  and the table's triggers. Existing CHECK and DEFAULT constraints are not in the suggestion list yet.
* Inside a type's parentheses (`decimal(18, `), `IDENTITY(` and after `CONSTRAINT ` (a new name) no suggestions are shown.

### 2.10 Snippets

Type the shortcut and press **Tab**:

| Shortcut | Produces | Shortcut | Produces |
|---|---|---|---|
| `ssf` | `SELECT * FROM` | `bt` | Transaction + TRY/CATCH + ROLLBACK |
| `st100` | `SELECT TOP 100 * FROM` | `tc` | `BEGIN TRY … END CATCH` |
| `scf` | `SELECT COUNT(*) FROM` | `ifex` | `IF EXISTS (SELECT 1 FROM …)` |
| `sdf` | `SELECT DISTINCT … FROM` | `cte` | `WITH cte AS (…)` |
| `ii` / `iis` | `INSERT INTO` / `INSERT … SELECT` | `cur` | Cursor loop |
| `uu` | `UPDATE … SET` | `cp` / `cv` | `CREATE OR ALTER PROCEDURE / VIEW` |
| `df` | `DELETE FROM` | `ct` / `tt` | `CREATE TABLE` / temp table `#` |
| `ij` / `lj` | `INNER / LEFT JOIN … ON` | `dt` | `DROP TABLE IF EXISTS` |
| `w2` | `WHERE … = ` | `sn` | `SET NOCOUNT ON;` |
| `ob` / `gb` | `ORDER BY` / `GROUP BY` | `rn` | `ROW_NUMBER() OVER (…)` |
| `be` | `BEGIN … END` | `sp_who` | Running and blocking sessions |
| `bakd` / `bakl` | `BACKUP DATABASE / LOG … WITH COMPRESSION` | `rsdb` | `RESTORE DATABASE … WITH REPLACE` |
| `chkdb` | `DBCC CHECKDB (…) WITH NO_INFOMSGS` | | |

**Your own / your team's snippets:** create a `<shortcut>.sql` file in `Documents\SqlSense\Snippets`
(or the folder set in Tools › Options › SqlSense › General › *Snippets folder* — it can point to a shared folder).
A first line `-- description` is the description shown; `$CURSOR$` marks the caret position after insertion. Example `audit.sql`:

```sql
-- Change history of a record
SELECT *
FROM dbo.AuditLog
WHERE TableName = N'$CURSOR$'
ORDER BY ChangedAt DESC;
```

### 2.11 Other

* Typing `(`, `[`, `'` adds the closing character; typing a closing character overwrites the existing one.
* Picking a suggestion with the caret in the middle of a word inserts only at the caret (replacing what was typed); the text after the caret is kept.
* `DECLARE @x ` / `CAST(… AS ` suggest data types; `USE ` suggests databases; `@` suggests declared variables.
* `USE TenDb` in a script changes the database used for suggestions in the statements below it. Each database's metadata is cached
  separately (30 minutes by default), so going back to a database you've used gives suggestions immediately. For a database never loaded
  before: database names, linked servers and logins still show immediately (taken from a cached database on the same server); tables and
  columns show once loading finishes.

---

## 3. SQL formatting

### 3.1 Usage

* **Ctrl+K, Ctrl+Y** — formats the selection, or the whole document if nothing is selected.
* Or Tools › SqlSense › Format SQL; Edit › Advanced › Format Document / Format Selection.

```sql
select c.CustomerID, c.Name, count(*) as n from dbo.Customers c inner join sales.Orders o on o.CustomerID = c.CustomerID and o.Total > 0 where c.Active = 1 group by c.CustomerID, c.Name order by n desc
```

becomes (default style)

```sql
SELECT c.CustomerID,
       c.Name,
       COUNT(*) AS n
FROM dbo.Customers c
    INNER JOIN sales.Orders o
        ON o.CustomerID = c.CustomerID
           AND o.Total > 0
WHERE c.Active = 1
GROUP BY c.CustomerID,
         c.Name
ORDER BY n DESC
```

### 3.2 Safety

After formatting, SqlSense compares every token with the original. If anything changed other than whitespace
and keyword case, **nothing is changed** and the reason is shown in the status bar. Comments and blank lines
between statements are preserved. You can always **Ctrl+Z** to undo.

### 3.3 Customising the style

Tools › Options › **SqlSense › Formatting**: indent size, case for keywords / functions / data types /
`@@ROWCOUNT`, leading commas, one column per line, line break before `AND`/`OR`, JOIN indentation, position of the first `WHEN`,
parenthesis style for subqueries and `CREATE TABLE` / `INSERT`, alias / comment alignment, keeping short statements on one line (*Collapse short code*), …

**INSERT column lists:** when an `INSERT INTO table (column, …)` line is longer than 80 characters (option *Wrap INSERT column
lists longer than*; 0 = never wrap, 1 = one column per line), columns are packed onto each line up to that width.
A single-row `VALUES` or the `SELECT` list of an `INSERT … SELECT` breaks after exactly the same columns, so line n of the
values matches line n of the columns. A multi-row `VALUES` keeps each row on one line. Parenthesis style follows
*CREATE TABLE / INSERT parentheses*, leading or trailing commas follow *Leading commas*:

```sql
INSERT INTO tblA (
    col1, col2, col3, col4, col5
  , col21, col22, col23, col24, col25
)
SELECT col1, col2, col3, col4, col5
     , col21, col22, col23, col24, col25
FROM tblB
```

**One style for the whole team:** export the style from SQL Prompt (`.json` file), then everyone runs
**Tools › SqlSense › Import SQL Prompt Style...**. After importing, a dialog lists how many settings were applied and
which settings have no equivalent yet; details are in Show Log.

---

## 4. Code folding

Long scripts can be folded by block, like VS Code. Left of the line numbers there is an **arrow** (chevron) at the start of each
foldable region; click it to fold/unfold. Hover over a folded region to see its contents.

Foldable regions:

| Region | Example |
|---|---|
| `BEGIN … END`, `BEGIN TRY … END TRY`, `BEGIN CATCH … END CATCH` blocks | `IF`, `WHILE`, procedure bodies |
| Multi-line `CASE … END` | |
| Multi-line statements | `SELECT`, `INSERT`, `UPDATE`, `DELETE`, `MERGE`, `WITH` (CTE), `EXEC`, `DECLARE`, `CREATE`/`ALTER` |
| Whole procedure / function / view / trigger | from `CREATE`/`ALTER` to the end of the batch (before `GO`) |
| Multi-line `( … )` parentheses | subqueries, `CREATE TABLE` column lists, `VALUES` |
| Comments | multi-line `/* … */`; consecutive `--` lines |
| Named regions | from `-- #region Name` to `-- #endregion` |

```sql
-- #region Prepare data
DECLARE @from date = '2024-01-01',
        @to   date = '2024-12-31';
-- #endregion

IF @from < @to
BEGIN
    SELECT c.CustomerID,
           c.Name
    FROM dbo.Customers c
    WHERE c.CreatedAt BETWEEN @from AND @to;
END
```

**Levels:** a region not inside any other region is level 1, a region directly inside it is level 2, … In the example above, the `#region`
and the `BEGIN … END` block are level 1; `DECLARE` and `SELECT` are level 2.

Shortcuts (press **Ctrl+K**, release, then press the second key), also in **Tools › SqlSense › Folding**:

| Keys | Command |
|---|---|
| Ctrl+K, Ctrl+1 … Ctrl+7 | **Fold Level N**: fold every level-N region (except the one containing the caret) |
| Ctrl+K, Ctrl+0 | **Fold All**: fold everything |
| Ctrl+K, Ctrl+J | **Unfold All**: unfold everything |
| Ctrl+K, Ctrl+[ | **Fold Recursively**: fold the region at the caret and every region inside it |
| Ctrl+K, Ctrl+] | **Unfold Recursively**: unfold the region at the caret and every region inside it |
| Ctrl+K, Ctrl+8 | **Fold All #region Blocks**: fold every `-- #region` region |
| (menu only) | **Fold All Comments**: fold every comment |
| Ctrl+M, Ctrl+M | Fold/unfold the region at the caret (built-in SSMS command) |

Tip: to see the "outline" of a long script, press **Ctrl+K, Ctrl+1** — every top-level statement, block and region collapses to one line.

Options in Tools › Options › **SqlSense › General**: *Code folding* (turns the whole feature on/off) and *Fold statements*
(turn off if you only want to fold blocks, parentheses, comments and regions, not individual statements).

---

## 5. Working with query results

Right-click the Results grid:

| Command | Effect |
|---|---|
| **Open Results in Excel** | Saves the results grid as an `.xlsx` file (keeping number/date types) and opens it in Excel. Up to 1,048,575 rows (Excel's limit); if no application can open `.xlsx`, the file path is shown in the status bar |
| **Script Results as INSERT** | Generates `INSERT … VALUES` statements from the results and opens them in a new query window (if that fails, copies them to the clipboard). The target table name is inferred from the SELECT that was run |

If you select several cells before clicking, only the selected cells are exported. Useful for: moving sample data between environments,
creating seed/test data scripts, sending figures to people who don't use SSMS.

---

## 6. Colouring windows by connection (avoid running against PROD by mistake)

Each query window's tab, editor frame and status bar are coloured by the connected server/database.

### 6.1 Setting a colour quickly from the tab

Right-click the query window's **tab** › **Connection Color**:

* **Db ›** pick a colour — applies to this exact *server/database*.
* **Server ›** pick a colour — applies to every database on this server.
* **No Color** — no colouring (for example to exclude `tempdb` from the server's colour), **Custom...** — pick any colour,
  **Remove Rule** — deletes the rule.

### 6.2 Rules file

**Tools › SqlSense › Edit Connection Colors...** opens `Documents\SqlSense\ConnectionColors.txt`. Saving the file applies it immediately:

```text
# server[/database] = colour
PROD-SQL01            = Red
PROD-SQL01/Reporting  = Orange
PROD-SQL01/tempdb     = none
*.test.contoso.com    = Green
localhost\*/*_Staging = #8E44AD
```

* `*` and `?` are wildcards, case-insensitive.
* A rule with a database wins over a server-only rule; otherwise the first matching line wins.
* Colours: `#RRGGBB`, `#AARRGGBB`, names (Red, DarkRed, Orange, Yellow, Gold, Green, DarkGreen, Teal, Cyan, Blue, Navy,
  Purple, Magenta, Pink, Brown, Gray, Black, White) or `none`.
* Windows that match no rule use the colour chosen in SSMS's Connect dialog (Options › *Use custom color*).
* You can put the file in a shared folder so the whole team uses the same colours: Tools › Options › SqlSense ›
  Connection Colors › *Connection colours file*.

SSMS's built-in *Set Tab Color* menu is hidden because it stores colours per file and would conflict (turn it back on in Options if needed).

---

## 7. Settings and troubleshooting

### 7.1 Tools › SqlSense menu

| Command | When to use |
|---|---|
| Format SQL (Ctrl+K, Ctrl+Y) | Formatting |
| Expand Wildcard (Ctrl+B, Ctrl+W) | Expand `SELECT *` |
| Folding › … | Fold/unfold code by level (section 4) |
| **Refresh Suggestions** (Ctrl+Shift+D) | Reloads the metadata of the database used by the query window now (or the database of the `USE` above the caret). Suggestions keep using the old copy until the new one is ready |
| Refresh All Suggestions | Marks every cached database/linked server to be reloaded on next use (for example after someone else changed schemas in several databases) |
| Disable / Enable SqlSense | Temporarily turns off all of SqlSense |
| **Show Log** | View diagnostics (Output pane "SqlSense") |
| Import SQL Prompt Style... | Import a formatting style |
| Edit Connection Colors... | Edit colour rules |

All options: **Tools › Options › SqlSense** (General, Suggestions, Formatting, Connection Colors).

**Automatic refresh after DDL:** when you run (F5 / Execute) a script containing `CREATE`, `ALTER`, `DROP` (table, view, procedure,
function, trigger, index, schema, user, role …), `SELECT … INTO` a real table, or `sp_rename`, SqlSense reloads the metadata
of the affected databases about 1–2 seconds later. Only the selected text is considered if there is a selection; both `USE` and three-part
names (`Archive.dbo.T`) are followed. Temp tables `#t` and table variables don't count. For long-running DDL (for example creating an index on a large table), the metadata
read waits until the DDL finishes. For DDL run through dynamic SQL (`EXEC('…')`) or from other tools, use Ctrl+Shift+D.
Turn off in Tools › Options › SqlSense › General › *Refresh metadata after running CREATE/ALTER/DROP*.

**Metadata loading status:** each time SqlSense reads the object list (first use of a connection/database, when another
database or linked server is mentioned, after Refresh Suggestions), SSMS's bottom status bar shows an animated icon and
the line *SqlSense: loading metadata for SERVER/Db…*, including on refresh (manual or after DDL). When done it shows *metadata ready for SERVER/Db: 1.234 objects in 0,8 s*;
on error it shows the reason (details in Show Log). Periodic refreshes (after the cache time) run silently without notification.
Turn off in Tools › Options › SqlSense › General › *Show metadata loading in the status bar*.

### 7.2 Common problems

| Symptom | Cause / fix |
|---|---|
| No Tools › SqlSense menu | Installed into the wrong instance, or SSMS was open during installation. Close SSMS, run `Install.cmd` again; check Extensions › Manage Extensions |
| Two suggestion lists appear | Turn off SSMS IntelliSense (section 1.4), then reopen the query window |
| Only keywords/functions suggested, no tables/columns | Signed in with **Microsoft Entra ID** (not supported yet — use Windows/SQL auth), SSMS doesn't provide the SQL login password (tick *Remember password*), or the schema is loading for the first time (large databases take a few seconds — the status bar shows *loading metadata…*). See Show Log |
| Newly created tables/columns missing | If created by a script in SSMS, they usually update automatically after a few seconds; otherwise (dynamic SQL, other tools, changed by someone else) press **Ctrl+Shift+D** (Refresh Suggestions) |
| Changed database in the dropdown but suggestions are still for the old one | Add `USE TenDb` at the top of the script, or Ctrl+Shift+D |
| Ctrl+Shift+D does nothing / does something else | The shortcut is taken by another command: reassign it in Tools › Options › Environment › Keyboard, command *SqlSense.RefreshSuggestions* (likewise for folding shortcuts: commands *SqlSense.FoldLevel1*, *SqlSense.FoldAll*, …) |
| Folding arrows appear twice on the same line | SSMS may also be folding by statement. Turn off SSMS IntelliSense (section 1.4) and reopen the query window, or turn off SqlSense's *Fold statements* |
| Format does nothing | Check the status bar: the formatter refused because the result would change tokens (usually caused by badly broken SQL syntax). Send that SQL to the development team |
| Installation reports an error | See the VSIXInstaller log at the path printed by the script (`%TEMP%\SqlSense_install_*.log`) |

When reporting a bug, include the **Show Log** content, the SqlSense version (Extensions › Manage Extensions) and the SSMS version
(Help › About).

---

## 8. For people building the installer

Requirements: Visual Studio 2022/2026 with the **Visual Studio extension development** workload, the .NET SDK and Node.js 22+
(to generate the Word guide from `deploy\HUONG-DAN-SU-DUNG.md`; on first run the script runs `npm ci` in `deploy\tools`).

```powershell
.\deploy\Build-Package.ps1 -Version 1.8.0           # test + rebuild + package
.\deploy\Build-Package.ps1 -Version 1.8.0 -Install  # ... and install into SSMS on this machine
.\deploy\Build-Package.ps1 -SkipTests               # keep the version, skip unit tests
```

Output: `dist\SqlSense-<version>\` and `dist\SqlSense-<version>.zip` (VSIX + install/uninstall scripts + Word guide +
LICENSE), plus `dist\SqlSense-<version>.vsix` and `dist\HUONG-DAN-SU-DUNG-<version>.docx` to attach separately to the
GitHub Release; `dist\SqlSense.vsix` is always the latest build. **You must increase `-Version`** for every release, otherwise machines with
the old version installed will refuse to install over it.
