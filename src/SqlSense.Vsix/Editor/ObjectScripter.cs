using System;
using System.Data.SqlClient;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Threading;
using SqlSense.Core.Completion;
using SqlSense.Diagnostics;
using SqlSense.Schema;

namespace SqlSense.Editor
{
    /// <summary>
    /// After an object is picked in "ALTER PROCEDURE |" (and FUNCTION/VIEW/TRIGGER), loads its definition in the
    /// background and replaces the statement with it, CREATE turned into ALTER. The name is already in the editor,
    /// so nothing waits on the network; the script lands only if the user hasn't typed on meanwhile, and Ctrl+Z
    /// goes back to the bare name.
    /// </summary>
    internal static class ObjectScripter
    {
        public static void Start(ITextView view, ObjectScriptRequest request)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var snapshot = view.TextBuffer.CurrentSnapshot;
            var caret = view.Caret.Position.BufferPosition.Position;
            if (request.StatementStart < 0 || request.StatementStart >= caret) return;

            var text = snapshot.GetText();
            var connectionString = SchemaService.ConnectionStringFor(SsmsConnection.GetActive(), SqlDocument.DatabaseFromUse(text, caret), out _);
            if (connectionString == null) return;

            var span = snapshot.CreateTrackingSpan(Span.FromBounds(request.StatementStart, caret), SpanTrackingMode.EdgeExclusive);
            var expected = span.GetText(snapshot);
            var name = request.Object.QualifiedName;
            EditorActions.Status($"SqlSense: scripting {name}…");

#pragma warning disable VSTHRD010, VSSDK007
            ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
            {
                string definition = null;
                Exception error = null;
                await TaskScheduler.Default;
                try
                {
                    definition = SchemaLoader.LoadDefinition(connectionString, request.Object.Schema, request.Object.Name);
                }
                catch (Exception ex) when (ex is SqlException || ex is InvalidOperationException)
                {
                    error = ex;
                }
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                Apply(view, span, expected, request, definition, error);
            }).FileAndForget("SqlSense/ScriptObject");
#pragma warning restore VSTHRD010, VSSDK007
        }

        private static void Apply(ITextView view, ITrackingSpan span, string expected, ObjectScriptRequest request,
            string definition, Exception error)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var name = request.Object.QualifiedName;
            if (view.IsClosed) return;
            if (error != null)
            {
                Log.Error("Scripting " + name, error);
                EditorActions.Status($"SqlSense: couldn't read the definition of {name}: {error.Message}");
                return;
            }
            var script = ModuleScript.Build(definition, request.Verb);
            if (script == null)
            {
                Log.Info($"No T-SQL definition for {name} (CLR, encrypted, or no VIEW DEFINITION permission).");
                EditorActions.Status($"SqlSense: {name} has no readable definition (CLR, encrypted, or no VIEW DEFINITION permission).");
                return;
            }

            var snapshot = view.TextBuffer.CurrentSnapshot;
            var target = span.GetSpan(snapshot);
            if (target.GetText() != expected || view.Caret.Position.BufferPosition.Position != target.End.Position)
            {
                EditorActions.Status($"SqlSense: the text changed while {name} was loading; its definition was not inserted.");
                return;
            }
            EditorActions.Replace(view, target.Span, script, 0);
            view.Caret.EnsureVisible();
            EditorActions.Status($"SqlSense: scripted {name} as {request.Verb}.");
        }
    }
}
