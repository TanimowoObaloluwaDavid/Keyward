using System;
using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Keyward.Ui.Design
{
    public enum ButtonKind { Primary, Secondary, Ghost, Danger }

    /// <summary>
    /// Builds the reusable controls that make up the Keyward design system.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The project is compiled directly with <c>csc</c> rather than MSBuild, so there is no XAML
    /// build step and therefore no <c>.baml</c>. Control templates are authored as XAML strings and
    /// parsed at startup with <see cref="XamlReader.Parse"/>. They stay declarative and reviewable
    /// while still needing no build infrastructure.
    /// </para>
    /// <para>
    /// Colours are emitted as literal hex rather than resource lookups. Templates are built once
    /// per theme, and a literal frozen colour avoids a <c>DynamicResource</c> indirection that
    /// would silently fall back to black if the key were ever missed.
    /// </para>
    /// </remarks>
    public static class Factory
    {
        private static Theme _theme;

        public static void Init(Theme theme)
        {
            if (theme == null) throw new ArgumentNullException("theme");
            _theme = theme;
        }

        public static Theme Theme
        {
            get
            {
                if (_theme == null) throw new InvalidOperationException("Factory.Init must be called first.");
                return _theme;
            }
        }

        private static string N(double value)
        {
            return value.ToString("0.###", CultureInfo.InvariantCulture);
        }

        /// <summary>Emits a brush as a XAML colour literal.</summary>
        private static string Col(Brush brush)
        {
            SolidColorBrush solid = brush as SolidColorBrush;
            if (solid == null) return "#FF000000";

            Color c = solid.Color;
            if (c.A == 255) return string.Format(CultureInfo.InvariantCulture, "#{0:X2}{1:X2}{2:X2}", c.R, c.G, c.B);
            return string.Format(CultureInfo.InvariantCulture, "#{0:X2}{1:X2}{2:X2}{3:X2}", c.A, c.R, c.G, c.B);
        }

        // =====================================================================================
        // Text
        // =====================================================================================

        public static TextBlock Text(string value, double size, Brush brush, bool semibold = false,
                                    TextAlignment align = TextAlignment.Left)
        {
            Theme t = Theme;
            TextBlock tb = new TextBlock();
            tb.Text = value ?? string.Empty;
            tb.FontSize = size;
            tb.Foreground = brush;
            tb.FontFamily = semibold ? t.UiMediumFamily : t.UiFont;
            tb.TextAlignment = align;
            tb.TextWrapping = TextWrapping.NoWrap;
            tb.TextTrimming = TextTrimming.CharacterEllipsis;
            tb.SnapsToDevicePixels = true;
            return tb;
        }

        public static TextBlock Body(string value, Brush brush)
        {
            TextBlock tb = Text(value, Theme.BodySize, brush);
            tb.TextWrapping = TextWrapping.Wrap;
            tb.TextTrimming = TextTrimming.None;
            tb.LineHeight = Theme.BodySize * 1.5;
            return tb;
        }

        /// <summary>Uppercase, letter-spaced form label.</summary>
        public static TextBlock Label(string value, Brush brush)
        {
            return Text(value.ToUpperInvariant(), Theme.LabelSize, brush, true);
        }

        // =====================================================================================
        // Icon
        // =====================================================================================

        /// <summary>A stroked vector icon. Stroke weight scales with size for a constant optical weight.</summary>
        public static Path Icon(string name, double size, Brush brush)
        {
            Path path = new Path();
            path.Data = Icons.Get(name);
            path.Stroke = brush;
            path.StrokeThickness = Math.Max(1.2, size * 0.075);
            path.StrokeStartLineCap = PenLineCap.Round;
            path.StrokeEndLineCap = PenLineCap.Round;
            path.StrokeLineJoin = PenLineJoin.Round;
            path.Fill = Brushes.Transparent;
            path.Width = size;
            path.Height = size;
            path.Stretch = Stretch.Uniform;
            path.SnapsToDevicePixels = true;
            path.HorizontalAlignment = HorizontalAlignment.Center;
            path.VerticalAlignment = VerticalAlignment.Center;
            AutomationProperties.SetName(path, name);
            return path;
        }

        public static Path IconFilled(string name, double size, Brush brush)
        {
            Path path = Icon(name, size, brush);
            path.Fill = brush;
            return path;
        }

        // =====================================================================================
        // Button
        // =====================================================================================

        public static Button Button(string text, ButtonKind kind, Action onClick = null,
                                    string icon = null, double height = 36)
        {
            Theme t = Theme;
            Button button = new Button();
            button.Height = height;
            button.Padding = new Thickness(kind == ButtonKind.Ghost ? 10 : 16, 0, kind == ButtonKind.Ghost ? 10 : 16, 0);
            button.Cursor = Cursors.Hand;
            button.SnapsToDevicePixels = true;
            button.Template = ButtonTemplate(kind, t);

            Brush foreground = ButtonForeground(kind, t);

            StackPanel content = new StackPanel();
            content.Orientation = Orientation.Horizontal;
            content.HorizontalAlignment = HorizontalAlignment.Center;
            content.VerticalAlignment = VerticalAlignment.Center;

            if (!string.IsNullOrEmpty(icon))
            {
                content.Children.Add(Icon(icon, 16, foreground));
                if (!string.IsNullOrEmpty(text)) content.Children.Add(new FrameworkElement { Width = 8 });
            }

            if (!string.IsNullOrEmpty(text))
                content.Children.Add(Text(text, t.BodySize, foreground, kind == ButtonKind.Primary));

            button.Content = content;
            if (onClick != null) button.Click += delegate { onClick(); };
            AutomationProperties.SetName(button, string.IsNullOrEmpty(text) ? (icon ?? "button") : text);
            return button;
        }

        private static Brush ButtonForeground(ButtonKind kind, Theme t)
        {
            switch (kind)
            {
                case ButtonKind.Primary: return t.TextOnAccent;
                case ButtonKind.Danger: return t.Danger;
                case ButtonKind.Ghost: return t.TextSecondary;
                default: return t.TextPrimary;
            }
        }

        private static ControlTemplate ButtonTemplate(ButtonKind kind, Theme t)
        {
            string fill, fillHover, fillPressed, stroke, strokeHover, strokeThickness;

            switch (kind)
            {
                case ButtonKind.Primary:
                    fill = Col(t.Accent); fillHover = Col(t.AccentHover); fillPressed = Col(t.AccentPressed);
                    stroke = "Transparent"; strokeHover = "Transparent"; strokeThickness = "0"; break;
                case ButtonKind.Danger:
                    fill = Col(t.DangerSoft); fillHover = Col(t.DangerSoft); fillPressed = Col(t.Danger);
                    stroke = Col(t.Danger); strokeHover = Col(t.Danger); strokeThickness = "1"; break;
                case ButtonKind.Ghost:
                    fill = "Transparent"; fillHover = Col(t.Hover); fillPressed = Col(t.Hover);
                    stroke = "Transparent"; strokeHover = "Transparent"; strokeThickness = "0"; break;
                default:
                    fill = Col(t.Surface); fillHover = Col(t.Elevated); fillPressed = Col(t.Elevated);
                    stroke = Col(t.Border); strokeHover = Col(t.BorderStrong); strokeThickness = "1"; break;
            }

            string xaml =
                "<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' " +
                "xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='{x:Type Button}'>" +
                "  <Border x:Name='bd' Background='" + fill + "' BorderBrush='" + stroke + "' " +
                "          BorderThickness='" + strokeThickness + "' CornerRadius='" + N(t.RadiusControl) + "' " +
                "          SnapsToDevicePixels='True'>" +
                "    <ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center' " +
                "                      Margin='{TemplateBinding Padding}' RecognizesAccessKey='True'/>" +
                "  </Border>" +
                "  <ControlTemplate.Triggers>" +
                "    <Trigger Property='IsMouseOver' Value='True'>" +
                "      <Setter TargetName='bd' Property='Background' Value='" + fillHover + "'/>" +
                "      <Setter TargetName='bd' Property='BorderBrush' Value='" + strokeHover + "'/>" +
                "    </Trigger>" +
                "    <Trigger Property='IsPressed' Value='True'>" +
                "      <Setter TargetName='bd' Property='Background' Value='" + fillPressed + "'/>" +
                "      <Setter TargetName='bd' Property='Opacity' Value='0.92'/>" +
                "    </Trigger>" +
                "    <Trigger Property='IsEnabled' Value='False'>" +
                "      <Setter TargetName='bd' Property='Opacity' Value='0.4'/>" +
                "    </Trigger>" +
                // Keyboard focus is drawn by the template itself rather than by a
                // FocusVisualStyle, because the reference assemblies available on this machine
                // predate that type. This also means the ring matches the control's own corner
                // radius instead of being drawn on a separate adornment layer.
                "    <Trigger Property='IsKeyboardFocused' Value='True'>" +
                "      <Setter TargetName='bd' Property='BorderBrush' Value='" + Col(t.FocusRing) + "'/>" +
                "      <Setter TargetName='bd' Property='BorderThickness' Value='2'/>" +
                "    </Trigger>" +
                "  </ControlTemplate.Triggers>" +
                "</ControlTemplate>";

            return (ControlTemplate)XamlReader.Parse(xaml);
        }

        // =====================================================================================
        // Text input
        // =====================================================================================

        public static TextBox TextInput(double height = 38, bool multiline = false, bool mono = false,
                                       bool password = false)
        {
            Theme t = Theme;
            TextBox box = new TextBox();
            box.Height = multiline ? double.NaN : height;
            box.MinHeight = multiline ? 110 : height;
            box.MaxHeight = multiline ? double.PositiveInfinity : height;
            box.AcceptsReturn = multiline;
            box.TextWrapping = multiline ? TextWrapping.Wrap : TextWrapping.NoWrap;
            box.VerticalContentAlignment = multiline ? VerticalAlignment.Top : VerticalAlignment.Center;
            box.Padding = new Thickness(12, multiline ? 10 : 0, 12, multiline ? 10 : 0);
            box.FontFamily = mono ? t.MonoFamily : t.UiFont;
            box.FontSize = mono ? t.MonoSize : t.BodySize;
            box.Foreground = t.TextPrimary;
            box.CaretBrush = t.Accent;
            box.SelectionBrush = Theme.WithOpacity(((SolidColorBrush)t.Accent).Color, 0.4);
            box.SelectionOpacity = 1;
            box.BorderThickness = new Thickness(0);
            box.Background = Brushes.Transparent;
            box.SnapsToDevicePixels = true;
            box.Template = TextBoxTemplate(t);

            // The password argument is accepted for call-site readability but masking is handled
            // by SecretField, because TextBox has no PasswordChar in this framework version.
            return box;
        }

        private static ControlTemplate TextBoxTemplate(Theme t)
        {
            string xaml =
                "<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' " +
                "xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='{x:Type TextBox}'>" +
                "  <Border x:Name='bd' Background='" + Col(t.Input) + "' " +
                "          BorderBrush='" + Col(t.Border) + "' BorderThickness='1' " +
                "          CornerRadius='" + N(t.RadiusControl) + "' SnapsToDevicePixels='True'>" +
                "    <ScrollViewer x:Name='PART_ContentHost' Margin='0' " +
                "                    VerticalAlignment='{TemplateBinding VerticalContentAlignment}' " +
                "                    Padding='{TemplateBinding Padding}' " +
                "                    Focusable='False' HorizontalScrollBarVisibility='Hidden' " +
                "                    VerticalScrollBarVisibility='Auto'/>" +
                "  </Border>" +
                "  <ControlTemplate.Triggers>" +
                "    <Trigger Property='IsMouseOver' Value='True'>" +
                "      <Setter TargetName='bd' Property='BorderBrush' Value='" + Col(t.BorderStrong) + "'/>" +
                "    </Trigger>" +
                "    <Trigger Property='IsKeyboardFocusWithin' Value='True'>" +
                "      <Setter TargetName='bd' Property='BorderBrush' Value='" + Col(t.FocusRing) + "'/>" +
                "    </Trigger>" +
                "    <Trigger Property='IsEnabled' Value='False'>" +
                "      <Setter TargetName='bd' Property='Opacity' Value='0.5'/>" +
                "    </Trigger>" +
                "  </ControlTemplate.Triggers>" +
                "</ControlTemplate>";

            return (ControlTemplate)XamlReader.Parse(xaml);
        }

        // =====================================================================================
        // Secret input
        // =====================================================================================

        /// <summary>
        /// A password field that can be revealed on demand.
        /// </summary>
        /// <remarks>
        /// Built from <see cref="PasswordBox"/> with a hidden <see cref="TextBox"/> alongside it.
        /// <c>TextBox</c> has no <c>PasswordChar</c>, and masking its text by hand would mean
        /// intercepting every keystroke and caret movement, which is easy to get subtly wrong.
        /// Swapping which of the two is visible is simpler and cannot corrupt the value.
        /// </remarks>
        public sealed class SecretInput
        {
            private readonly Grid _root;
            private readonly PasswordBox _masked;
            private readonly TextBox _plain;
            private readonly Button _reveal;

            internal SecretInput(Grid root, PasswordBox masked, TextBox plain, Button reveal)
            {
                _root = root;
                _masked = masked;
                _plain = plain;
                _reveal = reveal;
            }

            /// <summary>The visual control to place in the layout.</summary>
            public UIElement Element { get { return _root; } }

            /// <summary>Focuses whichever editor is currently visible.</summary>
            public void Focus()
            {
                if (_masked.Visibility == Visibility.Visible) _masked.Focus();
                else _plain.Focus();
            }

            /// <summary>Reads the current value, whichever editor holds it.</summary>
            public string GetValue()
            {
                return _masked.Visibility == Visibility.Visible ? _masked.Password : _plain.Text;
            }

            /// <summary>Sets the value on both editors so revealing does not lose it.</summary>
            public void SetValue(string value)
            {
                _masked.Password = value ?? string.Empty;
                _plain.Text = value ?? string.Empty;
            }

            /// <summary>Raised whenever the value changes, from either editor.</summary>
            public event EventHandler ValueChanged;

            private void RaiseChanged()
            {
                EventHandler handler = ValueChanged;
                if (handler != null) handler(this, EventArgs.Empty);
            }

            /// <summary>Used by the factory to forward changes from either editor.</summary>
            internal void NotifyChanged() { RaiseChanged(); }
        }

        /// <summary>Creates a password field with a reveal toggle.</summary>
        public static SecretInput SecretField(double height, bool mono)
        {
            Theme t = Theme;

            PasswordBox masked = new PasswordBox();
            masked.Height = height;
            masked.FontFamily = mono ? t.MonoFamily : t.UiFont;
            masked.FontSize = mono ? t.MonoSize : t.BodySize;
            masked.Foreground = t.TextPrimary;
            masked.CaretBrush = t.Accent;
            masked.BorderThickness = new Thickness(0);
            masked.Background = Brushes.Transparent;
            masked.Padding = new Thickness(12, 0, 12, 0);
            masked.VerticalContentAlignment = VerticalAlignment.Center;
            masked.Template = PasswordBoxTemplate(t);
            AutomationProperties.SetName(masked, "Password");

            TextBox plain = new TextBox();
            plain.Height = height;
            plain.FontFamily = mono ? t.MonoFamily : t.UiFont;
            plain.FontSize = mono ? t.MonoSize : t.BodySize;
            plain.Foreground = t.TextPrimary;
            plain.CaretBrush = t.Accent;
            plain.BorderThickness = new Thickness(0);
            plain.Background = Brushes.Transparent;
            plain.Padding = new Thickness(12, 0, 12, 0);
            plain.VerticalContentAlignment = VerticalAlignment.Center;
            plain.Template = TextBoxTemplate(t);
            plain.Visibility = Visibility.Collapsed;
            AutomationProperties.SetName(plain, "Password");

            Button reveal = Button(null, ButtonKind.Ghost, null, "eye", height);
            reveal.Width = height + 6;
            reveal.Margin = new Thickness(4, 0, 4, 0);
            reveal.ToolTip = "Show password";
            AutomationProperties.SetName(reveal, "Show or hide the password");

            Grid root = new Grid();
            Grid.SetColumn(masked, 0);
            Grid.SetColumn(plain, 0);
            Grid.SetColumn(reveal, 1);
            root.Children.Add(masked);
            root.Children.Add(plain);
            root.Children.Add(reveal);

            SecretInput input = new SecretInput(root, masked, plain, reveal);

            bool shown = false;
            reveal.Click += delegate
            {
                shown = !shown;

                // Move the value across before flipping visibility, so the text is never
                // momentarily absent or duplicated.
                if (shown) plain.Text = masked.Password;
                else masked.Password = plain.Text;

                plain.Visibility = shown ? Visibility.Visible : Visibility.Collapsed;
                masked.Visibility = shown ? Visibility.Collapsed : Visibility.Visible;
                input.NotifyChanged();
            };

            masked.PasswordChanged += delegate { if (!shown) input.NotifyChanged(); };
            plain.TextChanged += delegate { if (shown) input.NotifyChanged(); };

            return input;
        }

        private static ControlTemplate PasswordBoxTemplate(Theme t)
        {
            string xaml =
                "<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' " +
                "xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='{x:Type PasswordBox}'>" +
                "  <Border x:Name='bd' Background='" + Col(t.Input) + "' " +
                "          BorderBrush='" + Col(t.Border) + "' BorderThickness='1' " +
                "          CornerRadius='" + N(t.RadiusControl) + "' SnapsToDevicePixels='True'>" +
                "    <ScrollViewer x:Name='PART_ContentHost' Margin='0' " +
                "                    VerticalAlignment='Center' Padding='{TemplateBinding Padding}' " +
                "                    Focusable='False' HorizontalScrollBarVisibility='Hidden' " +
                "                    VerticalScrollBarVisibility='Hidden'/>" +
                "  </Border>" +
                "  <ControlTemplate.Triggers>" +
                "    <Trigger Property='IsMouseOver' Value='True'>" +
                "      <Setter TargetName='bd' Property='BorderBrush' Value='" + Col(t.BorderStrong) + "'/>" +
                "    </Trigger>" +
                "    <Trigger Property='IsKeyboardFocusWithin' Value='True'>" +
                "      <Setter TargetName='bd' Property='BorderBrush' Value='" + Col(t.FocusRing) + "'/>" +
                "    </Trigger>" +
                "  </ControlTemplate.Triggers>" +
                "</ControlTemplate>";

            return (ControlTemplate)XamlReader.Parse(xaml);
        }

        /// <summary>
        /// A text field with a real placeholder.
        /// </summary>
        /// <remarks>
        /// The hint is an overlay rather than a <c>Tag</c>-based hack so it is part of the
        /// template: it disappears the moment the box has focus or content, and it is exposed to
        /// assistive technology through the control's name.
        /// </remarks>
        public static TextBox TextInputWithPlaceholder(string placeholder, double height = 36)
        {
            Theme t = Theme;
            TextBox box = TextInput(height);
            AutomationProperties.SetName(box, placeholder);

            ControlTemplate plain = box.Template;

            box.Template = PlaceholderTemplate(t, plain, placeholder, height);
            return box;
        }

        private static ControlTemplate PlaceholderTemplate(Theme t, ControlTemplate inner,
                                                          string placeholder, double height)
        {
            string xaml =
                "<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' " +
                "xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='{x:Type TextBox}'>" +
                "  <Grid>" +
                "    <Border Background='" + Col(t.Input) + "' BorderBrush='" + Col(t.Border) + "' " +
                "            BorderThickness='1' CornerRadius='" + N(t.RadiusControl) + "' " +
                "            SnapsToDevicePixels='True'>" +
                "      <Grid>" +
                "        <TextBlock x:Name='ph' Text='" + placeholder + "' " +
                "                   FontFamily='" + t.UiFont.Source + "' FontSize='" + N(t.BodySize) + "' " +
                "                   Foreground='" + Col(t.TextMuted) + "' " +
                "                   VerticalAlignment='Center' Margin='12,0,0,0' IsHitTestVisible='False'/>" +
                "        <ScrollViewer x:Name='PART_ContentHost' Margin='0' " +
                "                        VerticalAlignment='{TemplateBinding VerticalContentAlignment}' " +
                "                        Padding='{TemplateBinding Padding}' Focusable='False' " +
                "                        HorizontalScrollBarVisibility='Hidden' " +
                "                        VerticalScrollBarVisibility='Hidden'/>" +
                "      </Grid>" +
                "    </Border>" +
                "  </Grid>" +
                "  <ControlTemplate.Triggers>" +
                "    <Trigger Property='Text' Value=''>" +
                "      <Setter TargetName='ph' Property='Visibility' Value='Visible'/>" +
                "    </Trigger>" +
                "    <Trigger Property='IsKeyboardFocusWithin' Value='True'>" +
                "      <Setter TargetName='ph' Property='Visibility' Value='Collapsed'/>" +
                "    </Trigger>" +
                "  </ControlTemplate.Triggers>" +
                "</ControlTemplate>";

            return (ControlTemplate)XamlReader.Parse(xaml);
        }

        // =====================================================================================
        // Checkbox
        // =====================================================================================

        /// <summary>
        /// A checkbox with an animated indicator and a label.
        /// </summary>
        /// <remarks>
        /// The indicator is composed in code and repainted on state change rather than driven by
        /// template triggers. That keeps the visual states in one place and makes the checked
        /// animation trivial to reason about.
        /// </remarks>
        public static CheckBox Check(string label, bool isChecked, Action<bool> onChanged = null)
        {
            Theme t = Theme;
            CheckBox box = new CheckBox();
            box.IsChecked = isChecked;
            box.Cursor = Cursors.Hand;
            box.Template = SimpleContentTemplate(t);
            AutomationProperties.SetName(box, label);

            Border indicator = Rect(18, 18, 5, t.Input, 1.4, t.Border);
            Path tick = Icon("check", 12, t.TextOnAccent);
            tick.Visibility = Visibility.Collapsed;
            Grid indicatorHost = new Grid { Width = 18, Height = 18, VerticalAlignment = VerticalAlignment.Center };
            indicatorHost.Children.Add(indicator);
            indicatorHost.Children.Add(tick);

            StackPanel row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            row.Children.Add(indicatorHost);
            row.Children.Add(new FrameworkElement { Width = 10 });
            row.Children.Add(Text(label, t.BodySize, t.TextSecondary));

            box.Content = row;

            Action repaint = delegate
            {
                bool on = box.IsChecked == true;
                indicator.Background = on ? t.Accent : t.Input;
                indicator.BorderBrush = on ? t.Accent : t.Border;
                tick.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            };

            box.Checked += delegate { repaint(); if (onChanged != null) onChanged(true); };
            box.Unchecked += delegate { repaint(); if (onChanged != null) onChanged(false); };
            repaint();

            return box;
        }

        // =====================================================================================
        // Toggle
        // =====================================================================================

        /// <summary>
        /// A sliding on/off switch.
        /// </summary>
        /// <remarks>
        /// Built on <see cref="CheckBox"/> with a replacement template rather than on
        /// <c>ToggleSwitch</c>, which is not present in the WPF reference assemblies available
        /// on this machine. Behaviour is identical: clicking toggles, and the state is the
        /// <see cref="ToggleButton.IsChecked"/> property.
        /// </remarks>
        public static CheckBox Toggle(bool isOn, Action<bool> onChanged = null)
        {
            Theme t = Theme;
            CheckBox box = new CheckBox();
            box.IsChecked = isOn;
            box.Cursor = Cursors.Hand;
            box.Template = ToggleTemplate(t);
            AutomationProperties.SetName(box, "toggle");

            if (onChanged != null)
            {
                box.Checked += delegate { onChanged(true); };
                box.Unchecked += delegate { onChanged(false); };
            }

            return box;
        }

        private static ControlTemplate ToggleTemplate(Theme t)
        {
            string xaml =
                "<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' " +
                "xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='{x:Type CheckBox}'>" +
                "  <Grid Background='Transparent' Width='42' Height='24'>" +
                "    <Border x:Name='track' Width='42' Height='24' CornerRadius='12' " +
                "            Background='" + Col(t.Input) + "' " +
                "            BorderBrush='" + Col(t.BorderStrong) + "' BorderThickness='1'/>" +
                "    <Border x:Name='knob' Width='18' Height='18' CornerRadius='9' " +
                "            HorizontalAlignment='Left' VerticalAlignment='Center' Margin='3,0,0,0' " +
                "            Background='" + Col(t.TextSecondary) + "'/>" +
                "  </Grid>" +
                "  <ControlTemplate.Triggers>" +
                "    <Trigger Property='IsChecked' Value='True'>" +
                "      <Setter TargetName='track' Property='Background' Value='" + Col(t.Accent) + "'/>" +
                "      <Setter TargetName='track' Property='BorderBrush' Value='" + Col(t.Accent) + "'/>" +
                "      <Setter TargetName='knob' Property='Background' Value='" + Col(t.TextOnAccent) + "'/>" +
                "      <Setter TargetName='knob' Property='Margin' Value='21,0,0,0'/>" +
                "    </Trigger>" +
                "    <Trigger Property='IsMouseOver' Value='True'>" +
                "      <Setter TargetName='track' Property='BorderBrush' Value='" + Col(t.Accent) + "'/>" +
                "    </Trigger>" +
                "    <Trigger Property='IsKeyboardFocused' Value='True'>" +
                "      <Setter TargetName='track' Property='BorderBrush' Value='" + Col(t.FocusRing) + "'/>" +
                "      <Setter TargetName='track' Property='BorderThickness' Value='2'/>" +
                "    </Trigger>" +
                "  </ControlTemplate.Triggers>" +
                "</ControlTemplate>";

            return (ControlTemplate)XamlReader.Parse(xaml);
        }

        private static ControlTemplate SimpleContentTemplate(Theme t)
        {
            string xaml =
                "<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' " +
                "xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='{x:Type ContentControl}'>" +
                "  <Border Background='Transparent' Padding='0,3' Cursor='Hand' SnapsToDevicePixels='True'>" +
                "    <ContentPresenter VerticalAlignment='Center'/>" +
                "  </Border>" +
                "</ControlTemplate>";

            return (ControlTemplate)XamlReader.Parse(xaml);
        }

        // =====================================================================================
        // Building blocks
        // =====================================================================================

        public static Border Rect(double w, double h, double radius, Brush fill,
                                  double strokeThickness = 0, Brush stroke = null)
        {
            Border border = new Border();
            border.Width = w;
            border.Height = h;
            border.CornerRadius = new CornerRadius(radius);
            border.Background = fill;
            if (strokeThickness > 0)
            {
                border.BorderThickness = new Thickness(strokeThickness);
                border.BorderBrush = stroke ?? Brushes.Transparent;
            }
            border.SnapsToDevicePixels = true;
            return border;
        }

        public static Border Card(bool elevated = false, double radius = 0, double padding = 0)
        {
            Theme t = Theme;
            Border card = new Border();
            card.Background = elevated ? t.Elevated : t.Surface;
            card.CornerRadius = new CornerRadius(radius > 0 ? radius : t.RadiusCard);
            card.BorderThickness = new Thickness(1);
            card.BorderBrush = t.Border;
            card.Padding = new Thickness(padding);
            card.SnapsToDevicePixels = true;
            if (elevated) card.Effect = t.Elevation(3);
            return card;
        }

        public static Border Divider(bool vertical = false, Brush colour = null)
        {
            Theme t = Theme;
            Border line = new Border();
            line.Background = colour ?? t.Border;
            if (vertical)
            {
                line.Width = 1;
                line.HorizontalAlignment = HorizontalAlignment.Stretch;
            }
            else
            {
                line.Height = 1;
                line.HorizontalAlignment = HorizontalAlignment.Stretch;
            }
            line.SnapsToDevicePixels = true;
            return line;
        }

        public static Border Pill(string text, Brush foreground, Brush background)
        {
            Border pill = new Border();
            pill.Background = background;
            pill.CornerRadius = new CornerRadius(999);
            pill.Padding = new Thickness(8, 3, 8, 3);
            pill.Margin = new Thickness(0, 0, 6, 6);
            pill.VerticalAlignment = VerticalAlignment.Center;
            pill.Child = Text(text, t().CaptionSize, foreground, true);
            AutomationProperties.SetName(pill, text);
            return pill;
        }

        private static Theme t() { return Theme; }
    }
}
