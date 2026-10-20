using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Data;
using System.Linq;
using System.Reflection;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using SqlSense.Core.Export;
using SqlSense.Diagnostics;

namespace SqlSense.Results
{
    /// <summary>The result grid a command acts on, read into a <see cref="ResultTable"/>.</summary>
    internal sealed class GridSnapshot
    {
        public ResultTable Table { get; set; }

        /// <summary>Zero-based position of the grid among the window's result sets.</summary>
        public int GridIndex { get; set; }

        public int GridCount { get; set; }

        /// <summary>True when only the selected cells were taken (more than one cell selected).</summary>
        public bool FromSelection { get; set; }
    }

    /// <summary>
    /// Reads the focused results grid of the active query window. SSMS 22 keeps it in internal types,
    /// so this walks them by reflection:
    /// SqlScriptEditorControl.m_sqlResultsControl (DisplaySQLResultsControl)
    ///   .m_gridResultsPage (GridResultsTabPage: FocusedGrid, m_gridContainers)
    ///   → ResultSetAndGridContainer { Grid (IGridControl2), QEResultSet (IGridStorage) }.
    /// Grid UI column 0 is the row-number column; storage columns before NumberOfDataColumns are too.
    /// </summary>
    internal static class ResultGridReader
    {
        private const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        /// <summary>Null when no query window with grid results is active; <paramref name="why"/> says why.</summary>
        public static GridSnapshot ReadActive(out string why)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            why = null;
            try
            {
                var editor = ActiveDocView();
                var results = editor == null ? null : Field(editor, "m_sqlResultsControl");
                var page = results == null ? null : Field(results, "m_gridResultsPage");
                var containers = (page == null ? null : Field(page, "m_gridContainers") as IEnumerable)?.Cast<object>().ToList();
                if (containers == null || containers.Count == 0)
                {
                    why = editor == null ? "open a query window first." : "run a query with results to grid first.";
                    return null;
                }

                var focused = Prop(page, "FocusedGrid");
                var index = focused == null ? -1 : containers.FindIndex(c => ReferenceEquals(Prop(c, "Grid"), focused));
                if (index < 0) index = 0;
                var container = containers[index];
                var grid = Prop(container, "Grid");
                var rs = Prop(container, "QEResultSet");
                if (grid == null || rs == null)
                {
                    why = "the results grid could not be read (SSMS internals changed?).";
                    return null;
                }

                var snapshot = Read(grid, rs);
                snapshot.GridIndex = index;
                snapshot.GridCount = containers.Count;
                return snapshot;
            }
            catch (Exception ex)
            {
                Log.Error("Reading the results grid", ex);
                why = "the results grid could not be read (see Tools › SqlSense › Show Log).";
                return null;
            }
        }

