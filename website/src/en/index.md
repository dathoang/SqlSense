---
title: SqlSense for SSMS 22
hide:
  - navigation
---

# SqlSense for SSMS 22

A free, open-source extension for **SQL Server Management Studio 22**: schema-aware T-SQL suggestions, generators for
the code you write all the time, and a SQL formatter that never changes what a statement means.

[:material-download: Download the latest release](https://github.com/dathoang/SqlSense/releases/latest){ .md-button .md-button--primary }
[:material-book-open-variant: User guide](user-guide.md){ .md-button }

## Features

<div class="grid cards" markdown>

-   :material-lightbulb-on-outline: **Context-aware suggestions**

    ---

    Tables after `FROM`/`JOIN`, columns by alias, procedures after `EXEC`, data types, databases, variables. Knows
    CTEs, temp tables and table variables. Fuzzy matching: `soh` finds `SalesOrderHeader`. Security statements
    (`ALTER ROLE`, `ADD MEMBER`, `GRANT … TO`) list existing users, roles, logins and schemas; `BACKUP`/`RESTORE`,
    `DBCC`, `ALTER DATABASE`, `ALTER INDEX` list databases, recent backup files, `WITH` options and indexes. Hover a
    name to see its columns, parameters or role members.

-   :material-auto-fix: **Code generators**

    ---

    Unique aliases, `JOIN … ON` conditions from foreign keys and same-named columns, `INSERT … VALUES` templates
    (also for tables in another database: `INSERT INTO DbA.dbo.`) and `EXEC` parameters, `SELECT *` expanded to the
    column list, snippets (`ssf`, `bt`, `cte` … + Tab).

-   :material-file-replace-outline: **ALTER with the current definition**

    ---

    Type `ALTER PROCEDURE ` and pick an object: its current definition is inserted with `CREATE` changed to `ALTER`.
    Works for functions, views and triggers too.

-   :material-format-align-left: **Safe formatting**

    ---

    **Ctrl+K, Ctrl+Y**. After formatting, SqlSense compares every token with the original; if anything other than
    whitespace and letter case would change, nothing is applied. Long `INSERT` column lists are filled line by line,
    with values breaking to match the columns. Imports SQL Prompt styles.

-   :material-unfold-less-horizontal: **Folding by level**

    ---

    Fold `BEGIN … END`, `CASE`, multi-line statements, parentheses, comments and `-- #region` blocks. Like VS Code:
    **Ctrl+K, Ctrl+1…7** fold by level, **Ctrl+K, Ctrl+0** fold all, **Ctrl+K, Ctrl+J** unfold all.

-   :material-table-arrow-right: **Export results**

    ---

    Right-click the results grid: open in Excel (numbers and dates keep their types) or script the rows as `INSERT`
    statements.

-   :material-palette-outline: **Colors by connection**

    ---

    Tabs, the editor frame and the status bar are colored by server/database, e.g. PROD in red, so you don't run a
    script in the wrong environment.

</div>

## Quick install

1. Download `SqlSense-<version>.zip` from [Releases](https://github.com/dathoang/SqlSense/releases/latest) and unzip it.
2. Close SSMS 22.
3. Double-click `Install.cmd`. No administrator rights needed.

After installing, SSMS has a **Tools › SqlSense** menu. Details, settings and troubleshooting are in the
[User guide](user-guide.md); the zip also contains a Vietnamese Word version, attached separately on the Releases page.

!!! info "Requirements"
    Windows 10/11 x64 and SQL Server Management Studio 22 (x64). Schema-aware suggestions need Windows or SQL Server
    Authentication; with Microsoft Entra ID you currently get keywords, functions and snippets only.

## Source code

SqlSense is open source under the [MIT license](https://github.com/dathoang/SqlSense/blob/main/LICENSE).
Source, build instructions and issues: [github.com/dathoang/SqlSense](https://github.com/dathoang/SqlSense).
