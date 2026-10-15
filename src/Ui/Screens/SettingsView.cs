using System;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using IOPath = System.IO.Path;
using Keyward.Core.Services;
using Keyward.Ui.Design;

namespace Keyward.Ui.Screens
{
    /// <summary>
    /// Settings.
    /// </summary>
    /// <remarks>
    /// Every control on this screen is wired to something that actually takes effect. There is no
    /// placeholder toggle and no "coming soon" row: a setting that does not work is worse than no
    /// setting at all, because the user believes it is protected when it is not.
    /// </remarks>
    public sealed class SettingsView
    {
        private readonly MainWindow _window;
        private readonly Settings _settings;

        public SettingsView(MainWindow window)
        {
            _window = window;
            _settings = window.AppSettings;
        }

        public void Refresh() { Build(); }

        public UIElement Build()
        {
            Theme t = Factory.Theme;

            ScrollViewer scroller = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            StackPanel page = new StackPanel { Margin = new Thickness(32, 28, 32, 40), MaxWidth = 720 };
            page.HorizontalAlignment = HorizontalAlignment.Left;

            page.Children.Add(Factory.Text("Settings", t.DisplaySize * 0.78, t.TextPrimary));
            page.Children.Add(Spacer(24));

            // ---- security ------------------------------------------------------------------
            page.Children.Add(Section(t, "Security"));

            page.Children.Add(Spacer(12));
            page.Children.Add(ComboSetting(t, "Lock automatically after",
                "The vault locks itself when it has not been used for this long.",
                new[] { "1 minute", "5 minutes", "15 minutes", "30 minutes", "1 hour", "Never" },
                SelectedIndex(_settings.AutoLockMinutes),
                delegate(int index)
                {
                    _settings.AutoLockMinutes = new[] { 1, 5, 15, 30, 60, 0 }[index];
                    _settings.Save();
                    _window.Session.ArmAutoLock(_settings.AutoLockMinutes);
                }));

            page.Children.Add(Spacer(10));
            page.Children.Add(SettingRow(t, "Lock when the computer wakes",
                "Locks the vault as soon as the machine resumes from sleep.",
                Factory.Toggle(_settings.LockOnSleep, delegate(bool on)
                {
                    _settings.LockOnSleep = on;
                    _settings.Save();
                })));

            page.Children.Add(Spacer(10));
            page.Children.Add(SettingRow(t, "Lock when Keyward closes",
                "Recommended. Keeps your vault shut whenever the application is not open.",
                Factory.Toggle(_settings.LockOnExit, delegate(bool on)
                {
                    _settings.LockOnExit = on;
                    _settings.Save();
                })));

            page.Children.Add(Spacer(22));

            // ---- clipboard -----------------------------------------------------------------
            page.Children.Add(Section(t, "Clipboard"));

            page.Children.Add(Spacer(12));
            page.Children.Add(SettingRow(t, "Clear copied secrets",
                "Removes a copied password from the clipboard after a short delay. " +
                "The clipboard is shared with every other program, so this narrows the window rather than closing it.",
                Factory.Toggle(_settings.ClearClipboard, delegate(bool on)
                {
                    _settings.ClearClipboard = on;
                    _settings.Save();
                    Rebuild();
                })));

            if (_settings.ClearClipboard)
            {
                page.Children.Add(Spacer(10));
                page.Children.Add(ComboSetting(t, "Clear after",
                    string.Empty,
                    new[] { "10 seconds", "30 seconds", "1 minute", "2 minutes", "5 minutes" },
                    ClipboardIndex(_settings.ClipboardClearSeconds),
                    delegate(int index)
                    {
                        _settings.ClipboardClearSeconds = new[] { 10, 30, 60, 120, 300 }[index];
                        _settings.Save();
                    }));
            }

            page.Children.Add(Spacer(22));

            // ---- appearance ----------------------------------------------------------------
            page.Children.Add(Section(t, "Appearance"));

            page.Children.Add(Spacer(12));
            page.Children.Add(ComboSetting(t, "Theme",
                "Keyward follows Windows by default.",
                new[] { "Match system", "Light", "Dark" },
                (int)_settings.Theme,
                delegate(int index)
                {
                    _settings.Theme = (AppTheme)index;
                    _settings.Save();
                    _window.ApplyTheme();
                }));

            page.Children.Add(Spacer(22));

            // ---- vault file ----------------------------------------------------------------
            page.Children.Add(Section(t, "Vault file"));

            page.Children.Add(Spacer(12));

            Border pathCard = Factory.Card();
            pathCard.Padding = new Thickness(14);
            pathCard.HorizontalAlignment = HorizontalAlignment.Stretch;
            StackPanel pathStack = new StackPanel();
            TextBlock path = Factory.Text(_settings.VaultPath, t.MonoSize, t.TextPrimary);
            path.FontFamily = t.MonoFamily;
            path.TextWrapping = TextWrapping.Wrap;
            pathStack.Children.Add(path);
            page.Children.Add(pathCard);
            _pathCard = pathCard;

            TextBlock size = Factory.Text(VaultSizeDescription(), t.CaptionSize, t.TextMuted);
            size.Margin = new Thickness(2, 8, 0, 0);
            page.Children.Add(size);

            page.Children.Add(Spacer(10));
            StackPanel actions = new StackPanel { Orientation = Orientation.Horizontal };
            actions.Children.Add(Factory.Button("Back up vault", ButtonKind.Secondary, Backup, "download", 36));
            actions.Children.Add(new FrameworkElement { Width = 8 });
            actions.Children.Add(Factory.Button("Change master password", ButtonKind.Secondary, ChangeMaster, "key", 36));
            page.Children.Add(actions);

            page.Children.Add(Spacer(22));

            // ---- keyboard shortcuts --------------------------------------------------------
            page.Children.Add(Section(t, "Keyboard shortcuts"));
            page.Children.Add(Spacer(12));
            page.Children.Add(ShortcutTable(t));

            page.Children.Add(Spacer(26));

            // ---- about ---------------------------------------------------------------------
            page.Children.Add(Section(t, "About"));
            page.Children.Add(Spacer(12));

            Border about = Factory.Card();
            about.Padding = new Thickness(16);
            StackPanel aboutStack = new StackPanel();
            aboutStack.Children.Add(Factory.Text("Keyward 1.0", t.HeadingSize, t.TextPrimary, true));
            aboutStack.Children.Add(Factory.Body(AboutText(), t.TextSecondary));
            about.Child = aboutStack;
            page.Children.Add(about);

            // A ScrollViewer measures its child at infinite width, so wrap the page in a Grid to
            // give it a bounded space to sit in.
            Grid host = new Grid();
            host.Children.Add(page);
            scroller.Content = host;
            return scroller;
        }

