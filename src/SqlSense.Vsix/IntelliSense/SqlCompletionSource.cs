using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Media;
using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.Text;
using SqlSense.Core.Completion;
using SqlSense.Diagnostics;
using SqlSense.Editor;
using SqlSense.Schema;
using SqlSense.Settings;

namespace SqlSense.IntelliSense
{
    /// <summary>Feeds the editor's completion session with SqlSense suggestions.</summary>
    internal sealed class SqlCompletionSource : ICompletionSource
    {
        private const int MaxItems = 4000;
        private readonly ITextBuffer _buffer;
        private readonly IGlyphService _glyphs;
        private readonly Dictionary<CompletionItemKind, ImageSource> _icons = new Dictionary<CompletionItemKind, ImageSource>();

        public SqlCompletionSource(ITextBuffer buffer, IGlyphService glyphs)
        {
            _buffer = buffer;
            _glyphs = glyphs;
        }

        public void AugmentCompletionSession(ICompletionSession session, IList<CompletionSet> completionSets)
        {
            var settings = SqlSenseSettings.Current;
            if (!settings.Enabled || !SqlDocument.IsSql(_buffer)) return;
            if (!session.Properties.ContainsProperty(SqlCommandFilter.SessionMarker)) return; // only sessions SqlSense started

            try
            {
                var snapshot = _buffer.CurrentSnapshot;
                var trigger = session.GetTriggerPoint(snapshot);
                if (trigger == null) return;
                var caret = trigger.Value.Position;
                var text = snapshot.GetText();

                var conn = SsmsConnection.GetActive();
                var schema = SchemaService.Get(conn, SqlDocument.DatabaseFromUse(text, caret));
                var engine = new CompletionEngine(settings.Completion, SnippetCache.Get(settings))
                {
                    NewLine = SqlDocument.LineBreak(snapshot),
                    FilterByPrefix = false,
                };
                var result = engine.GetCompletions(text, caret, schema);
                if (result.Items.Count == 0) return;

                var start = Math.Min(result.Context.PrefixStart, caret);
                var span = snapshot.CreateTrackingSpan(start, caret - start, SpanTrackingMode.EdgeInclusive);
                var completions = result.Items.Take(MaxItems).Select(ToCompletion).ToList();
                completionSets.Add(new SqlCompletionSet("SqlSense", "SqlSense", span, completions));
            }
            catch (Exception ex)
            {
                Log.Error("Building suggestions", ex);
            }
        }

        private Completion ToCompletion(CompletionItem item)
        {
            var c = new Completion(item.DisplayText, item.InsertText, item.Description, Icon(item.Kind), null);
            c.Properties.AddProperty(typeof(CompletionItem), item);
            return c;
        }

        private ImageSource Icon(CompletionItemKind kind)
        {
            if (_glyphs == null) return null;
            if (_icons.TryGetValue(kind, out var icon)) return icon;
            StandardGlyphGroup group;
            var item = StandardGlyphItem.GlyphItemPublic;
            switch (kind)
            {
                case CompletionItemKind.Keyword: group = StandardGlyphGroup.GlyphKeyword; break;
                case CompletionItemKind.Snippet: group = StandardGlyphGroup.GlyphCSharpExpansion; break;
                case CompletionItemKind.JoinCondition: group = StandardGlyphGroup.GlyphGroupOperator; break;
                case CompletionItemKind.Column: group = StandardGlyphGroup.GlyphGroupField; break;
                case CompletionItemKind.Alias: group = StandardGlyphGroup.GlyphReference; break;
                case CompletionItemKind.Table: group = StandardGlyphGroup.GlyphGroupStruct; break;
                case CompletionItemKind.LocalTable: group = StandardGlyphGroup.GlyphGroupStruct; item = StandardGlyphItem.GlyphItemPrivate; break;
                case CompletionItemKind.View: group = StandardGlyphGroup.GlyphGroupTypedef; break;
                case CompletionItemKind.Synonym: group = StandardGlyphGroup.GlyphGroupTypedef; item = StandardGlyphItem.GlyphItemShortcut; break;
                case CompletionItemKind.TableFunction: group = StandardGlyphGroup.GlyphGroupMethod; item = StandardGlyphItem.GlyphItemProtected; break;
                case CompletionItemKind.ScalarFunction: group = StandardGlyphGroup.GlyphExtensionMethod; break;
                case CompletionItemKind.Procedure: group = StandardGlyphGroup.GlyphGroupMethod; break;
                case CompletionItemKind.Schema: group = StandardGlyphGroup.GlyphGroupNamespace; break;
                case CompletionItemKind.Database: group = StandardGlyphGroup.GlyphGroupModule; break;
                case CompletionItemKind.Variable: group = StandardGlyphGroup.GlyphGroupVariable; break;
                case CompletionItemKind.Function: group = StandardGlyphGroup.GlyphGroupIntrinsic; break;
                case CompletionItemKind.DataType: group = StandardGlyphGroup.GlyphGroupValueType; break;
                default: group = StandardGlyphGroup.GlyphGroupUnknown; break;
            }
            icon = _glyphs.GetGlyph(group, item);
            _icons[kind] = icon;
            return icon;
        }

        public void Dispose()
        {
        }
    }

    /// <summary>Snippet library cached until settings change.</summary>
    internal static class SnippetCache
    {
        private static SnippetLibrary _library;
        private static SqlSenseSettings _for;

        public static SnippetLibrary Get(SqlSenseSettings settings)
        {
            if (_library == null || !ReferenceEquals(_for, settings))
            {
                _library = settings.LoadSnippets();
                _for = settings;
            }
            return _library;
        }
    }
}
