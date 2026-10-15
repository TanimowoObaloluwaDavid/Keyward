using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Keyward.Ui.Design;

namespace Keyward.Ui
{
    /// <summary>
    /// Copies a secret to the clipboard and clears it again after a configurable delay.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The clipboard is shared with every other application on the desktop, so a copied password
    /// is readable by anything running as the user until something else overwrites it. Clearing it
    /// narrows that window but cannot close it, and this class never claims otherwise: the
    /// confirmation message reports whether the clear actually succeeded.
    /// </para>
    /// <para>
    /// Windows offers no reliable way to detect that another application has already replaced the
    /// clipboard contents, and the OS may also keep a separate clipboard history. Those are
    /// documented limitations, not bugs to be papered over.
    /// </para>
    /// </remarks>
    public sealed class ClipboardGuard
    {
        private readonly DispatcherTimer _timer;
        private int _clearSeconds;
        private string _lastClearedValue;
        private DateTime _clearedAtUtc;

        /// <summary>Raised with a message suitable for a transient toast.</summary>
        public event Action<string> Notified;

        public ClipboardGuard()
        {
            _timer = new DispatcherTimer();
            _timer.Tick += OnTick;
        }

        /// <summary>True when a clear actually completed, used so the UI never overstates success.</summary>
        public bool LastClearSucceeded { get; private set; }

        public DateTime LastClearedAtUtc { get { return _clearedAtUtc; } }

        /// <summary>Copies <paramref name="value"/> and schedules the clear.</summary>
        /// <returns>True when the value actually reached the clipboard.</returns>
        public bool Copy(string value, int clearSeconds)
        {
            _timer.Stop();
            _clearSeconds = clearSeconds;

            if (value == null) return false;

            bool copied;
            try
            {
                // Clipboard access is a shared, contended resource and fails for entirely
                // mundane reasons such as another process holding it open. Retry briefly, then
                // give up honestly rather than pretending it worked.
                copied = TrySetClipboard(value, 5);
            }
            catch (Exception)
            {
                copied = false;
            }

            if (!copied)
            {
                Raise("Could not reach the clipboard. Close any program using it and try again.");
                return false;
            }

            Raise(clearSeconds > 0
                ? "Copied. It will be cleared from the clipboard in " + clearSeconds + " seconds."
                : "Copied.");

            if (clearSeconds > 0)
            {
                _timer.Interval = TimeSpan.FromSeconds(clearSeconds);
                _timer.Start();
            }

            return true;
        }

        private static bool TrySetClipboard(string value, int attempts)
        {
            for (int i = 0; i < attempts; i++)
            {
                try
                {
                    Clipboard.SetText(value);
                    return true;
                }
                catch (System.Runtime.InteropServices.COMException)
                {
                    System.Threading.Thread.Sleep(40);
                }
            }
            return false;
        }

        private void OnTick(object sender, EventArgs e)
        {
            _timer.Stop();
            ClearNow();
        }

        /// <summary>Clears the clipboard if it still holds what we put there.</summary>
        public void ClearNow()
        {
            _timer.Stop();

            bool cleared;
            try
            {
                if (Clipboard.ContainsText())
                {
                    string current = Clipboard.GetText();
                    // Only clear if the clipboard is still ours. If the user has since copied
                    // something else, silently wiping their clipboard would be worse than useless.
                    if (_lastClearedValue != null && string.Equals(current, _lastClearedValue, StringComparison.Ordinal))
                    {
                        Clipboard.Clear();
                        cleared = true;
                    }
                    else
                    {
                        cleared = true; // someone else's content; nothing of ours remains
                    }
                }
                else
                {
                    cleared = true;
                }
            }
            catch (Exception)
            {
                cleared = false;
            }

            LastClearSucceeded = cleared;
            _lastClearedValue = null;
            _clearedAtUtc = DateTime.UtcNow;

            if (!cleared)
            {
                Raise("The clipboard could not be cleared. Copy something else to remove the secret.");
            }
        }

        /// <summary>Records what was placed on the clipboard so the clear can verify ownership.</summary>
        public void Remember(string value)
        {
            _lastClearedValue = value;
        }

        public void CancelPendingClear()
        {
            _timer.Stop();
            _lastClearedValue = null;
        }

        private void Raise(string message)
        {
            Action<string> handler = Notified;
            if (handler != null) handler(message);
        }
    }

    /// <summary>A transient, non-blocking message shown at the edge of the window.</summary>
    public sealed class Toast
    {
        private readonly Border _border;
        private readonly DispatcherTimer _timer;

        public Toast()
        {
            Theme t = Factory.Theme;
            _border = Factory.Card(true, t.RadiusControl);
            _border.Padding = new Thickness(16, 11, 16, 11);
            _border.HorizontalAlignment = HorizontalAlignment.Right;
            _border.VerticalAlignment = VerticalAlignment.Bottom;
            _border.Margin = new Thickness(0, 0, 24, 24);
            _border.Visibility = Visibility.Collapsed;
            _border.Child = Factory.Text(string.Empty, t.BodySize, t.TextPrimary);

            _timer = new DispatcherTimer();
            _timer.Interval = TimeSpan.FromSeconds(3.5);
            _timer.Tick += delegate
            {
                _timer.Stop();
                _border.Visibility = Visibility.Collapsed;
            };
        }