        private static object ActiveDocView()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (!(Package.GetGlobalService(typeof(SVsShellMonitorSelection)) is IVsMonitorSelection selection)) return null;
            if (selection.GetCurrentElementValue((uint)VSConstants.VSSELELEMID.SEID_DocumentFrame, out var frameObj) != VSConstants.S_OK) return null;
            if (!(frameObj is IVsWindowFrame frame)) return null;
            return frame.GetProperty((int)__VSFPROPID.VSFPROPID_DocView, out var view) == VSConstants.S_OK ? view : null;
        }

        private static GridSnapshot Read(object grid, object rs)
        {
            var total = Convert.ToInt32(Prop(rs, "TotalNumberOfColumns"));
            var dataCount = Convert.ToInt32(Prop(rs, "NumberOfDataColumns"));
            var offset = Math.Max(0, total - dataCount);
            var rowCount = Convert.ToInt64(Call(rs, "NumRows"));
            var columns = Columns(rs, dataCount);

            // Selection, in UI coordinates; a single cell (or nothing) means "the whole grid".
            var blocks = new List<(long Top, long Bottom, int Left, int Right)>();
            if (Prop(grid, "SelectedCells") is IEnumerable selected)
            {
                foreach (var b in selected)
                {
                    var x = Convert.ToInt32(Prop(b, "X"));
                    var right = Convert.ToInt32(Prop(b, "Right"));
                    var y = Convert.ToInt64(Prop(b, "Y"));
                    var bottom = Convert.ToInt64(Prop(b, "Bottom"));
                    blocks.Add((Math.Min(y, bottom), Math.Max(y, bottom), Math.Min(x, right), Math.Max(x, right)));
                }
            }
            var fromSelection = blocks.Count > 1 || (blocks.Count == 1 && (blocks[0].Top != blocks[0].Bottom || blocks[0].Left != blocks[0].Right));

            IList<int> dataCols;
            IEnumerable<long> rows;
            if (fromSelection)
            {
                var storageCols = new SortedSet<int>();
                var rowSet = new SortedSet<long>();
                foreach (var (top, bottom, left, right) in blocks)
                {
                    for (var ui = left; ui <= right; ui++)
                        storageCols.Add(StorageIndex(grid, ui));
                    for (var r = Math.Max(0, top); r <= bottom && r < rowCount; r++)
                        rowSet.Add(r);
                }
                // Selecting only the row-number column (a row header click) means whole rows.
                dataCols = storageCols.Where(s => s >= offset && s < total).Select(s => s - offset).ToList();
                if (dataCols.Count == 0) dataCols = Enumerable.Range(0, dataCount).ToList();
                rows = rowSet;
            }
            else
            {
                dataCols = Enumerable.Range(0, dataCount).ToList();
                rows = LongRange(rowCount);
            }

            var cellTypes = new[] { typeof(long), typeof(int) };
            var isNull = Method(rs, "IsCellDataNull", cellTypes);
            var getData = Method(rs, "GetCellData", cellTypes);
            var getText = Method(rs, "GetCellDataAsString", cellTypes);
            var data = new List<object[]>();
            foreach (var r in rows)
            {
                var values = new object[dataCols.Count];
                for (var i = 0; i < dataCols.Count; i++)
                    values[i] = Cell(rs, isNull, getData, getText, r, dataCols[i] + offset);
                data.Add(values);
            }

            return new GridSnapshot
            {
                Table = new ResultTable(dataCols.Select(i => columns[i]).ToList(), data),
                FromSelection = fromSelection,
            };
        }

        private static IEnumerable<long> LongRange(long count)
        {
            for (long i = 0; i < count; i++) yield return i;
        }

        private static int StorageIndex(object grid, int uiIndex)
        {
            try
            {
                return Convert.ToInt32(Call(grid, "GetStorageColumnIndexByUIIndex", uiIndex));
            }
            catch (Exception)
            {
                return uiIndex;
            }
        }

        private static object Cell(object rs, MethodInfo isNull, MethodInfo getData, MethodInfo getText, long row, int storageCol)
        {
            var args = new object[] { row, storageCol };
            try
            {
                if (isNull != null && isNull.Invoke(rs, args) is bool b && b) return null;
                if (getData != null) return getData.Invoke(rs, args);
            }
            catch (TargetInvocationException)
            {
                // Some storage types only render as text; fall back below.
            }
            return getText?.Invoke(rs, args);
        }

        /// <summary>Column names and server types from the reader's schema table, falling back to ColumnNames.</summary>
        private static List<ResultColumn> Columns(object rs, int dataCount)
        {
            var schema = Field(rs, "m_schemaTable") as DataTable;
            var names = Prop(rs, "ColumnNames") as StringCollection;
            var result = new List<ResultColumn>(dataCount);
            for (var i = 0; i < dataCount; i++)
            {
                string name = null, type = null;
                if (schema != null && i < schema.Rows.Count)
                {
                    var row = schema.Rows[i];
                    if (schema.Columns.Contains("ColumnName")) name = row["ColumnName"] as string;
                    if (schema.Columns.Contains("DataTypeName")) type = row["DataTypeName"] as string;
                }
                if (string.IsNullOrEmpty(name) && names != null)
                {
                    // ColumnNames may or may not include the row-number column.
                    var k = names.Count > dataCount ? i + names.Count - dataCount : i;
                    if (k < names.Count) name = names[k];
                }
                result.Add(new ResultColumn(string.IsNullOrEmpty(name) ? "(No column name)" : name, type));
            }
            return result;
        }

        private static object Field(object obj, string name)
        {
            for (var t = obj.GetType(); t != null; t = t.BaseType)
            {
                var f = t.GetField(name, Any | BindingFlags.DeclaredOnly);
                if (f != null) return f.GetValue(obj);
            }
            return null;
        }

        private static object Prop(object obj, string name)
        {
            for (var t = obj.GetType(); t != null; t = t.BaseType)
            {
                var p = t.GetProperty(name, Any | BindingFlags.DeclaredOnly);
                if (p != null && p.GetIndexParameters().Length == 0) return p.GetValue(obj);
            }
            // Explicit interface implementations (IGridControl.SelectedCells, …).
            foreach (var itf in obj.GetType().GetInterfaces())
            {
                var p = itf.GetProperty(name);
                if (p != null && p.GetIndexParameters().Length == 0) return p.GetValue(obj);
            }
            return null;
        }

        private static MethodInfo Method(object obj, string name, Type[] types)
        {
            for (var t = obj.GetType(); t != null; t = t.BaseType)
            {
                var m = t.GetMethod(name, Any | BindingFlags.DeclaredOnly, null, types, null);
                if (m != null) return m;
            }
            return obj.GetType().GetInterfaces().Select(itf => itf.GetMethod(name, types)).FirstOrDefault(m => m != null);
        }

        private static object Call(object obj, string name, params object[] args)
        {
            var m = Method(obj, name, args.Select(a => a.GetType()).ToArray())
                    ?? throw new MissingMethodException(obj.GetType().FullName, name);
            return Invoke(m, obj, args);
        }

        private static object Invoke(MethodInfo m, object obj, object[] args)
        {
            try
            {
                return m.Invoke(obj, args);
            }
            catch (TargetInvocationException ex) when (ex.InnerException != null)
            {
                throw ex.InnerException;
            }
        }
    }
}
