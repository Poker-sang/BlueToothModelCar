using System;
using System.Collections.Generic;
using System.Diagnostics;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Foundation;
using Windows.Security.Cryptography;

namespace BlueToothModelCar;

internal class BlueToothCore
{
    /// <summary>
    /// 获取特征委托
    /// </summary>
    public delegate void CharacteristicAddedEvent(GattCharacteristic gattCharacteristic);

    /// <summary>
    /// 获取服务委托
    /// </summary>
    public delegate void CharacteristicFinishEvent(int size);

    /// <summary>
    /// 定义搜索蓝牙设备委托
    /// </summary>
    public delegate void DeviceWatcherChangedEvent(BluetoothLEDevice bluetoothLeDevice);

    /// <summary>
    /// 接受数据委托
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="data"></param>
    public delegate void RecDataEvent(GattCharacteristic sender, byte[] data);

    /// <summary>
    /// 特性通知类型通知启用
    /// </summary>
    private const GattClientCharacteristicConfigurationDescriptorValue CharacteristicNotificationType = GattClientCharacteristicConfigurationDescriptorValue.Notify;

    private bool _asyncLock;

    private BluetoothLEAdvertisementWatcher? _watcher;

    public HashSet<GattCharacteristic> Characteristics = [];

    public BlueToothCore() => CharacteristicAdded += OnCharacteristicAdded;

    /// <summary>
    /// 当前连接的服务
    /// </summary>
    public GattDeviceService? CurrentService { get; private set; }

    /// <summary>
    /// 当前连接的蓝牙设备
    /// </summary>
    public BluetoothLEDevice? CurrentDevice { get; private set; }

    /// <summary>
    /// 写特征对象
    /// </summary>
    public GattCharacteristic? CurrentWriteCharacteristic { get; private set; }

    /// <summary>
    /// 通知特征对象
    /// </summary>
    public GattCharacteristic? CurrentNotifyCharacteristic { get; private set; }

    /// <summary>
    /// 存储检测到的设备
    /// </summary>
    public Dictionary<string, BluetoothLEDevice> DeviceList { get; } = [];

    /// <summary>
    /// 当前连接的蓝牙Mac
    /// </summary>
    private string CurrentDeviceMac { get; set; } = "";

    /// <summary>
    /// 搜索蓝牙事件
    /// </summary>
    public event Action<BluetoothLEDevice>? NewDevice;

    /// <summary>
    /// 搜索蓝牙事件
    /// </summary>
    public event DeviceWatcherChangedEvent? DeviceWatcherChanged;

    /// <summary>
    /// 获取服务事件
    /// </summary>
    public event CharacteristicFinishEvent? CharacteristicFinish;

    /// <summary>
    /// 获取特征事件
    /// </summary>
    public event CharacteristicAddedEvent? CharacteristicAdded;

    /// <summary>
    /// 接受数据事件
    /// </summary>
    public event RecDataEvent? RecData;

    private void OnCharacteristicAdded(GattCharacteristic gatt)
    {
        Debug.WriteLine($"handle:[0x{gatt.AttributeHandle:X4}]  char properties:[{gatt.CharacteristicProperties}]  UUID:[{gatt.Uuid}]");
        _ = Characteristics.Add(gatt);

        if (gatt.CharacteristicProperties.HasFlag(GattCharacteristicProperties.Write) ||gatt.CharacteristicProperties.HasFlag(GattCharacteristicProperties.Notify))
            SetOperation(gatt);
    }

    /// <summary>
    /// 搜索蓝牙设备
    /// </summary>
    public void StartBleDeviceWatcher()
    {
        _watcher = new()
        {
            ScanningMode = BluetoothLEScanningMode.Active,
            SignalStrengthFilter = {
                // only activate the watcher when we're recieving values >= -80
                InRangeThresholdInDBm = -80,
                // stop watching if the value drops below -90 (user walked away)
                OutOfRangeThresholdInDBm = -90
            }
        };

        // register callback for when we see an advertisements
        _watcher.Received += OnAdvertisementReceived;

        // wait 5 seconds to make sure the device is really out of range
        _watcher.SignalStrengthFilter.OutOfRangeTimeout = TimeSpan.FromMilliseconds(5000);
        _watcher.SignalStrengthFilter.SamplingInterval = TimeSpan.FromMilliseconds(2000);

        // starting watching for advertisements
        _watcher.Start();

        Debug.WriteLine("自动发现设备中..");
    }

    /// <summary>
    /// 停止搜索蓝牙
    /// </summary>
    public void StopBleDeviceWatcher() => _watcher?.Stop();

