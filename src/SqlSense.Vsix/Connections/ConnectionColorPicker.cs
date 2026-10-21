using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.VisualStudio.PlatformUI;
using Microsoft.VisualStudio.Shell;
using SqlSense.Core.Connections;

namespace SqlSense.Connections
{
    /// <summary>Palette dialog opened from a document tab's Connection Color menu.</summary>
    internal sealed class ConnectionColorPicker : DialogWindow
    {
        private readonly Color? _current;

        private ConnectionColorPicker(string target, Color? current)
        {
            _current = current;
            Title = "SqlSense Connection Color";
            SizeToContent = SizeToContent.WidthAndHeight;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            HasMinimizeButton = false;
            HasMaximizeButton = false;
            ShowInTaskbar = false;
            SetResourceReference(BackgroundProperty, VsBrushes.ToolWindowBackgroundKey);
            SetResourceReference(ForegroundProperty, VsBrushes.ToolWindowTextKey);

            var root = new StackPanel { Margin = new Thickness(12) };
            root.Children.Add(new TextBlock { Text = "Color for " + target, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 8) });

            var swatches = new WrapPanel { Width = 6 * 34 };
            foreach (var pair in ColorText.Palette)
                swatches.Children.Add(Swatch(pair.Key, pair.Value));
            root.Children.Add(swatches);

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
            buttons.Children.Add(TextButton("Custom...", (s, e) => PickCustom()));
            buttons.Children.Add(TextButton("No color", (s, e) => Choose(null)));
            var cancel = TextButton("Cancel", (s, e) => DialogResult = false);
            cancel.IsCancel = true;
            buttons.Children.Add(cancel);
            root.Children.Add(buttons);

            Content = root;
        }

        /// <summary>The chosen colour; null when "No color" was chosen.</summary>
        public uint? Selected { get; private set; }

        /// <summary>Shows the picker. False when cancelled.</summary>
        public static bool Show(string target, Color? current, out uint? selected)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var picker = new ConnectionColorPicker(target, current);
            var ok = picker.ShowModal() == true;
            selected = picker.Selected;
            return ok;
        }

        private Button Swatch(string name, uint argb)
        {
            var color = Color.FromArgb((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb);
            var selected = _current == color;
            var button = new Button
            {
                Width = 30,
                Height = 30,
                Margin = new Thickness(2),
                ToolTip = name,
                Background = new SolidColorBrush(color),
                BorderThickness = new Thickness(selected ? 3 : 1),
                Content = selected ? new TextBlock { Text = "✓", Foreground = color.R * 0.3 + color.G * 0.59 + color.B * 0.11 > 150 ? Brushes.Black : Brushes.White, FontWeight = FontWeights.Bold } : null,
            };
            button.SetResourceReference(BorderBrushProperty, VsBrushes.ToolWindowTextKey);
            button.Click += (s, e) => Choose(argb);
            return button;
        }

        private static Button TextButton(string text, RoutedEventHandler click)
        {
            var button = new Button { Content = text, MinWidth = 75, Margin = new Thickness(6, 0, 0, 0), Padding = new Thickness(8, 2, 8, 2) };
            button.Click += click;
            return button;
        }

        private void PickCustom()
        {
            using (var dialog = new System.Windows.Forms.ColorDialog { FullOpen = true, AnyColor = true })
            {
                if (_current is Color c) dialog.Color = System.Drawing.Color.FromArgb(c.A, c.R, c.G, c.B);
                if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
                Choose(0xFF000000u | (uint)(dialog.Color.ToArgb() & 0xFFFFFF));
            }
        }

        private void Choose(uint? argb)
        {
            Selected = argb;
            DialogResult = true;
        }
    }
}
