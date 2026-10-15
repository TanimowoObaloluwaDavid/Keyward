using System;
using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Keyward.Core.Crypto;
using Keyward.Core.Services;
using Keyward.Ui.Design;

namespace Keyward.Ui.Screens
{
    /// <summary>
    /// The first-run and unlock experience.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This screen has to do two jobs at once: create a vault for someone who has never used
    /// Keyward, and unlock one for someone returning. The differences that matter are the confirm
    /// field, the strength guidance, and the wording about recovery - so the layout stays the same
    /// and only those parts change.
    /// </para>
    /// <para>
    /// The recovery warning is stated plainly and cannot be dismissed by clicking past it. A user
    /// who does not understand that losing the master password means losing the vault cannot make
    /// an informed decision, and this is the only moment they can be told.
    /// </para>
    /// </remarks>
    public sealed class WelcomeView
    {
        private readonly MainWindow _window;
        private readonly string _message;

        private bool _isFirstRun;
        private Factory.SecretInput _passwordInput;
        private Factory.SecretInput _confirmInput;
        private TextBlock _error;
        private TextBlock _strengthLabel;
        private Border _strengthFill;
        private Button _submit;
        private bool _busy;

        public WelcomeView(MainWindow window, string message)
        {
            _window = window;
            _message = message;
            _isFirstRun = !window.Session.VaultExists;
        }

        public UIElement Build()
        {
            Theme t = Factory.Theme;
            Settings settings = _window.AppSettings;

            Grid root = new Grid { Background = t.Background };

            // A very soft brand glow, so the screen is not a flat slab of colour.
            Ellipse glow = new Ellipse();
            glow.Width = 620;
            glow.Height = 620;
            glow.HorizontalAlignment = HorizontalAlignment.Left;
            glow.VerticalAlignment = VerticalAlignment.Top;
            glow.Margin = new Thickness(-180, -220, 0, 0);
            glow.Fill = new RadialGradientBrush
            {
                GradientStops =
                {
                    new GradientStop(((SolidColorBrush)Theme.WithOpacity(((SolidColorBrush)t.Accent).Color, t.IsDark ? 0.10 : 0.14)).Color, 0),
                    new GradientStop(Colors.Transparent, 1)
                }
            };
            root.Children.Add(glow);

            StackPanel column = new StackPanel
            {
                Width = 440,
                Margin = new Thickness(24, 24, 24, 24),
                VerticalAlignment = VerticalAlignment.Center
            };

            // ---- brand -------------------------------------------------------------------
            Border mark = Factory.Rect(52, 52, 14, t.AccentSoft, 0);
            Grid markHost = new Grid { Width = 52, Height = 52, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 18) };
            markHost.Children.Add(mark);
            markHost.Children.Add(Factory.Icon("vault", 26, t.Accent));
            column.Children.Add(markHost);

            column.Children.Add(Factory.Text(_isFirstRun ? "Create your vault" : "Welcome back",
                t.DisplaySize, t.TextPrimary, false));

            TextBlock subtitle = Factory.Body(
                _isFirstRun
                    ? "Everything you store here is encrypted on this computer. Nothing is uploaded, and there is no account to create."
                    : "Enter your master password to decrypt your vault.",
                t.TextSecondary);
            subtitle.Margin = new Thickness(0, 10, 0, 0);
            column.Children.Add(subtitle);

            if (!string.IsNullOrEmpty(_message))
            {
                Border note = Factory.Card(false, t.RadiusControl, 14);
                note.Background = t.WarningSoft;
                note.BorderBrush = t.Warning;
                note.Margin = new Thickness(0, 16, 0, 0);
                note.Child = WarningRow(t, "info", t.Warning, _message, null);
                column.Children.Add(note);
            }

            // ---- password ----------------------------------------------------------------
            // Breathing room below the subtitle in both modes; on first run the brand mark
            // already provides separation, but the unlock screen has no mark above the copy.
            column.Children.Add(Spacer(20));

            column.Children.Add(Factory.Label("Master password", t.TextSecondary));
            column.Children.Add(Spacer(8));

            _passwordInput = Factory.SecretField(42, false);
            AutomationProperties.SetName(_passwordInput.Element, "Master password");
            _passwordInput.ValueChanged += delegate { OnPasswordChanged(); };
            column.Children.Add(_passwordInput.Element);

