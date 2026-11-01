using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Windows.Graphics;
using Windows.UI;

namespace FlatOut4SaveEditor
{
    /// <summary>
    /// Provides application-specific behavior to supplement the default Application class.
    /// </summary>
    public partial class App : Application
    {
        private bool closeConfirmed;

        public static Window MainWindow { get; private set; } = null!;

        // Command line: [<save path>] [--open <save path>] [--snapshot <png dir>] [--theme light|dark]
        //               [--size <width>x<height>] [--search <query>] [--scroll <steps>] [--unlock] [--selftest]
        public static string? StartupPath { get; private set; }

        public static string? SnapshotDirectory { get; private set; }

        public static string? SnapshotSearch { get; private set; }

        public static int SnapshotScrollSteps { get; private set; } = 2;

        public static bool SnapshotUnlock { get; private set; }

        public static bool SnapshotSelfTest { get; private set; }

        public static void SnapshotLog(string message)
        {
            if (SnapshotDirectory is not null)
            {
                Directory.CreateDirectory(SnapshotDirectory);
                File.AppendAllText(Path.Combine(SnapshotDirectory, "snapshot.log"), $"[{DateTime.Now:HH:mm:ss.fff}] {message}{Environment.NewLine}");
            }
        }

        /// <summary>
        /// Initializes the singleton application object.  This is the first line of authored code
        /// executed, and as such is the logical equivalent of main() or WinMain().
        /// </summary>
        public App()
        {
            this.InitializeComponent();
            UnhandledException += (_, e) =>
            {
                WriteCrashLog(e.Exception);
                if (SnapshotDirectory is not null)
                {
                    // Unattended run: end here, before WinUI's fail-fast raises a System Error box on
                    // the user's screen (SetErrorMode does not cover that path).
                    SnapshotLog($"CRASH {e.Exception.GetType().Name} 0x{e.Exception.HResult:X8}; see {CrashLogPath}");
                    System.Diagnostics.Process.GetCurrentProcess().Kill();
                }
            };
        }

        public static string CrashLogPath => Path.Combine(Path.GetTempPath(), "FlatOut4SaveEditor-crash.log");

        private static void WriteCrashLog(Exception exception)
        {
            try
            {
                File.AppendAllText(CrashLogPath, $"[{DateTime.Now:O}] {exception}{Environment.NewLine}{Environment.NewLine}");
            }
            catch
            {
            }
        }

        /// <summary>
        /// Invoked when the application is launched normally by the end user.  Other entry points
        /// will be used such as when the application is launched to open a specific file.
        /// </summary>
        /// <param name="e">Details about the launch request and process.</param>
        protected override void OnLaunched(LaunchActivatedEventArgs e)
        {
            string[] args = Environment.GetCommandLineArgs().Skip(1).ToArray();
            ElementTheme theme = ParseArguments(args, out SizeInt32 size);

            MainWindow ??= new Window
            {
                Title = "FlatOut 4 Save Editor"
            };

            MainWindow.SystemBackdrop = new MicaBackdrop();
            MainWindow.ExtendsContentIntoTitleBar = true;

            if (MainWindow.Content is not Frame rootFrame)
            {
                rootFrame = new Frame();
                rootFrame.NavigationFailed += OnNavigationFailed;
                rootFrame.ActualThemeChanged += (_, _) => ApplyTitleBarTheme(rootFrame.ActualTheme);
                rootFrame.RequestedTheme = theme;
                MainWindow.Content = rootFrame;
            }

            _ = rootFrame.Navigate(typeof(MainPage), e.Arguments);
            if (rootFrame.Content is MainPage page)
            {
                MainWindow.SetTitleBar(page.TitleBarElement);
            }

            AppWindow appWindow = MainWindow.AppWindow;
            appWindow.SetIcon("Assets/Square44x44Logo.targetsize-24_altform-unplated.png");
            SizeWindow(appWindow, size);
            if (appWindow.Presenter is OverlappedPresenter presenter)
            {
                presenter.PreferredMinimumWidth = 960;
                presenter.PreferredMinimumHeight = 620;
            }

            appWindow.Closing += OnWindowClosing;
            ApplyTitleBarTheme(rootFrame.ActualTheme);

            if (SnapshotDirectory is null)
            {
                // Reopen where the user left it, unless that spot is no longer on any display.
                if (!sizeRequested)
                {
                    _ = Views.WindowPlacement.TryRestore(appWindow);
                }

                Views.WindowPlacement.Track(appWindow);
            }

            if (SnapshotDirectory is not null)
            {
                // A crash in an unattended run must not leave Windows error boxes on the user's
                // screen: no fault dialog, no hard-error popup, no Windows Error Reporting UI.
                _ = SetErrorMode(SemFailCriticalErrors | SemNoGpFaultErrorBox | SemNoOpenFileErrorBox);
                _ = WerSetFlags(WerFaultReportingNoUi);
                Services.FlatOut4RecentFiles.Enabled = false;

                // Render off-screen without taking focus from whatever the user is doing, and keep
                // the window off the taskbar and Alt+Tab so it does not look like one that failed to open.
                appWindow.IsShownInSwitchers = false;
                appWindow.Move(new PointInt32(-8000 - appWindow.Size.Width, 120));
                appWindow.Show(false);
            }
            else
            {
                MainWindow.Activate();
            }
        }