    /// <summary>
    /// 主动断开连接
    /// </summary>
    /// <returns></returns>
    public void Dispose()
    {
        CurrentDeviceMac = "";
        CurrentService?.Dispose();
        CurrentDevice?.Dispose();
        CurrentService = null;
        CurrentDevice = null;
        CurrentWriteCharacteristic = null;
        CurrentNotifyCharacteristic = null;
        Debug.WriteLine("主动断开连接");
    }

    /// <summary>
    /// 匹配
    /// </summary>
    /// <param name="device"></param>
    public void StartMatching(BluetoothLEDevice device) => CurrentDevice = device;

    /// <summary>
    /// 发送数据接口
    /// </summary>
    /// <returns></returns>
    public void Write(params byte[] data)
    {
        if (CurrentWriteCharacteristic != null)
        {
            CurrentWriteCharacteristic.WriteValueAsync(CryptographicBuffer.CreateFromByteArray(data), GattWriteOption.WriteWithResponse).Completed = (asyncInfo, asyncStatus) =>
            {
                if (asyncStatus == AsyncStatus.Completed)
                {
                    var a = asyncInfo.GetResults();
                    Debug.WriteLine("发送数据：" + BitConverter.ToString(data) + " State : " + a);
                }
            };
        }
    }

    /// <summary>
    /// 获取蓝牙服务
    /// </summary>
    public void FindService()
    {
        if (CurrentDevice is null)
            return;
        CurrentDevice.GetGattServicesAsync().Completed = (asyncInfo, asyncStatus) =>
        {
            if (asyncStatus == AsyncStatus.Completed)
            {
                var services = asyncInfo.GetResults().Services;
                Debug.WriteLine("GattServices size=" + services.Count);
                foreach (var ser in services)
                {
                    FindCharacteristic(ser);
                }
                CharacteristicFinish?.Invoke(services.Count);
            }
        };
    }

    /// <summary>
    /// 按MAC地址直接组装设备ID查找设备
    /// </summary>
    public void SelectDeviceFromIdAsync(string mac)
    {
        CurrentDeviceMac = mac;
        CurrentDevice = null;
        BluetoothAdapter.GetDefaultAsync().Completed = (asyncInfo, asyncStatus) =>
        {
            if (asyncStatus == AsyncStatus.Completed)
            {
                var mBluetoothAdapter = asyncInfo.GetResults();
                var bytes1 = BitConverter.GetBytes(mBluetoothAdapter.BluetoothAddress);//ulong转换为byte数组
                Array.Reverse(bytes1);
                var macAddress = BitConverter.ToString(bytes1, 2, 6).Replace('-', ':').ToLower();
                var id = "BluetoothLE#BluetoothLE" + macAddress + "-" + mac;
                Matching(id);
            }
        };
    }

    /// <summary>
    /// 获取操作
    /// </summary>
    /// <returns></returns>
    public void SetOperation(GattCharacteristic gattCharacteristic)
    {
        if (CurrentDevice is null)
            return;
        var bytes1 = BitConverter.GetBytes(CurrentDevice.BluetoothAddress);
        Array.Reverse(bytes1);
        CurrentDeviceMac = BitConverter.ToString(bytes1, 2, 6).Replace('-', ':').ToLower();

        var msg = "正在连接设备<" + CurrentDeviceMac + ">..";
        Debug.WriteLine(msg);

        if (gattCharacteristic.CharacteristicProperties.HasFlag(GattCharacteristicProperties.Write))
            CurrentWriteCharacteristic = gattCharacteristic;
        if (gattCharacteristic.CharacteristicProperties.HasFlag(GattCharacteristicProperties.Notify))
        {
            CurrentNotifyCharacteristic = gattCharacteristic;
            CurrentNotifyCharacteristic.ProtectionLevel = GattProtectionLevel.Plain;
            CurrentNotifyCharacteristic.ValueChanged += Characteristic_ValueChanged;
            EnableNotifications(CurrentNotifyCharacteristic);
        }

        if ((uint)gattCharacteristic.CharacteristicProperties is 26)
        {

        }

        if (gattCharacteristic.CharacteristicProperties is (GattCharacteristicProperties.Write | GattCharacteristicProperties.Notify))
        {
            CurrentWriteCharacteristic = gattCharacteristic;
            CurrentNotifyCharacteristic = gattCharacteristic;
            CurrentNotifyCharacteristic.ProtectionLevel = GattProtectionLevel.Plain;
            CurrentNotifyCharacteristic.ValueChanged += Characteristic_ValueChanged;
            EnableNotifications(CurrentNotifyCharacteristic);
        }

        CurrentDevice.ConnectionStatusChanged += CurrentDevice_ConnectionStatusChanged;
    }