        private Border _pathCard;

        private static UIElement Spacer(double height) { return new FrameworkElement { Height = height }; }

        private void Rebuild() { Build(); }

        private string VaultSizeDescription()
        {
            try
            {
                FileInfo info = new System.IO.FileInfo(_settings.VaultPath);
                if (!info.Exists) return "No vault file yet.";
                return string.Format(CultureInfo.CurrentCulture,
                    "{0:N0} bytes on disk. Everything inside is encrypted; the filename and size are not.",
                    info.Length);
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }

        private string AboutText()
        {
            return "A local-first password manager. Your vault is encrypted on this computer with " +
                   "AES-256-CBC and authenticated with HMAC-SHA256, and the key is derived from your " +
                   "master password using PBKDF2-HMAC-SHA512.\n\n" +
                   "Keyward has never been independently audited or certified. It has not been " +
                   "penetration tested by a third party. Treat it as carefully-written software, " +
                   "not as a guarantee.\n\n" +
                   "There is no account, no sync and no telemetry. Nothing about your vault leaves " +
                   "this machine.";
        }

        private static UIElement Section(Theme t, string title)
        {
            TextBlock block = Factory.Label(title, t.Accent);
            block.Margin = new Thickness(2, 0, 0, 0);
            return block;
        }

        /// <summary>A settings row: title, explanation and a control on the right.</summary>
        private UIElement SettingRow(Theme t, string title, string description, FrameworkElement control)
        {
            Grid row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            StackPanel text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 18, 0) };
            text.Children.Add(Factory.Text(title, t.BodySize, t.TextPrimary, true));
            if (!string.IsNullOrEmpty(description))
            {
                TextBlock body = Factory.Body(description, t.TextMuted);
                body.Margin = new Thickness(0, 3, 0, 0);
                body.MaxWidth = 430;
                text.Children.Add(body);
            }
            row.Children.Add(text);

