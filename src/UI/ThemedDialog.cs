using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;
using CodexMarkdownReader.Platform;

namespace CodexMarkdownReader.UI
{
    public enum DialogIcon { None, Info, Warning, Question }

    /// <summary>A small modal dialog that follows the app theme (replaces system MessageBoxes).</summary>
    public sealed class ThemedDialog : Window
    {
        private int result = -1;
        private readonly CheckBox checkBox;
        private readonly int cancelIndex;

        public int Result { get { return result; } }
        public bool IsChecked { get { return checkBox != null && checkBox.IsChecked == true; } }

        public ThemedDialog(Window owner, string title, string heading, string message, string detail, DialogIcon icon,
            string[] buttons, int defaultIndex, int cancelIndex, string checkboxText, UIElement extraContent)
        {
            this.cancelIndex = cancelIndex;
            if (owner != null && owner.IsVisible) Owner = owner;
            Language = ReaderTypography.Language;
            FontFamily = ReaderTypography.Interface;
            FontSize = 13;
            Title = String.IsNullOrEmpty(title) ? "墨读 Markdown" : title;
            Width = 500;
            SizeToContent = SizeToContent.Height;
            MinHeight = 160;
            ResizeMode = ResizeMode.NoResize;
            WindowStyle = WindowStyle.SingleBorderWindow;
            WindowStartupLocation = Owner != null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen;
            ShowInTaskbar = Owner == null;
            UseLayoutRounding = true;
            SnapsToDevicePixels = true;
            SetResourceReference(BackgroundProperty, "Surface");
            SetResourceReference(ForegroundProperty, "Text");

            WindowChrome chrome = new WindowChrome();
            chrome.CaptionHeight = 38;
            chrome.ResizeBorderThickness = new Thickness(0);
            chrome.CornerRadius = new CornerRadius(0);
            chrome.GlassFrameThickness = new Thickness(0);
            chrome.UseAeroCaptionButtons = false;
            WindowChrome.SetWindowChrome(this, chrome);

            DockPanel root = new DockPanel();
            root.LastChildFill = true;

            // Caption
            Grid caption = new Grid();
            caption.Height = 38;
            caption.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            caption.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            TextBlock captionText = new TextBlock();
            captionText.Text = Title;
            captionText.FontSize = 12;
            captionText.Margin = new Thickness(16, 0, 0, 0);
            captionText.VerticalAlignment = VerticalAlignment.Center;
            captionText.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
            caption.Children.Add(captionText);
            Button close = new Button();
            close.SetResourceReference(StyleProperty, "CaptionCloseButton");
            close.Height = 38;
            close.Content = Icons.CreateCaption(Icons.CaptionClose);
            close.ToolTip = "关闭";
            AutomationProperties.SetName(close, "关闭");
            close.Click += delegate { result = this.cancelIndex; Close(); };
            WindowChrome.SetIsHitTestVisibleInChrome(close, true);
            Grid.SetColumn(close, 1);
            caption.Children.Add(close);
            DockPanel.SetDock(caption, Dock.Top);
            root.Children.Add(caption);

            // Footer
            Border footer = new Border();
            footer.BorderThickness = new Thickness(0, 1, 0, 0);
            footer.SetResourceReference(Border.BorderBrushProperty, "Border");
            footer.SetResourceReference(Border.BackgroundProperty, "Surface2");
            footer.Padding = new Thickness(20, 12, 20, 12);
            DockPanel footerDock = new DockPanel();
            footerDock.LastChildFill = false;
            StackPanel buttonRow = new StackPanel();
            buttonRow.Orientation = Orientation.Horizontal;
            DockPanel.SetDock(buttonRow, Dock.Right);
            Button defaultButton = null;
            for (int i = 0; i < buttons.Length; i++)
            {
                int index = i;
                Button button = new Button();
                button.Content = buttons[i];
                button.SetResourceReference(StyleProperty, i == defaultIndex ? "PrimaryButton" : "TextButton");
                button.Margin = new Thickness(8, 0, 0, 0);
                if (i == defaultIndex) { button.IsDefault = true; defaultButton = button; }
                if (i == cancelIndex) button.IsCancel = true;
                button.Click += delegate { result = index; Close(); };
                buttonRow.Children.Add(button);
            }
            footerDock.Children.Add(buttonRow);
            if (!String.IsNullOrEmpty(checkboxText))
            {
                checkBox = new CheckBox();
                checkBox.Content = checkboxText;
                checkBox.VerticalAlignment = VerticalAlignment.Center;
                DockPanel.SetDock(checkBox, Dock.Left);
                footerDock.Children.Add(checkBox);
            }
            footer.Child = footerDock;
            DockPanel.SetDock(footer, Dock.Bottom);
            root.Children.Add(footer);

            // Body
            Grid body = new Grid();
            body.Margin = new Thickness(22, 6, 24, 20);
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            if (icon != DialogIcon.None)
            {
                Border badge = new Border();
                badge.Width = 32; badge.Height = 32;
                badge.CornerRadius = new CornerRadius(16);
                badge.Margin = new Thickness(0, 0, 14, 0);
                badge.VerticalAlignment = VerticalAlignment.Top;
                badge.SetResourceReference(Border.BackgroundProperty, icon == DialogIcon.Warning ? "WarningBg" : "AccentSoft");
                FrameworkElement glyph = Icons.Create(icon == DialogIcon.Warning ? Icons.Warning : Icons.Info, 18, 1.5);
                glyph.HorizontalAlignment = HorizontalAlignment.Center;
                glyph.VerticalAlignment = VerticalAlignment.Center;
                badge.Child = glyph;
                badge.SetResourceReference(TextElement.ForegroundProperty, icon == DialogIcon.Warning ? "AlertWarning" : "Accent");
                body.Children.Add(badge);
            }
            StackPanel text = new StackPanel();
            Grid.SetColumn(text, 1);
            if (!String.IsNullOrEmpty(heading))
            {
                TextBlock headingText = new TextBlock();
                headingText.Text = heading;
                headingText.FontSize = 16;
                headingText.FontWeight = FontWeights.SemiBold;
                headingText.TextWrapping = TextWrapping.Wrap;
                headingText.Margin = new Thickness(0, 4, 0, 8);
                text.Children.Add(headingText);
            }
            if (!String.IsNullOrEmpty(message))
            {
                TextBlock messageText = new TextBlock();
                messageText.Text = message;
                messageText.TextWrapping = TextWrapping.Wrap;
                messageText.LineHeight = 20;
                messageText.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
                text.Children.Add(messageText);
            }
            if (!String.IsNullOrEmpty(detail))
            {
                TextBox detailBox = new TextBox();
                detailBox.Text = detail;
                detailBox.IsReadOnly = true;
                detailBox.TextWrapping = TextWrapping.Wrap;
                detailBox.FontFamily = ReaderTypography.Code;
                detailBox.FontSize = 12;
                detailBox.Margin = new Thickness(0, 10, 0, 0);
                detailBox.Padding = new Thickness(8, 6, 8, 6);
                detailBox.BorderThickness = new Thickness(1);
                detailBox.SetResourceReference(StyleProperty, "BareTextBox");
                detailBox.SetResourceReference(Control.BackgroundProperty, "CodeBg");
                detailBox.SetResourceReference(Control.ForegroundProperty, "Text");
                Border detailBorder = new Border();
                detailBorder.CornerRadius = new CornerRadius(6);
                detailBorder.BorderThickness = new Thickness(1);
                detailBorder.SetResourceReference(Border.BorderBrushProperty, "Border");
                detailBorder.Margin = new Thickness(0, 10, 0, 0);
                detailBox.Margin = new Thickness(0);
                detailBorder.Child = detailBox;
                text.Children.Add(detailBorder);
            }
            if (extraContent != null)
            {
                FrameworkElement extra = extraContent as FrameworkElement;
                if (extra != null) extra.Margin = new Thickness(0, 12, 0, 0);
                text.Children.Add(extraContent);
            }
            body.Children.Add(text);
            root.Children.Add(body);

            Border frame = new Border();
            frame.BorderThickness = new Thickness(1);
            frame.SetResourceReference(Border.BorderBrushProperty, "WindowBorder");
            frame.Child = root;
            Content = frame;

            SourceInitialized += delegate
            {
                try { NativeWindowVisuals.Apply(new WindowInteropHelper(this).Handle, ReaderApp.Palette != null && ReaderApp.Palette.IsDark, false); }
                catch { }
            };
            Loaded += delegate { if (defaultButton != null) defaultButton.Focus(); };
            PreviewKeyDown += delegate(object sender, KeyEventArgs e)
            {
                Key key = e.Key == Key.ImeProcessed ? e.ImeProcessedKey : e.Key;
                if (key == Key.Escape) { result = this.cancelIndex; Close(); e.Handled = true; }
            };
        }

        /// <summary>
        /// Self-test only: when set, dialogs are not shown. The function receives the heading and returns the
        /// index of the button to "press"; every heading is recorded in <see cref="TestLog"/>.
        /// </summary>
        internal static Func<string, int> TestResponder;
        internal static readonly List<string> TestLog = new List<string>();

        public static int Show(Window owner, string heading, string message, DialogIcon icon, string[] buttons, int defaultIndex, int cancelIndex)
        {
            return Show(owner, heading, message, null, icon, buttons, defaultIndex, cancelIndex);
        }

        public static int Show(Window owner, string heading, string message, string detail, DialogIcon icon, string[] buttons, int defaultIndex, int cancelIndex)
        {
            if (TestResponder != null)
            {
                TestLog.Add(heading ?? "");
                return TestResponder(heading ?? "");
            }
            ThemedDialog dialog = new ThemedDialog(owner, null, heading, message, detail, icon, buttons, defaultIndex, cancelIndex, null, null);
            dialog.ShowDialog();
            return dialog.Result;
        }

        public static void Inform(Window owner, string heading, string message, DialogIcon icon)
        {
            Show(owner, heading, message, icon, new string[] { "确定" }, 0, 0);
        }
    }
}
