using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Gaming.Input;
using Windows.System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using WinRT;
using Windows.Storage;
using Windows.Storage.Streams;

namespace BlueToothModelCar;

public sealed partial class MainWindow : Window
{
    private readonly HttpClient _client = new();
    private readonly BlueToothCore _core = new();

    private readonly ObservableCollection<BlueToothItem> _devices = [];

    private Gamepad? _gamepad;

    public MainWindow()
    {
        _core.DeviceWatcherChanged += DeviceWatcherChanged;
        _core.CharacteristicFinish += CharacteristicFinish;
        _core.RecData += RecData;
        _core.NewDevice += sender => DispatcherQueue.TryEnqueue(() => _devices.Add(new(sender.Name, sender.DeviceId, sender)));
        _core.StartBleDeviceWatcher();
        InitializeComponent();
        AddRoutedEventHandler(Forward, Button_Forward);
        AddRoutedEventHandler(Backward, Button_Backward);
        AddRoutedEventHandler(TurnLeft, Button_TurnLeft);
        AddRoutedEventHandler(TurnRight, Button_TurnRight);
        if (Gamepad.Gamepads.Count is 0)
            Gamepad.GamepadAdded += OnGamepadAdded;
        else
            _gamepad = Gamepad.Gamepads[0];

        Gamepad.GamepadRemoved += (e, g) =>
        {
            if (_gamepad == g)
                _gamepad = null;
            Gamepad.GamepadAdded += OnGamepadAdded;
        };
        _ = DispatcherQueue.TryEnqueue(() => Task.Run(GamepadLoop));
        _ = DispatcherQueue.TryEnqueue(() => Task.Run(StreamLoop));
        return;

        void OnGamepadAdded(object? e, Gamepad g)
        {
            _gamepad = g;
            Gamepad.GamepadAdded -= OnGamepadAdded;
        }
    }

    private bool Cancel { get; set; }

    private bool CaptureFrame { get; set; }

    private bool IsOpen { get; set; }

    private async void StreamLoop()
    {
        while (!Cancel)
        {
            try
            {
                _ = DispatcherQueue.TryEnqueue(async () =>
                {
                    var bytes = await _client.GetByteArrayAsync("http://192.168.26.189/capture");
                    var stream = new MemoryStream(bytes);
                    var source = new BitmapImage();
                    source.SetSource(stream.AsRandomAccessStream());
                    if (CaptureFrame && !IsOpen)
                    {
                        CaptureFrame = false;
                        ContentDialogImage.Source = source;
                        var file = await ApplicationData.Current.LocalFolder.CreateFileAsync(DateTime.Now.ToString(CultureInfo.CurrentCulture).Replace(':', ' ').Replace('/', ' ') + ".png");
                        await using (var s = await file.OpenStreamForWriteAsync())
                        {
                            stream.Position = 0;
                            await stream.CopyToAsync(s);
                        }
                        IsOpen = true;
                        _ = await ContentDialog.ShowAsync();
                    }
                    else
                        Image.Source = source;
                });
            }
            catch
            {
            }
            await Task.Delay(1000);
        }
    }

    private void ContentDialog_CloseButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        IsOpen = false;
    }

    private async void GamepadLoop()
    {
        while (!Cancel)
        {
            if (_gamepad is not null)
            {
                var currentReading = _gamepad.GetCurrentReading();
                switch (currentReading.LeftThumbstickY)
                {
                    case > 0.3: CarForward(); break;
                    case < -0.3: CarBackward(); break;
                    default:
                    {
                        switch (currentReading.LeftThumbstickX)
                        {
                            case > 0.3: CarTurnRight(); break;
                            case < -0.3: CarTurnLeft(); break;
                            default: CarBrake(); break;
                        }
                        break;
                    }
                }
                if (currentReading.Buttons.HasFlag(GamepadButtons.X))
                    CarBrake();

                await Task.Delay(500);
            }
        }
    }
    private async void UIElement_OnTapped(object sender, TappedRoutedEventArgs e)
    {
        _ = await Launcher.LaunchFolderPathAsync(ApplicationData.Current.LocalFolder.Path);
    }

    private void AddRoutedEventHandler(UIElement element, PointerEventHandler handler)
    {
        element.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(handler), true);
        element.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler(Button_Brake), true);
    }

    private void CharacteristicFinish(int size)
    {
        if (size <= 0)
        {
            Debug.WriteLine("设备未连上");
        }
    }

    private void RecData(GattCharacteristic sender, byte[] data)
    {
        var str = BitConverter.ToString(data);
        if (str.Contains("4B"))
            CaptureFrame = true;
        else
            Debug.WriteLine(sender.Uuid + "             " + str);
    }

    private void DeviceWatcherChanged(BluetoothLEDevice currentDevice)
    {
        var bytes = BitConverter.GetBytes(currentDevice.BluetoothAddress);
        Array.Reverse(bytes);
        var address = BitConverter.ToString(bytes, 2, 6).Replace('-', ':').ToLower();
        Debug.WriteLine("发现设备：<" + currentDevice.Name + ">  address:<" + address + ">");
    }

    private void ConnectDevice(BluetoothLEDevice device)
    {
        _core.StopBleDeviceWatcher();
        _core.StartMatching(device);
        _core.FindService();
    }

    private void ComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        ConnectDevice(((BlueToothItem)((ComboBox)sender).SelectedItem).Device);

    private void Button_Forward(object sender, PointerRoutedEventArgs e) => CarForward();
    private void Button_Backward(object sender, PointerRoutedEventArgs e) => CarBackward();
    private void Button_TurnLeft(object sender, PointerRoutedEventArgs e) => CarTurnLeft();
    private void Button_TurnRight(object sender, PointerRoutedEventArgs e) => CarTurnRight();
    private void Button_Brake(object sender, IWinRTObject e) => CarBrake();

    private void UIElement_OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        switch (e.Key)
        {
            case VirtualKey.Up: CarForward(); break;
            case VirtualKey.Down: CarForward(); break;
            case VirtualKey.Left: CarTurnLeft(); break;
            case VirtualKey.Right: CarTurnRight(); break;
            case VirtualKey.Space: CarBrake(); break;
        }
    }

    private void UIElement_OnKeyUp(object sender, KeyRoutedEventArgs e)
    {
        CarBrake();
    }

    private void FrameworkElement_OnUnloaded(object sender, RoutedEventArgs e) => Cancel = true;

#pragma warning disable IDE0230 // 使用 UTF-8 字符串文本
    private void CarTurnLeft() => _core.Write(0X46);
    private void CarTurnRight() => _core.Write(0X42);
    private void CarForward() => _core.Write(0X41);
    private void CarBackward() => _core.Write(0X45);
    private void CarBrake() => _core.Write(0x5A);
#pragma warning restore IDE0230 // 使用 UTF-8 字符串文本
}
