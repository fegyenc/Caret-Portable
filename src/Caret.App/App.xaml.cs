using Microsoft.UI.Xaml;

namespace Typedown.WinUI
{
    public partial class App : Application
    {
        private Window? window;

        public App()
        {
            InitializeComponent();
        }

        protected override void OnLaunched(LaunchActivatedEventArgs args)
        {
            // Before any window: XAML resolves its {u:Loc} strings as it loads.
            Utilities.Locale.Load(new ViewModels.SettingsViewModel().Language);
            window = new MainWindow();
            window.Activate();
        }
    }
}
