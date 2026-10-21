# SqlSense

T-SQL suggestions, autocomplete and formatter extension for **SSMS 22** (x64, Visual Studio 18 shell).
Independent implementation: never copy, decompile into, or load Redgate SQL Prompt code or binaries.

## Commands

```powershell
# Engine tests (fast; run after any change in SqlSense.Core)
dotnet test tests\SqlSense.Core.Tests

# Build the VSIX (needs VS MSBuild + VSSDK; dotnet build can't build the VSIX project)
& "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" src\SqlSense.Vsix\SqlSense.Vsix.csproj -restore -p:Configuration=Release

# Install into SSMS 22 (SSMS 22 must be closed; 04cbd0c1 = this machine's instance id from SSMS.isolation.ini)
& "C:\Program Files\Microsoft SQL Server Management Studio 22\Release\Common7\IDE\VSIXInstaller.exe" /quiet /instanceIds:04cbd0c1 src\SqlSense.Vsix\bin\Release\SqlSense.vsix
```

Bump `Version` in `source.extension.vsixmanifest` before reinstalling over an existing install.

Release package: `.\deploy\Build-Package.ps1 -Version x.y.z [-Install]` (tests, Rebuild, `dist\SqlSense-x.y.z.zip`).
`deploy\package\*` ships to users as-is (Install/Uninstall scripts; Windows PowerShell 5.1, saved as UTF-8 with BOM
because of the Vietnamese messages). The user guide is `deploy\HUONG-DAN-SU-DUNG.md`; update it with new features.
`Directory.Build.props` redirects bin/obj to `%TEMP%\ssb` only when the checkout path is over 100 chars; here they're normal.

## Layout

- `src/SqlSense.Core` (netstandard2.0): all language logic, no VS dependencies. Put new behaviour here and unit-test it.
  - `Lexing/SqlLexer` wraps the ScriptDom lexer (`TSql170Parser.GetTokenStream`), which tolerates incomplete SQL. Don't switch to full parsing for editor features.
  - `Analysis/StatementAnalysis`: statement splitting (`SplitStatements` + `IsContinuation`), table refs, CTEs, temp tables, variables.
  - `Analysis/ContextAnalyzer`: classifies the caret into a `ContextKind` and collects tables in scope.
  - `Completion/*`: `CompletionEngine` (items + ranking), `CompletionMatcher` (prefix > initials > substring), `Generators` (aliases, FK join conditions, `StarExpander`), `Snippets`.
  - `Formatting/SqlFormatter`: token-stream formatter with a frame stack (Root/Block/Subquery/ColumnList/Paren/Case).
- `src/SqlSense.Vsix` (net48, classic csproj): SSMS shell only.
  - `Editor/SqlCommandFilter`: `IOleCommandTarget` added via `IVsTextView.AddCommandFilter`; routes TYPECHAR/TAB/RETURN/arrows/Esc/Ctrl+Space/Format.
  - `Editor/ObjectScripter`: after an object is picked in `ALTER PROCEDURE|FUNCTION|VIEW|TRIGGER` (empty batch; `CompletionItem.ScriptRequest`), loads `OBJECT_DEFINITION` in the background and replaces the statement with `Core/Completion/ModuleScript.Build` (CREATE → ALTER), only if the text is unchanged.
  - `IntelliSense/*`: legacy `ICompletionSource` + custom `SqlCompletionSet` filtering. The source only answers sessions tagged with `SqlCommandFilter.SessionMarker`.
  - `Schema/SsmsConnection`: reads the active window's connection by reflection (`SqlPackageBase` → `ServiceCache.ScriptFactory.CurrentlyActiveWndConnectionInfo.UIConnectionInfo`). `SchemaLoader` is one multi-result-set metadata query; `SchemaService` caches it per server/db/login and loads in the background.
  - `Connections/ConnectionColorService`: every 1 s reads each document frame's DocView (SSMS `SqlScriptEditorControl`) → `ConnectionInfoList[0]` (server, `AdvancedOptions["DATABASE"]`, `CUSTOM_CONNECTION_COLOR`), resolves a colour with `Core/Connections/ConnectionColorRules` (file `Documents\SqlSense\ConnectionColors.txt`), paints the tab via the shell's `DocumentView.IsTabGroupColorized`/`TabAccentBrush` (reflection; the document well's content frame uses the same brush while `ViewManager.Preferences.ColorizeDocumentTabs` is on, which SqlSense turns on in memory) and the status bar via `QEStatusBarManager.singleServerBackColor` + `UpdateStatusBar()`. The document-tab right-click menu (`IDM_VS_CTXT_EZDOCWINTAB` › Connection Color) acts on the active frame (the shell activates a tab before showing its menu), lists `ColorText.Palette` in fixed vsct slots (Database/Server submenus, ids in `PackageIds.Tab*`; keep `TabPaletteSlots` = palette size) and rewrites the file via `ConnectionColorRules.SetRule/RemoveRule`. `SetTabColorBlocker` (priority command target) hides the shell's VsStd17 Set Tab Color commands.
  - `Settings/registration.json` is the Unified Settings manifest (Tools › Options › SqlSense; monikers `sqlSense.*`, one-time migration from the old DialogPage registry values). `Settings/SettingsStore` reads it via `ISettingsManager` (service GUID declared locally; the SDK type is internal), re-reads on change and publishes to `SqlSenseSettings.Current` (read from any thread). Adding an option = JSON entry + read (and write if StyleImport sets it) in `SettingsStore`.
- `tests/SqlSense.Core.Tests`: xUnit; `TestSchema` is the shared fake database. Completion tests mark the caret with `|`.

## Rules that matter

- **Formatter must never change semantics.** `SqlFormatter.Format` runs `Verify` (same non-whitespace tokens, case-insensitive only for words) and returns the original text on mismatch. Keep that check. Every new formatting rule needs a golden test plus an entry in `Formatting_never_changes_tokens_and_is_idempotent`.
- Many T-SQL words are identifiers to the lexer, not keywords (THROW, TRY/CATCH, APPLY, USING, OFFSET, OUTPUT, NOCOUNT …). Handle them through `IsSoftKeyword`/`StatementStarters`, gated on context, so column names aren't upper-cased.
- Never block a keystroke on the network: schema loads only through `SchemaService` (background `Task.Run`), and suggestions must work with `schema == null`.
- `SqlDocument.IsSql` is decided lazily per command, because SSMS can assign the T-SQL language service after the view is created.
- `Log` is safe from any thread; `LogPane` is UI-thread only. Keep the build warning-free (VSTHRD/VSSDK analyzers are on).
- Only Windows and SQL auth produce schema suggestions; Entra ID is a known gap (`SsmsConnectionInfo.Unsupported`).

## Repo notes

- Remote: https://github.com/dathoang/SqlSense (public). `docs/` is git-ignored: it holds private REA notes on SQL Prompt's internals and must not be committed.
- SSMS's built-in IntelliSense should be off while testing (Tools › Options › Text Editor › Transact-SQL › IntelliSense).
- Runtime diagnostics: Tools › SqlSense › Show Log (Output pane "SqlSense").
