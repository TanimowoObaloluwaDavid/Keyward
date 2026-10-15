using System;
using System.ComponentModel;
using System.Text;
using System.Windows;using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using Keyward.Core.Services;
using Keyward.Ui.Design;
using Keyward.Ui.Screens;

namespace Keyward.Ui
{
    /// <summary>The top-level views the shell can show.</summary>
    public enum AppScreen
    {
        Welcome,
        Vault,
        Generator,
        Settings
    }

    /// <summary>
    /// Writes unhandled exceptions to a log file so a crash is diagnosable instead of vanishing.
    /// </summary>
    /// <remarks>
    /// The log deliberately contains no vault data. It records the exception type, message,
    /// stack and the running applications, which is enough to locate a fault and cannot disclose
    /// a password, because the exception message never embeds decrypted content.
    /// </remarks>
    internal static class CrashLog
    {
        private static string Path_
        {
            get { return System.IO.Path.Combine(AppPaths.DataDirectory, "crash.log"); }
        }

        internal static void Install()
        {
            AppDomain.CurrentDomain.UnhandledException += delegate (object s, UnhandledExceptionEventArgs e)
            {
                Write("Unhandled", e.ExceptionObject as Exception);
            };

            System.Windows.Threading.Dispatcher.CurrentDispatcher.UnhandledException +=
                delegate (object s, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
            {
                Write("Dispatcher", e.Exception);
            };
        }

        private static void Write(string kind, Exception ex)
        {
            try
            {
                AppPaths.EnsureDataDirectory();

                StringBuilder sb = new StringBuilder();
                sb.AppendLine("---- " + kind + " at " + DateTime.Now.ToString("u") + " ----");
                if (ex != null) sb.AppendLine(ex.ToString());
                else sb.AppendLine("(no exception object)");
                sb.AppendLine();

                System.IO.File.AppendAllText(Path_, sb.ToString());
            }
            catch (Exception)
            {
                // If logging also fails there is nothing sensible left to do.
            }
        }
    }

    /// <summary>
    /// Dumps the visual tree geometry to a file when KEYWARD_DUMP_LAYOUT is set.
    /// </summary>
    /// <remarks>
    /// Purely a development aid. It is inert unless the environment variable is present, and it
    /// writes only control types and coordinates - never control content, so it cannot disclose
    /// a password.
    /// </remarks>
    internal static class LayoutDiagnostics
    {
        internal static void DumpIfRequested(Window window)
        {
            string target = Environment.GetEnvironmentVariable("KEYWARD_DUMP_LAYOUT");
            if (string.IsNullOrEmpty(target)) return;

            try
            {
                System.Text.StringBuilder sb = new System.Text.StringBuilder();
                sb.AppendLine("window Actual  : " + window.ActualWidth + " x " + window.ActualHeight);
                sb.AppendLine("window Size    : " + window.Width + " x " + window.Height);

                System.Windows.Media.Visual root = window.Content as System.Windows.Media.Visual;
                Walk(root, 0, sb);

                System.IO.File.WriteAllText(target, sb.ToString());
            }
            catch (Exception)
            {
            }
        }

        private static void Walk(DependencyObject node, int depth, System.Text.StringBuilder sb)
        {
            if (node == null || depth > 6) return;

            FrameworkElement element = node as FrameworkElement;
            if (element != null && element.ActualWidth > 0)
            {
                Point origin = new Point(double.NaN, double.NaN);
                try
                {
                    origin = element.TranslatePoint(new Point(0, 0),
                        Application.Current.MainWindow as UIElement);
                }
                catch (Exception) { }

                string xs = double.IsNaN(origin.X) ? "?" : origin.X.ToString("F0");
                string ys = double.IsNaN(origin.Y) ? "?" : origin.Y.ToString("F0");

                sb.AppendLine(new string(' ', depth * 2) +
                    element.GetType().Name.PadRight(20) +
                    " w=" + element.ActualWidth.ToString("F0").PadLeft(6) +
                    " h=" + element.ActualHeight.ToString("F0").PadLeft(6) +
                    " x=" + xs.PadLeft(6) +
                    " y=" + ys.PadLeft(6));
            }

            System.Windows.Media.Visual visual = node as System.Windows.Media.Visual;
            if (visual == null) return;
            int children = System.Windows.Media.VisualTreeHelper.GetChildrenCount(visual);
            if (children > 40) children = 40;

            for (int i = 0; i < children; i++)
            {
                Walk(System.Windows.Media.VisualTreeHelper.GetChild(visual, i), depth + 1, sb);
            }
        }
    }