        public UIElement Element { get { return _border; } }

        /// <summary>Shows a message. Errors stay longer because they need reading.</summary>
        public void Show(string message, bool isError = false)
        {
            Theme t = Factory.Theme;
            TextBlock text = _border.Child as TextBlock;
            if (text != null)
            {
                text.Text = message ?? string.Empty;
                text.Foreground = isError ? t.Danger : t.TextPrimary;
            }

            _border.Visibility = Visibility.Visible;
            _timer.Interval = TimeSpan.FromSeconds(isError ? 6 : 3.5);
            _timer.Stop();
            _timer.Start();
        }
    }

    /// <summary>
    /// A modal confirmation dialog. Destructive actions must never be a single stray click, so
    /// deletion always goes through here.
    /// </summary>
    public static class Dialogs
    {
        /// <summary>Shows a yes/no dialog. Returns true only when the user confirms.</summary>
        public static bool Confirm(Window owner, string title, string message, string confirmText,
                                  bool destructive = false)
        {
            Theme t = Factory.Theme;
            Window dialog = new Window();
            dialog.Owner = owner;
            dialog.WindowStyle = WindowStyle.None;
            dialog.AllowsTransparency = true;
            dialog.Background = Brushes.Transparent;
            dialog.ShowInTaskbar = false;
            dialog.ResizeMode = ResizeMode.NoResize;
            dialog.SizeToContent = SizeToContent.WidthAndHeight;

            Border card = Factory.Card(true, t.RadiusPanel);
            card.Background = t.Elevated;
            card.Padding = new Thickness(24);
            card.MinWidth = 400;
            card.MaxWidth = 460;
            card.Effect = t.Elevation(6);

            StackPanel stack = new StackPanel();

            StackPanel head = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 14) };
            head.Children.Add(Factory.Icon(destructive ? "warning" : "info", 20, destructive ? t.Danger : t.Accent));
            StackPanel headText = new StackPanel { Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            headText.Children.Add(Factory.Text(title, t.HeadingSize, t.TextPrimary, true));
            head.Children.Add(headText);
            stack.Children.Add(head);

            TextBlock body = Factory.Body(message, t.TextSecondary);
            body.Margin = new Thickness(0, 0, 0, 22);
            stack.Children.Add(body);

            StackPanel actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            actions.Children.Add(Factory.Button("Cancel", ButtonKind.Ghost, delegate { dialog.DialogResult = false; }));
            actions.Children.Add(new FrameworkElement { Width = 8 });
            actions.Children.Add(Factory.Button(confirmText, destructive ? ButtonKind.Danger : ButtonKind.Primary,
                delegate { dialog.DialogResult = true; }));
            stack.Children.Add(actions);

            card.Child = stack;
            dialog.Content = card;

            dialog.Loaded += delegate
            {
                dialog.Owner = owner;
                // Centre over the owner rather than the screen so the dialog reads as attached
                // to the window it is about.
                dialog.Left = owner.Left + (owner.ActualWidth - dialog.ActualWidth) / 2;
                dialog.Top = owner.Top + (owner.ActualHeight - dialog.ActualHeight) / 2.6;
            };

            bool? result = dialog.ShowDialog();
            return result == true;
        }

        /// <summary>Shows an informational dialog with a single acknowledge button.</summary>
        public static void Info(Window owner, string title, string message)
        {
            Theme t = Factory.Theme;
            Window dialog = new Window();
            dialog.Owner = owner;
            dialog.WindowStyle = WindowStyle.None;
            dialog.AllowsTransparency = true;
            dialog.Background = Brushes.Transparent;
            dialog.ShowInTaskbar = false;
            dialog.ResizeMode = ResizeMode.NoResize;
            dialog.SizeToContent = SizeToContent.WidthAndHeight;

            Border card = Factory.Card(true, t.RadiusPanel);
            card.Background = t.Elevated;
            card.Padding = new Thickness(24);
            card.MinWidth = 400;
            card.MaxWidth = 470;
            card.Effect = t.Elevation(6);

            StackPanel stack = new StackPanel();
            stack.Children.Add(Factory.Text(title, t.HeadingSize, t.TextPrimary, true));
            TextBlock body = Factory.Body(message, t.TextSecondary);
            body.Margin = new Thickness(0, 10, 0, 20);
            stack.Children.Add(body);

            StackPanel actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            actions.Children.Add(Factory.Button("Close", ButtonKind.Primary, delegate { dialog.DialogResult = true; }));
            stack.Children.Add(actions);

            card.Child = stack;
            dialog.Content = card;

            dialog.Loaded += delegate
            {
                dialog.Left = owner.Left + (owner.ActualWidth - dialog.ActualWidth) / 2;
                dialog.Top = owner.Top + (owner.ActualHeight - dialog.ActualHeight) / 2.6;
            };

            dialog.ShowDialog();
        }
    }
}