            if (_isFirstRun)
            {
                // Live strength guidance.
                _strengthFill = new Border
                {
                    Height = 5,
                    CornerRadius = new CornerRadius(3),
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Width = 0,
                    Background = t.Border
                };
                Grid strengthRow = new Grid { Margin = new Thickness(0, 12, 0, 0) };
                strengthRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                strengthRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                Border track = new Border
                {
                    Height = 5,
                    CornerRadius = new CornerRadius(3),
                    Background = t.Border,
                    VerticalAlignment = VerticalAlignment.Center,
                    Width = 168
                };
                Grid trackHost = new Grid { Margin = new Thickness(0, 0, 12, 0) };
                trackHost.Children.Add(track);
                trackHost.Children.Add(_strengthFill);
                Grid.SetColumn(trackHost, 0);
                _strengthLabel = Factory.Text(string.Empty, t.CaptionSize, t.TextMuted);
                _strengthLabel.VerticalAlignment = VerticalAlignment.Center;
                Grid.SetColumn(_strengthLabel, 1);
                strengthRow.Children.Add(trackHost);
                strengthRow.Children.Add(_strengthLabel);
                column.Children.Add(strengthRow);

                column.Children.Add(Spacer(12));
                column.Children.Add(Factory.Label("Confirm master password", t.TextSecondary));
                column.Children.Add(Spacer(6));
                _confirmInput = Factory.SecretField(42, false);
                AutomationProperties.SetName(_confirmInput.Element, "Confirm master password");
                column.Children.Add(_confirmInput.Element);
            }

            // ---- recovery warning --------------------------------------------------------
            if (_isFirstRun)
            {
                Border warn = Factory.Card(false, t.RadiusControl, 14);
                warn.Background = t.WarningSoft;
                warn.BorderBrush = t.Warning;
                warn.Margin = new Thickness(0, 16, 0, 0);
                warn.Child = WarningRow(t, "warning", t.Warning,
                    "Nobody can reset it or open your vault without it. If you forget it, your data is " +
                    "gone. Write it down and keep it somewhere safe.",
                    "There is no way to recover this password");
                column.Children.Add(warn);
            }

            // ---- error ------------------------------------------------------------------
            _error = Factory.Text(string.Empty, t.CaptionSize, t.Danger);
            _error.TextWrapping = TextWrapping.Wrap;
            _error.Margin = new Thickness(0, 12, 0, 0);
            column.Children.Add(_error);

            column.Children.Add(Spacer(16));

            _submit = Factory.Button(_isFirstRun ? "Create vault" : "Unlock", ButtonKind.Primary, Submit, null, 44);
            _submit.IsDefault = true;
            _submit.HorizontalAlignment = HorizontalAlignment.Stretch;
            column.Children.Add(_submit);

            // Centre with a three-column grid (star / auto / star). The ScrollViewer sits inside
            // the centred column so it inherits a bounded width; putting it outside would let it
            // measure the panel at infinite width and push it off to one side.
            ScrollViewer scroller = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };

            Grid centering = new Grid();
            centering.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            centering.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            centering.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            scroller.Content = column;
            Grid.SetColumn(scroller, 1);
            centering.Children.Add(scroller);

            root.Children.Add(centering);

