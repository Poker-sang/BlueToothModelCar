using Microsoft.UI.Xaml;
using WinUI3Utilities;

namespace BlueToothModelCar;

public partial class App : Application
{
    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var mainWindow = new MainWindow();
        mainWindow.Initialize(new()
        {
            Size = new(600, 800),
            ExtendTitleBar = true,
            Title = nameof(BlueToothModelCar)
        });
        mainWindow.Activate();
    }
}
