using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Keyward.Core.Crypto;
using Keyward.Ui.Design;

namespace Keyward.Ui.Screens
{
    /// <summary>
    /// The password and pass-phrase generator.
    /// </summary>
    /// <remarks>
    /// The control set is deliberately small: a big readable result, the few dials that matter,
    /// and a copy button. Options that do not change the strength of the result are left out
    /// rather than padding the panel.
    /// </remarks>
    public sealed class GeneratorView
    {
        private readonly MainWindow _window;

        private GeneratorOptions _options = new GeneratorOptions();
        private TextBlock _result;
        private TextBlock _resultMeta;
        private StackPanel _passwordControls;
        private StackPanel _passphraseControls;        private TextBox _wordCountBox;
        private TextBox _separatorBox;

        public GeneratorView(MainWindow window)
        {
            _window = window;
        }

        public void Refresh() { Build(); }

        public UIElement Build()
        {
            Theme t = Factory.Theme;

            ScrollViewer scroller = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            StackPanel page = new StackPanel { Margin = new Thickness(32, 28, 32, 40), MaxWidth = 760 };
            page.HorizontalAlignment = HorizontalAlignment.Left;

            page.Children.Add(Factory.Text("Generator", t.DisplaySize * 0.78, t.TextPrimary));
            TextBlock intro = Factory.Body(
                "Generated with the operating system's cryptographic random number generator, the same source used for keys and salts.",
                t.TextSecondary);
            intro.Margin = new Thickness(0, 8, 0, 0);
            page.Children.Add(intro);

            page.Children.Add(Spacer(26));

            // ---- result card ----------------------------------------------------------------
            Border card = Factory.Card(true, t.RadiusPanel);
            card.Padding = new Thickness(20);
            card.HorizontalAlignment = HorizontalAlignment.Stretch;

            Grid resultGrid = new Grid();
            resultGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            resultGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            StackPanel resultStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };

            _result = Factory.Text(string.Empty, t.MonoSize + 3, t.TextPrimary, false);
            _result.FontFamily = t.MonoFamily;
            _result.TextWrapping = TextWrapping.Wrap;
            _result.TextTrimming = TextTrimming.None;
            resultStack.Children.Add(_result);

            _resultMeta = Factory.Text(string.Empty, t.CaptionSize, t.TextMuted);
            _resultMeta.Margin = new Thickness(0, 8, 0, 0);
            resultStack.Children.Add(_resultMeta);

            Grid.SetColumn(resultStack, 0);
            resultGrid.Children.Add(resultStack);

            StackPanel actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            actions.Children.Add(Factory.Button(null, ButtonKind.Ghost, Regenerate, "refresh", 38));
            actions.Children.Add(Factory.Button("Copy", ButtonKind.Primary, CopyResult, "copy", 38));
            Grid.SetColumn(actions, 1);
            resultGrid.Children.Add(actions);

            card.Child = resultGrid;
            page.Children.Add(card);

            page.Children.Add(Spacer(20));

            // ---- mode ------------------------------------------------------------------------
            Grid modeRow = new Grid();
            modeRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            modeRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            Border passwordTab = ModeTab(t, "Password", _options.Mode == GeneratorMode.Password);
            Grid.SetColumn(passwordTab, 0);
            modeRow.Children.Add(passwordTab);

            Border phraseTab = ModeTab(t, "Pass-phrase", _options.Mode == GeneratorMode.Passphrase);
            phraseTab.Margin = new Thickness(8, 0, 0, 0);
            Grid.SetColumn(phraseTab, 1);
            modeRow.Children.Add(phraseTab);

            page.Children.Add(modeRow);
            page.Children.Add(Spacer(20));

            // ---- options ---------------------------------------------------------------------
            Border optionsCard = Factory.Card();
            optionsCard.Padding = new Thickness(20);
            optionsCard.HorizontalAlignment = HorizontalAlignment.Stretch;

            Grid optionsHost = new Grid();
            optionsCard.Child = optionsHost;

            _passwordControls = BuildPasswordOptions(t);
            _passphraseControls = BuildPassphraseOptions(t);