        // size is in effective (DPI-independent) pixels; AppWindow works in physical pixels.
        private static void SizeWindow(AppWindow appWindow, SizeInt32 size)
        {
            double scale = GetDpiForWindow(WinRT.Interop.WindowNative.GetWindowHandle(MainWindow)) / 96.0;
            int width = (int)(size.Width * scale);
            int height = (int)(size.Height * scale);

            if (SnapshotDirectory is null)
            {
                RectInt32 work = DisplayArea.GetFromWindowId(appWindow.Id, DisplayAreaFallback.Primary).WorkArea;
                width = Math.Min(width, (int)(work.Width * 0.92));
                height = Math.Min(height, (int)(work.Height * 0.92));
                appWindow.MoveAndResize(new RectInt32(
                    work.X + ((work.Width - width) / 2),
                    work.Y + ((work.Height - height) / 2),
                    width,
                    height));
                return;
            }

            appWindow.Resize(new SizeInt32(width, height));
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern uint GetDpiForWindow(IntPtr hwnd);

        private const uint SemFailCriticalErrors = 0x0001;
        private const uint SemNoGpFaultErrorBox = 0x0002;
        private const uint SemNoOpenFileErrorBox = 0x8000;
        private const uint WerFaultReportingNoUi = 0x0020;

        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        private static extern uint SetErrorMode(uint mode);

        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        private static extern int WerSetFlags(uint flags);

        // --size given: use it instead of the remembered window placement.
        private static bool sizeRequested;

        private static ElementTheme ParseArguments(string[] args, out SizeInt32 size)
        {
            ElementTheme theme = ElementTheme.Default;
            size = new SizeInt32(1360, 880);
            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i];
                string? next = i + 1 < args.Length ? args[i + 1] : null;
                switch (arg.ToLowerInvariant())
                {
                    case "--open" when next is not null:
                        StartupPath = next;
                        i++;
                        break;
                    case "--snapshot" when next is not null:
                        SnapshotDirectory = next;
                        i++;
                        break;
                    case "--recent-store" when next is not null:
                        Services.FlatOut4RecentFiles.StorePathOverride = next;
                        i++;
                        break;
                    case "--unlock":
                        SnapshotUnlock = true;
                        break;
                    case "--selftest":
                        SnapshotSelfTest = true;
                        break;
                    case "--search" when next is not null:
                        SnapshotSearch = next;
                        i++;
                        break;
                    case "--scroll" when next is not null && int.TryParse(next, out int steps):
                        SnapshotScrollSteps = steps;
                        i++;
                        break;
                    case "--theme" when next is not null:
                        theme = next.Equals("dark", StringComparison.OrdinalIgnoreCase) ? ElementTheme.Dark : ElementTheme.Light;
                        i++;
                        break;
                    case "--size" when next is not null:
                        string[] parts = next.Split('x');
                        if (parts.Length == 2 && int.TryParse(parts[0], out int width) && int.TryParse(parts[1], out int height))
                        {
                            size = new SizeInt32(width, height);
                            sizeRequested = true;
                        }

                        i++;
                        break;
                    default:
                        if (!arg.StartsWith("--", StringComparison.Ordinal) && File.Exists(arg))
                        {
                            StartupPath = arg;
                        }

                        break;
                }
            }

            return theme;
        }

        private async void OnWindowClosing(AppWindow sender, AppWindowClosingEventArgs args)
        {
            if (closeConfirmed || MainWindow.Content is not Frame { Content: MainPage page } || page.Session?.HasChanges != true)
            {
                if (SnapshotDirectory is null)
                {
                    Views.WindowPlacement.Save(sender);
                }

                return;
            }

            args.Cancel = true;
            if (await page.ConfirmDiscardAsync())
            {
                closeConfirmed = true;
                MainWindow.Close();
            }
        }

        private static void ApplyTitleBarTheme(ElementTheme theme)
        {
            AppWindowTitleBar titleBar = MainWindow.AppWindow.TitleBar;
            bool dark = theme == ElementTheme.Dark;
            Color text = dark ? Colors.White : Color.FromArgb(255, 26, 26, 26);
            Color inactiveText = dark ? Color.FromArgb(255, 150, 150, 150) : Color.FromArgb(255, 120, 120, 120);
            Color hover = dark ? Color.FromArgb(24, 255, 255, 255) : Color.FromArgb(18, 0, 0, 0);
            Color pressed = dark ? Color.FromArgb(40, 255, 255, 255) : Color.FromArgb(30, 0, 0, 0);

            titleBar.ButtonBackgroundColor = Colors.Transparent;
            titleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
            titleBar.ButtonForegroundColor = text;
            titleBar.ButtonInactiveForegroundColor = inactiveText;
            titleBar.ButtonHoverBackgroundColor = hover;
            titleBar.ButtonHoverForegroundColor = text;
            titleBar.ButtonPressedBackgroundColor = pressed;
            titleBar.ButtonPressedForegroundColor = text;
        }

        /// <summary>
        /// Invoked when Navigation to a certain page fails
        /// </summary>
        /// <param name="sender">The Frame which failed navigation</param>
        /// <param name="e">Details about the navigation failure</param>
        void OnNavigationFailed(object sender, NavigationFailedEventArgs e)
        {
            throw new Exception("Failed to load Page " + e.SourcePageType.FullName);
        }
    }
}