    /// <summary>Application entry point.</summary>
    public sealed class KeywardApp : Application
    {
        [STAThread]
        public static void Main(string[] args)
        {
            KeywardApp app = new KeywardApp();
            app.Run();
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            CrashLog.Install();

            // A second instance would fight over the same vault file, so hand the request to the
            // window that already exists and exit.
            if (Shell.TryBecomePrimaryInstance())
            {
                MainWindow window = new MainWindow();
                MainWindow = window;
                window.Show();
            }
            else
            {
                Shutdown();
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            Shell.TryReleasePrimaryInstance();
            base.OnExit(e);
        }
    }

    /// <summary>
    /// Single-instance guard, implemented with a named OS mutex.
    /// </summary>
    /// <remarks>
    /// Two copies of the app pointed at one vault file would each hold a decrypted copy in memory
    /// and race each other's writes. A named mutex is the cheapest reliable way to prevent that on
    /// Windows without a separate IPC channel.
    /// </remarks>
    internal static class Shell
    {
        private const string MutexName = @"Local\Keyward.SingleInstance.v1";
        private static System.Threading.Mutex _mutex;

        internal static bool TryBecomePrimaryInstance()
        {
            try
            {
                bool created;
                _mutex = new System.Threading.Mutex(true, MutexName, out created);
                return created;
            }
            catch (Exception)
            {
                // If the mutex cannot be created for any reason, prefer running the app over
                // refusing to start.
                return true;
            }
        }

        internal static void TryReleasePrimaryInstance()
        {
            try
            {
                if (_mutex != null)
                {
                    _mutex.ReleaseMutex();
                    _mutex.Dispose();
                    _mutex = null;
                }
            }
            catch (Exception)
            {
            }
        }
    }

    /// <summary>
    /// The application window. Owns the session, the theme, global shortcuts and auto-lock, and
    /// routes between the welcome flow and the unlocked workspace.
    /// </summary>
    public sealed class MainWindow : Window
    {
        private readonly Settings _settings;
        private readonly VaultSession _session;
        private readonly ClipboardGuard _clipboard;

        // Deliberately not a field initializer: Toast builds themed brushes in its constructor,
        // and field initializers run before Factory.Init has been given a theme.
        private Toast _toast;

        private Theme _theme;
        private Grid _shell;
        private TextBlock _lockCountdown;
        private Button _lockButton;
        private DispatcherTimer _autoLockTimer;
        private PowerModeChangedEventHandler _powerHandler;

        private AppScreen _screen = AppScreen.Welcome;
        private VaultView _vaultView;
        private GeneratorView _generatorView;
        private SettingsView _settingsView;

        public MainWindow()
        {
            _settings = Settings.Load();
            AppPaths.EnsureDataDirectory();
            _session = new VaultSession(_settings.VaultPath);
            _clipboard = new ClipboardGuard();

            _theme = Theme.Resolve(_settings.Theme, Theme.SystemIsDark());
            Factory.Init(_theme);

            _toast = new Toast();
            _clipboard.Notified += delegate(string message) { _toast.Show(message); };

            Title = "Keyward";

            // Size against the work area in device-independent units.
            //
            // WPF sizes windows in DIPs while the OS thinks in physical pixels. On a scaled
            // display a fixed 1180 DIP window can be larger than the screen, which puts the
            // right-hand pane off-screen entirely. Deriving the size from the work area means
            // the window always fits, on any display and at any scaling.
            Rect work = SystemParameters.WorkArea;
            double availableWidth = work.Width - 48;
            double availableHeight = work.Height - 48;

            Width = Math.Min(1180, Math.Max(720, availableWidth));
            Height = Math.Min(780, Math.Max(560, availableHeight));
            MinWidth = Math.Min(940, Math.Max(640, availableWidth - 60));
            MinHeight = Math.Min(600, Math.Max(460, availableHeight - 60));

            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = _theme.Background;
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Ideal);
            UseLayoutRounding = true;
            SnapsToDevicePixels = true;