            _optionsHost = new ContentControl { HorizontalContentAlignment = HorizontalAlignment.Stretch };
            UpdateModeVisibility();
            optionsCard.Child = _optionsHost;

            page.Children.Add(optionsCard);

            // A ScrollViewer measures its child at infinite width, so wrap the page in a Grid to
            // give it a bounded space to sit in.
            Grid host = new Grid();
            host.Children.Add(page);
            scroller.Content = host;
            Regenerate();
            return scroller;
        }

        private ContentControl _optionsHost;

        private void UpdateModeVisibility()
        {
            if (_optionsHost == null) return;
            _optionsHost.Content = _options.Mode == GeneratorMode.Passphrase
                ? (object)_passphraseControls
                : _passwordControls;
        }

        private Border ModeTab(Theme t, string label, bool active)
        {
            Border border = Factory.Rect(double.NaN, 40, t.RadiusControl, active ? t.Selection : t.Input,
                                        active ? 0 : 1, active ? Brushes.Transparent : t.Border);
            border.MouseLeftButtonUp += delegate
            {
                _options.Mode = active ? _options.Mode
                    : (label == "Password" ? GeneratorMode.Password : GeneratorMode.Passphrase);
                Build();
            };
            border.Cursor = Cursors.Hand;
            border.Child = Factory.Text(label, t.BodySize, active ? t.TextPrimary : t.TextSecondary, active,
                TextAlignment.Center);
            return border;
        }

        private static UIElement Spacer(double height) { return new FrameworkElement { Height = height }; }

        private StackPanel BuildPasswordOptions(Theme t)
        {
            StackPanel stack = new StackPanel();

            // Length
            stack.Children.Add(Factory.Label("Length", t.TextMuted));
            stack.Children.Add(Spacer(8));
            Grid lengthRow = new Grid();
            lengthRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            lengthRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            Slider slider = new Slider();
            slider.Minimum = 8;
            slider.Maximum = 64;
            slider.Value = _options.Length;
            slider.IsSnapToTickEnabled = true;
            slider.TickFrequency = 4;
            slider.Width = 320;
            slider.Foreground = t.Accent;
            slider.ValueChanged += delegate
            {
                _options.Length = (int)slider.Value;
                if (_result != null) Regenerate();
            };
            Grid.SetColumn(slider, 0);
            lengthRow.Children.Add(slider);

            TextBlock lengthValue = Factory.Text(_options.Length.ToString(CultureInfo.CurrentCulture),
                t.BodySize, t.TextPrimary, true);
            lengthValue.VerticalAlignment = VerticalAlignment.Center;
            lengthValue.Width = 40;
            lengthValue.TextAlignment = TextAlignment.Right;
            Grid.SetColumn(lengthValue, 1);
            lengthRow.Children.Add(lengthValue);
            _lengthValue = lengthValue;
            _lengthSlider = slider;

            stack.Children.Add(lengthRow);
            stack.Children.Add(Spacer(18));

            stack.Children.Add(Factory.Label("Character types", t.TextMuted));
            stack.Children.Add(Spacer(8));

            WrapPanel types = new WrapPanel();
            types.Children.Add(Factory.Check("Uppercase A-Z", _options.UseUppercase, delegate(bool on)
            {
                _options.UseUppercase = on; Regenerate();
            }));
            types.Children.Add(Factory.Check("Lowercase a-z", _options.UseLowercase, delegate(bool on)
            {
                _options.UseLowercase = on; Regenerate();
            }));
            types.Children.Add(Factory.Check("Numbers 0-9", _options.UseDigits, delegate(bool on)
            {
                _options.UseDigits = on; Regenerate();
            }));
            types.Children.Add(Factory.Check("Symbols !@#", _options.UseSymbols, delegate(bool on)
            {
                _options.UseSymbols = on; Regenerate();
            }));
            stack.Children.Add(types);

            stack.Children.Add(Spacer(10));
            stack.Children.Add(Factory.Check("Include each selected type", _options.RequireEachClass, delegate(bool on)
            {
                _options.RequireEachClass = on; Regenerate();
            }));
            stack.Children.Add(Factory.Check("Exclude ambiguous characters (O/0, l/1)", _options.ExcludeAmbiguous, delegate(bool on)
            {
                _options.ExcludeAmbiguous = on; Regenerate();
            }));

            stack.Children.Add(Spacer(12));
            _errorText = Factory.Text(string.Empty, t.CaptionSize, t.Danger);
            _errorText.TextWrapping = TextWrapping.Wrap;
            stack.Children.Add(_errorText);

            return stack;
        }