    private void OnAdvertisementReceived(BluetoothLEAdvertisementWatcher watcher, BluetoothLEAdvertisementReceivedEventArgs eventArgs) =>
        BluetoothLEDevice.FromBluetoothAddressAsync(eventArgs.BluetoothAddress).Completed = (asyncInfo, asyncStatus) =>
        {
            if (asyncStatus == AsyncStatus.Completed)
            {
                if (asyncInfo.GetResults() == null)
                {
                    //Debug.WriteLine("没有得到结果集");
                }
                else
                {
                    var currentDevice = asyncInfo.GetResults();

                    if (DeviceList.TryAdd(currentDevice.DeviceId, currentDevice))
                    {
                        NewDevice?.Invoke(currentDevice);
                        DeviceWatcherChanged?.Invoke(currentDevice);
                    }
                    else
                        currentDevice.Dispose();
                }
            }
        };

    /// <summary>
    /// 获取特性
    /// </summary>
    private void FindCharacteristic(GattDeviceService gattDeviceService)
    {
        CurrentService = gattDeviceService;
        CurrentService.GetCharacteristicsAsync().Completed = (asyncInfo, asyncStatus) =>
        {
            if (asyncStatus == AsyncStatus.Completed)
            {
                var services = asyncInfo.GetResults().Characteristics;
                foreach (var c in services)
                {
                    CharacteristicAdded?.Invoke(c);
                }
            }
        };
    }

    /// <summary>
    /// 搜索到的蓝牙设备
    /// </summary>
    /// <returns></returns>
    private void Matching(string id)
    {
        try
        {
            BluetoothLEDevice.FromIdAsync(id).Completed = (asyncInfo, asyncStatus) =>
            {
                switch (asyncStatus)
                {
                    case AsyncStatus.Completed:
                    {
                        var bleDevice = asyncInfo.GetResults();
                        if (DeviceList.TryAdd(bleDevice.DeviceId, bleDevice))
                            NewDevice?.Invoke(bleDevice);
                        NewDevice?.Invoke(bleDevice);
                        Debug.WriteLine(bleDevice);
                        break;
                    }
                    case AsyncStatus.Started:
                    case AsyncStatus.Canceled:
                    case AsyncStatus.Error:
                        Debug.WriteLine(asyncStatus.ToString());
                        break;
                }
            };
        }
        catch (Exception e)
        {
            var msg = "没有发现设备" + e;
            Debug.WriteLine(msg);
            StartBleDeviceWatcher();
        }
    }

    private void CurrentDevice_ConnectionStatusChanged(BluetoothLEDevice sender, object args)
    {
        if (sender.ConnectionStatus == BluetoothConnectionStatus.Disconnected && CurrentDeviceMac != "")
        {
            if (!_asyncLock)
            {
                _asyncLock = true;
                Debug.WriteLine("设备已断开");
                CurrentDevice?.Dispose();
                CurrentDevice = null;
                CurrentService = null;
                CurrentWriteCharacteristic = null;
                CurrentNotifyCharacteristic = null;
                SelectDeviceFromIdAsync(CurrentDeviceMac);
            }
        }
        else
        {
            if (!_asyncLock)
            {
                _asyncLock = true;
                Debug.WriteLine("设备已连接");
            }
        }
    }

    /// <summary>
    /// 设置特征对象为接收通知对象
    /// </summary>
    /// <param name="characteristic"></param>
    /// <returns></returns>
    private void EnableNotifications(GattCharacteristic characteristic)
    {

        if (CurrentDevice is null)
            return;
        Debug.WriteLine("收通知对象=" + CurrentDevice.Name + ":" + CurrentDevice.ConnectionStatus);
        characteristic.WriteClientCharacteristicConfigurationDescriptorAsync(CharacteristicNotificationType).Completed = (asyncInfo, asyncStatus) =>
        {
            if (asyncStatus == AsyncStatus.Completed)
            {
                var status = asyncInfo.GetResults();
                if (status == GattCommunicationStatus.Unreachable)
                {
                    Debug.WriteLine("设备不可用");
                    if (CurrentNotifyCharacteristic != null && !_asyncLock)
                    {
                        EnableNotifications(CurrentNotifyCharacteristic);
                    }
                    return;
                }
                _asyncLock = false;
                Debug.WriteLine("设备连接状态" + status);
            }
        };
    }

    /// <summary>
    /// 接受到蓝牙数据
    /// </summary>
    private void Characteristic_ValueChanged(GattCharacteristic sender, GattValueChangedEventArgs args)
    {
        CryptographicBuffer.CopyToByteArray(args.CharacteristicValue, out var data);
        RecData?.Invoke(sender, data);
    }
}
