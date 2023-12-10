using Windows.Devices.Bluetooth;

namespace BlueToothModelCar;

public record BlueToothItem(
    string Name,
    string Id,
    BluetoothLEDevice Device);