            if (control != null)
            {
                control.VerticalAlignment = VerticalAlignment.Center;
                Grid.SetColumn(control, 1);
                row.Children.Add(control);
            }

            Border card = Factory.Card();
            card.Padding = new Thickness(16);
            card.HorizontalAlignment = HorizontalAlignment.Stretch;
            card.Child = row;
            return card;
        }

        /// <summary>A settings row whose control is a drop-down, wired to a selection callback.</summary>
        private UIElement ComboSetting(Theme t, string title, string description, string[] values,
                                       int selected, Action<int> onChanged)
        {
            ComboBox box = ComboBoxFor(values, selected);
            box.SelectionChanged += delegate { if (onChanged != null) onChanged(box.SelectedIndex); };
            return SettingRow(t, title, description, box);
        }

        private ComboBox ComboBoxFor(string[] values)
        {
            return ComboBoxFor(values, 0);
        }

        private ComboBox ComboBoxFor(string[] values, int selected)
        {
            Theme t = Factory.Theme;
            ComboBox box = new ComboBox();
            box.ItemsSource = values;
            box.SelectedIndex = selected;
            box.Width = 170;
            box.Height = 34;
            box.FontFamily = t.UiFont;
            box.FontSize = t.BodySize;
            box.Foreground = t.TextPrimary;
            box.VerticalContentAlignment = VerticalAlignment.Center;
            box.Padding = new Thickness(10, 0, 10, 0);
            return box;
        }

        private int SelectedIndex(int minutes)
        {
            int[] values = { 1, 5, 15, 30, 60, 0 };
            for (int i = 0; i < values.Length; i++) if (values[i] == minutes) return i;
            return 1;
        }

        private int ClipboardIndex(int seconds)
        {
            int[] values = { 10, 30, 60, 120, 300 };
            for (int i = 0; i < values.Length; i++) if (values[i] == seconds) return i;
            return 1;
        }

        private UIElement ShortcutTable(Theme t)
        {
            Border card = Factory.Card();
            card.Padding = new Thickness(18);
            StackPanel stack = new StackPanel();
            AddShortcutRow(stack, t, "Search the vault", "Ctrl + F");
            AddShortcutRow(stack, t, "New login", "Ctrl + N");
            AddShortcutRow(stack, t, "New secure note", "Ctrl + Shift + N");
            AddShortcutRow(stack, t, "Copy the selected password", "Ctrl + D");
            AddShortcutRow(stack, t, "Edit the selected item", "Ctrl + E");
            AddShortcutRow(stack, t, "Lock the vault", "Ctrl + L");
            AddShortcutRow(stack, t, "Open the generator", "Ctrl + G");
            AddShortcutRow(stack, t, "Open settings", "Ctrl + ,");
            AddShortcutRow(stack, t, "Clear the search box", "Esc");
            card.Child = stack;
            return card;
        }

