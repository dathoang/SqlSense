using System;
using System.ComponentModel.Composition;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Utilities;
using SqlSense.Settings;

namespace SqlSense.Connections
{
    /// <summary>
    /// One side of the connection-colour frame around a query editor. Hidden unless
    /// <see cref="ConnectionColorService"/> has given the view's buffer a colour.
    /// </summary>
    internal sealed class ConnectionColorMargin : Border, IWpfTextViewMargin
    {
        private readonly IWpfTextView _view;
        private readonly string _name;
        private readonly bool _horizontal;
        private bool _disposed;

        public ConnectionColorMargin(IWpfTextView view, string name, bool horizontal)
        {
            _view = view;
            _name = name;
            _horizontal = horizontal;
            ClipToBounds = true;
            Update();
            ConnectionColorService.Changed += OnChanged;
            _view.Closed += OnClosed;
        }

        public FrameworkElement VisualElement => this;
        public double MarginSize => _horizontal ? ActualHeight : ActualWidth;
        public bool Enabled => true;

        public ITextViewMargin GetTextViewMargin(string marginName)
            => string.Equals(marginName, _name, StringComparison.OrdinalIgnoreCase) ? this : null;

        private void Update()
        {
            var color = ConnectionColorService.BorderColorOf(_view.TextDataModel.DocumentBuffer);
            if (color == null)
            {
                Visibility = Visibility.Collapsed;
                return;
            }
            var thickness = SqlSenseSettings.Current.BorderThickness;
            if (_horizontal) Height = thickness;
            else Width = thickness;
            if (!(Background is SolidColorBrush b && b.Color == color.Value))
            {
                var brush = new SolidColorBrush(color.Value);
                brush.Freeze();
                Background = brush;
            }
            Visibility = Visibility.Visible;
        }

        private void OnChanged(object sender, EventArgs e) => Update();

        private void OnClosed(object sender, EventArgs e) => Dispose();

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            ConnectionColorService.Changed -= OnChanged;
            _view.Closed -= OnClosed;
        }
    }

    internal abstract class ConnectionColorMarginProvider : IWpfTextViewMarginProvider
    {
        private readonly string _name;
        private readonly bool _horizontal;

        protected ConnectionColorMarginProvider(string name, bool horizontal)
        {
            _name = name;
            _horizontal = horizontal;
        }

        public IWpfTextViewMargin CreateMargin(IWpfTextViewHost wpfTextViewHost, IWpfTextViewMargin marginContainer)
            => new ConnectionColorMargin(wpfTextViewHost.TextView, _name, _horizontal);
    }

    [Export(typeof(IWpfTextViewMarginProvider))]
    [Name(Name)]
    [MarginContainer(PredefinedMarginNames.Top)]
    [ContentType("text")]
    [TextViewRole(PredefinedTextViewRoles.Document)]
    internal sealed class ConnectionColorTopMarginProvider : ConnectionColorMarginProvider
    {
        public const string Name = "SqlSenseConnectionColorTop";
        public ConnectionColorTopMarginProvider() : base(Name, horizontal: true) { }
    }

    [Export(typeof(IWpfTextViewMarginProvider))]
    [Name(Name)]
    [MarginContainer(PredefinedMarginNames.Bottom)]
    [Order(After = PredefinedMarginNames.HorizontalScrollBarContainer)]
    [ContentType("text")]
    [TextViewRole(PredefinedTextViewRoles.Document)]
    internal sealed class ConnectionColorBottomMarginProvider : ConnectionColorMarginProvider
    {
        public const string Name = "SqlSenseConnectionColorBottom";
        public ConnectionColorBottomMarginProvider() : base(Name, horizontal: true) { }
    }

    [Export(typeof(IWpfTextViewMarginProvider))]
    [Name(Name)]
    [MarginContainer(PredefinedMarginNames.Left)]
    [Order(Before = PredefinedMarginNames.Glyph)]
    [ContentType("text")]
    [TextViewRole(PredefinedTextViewRoles.Document)]
    internal sealed class ConnectionColorLeftMarginProvider : ConnectionColorMarginProvider
    {
        public const string Name = "SqlSenseConnectionColorLeft";
        public ConnectionColorLeftMarginProvider() : base(Name, horizontal: false) { }
    }

    [Export(typeof(IWpfTextViewMarginProvider))]
    [Name(Name)]
    [MarginContainer(PredefinedMarginNames.Right)]
    [Order(After = PredefinedMarginNames.VerticalScrollBarContainer)]
    [ContentType("text")]
    [TextViewRole(PredefinedTextViewRoles.Document)]
    internal sealed class ConnectionColorRightMarginProvider : ConnectionColorMarginProvider
    {
        public const string Name = "SqlSenseConnectionColorRight";
        public ConnectionColorRightMarginProvider() : base(Name, horizontal: false) { }
    }
}
