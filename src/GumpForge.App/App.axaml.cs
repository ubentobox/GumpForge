using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core;
using Avalonia.Data.Core.Plugins;
using System.Linq;
using Avalonia.Markup.Xaml;
using GumpForge.App.ViewModels;
using GumpForge.App.Views;

namespace GumpForge.App;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var profileWindow = new ProfileWindow();
            profileWindow.Closed += (_, _) =>
            {
                var vm = new MainWindowViewModel();

                if (profileWindow.SelectedProfile is not null)
                    vm.ApplyProfile(profileWindow.SelectedProfile);

                var mainWindow = new MainWindow { DataContext = vm };
                desktop.MainWindow = mainWindow;
                mainWindow.Show();
            };

            desktop.MainWindow = profileWindow;
        }

        base.OnFrameworkInitializationCompleted();
    }
}