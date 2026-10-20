using System.ComponentModel.Composition;
using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Utilities;

namespace SqlSense.IntelliSense
{
    [Export(typeof(ICompletionSourceProvider))]
    [ContentType("text")]
    [Name("SqlSense completion")]
    [Order(Before = "default")]
    internal sealed class SqlCompletionSourceProvider : ICompletionSourceProvider
    {
        [Import]
        internal IGlyphService GlyphService { get; set; }

        public ICompletionSource TryCreateCompletionSource(ITextBuffer textBuffer) =>
            textBuffer.Properties.GetOrCreateSingletonProperty(() => new SqlCompletionSource(textBuffer, GlyphService));
    }
}
