using Microsoft.UI.Xaml;
using WinUI3Utilities;

namespace BlueToothModelCar;

public partial class App : Application
{
    public App()
    {
        InitializeComponent();
        CurrentContext.Title = "自平衡车";
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var mainWindow = new MainWindow();
        mainWindow.Initialize(new()
        {
            Size = new(600, 800),
            TitleBarType = TitleBarType.Window,
        }, nameof(BlueToothModelCar), CurrentContext.TitleBar);
        mainWindow.Activate();
    }
}
