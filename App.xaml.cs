using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml.Navigation;
using Windows.UI;
using WinRT.Interop;

namespace FlatOut4SaveEditor
{
    /// <summary>
    /// Provides application-specific behavior to supplement the default Application class.
    /// </summary>
    public partial class App : Application
    {
        public static Window MainWindow { get; private set; } = null!;

        /// <summary>
        /// Initializes the singleton application object.  This is the first line of authored code
        /// executed, and as such is the logical equivalent of main() or WinMain().
        /// </summary>
        public App()
        {
            this.InitializeComponent();
        }

        /// <summary>
        /// Invoked when the application is launched normally by the end user.  Other entry points
        /// will be used such as when the application is launched to open a specific file.
        /// </summary>
        /// <param name="e">Details about the launch request and process.</param>
        protected override void OnLaunched(LaunchActivatedEventArgs e)
        {
            MainWindow ??= new Window
            {
                Title = "FlatOut 4 Save Editor"
            };

            if (MainWindow.Content is not Frame rootFrame)
            {
                rootFrame = new Frame();
                rootFrame.NavigationFailed += OnNavigationFailed;
                rootFrame.ActualThemeChanged += (_, _) => ApplyTitleBarTheme(rootFrame.ActualTheme);
                MainWindow.Content = rootFrame;
            }

            _ = rootFrame.Navigate(typeof(MainPage), e.Arguments);
            ApplyTitleBarTheme(rootFrame.ActualTheme);
            MainWindow.Activate();
        }

        private static void ApplyTitleBarTheme(ElementTheme theme)
        {
            if (!AppWindowTitleBar.IsCustomizationSupported())
            {
                return;
            }

            IntPtr hwnd = WindowNative.GetWindowHandle(MainWindow);
            WindowId windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
            AppWindowTitleBar titleBar = AppWindow.GetFromWindowId(windowId).TitleBar;

            bool dark = theme == ElementTheme.Dark;
            if (dark)
            {
                Color background = Color.FromArgb(255, 32, 32, 32);
                Color hover = Color.FromArgb(255, 58, 58, 58);
                Color pressed = Color.FromArgb(255, 76, 76, 76);
                Color inactiveText = Color.FromArgb(255, 150, 150, 150);

                titleBar.BackgroundColor = background;
                titleBar.ForegroundColor = Colors.White;
                titleBar.InactiveBackgroundColor = background;
                titleBar.InactiveForegroundColor = inactiveText;
                titleBar.ButtonBackgroundColor = background;
                titleBar.ButtonForegroundColor = Colors.White;
                titleBar.ButtonHoverBackgroundColor = hover;
                titleBar.ButtonHoverForegroundColor = Colors.White;
                titleBar.ButtonPressedBackgroundColor = pressed;
                titleBar.ButtonPressedForegroundColor = Colors.White;
                titleBar.ButtonInactiveBackgroundColor = background;
                titleBar.ButtonInactiveForegroundColor = inactiveText;
            }
            else
            {
                Color background = Color.FromArgb(255, 243, 243, 243);
                Color hover = Color.FromArgb(255, 229, 229, 229);
                Color pressed = Color.FromArgb(255, 212, 212, 212);
                Color text = Color.FromArgb(255, 32, 32, 32);
                Color inactiveText = Color.FromArgb(255, 110, 110, 110);

                titleBar.BackgroundColor = background;
                titleBar.ForegroundColor = text;
                titleBar.InactiveBackgroundColor = background;
                titleBar.InactiveForegroundColor = inactiveText;
                titleBar.ButtonBackgroundColor = background;
                titleBar.ButtonForegroundColor = text;
                titleBar.ButtonHoverBackgroundColor = hover;
                titleBar.ButtonHoverForegroundColor = text;
                titleBar.ButtonPressedBackgroundColor = pressed;
                titleBar.ButtonPressedForegroundColor = text;
                titleBar.ButtonInactiveBackgroundColor = background;
                titleBar.ButtonInactiveForegroundColor = inactiveText;
            }
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