        private void AddShortcutRow(StackPanel parent, Theme t, string label, string keys)
        {
            Grid row = new Grid { Margin = new Thickness(0, 0, 0, 9) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            row.Children.Add(Factory.Text(label, t.BodySize, t.TextSecondary));

            Border key = Factory.Rect(double.NaN, 24, 6, t.Input, 1, t.Border);
            key.Padding = new Thickness(9, 0, 9, 0);
            key.Child = Factory.Text(keys, t.CaptionSize, t.TextSecondary, true);
            Grid.SetColumn(key, 1);
            row.Children.Add(key);

            parent.Children.Add(row);
        }

        private void Backup()
        {
            try
            {
                AppPaths.EnsureDataDirectory();
                Directory.CreateDirectory(AppPaths.BackupDirectory);

                string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
                string destination = IOPath.Combine(AppPaths.BackupDirectory, "vault-" + stamp + ".kvw");

                File.Copy(_settings.VaultPath, destination, true);
                AtomicFileBridge.Restrict(destination);

                Dialogs.Info(_window, "Backup created",
                    "A copy of your vault was written to:\n\n" + destination + "\n\n" +
                    "It is encrypted with the same master password. Keep it somewhere safe and " +
                    "delete copies you no longer need.");
            }
            catch (Exception ex)
            {
                Dialogs.Info(_window, "Backup failed", ex.Message);
            }
        }

        private void ChangeMaster()
        {
            Window dialog = new Window();
            dialog.Owner = _window;
            dialog.WindowStyle = WindowStyle.None;
            dialog.AllowsTransparency = true;
            dialog.Background = Brushes.Transparent;
            dialog.ShowInTaskbar = false;
            dialog.ResizeMode = ResizeMode.NoResize;
            dialog.SizeToContent = SizeToContent.WidthAndHeight;

            Theme t = Factory.Theme;
            Border card = Factory.Card(true, t.RadiusPanel);
            card.Background = t.Elevated;
            card.Padding = new Thickness(24);
            card.MinWidth = 420;
            card.Effect = t.Elevation(6);

            Factory.SecretInput currentInput = Factory.SecretField(40, false);
            AutomationProperties.SetName(currentInput.Element, "Current master password");
            Factory.SecretInput nextInput = Factory.SecretField(40, false);
            AutomationProperties.SetName(nextInput.Element, "New master password");
            TextBlock error = Factory.Text(string.Empty, t.CaptionSize, t.Danger);

            StackPanel stack = new StackPanel();
            stack.Children.Add(Factory.Text("Change master password", t.HeadingSize, t.TextPrimary, true));
            UIElement[] boxes = { currentInput.Element, nextInput.Element };
            string[] labels = { "Current master password", "New master password" };
            for (int i = 0; i < 2; i++)
            {
                stack.Children.Add(Spacer(16));
                stack.Children.Add(Factory.Label(labels[i], t.TextSecondary));
                stack.Children.Add(Spacer(6));
                stack.Children.Add(boxes[i]);
            }
            stack.Children.Add(Spacer(12));
            stack.Children.Add(error);

            stack.Children.Add(Spacer(20));
            StackPanel actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            actions.Children.Add(Factory.Button("Cancel", ButtonKind.Ghost, delegate { dialog.DialogResult = false; }));
            actions.Children.Add(new FrameworkElement { Width = 8 });
            actions.Children.Add(Factory.Button("Change", ButtonKind.Primary, delegate
            {
                string a = currentInput.GetValue();
                string b = nextInput.GetValue();

                if (string.IsNullOrEmpty(b))
                {
                    error.Text = "Enter a new master password.";
                    return;
                }

                if (b.Length < 10)
                {
                    error.Text = "Use at least 10 characters.";
                    return;
                }

                UnlockOutcome outcome = _window.Session.ChangeMasterPassword(a, b);
                if (!outcome.Success)
                {
                    error.Text = outcome.Message;
                    return;
                }

                dialog.DialogResult = true;
                _window.Notifications.Show("Master password changed.");
            }));

            stack.Children.Add(actions);
            card.Child = stack;
            dialog.Content = card;

            dialog.Loaded += delegate
            {
                dialog.Left = _window.Left + (_window.ActualWidth - dialog.ActualWidth) / 2;
                dialog.Top = _window.Top + (_window.ActualHeight - dialog.ActualHeight) / 2.6;
            };

            dialog.ShowDialog();
        }
    }

    /// <summary>Small bridge so the settings screen can reuse the core ACL helper.</summary>
    internal static class AtomicFileBridge
    {
        internal static void Restrict(string path)
        {
            Keyward.Core.Storage.AtomicFile.RestrictToCurrentUser(path);        }
    }
}