            return root;
        }

        /// <summary>
        /// An icon beside wrapping text.
        /// </summary>
        /// <remarks>
        /// Built as a two-column Grid rather than a horizontal StackPanel. Inside a horizontal
        /// StackPanel the text child is offered infinite width, so it renders on a single line and
        /// is silently clipped instead of wrapping.
        /// </remarks>
        private static UIElement WarningRow(Theme t, string icon, Brush accent, string message,
                                            string heading)
        {
            Grid row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            Path glyph = Factory.Icon(icon, 17, accent);
            glyph.VerticalAlignment = VerticalAlignment.Top;
            glyph.Margin = new Thickness(0, 1, 0, 0);
            Grid.SetColumn(glyph, 0);
            row.Children.Add(glyph);

            StackPanel text = new StackPanel { Margin = new Thickness(12, 0, 0, 0) };
            if (!string.IsNullOrEmpty(heading))
                text.Children.Add(Factory.Text(heading, t.BodySize, t.TextPrimary, true));

            TextBlock body = Factory.Body(message, t.TextSecondary);
            if (!string.IsNullOrEmpty(heading)) body.Margin = new Thickness(0, 4, 0, 0);
            text.Children.Add(body);

            Grid.SetColumn(text, 1);
            row.Children.Add(text);
            return row;
        }

        /// <summary>Shows what unlocking will actually cost on this machine.</summary>
        private UIElement BuildCostNote(Theme t, Settings settings)
        {
            string note = string.Format(CultureInfo.InvariantCulture,
                "Vault data is encrypted with AES-256 and unlocked using PBKDF2-HMAC-SHA512 at {0:N0} iterations. " +
                "There is no recovery and no backdoor.",
                KdfParameters.DefaultIterations);

            TextBlock text = Factory.Text(note, t.CaptionSize, t.TextMuted);
            text.TextWrapping = TextWrapping.Wrap;
            text.LineHeight = t.CaptionSize * 1.45;
            return text;
        }

        private static UIElement Spacer(double height)
        {
            return new FrameworkElement { Height = height };
        }

        private void OnPasswordChanged()
        {
            if (!_isFirstRun) return;
            if (_strengthLabel == null) return;

            string value = _passwordInput == null ? string.Empty : _passwordInput.GetValue();
            PasswordStrength strength = PasswordGenerator.Estimate(value);

            Theme t = Factory.Theme;
            Brush colour;
            switch (strength.Score)
            {
                case 0: colour = t.Danger; break;
                case 1: colour = t.Danger; break;
                case 2: colour = t.Warning; break;
                case 3: colour = t.Success; break;
                default: colour = t.Success; break;
            }

            _strengthLabel.Text = value.Length == 0
                ? string.Empty
                : strength.Label + "  \u00b7  about " + strength.Bits + " bits";
            _strengthLabel.Foreground = value.Length == 0 ? t.TextMuted : colour;

            double trackWidth = 168;
            double filled = trackWidth * (strength.Bits >= 100 ? 1.0 : Math.Max(0.08, strength.Bits / 100.0));
            _strengthFill.Width = value.Length == 0 ? 0 : filled;
            _strengthFill.Background = value.Length == 0 ? t.Border : colour;
        }

        private void Submit()
        {
            if (_busy) return;

            Theme t = Factory.Theme;
            string password = _passwordInput == null ? string.Empty : _passwordInput.GetValue();

            if (string.IsNullOrEmpty(password))
            {
                Fail("Enter your master password.");
                return;
            }

            if (_isFirstRun)
            {
                if (password.Length < 10)
                {
                    Fail("Use at least 10 characters. Length matters more than symbols.");
                    return;
                }

                string confirm = _confirmInput == null ? string.Empty : _confirmInput.GetValue();
                if (!string.IsNullOrEmpty(confirm))
                {
                    if (!string.Equals(confirm, password, StringComparison.Ordinal))
                    {
                        Fail("Those passwords do not match.");
                        return;
                    }
                }
                else
                {
                    Fail("Confirm your master password.");
                    return;
                }
            }

            _busy = true;
            _submit.IsEnabled = false;
            _error.Text = string.Empty;

            try
            {
                // Derivation is deliberately slow; show progress rather than freezing the window.
                _submit.Content = Factory.Text("Working\u2026", t.BodySize, t.TextOnAccent, true);

                UnlockOutcome outcome = _window.Session.Unlock(password);
                if (!outcome.Success)
                {
                    Fail(outcome.Message);
                    return;
                }

                _window.AppSettings.HasCompletedOnboarding = true;
                _window.AppSettings.Save();
                _window.Session.ArmAutoLock(_window.AppSettings.AutoLockMinutes);
                _window.GoVault(null);

                if (outcome.VaultCreated)
                    _window.Notifications.Show("Vault created and encrypted on this computer.");
                else
                    _window.Notifications.Show("Vault unlocked.");
            }
            catch (Exception ex)
            {
                Fail("The vault could not be opened. " + ex.Message);
            }
            finally
            {
                _busy = false;
                if (_submit != null) _submit.IsEnabled = true;
            }
        }

        private void Fail(string message)
        {
            _error.Text = message ?? "Something went wrong.";
        }
    }
}