        private Slider _lengthSlider;
        private TextBlock _lengthValue;
        private TextBlock _errorText;

        private StackPanel BuildPassphraseOptions(Theme t)
        {
            StackPanel stack = new StackPanel();

            stack.Children.Add(Factory.Label("Words", t.TextMuted));
            stack.Children.Add(Spacer(8));

            _wordCountBox = Factory.TextInput(38);
            _wordCountBox.Text = _options.WordCount.ToString(CultureInfo.InvariantCulture);
            _wordCountBox.Width = 90;
            _wordCountBox.HorizontalAlignment = HorizontalAlignment.Left;
            _wordCountBox.TextChanged += delegate { ApplyPassphraseOptions(); };
            stack.Children.Add(_wordCountBox);

            stack.Children.Add(Spacer(18));
            stack.Children.Add(Factory.Label("Separator", t.TextMuted));
            stack.Children.Add(Spacer(8));
            _separatorBox = Factory.TextInput(38);
            _separatorBox.Text = _options.Separator;
            _separatorBox.Width = 90;
            _separatorBox.HorizontalAlignment = HorizontalAlignment.Left;
            _separatorBox.TextChanged += delegate { ApplyPassphraseOptions(); };
            stack.Children.Add(_separatorBox);

            stack.Children.Add(Spacer(16));
            stack.Children.Add(Factory.Check("Capitalise words", _options.CapitalizeWords, delegate(bool on)
            {
                _options.CapitalizeWords = on; Regenerate();
            }));
            stack.Children.Add(Factory.Check("Append a two-digit number", _options.IncludeNumber, delegate(bool on)
            {
                _options.IncludeNumber = on; Regenerate();
            }));

            stack.Children.Add(Spacer(14));
            TextBlock note = Factory.Body(
                "Each word is drawn from a list of 256, so every word adds exactly 8 bits of " +
                "entropy. Four words therefore give 32 bits.",
                t.TextMuted);
            stack.Children.Add(note);

            return stack;
        }

        private void ApplyPassphraseOptions()
        {
            int count;
            if (int.TryParse(_wordCountBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out count))
                _options.WordCount = count;

            _options.Separator = _separatorBox.Text ?? string.Empty;
            Regenerate();
        }

        private string _current = string.Empty;

        private void Regenerate()
        {
            Theme t = Factory.Theme;

            if (_lengthValue != null && _lengthSlider != null)
                _lengthValue.Text = ((int)_lengthSlider.Value).ToString(CultureInfo.CurrentCulture);

            try
            {
                _current = PasswordGenerator.Generate(_options);
                if (_result != null) _result.Text = _current;
                if (_errorText != null) _errorText.Text = string.Empty;

                if (_resultMeta != null)
                {
                    _resultMeta.Text = _options.Mode == GeneratorMode.Passphrase
                        ? _options.WordCount + " words"
                        : _options.Length + " characters";
                    _resultMeta.Foreground = t.TextMuted;
                }
            }
            catch (GeneratorConfigurationException ex)
            {
                _current = string.Empty;
                if (_result != null) _result.Text = "—";
                if (_errorText != null) _errorText.Text = ex.Message;
                if (_resultMeta != null) _resultMeta.Text = string.Empty;
            }
        }

        private void CopyResult()
        {
            if (string.IsNullOrEmpty(_current))
            {
                _window.Notifications.Show("Fix the options first, then copy.");
                return;
            }

            int clear = _window.AppSettings.ClearClipboard ? _window.AppSettings.ClipboardClearSeconds : 0;
            if (_window.Clipboard.Copy(_current, clear))
            {
                _window.Clipboard.Remember(_current);
            }
        }
    }
}