            _session.Locked += delegate
            {
                _clipboard.CancelPendingClear();
                GoWelcome("Your vault is locked.");
            };

            BuildShell();
            Route(_session.IsUnlocked ? AppScreen.Vault : AppScreen.Welcome, null);

            // The window handle does not exist until the source is initialised, so the dark
            // title bar can only be requested at that point.
            SourceInitialized += delegate { ApplySystemChrome(); };

            ContentRendered += delegate { LayoutDiagnostics.DumpIfRequested(this); };

            StartAutoLockTimer();            HookPowerEvents();

            PreviewKeyDown += OnGlobalKeyDown;
            PreviewMouseDown += delegate { _session.RegisterActivity(); };
            PreviewMouseMove += delegate { _session.RegisterActivity(); };
            Closed += OnClosed;
        }

        // =====================================================================================
        // Shell construction
        // =====================================================================================

        /// <summary>
        /// Asks DWM to render the standard title bar in dark mode.
        /// </summary>
        /// <remarks>
        /// A light Windows title bar above a near-black application looks like a bug. Going fully
        /// borderless would be more distinctive still, but it would take away minimise, snap and
        /// accessibility affordances the OS provides for free, so the native chrome is kept and
        /// simply themed to match.
        /// </remarks>
        private void ApplySystemChrome()
        {
            try
            {
                if (!_theme.IsDark) return;

                IntPtr handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
                if (handle == IntPtr.Zero) return;

                int enabled = 1;

                // 20 is the current attribute; 19 was its pre-2004 name. Try the current one and
                // fall back, so the title bar still matches on older builds.
                if (DwmSetWindowAttribute(handle, 20, ref enabled, sizeof(int)) != 0)
                    DwmSetWindowAttribute(handle, 19, ref enabled, sizeof(int));
            }
            catch (Exception)
            {
                // Cosmetic only. A light title bar is not a functional problem.
            }
        }

        [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        private void BuildShell()
        {
            Grid root = new Grid();
            root.Background = _theme.Background;

            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            root.Children.Add(BuildTitleBar());
            Grid.SetRow(root.Children[root.Children.Count - 1], 0);

            Grid content = new Grid();
            Grid.SetRow(content, 1);
            _shell = content;
            root.Children.Add(content);

            Grid overlay = new Grid();
            overlay.Children.Add(_toast.Element);
            Grid.SetRow(overlay, 1);
            root.Children.Add(overlay);

            Content = root;
        }

        private UIElement BuildTitleBar()
        {
            Grid bar = new Grid();
            bar.Height = 46;
            bar.Background = _theme.Sidebar;

            // Drag region: the bar itself moves the window, except where a control consumes the
            // click. Placed behind the content so buttons still take precedence.
            bar.MouseLeftButtonDown += delegate
            {
                try { DragMove(); }
                catch (InvalidOperationException) { }
            };

            Border bottom = Factory.Divider();
            bar.Children.Add(bottom);

            StackPanel left = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 0, 0) };
            left.Children.Add(Factory.Icon("vault", 20, _theme.Accent));
            left.Children.Add(new FrameworkElement { Width = 10 });
            left.Children.Add(Factory.Text("Keyward", _theme.BodySize, _theme.TextPrimary, true));
            bar.Children.Add(left);

            // Window caption buttons, drawn by hand to match the product rather than the OS.
            StackPanel right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 0, 6, 0) };

            _lockCountdown = Factory.Text(string.Empty, _theme.CaptionSize, _theme.TextMuted);
            _lockCountdown.VerticalAlignment = VerticalAlignment.Center;
            _lockCountdown.Margin = new Thickness(0, 0, 14, 0);
            right.Children.Add(_lockCountdown);

            Button lockButton = Factory.Button(null, ButtonKind.Ghost, LockNow, "lock", 30);
            lockButton.Width = 34;
            lockButton.Margin = new Thickness(0, 0, 4, 0);
            AutomationProperties.SetName(lockButton, "Lock vault (Ctrl+L)");
            lockButton.IsEnabled = _session.IsUnlocked;
            _lockButton = lockButton;
            right.Children.Add(lockButton);

            right.Children.Add(CaptionButton("minus", delegate { WindowState = WindowState.Minimized; }, "Minimise"));
            right.Children.Add(CaptionButton("close", Close, "Close"));

            bar.Children.Add(right);

            return bar;
        }

        private UIElement CaptionButton(string icon, Action onClick, string name)
        {
            Button b = Factory.Button(null, ButtonKind.Ghost, onClick, icon, 30);
            b.Width = 38;
            b.Margin = new Thickness(2, 0, 0, 0);
            AutomationProperties.SetName(b, name);
            return b;
        }

        private void RefreshTitleBarState()
        {
            if (_lockButton != null) _lockButton.IsEnabled = _session.IsUnlocked;
        }

        // =====================================================================================
        // Routing
        // =====================================================================================

        internal void Route(AppScreen target, object parameter)
        {
            _screen = target;
            _shell.Children.Clear();
            RefreshTitleBarState();

            UIElement view;
            switch (target)
            {
                case AppScreen.Vault:
                    if (_vaultView == null) _vaultView = new VaultView(this);
                    view = _vaultView.Build(parameter as string);
                    break;

                case AppScreen.Generator:
                    if (_generatorView == null) _generatorView = new GeneratorView(this);
                    view = _generatorView.Build();
                    break;

                case AppScreen.Settings:
                    if (_settingsView == null) _settingsView = new SettingsView(this);
                    view = _settingsView.Build();
                    break;

                default:
                    view = new WelcomeView(this, parameter as string).Build();
                    break;
            }

            _shell.Children.Add(view);
        }

        internal AppScreen CurrentScreen { get { return _screen; } }

        internal void GoVault(string search) { Route(AppScreen.Vault, search); }
        internal void GoGenerator() { Route(AppScreen.Generator, null); }
        internal void GoSettings() { Route(AppScreen.Settings, null); }
        internal void GoWelcome(string message) { Route(AppScreen.Welcome, message); }

        internal Settings AppSettings { get { return _settings; } }
        internal VaultSession Session { get { return _session; } }
        internal ClipboardGuard Clipboard { get { return _clipboard; } }
        internal Theme AppTheme { get { return _theme; } }
        internal Toast Notifications { get { return _toast; } }

        // =====================================================================================
        // Locking and theming
        // =====================================================================================

        internal void LockNow()
        {
            if (!_session.IsUnlocked) return;

            bool confirm = Dialogs.Confirm(this, "Lock the vault?",
                "Your vault will be encrypted again in memory and you will need your master password to open it.",
                "Lock", false);

            if (confirm) _session.Lock();
        }

        /// <summary>Rebuilds the entire window after a theme change.</summary>
        internal void ApplyTheme()
        {
            _theme = Theme.Resolve(_settings.Theme, Theme.SystemIsDark());
            Factory.Init(_theme);
            Background = _theme.Background;

            _vaultView = null;
            _generatorView = null;
            _settingsView = null;

            BuildShell();
            Route(_session.IsUnlocked ? _screen : AppScreen.Welcome, null);
        }

        // =====================================================================================
        // Auto-lock
        // =====================================================================================

        private void StartAutoLockTimer()
        {
            _autoLockTimer = new DispatcherTimer();
            _autoLockTimer.Interval = TimeSpan.FromSeconds(1);
            _autoLockTimer.Tick += delegate
            {
                if (!_session.IsUnlocked)
                {
                    if (_lockCountdown != null) _lockCountdown.Text = string.Empty;
                    return;
                }

                if (_session.IsIdleExpired)
                {
                    _session.Lock();
                    _toast.Show("Vault locked automatically after " + _settings.AutoLockMinutes + " minutes of inactivity.");
                    return;
                }

                if (_lockCountdown != null)
                {
                    TimeSpan? remaining = _session.TimeUntilAutoLock;
                    // FormatRemaining returns only the duration; the label is added here so the
                    // wording lives in one place.
                    _lockCountdown.Text = remaining.HasValue
                        ? "Locks in " + FormatRemaining(remaining.Value)
                        : string.Empty;
                }
            };
            _autoLockTimer.Start();
            _session.ArmAutoLock(_settings.AutoLockMinutes);
        }

        /// <summary>Formats a remaining auto-lock delay. The caller supplies the surrounding label.</summary>
        private static string FormatRemaining(TimeSpan remaining)
        {
            if (remaining.TotalMinutes >= 1)
                return ((int)remaining.TotalMinutes) + "m";
            return Math.Max(1, (int)Math.Ceiling(remaining.TotalSeconds)) + "s";
        }

        /// <summary>Locks on resume from sleep so a walked-away machine does not stay open.</summary>
        private void HookPowerEvents()
        {
            try
            {
                _powerHandler = delegate (object s, PowerModeChangedEventArgs e)
                {
                    if (e.Mode == PowerModes.Resume && _settings.LockOnSleep && _session.IsUnlocked)
                    {
                        Dispatcher.BeginInvoke(new Action(delegate { _session.Lock(); }));
                    }
                };
                Microsoft.Win32.SystemEvents.PowerModeChanged += _powerHandler;
            }
            catch (Exception)
            {
                // Power events are unavailable on some sessions; auto-lock on idle still applies.
                _powerHandler = null;
            }
        }

        private void OnClosed(object sender, EventArgs e)
        {
            if (_powerHandler != null)
            {
                try { Microsoft.Win32.SystemEvents.PowerModeChanged -= _powerHandler; }
                catch (Exception) { }
                _powerHandler = null;
            }

            if (_settings.LockOnExit) _session.Lock();

            _clipboard.ClearNow();
            _session.Dispose();
            if (_autoLockTimer != null) _autoLockTimer.Stop();
        }

        // =====================================================================================
        // Global shortcuts
        // =====================================================================================

        private void OnGlobalKeyDown(object sender, KeyEventArgs e)
        {
            _session.RegisterActivity();

            bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;
            bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift;

            if (!ctrl)
            {
                if (e.Key == Key.Escape)
                {
                    if (_screen == AppScreen.Vault && _vaultView != null)
                    {
                        if (!_vaultView.ClearSearch()) e.Handled = true;
                    }
                }
                return;
            }

            switch (e.Key)
            {
                case Key.L:
                    if (!_session.IsUnlocked) return;
                    _session.Lock();
                    e.Handled = true;
                    break;

                case Key.F:
                case Key.K:
                    if (_screen != AppScreen.Vault) { Route(AppScreen.Vault, null); }
                    if (_vaultView != null) { _vaultView.FocusSearch(); e.Handled = true; }
                    break;

                case Key.N:
                    if (!_session.IsUnlocked) return;
                    if (shift) _vaultView.CreateNote();
                    else _vaultView.CreateLogin();
                    e.Handled = true;
                    break;

                case Key.G:
                    if (!_session.IsUnlocked) return;
                    GoGenerator();
                    e.Handled = true;
                    break;

                case Key.OemComma:
                    GoSettings();
                    e.Handled = true;
                    break;

                case Key.D:
                    if (_screen == AppScreen.Vault && _vaultView != null)
                    {
                        _vaultView.CopySelectedPassword();
                        e.Handled = true;
                    }
                    break;

                case Key.E:
                    if (_screen == AppScreen.Vault && _vaultView != null)
                    {
                        _vaultView.EditSelected();
                        e.Handled = true;
                    }
                    break;
            }
        }
    }
}
