using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using UnityEngine;

public class MXRequester : MonoBehaviour
{
    [Serializable]
    public class DeviceSubscriber
    {
        public string address;
        public short ReadValue
        {
            get => _readValue;
            set
            {
                if (_readValue == value)
                    return;

                _readValue = value;
                callbacks?.Invoke(value);
            }
        }
        private short _readValue;
        public Action<short> callbacks;

        public DeviceSubscriber(string address)
        {
            this.address = address;
        }
    }

    private static MXRequester _instance = null;
    public static MXRequester Get => _instance;

    private MXInterface _mxComponent;

    // 콜백 전달용 큐
    private ConcurrentQueue<MXInterface.GetDeviceRequest> _getDeviceCallbackEnqueue = new();
    private ConcurrentQueue<MXInterface.SetDeviceRequest> _setCallbackEnqueue = new();
    private ConcurrentQueue<MXInterface.ReadDeviceRequest> _readDatasEnqueue = new();

    private ConcurrentQueue<MXInterface.BufferReadRequest> _bufferReadCallbackQueue = new();
    private ConcurrentQueue<MXInterface.BufferWriteRequest> _bufferWriteCallbackQueue = new();

    [SerializeField] private int _interval = 10;
    [SerializeField] private int _capacity = 100;
    [SerializeField] private int _stationNumber = 1;
    [SerializeField] private string _password;
    [SerializeField] private bool _useAutoConnect = false;

    private bool _updated = false;
    private bool _changed = false;
    private List<DeviceSubscriber> _deviceList = new(100);
    [SerializeField] private List<string> _addressList = new(100);

    public void AddGetDeviceRequest(string deviceAddress, Action<short> callback = null)
    {
        _mxComponent.AddGetDeviceRequest(new MXInterface.GetDeviceRequest(deviceAddress, callback));
        _updated = true;
    }
    public void AddSetDeviceRequest(string deviceAddress, short writeValue, Action<bool> callback = null)
    {
        _mxComponent.AddSetDeviceRequest(new MXInterface.SetDeviceRequest(deviceAddress, writeValue, callback));
        _updated = true;
    }

    /// <summary>
    /// 버퍼 메모리 블록 읽기 요청
    /// </summary>
    /// <param name="startIO">선두 I/O 번호 (예: 0x20)</param>
    /// <param name="address">버퍼 메모리 시작 주소 (예: 800)</param>
    /// <param name="size">읽을 워드 수 (예: 10)</param>
    /// <param name="callback">결과(short[])를 받을 콜백</param>
    public void AddBufferRead(int startIO, int address, int size, Action<short[]> callback)
    {
        if (size <= 0) return;
        _mxComponent.AddBufferReadRequest(new MXInterface.BufferReadRequest(startIO, address, size, callback));
        _updated = true;
    }

    /// <summary>
    /// 버퍼 메모리 블록 쓰기 요청
    /// </summary>
    /// <param name="startIO">선두 I/O 번호</param>
    /// <param name="address">버퍼 메모리 시작 주소</param>
    /// <param name="data">쓸 데이터 배열 (short[])</param>
    /// <param name="callback">성공 여부 콜백</param>
    public void AddBufferWrite(int startIO, int address, short[] data, Action<bool> callback = null)
    {
        if (data == null || data.Length == 0) return;
        _mxComponent.AddBufferWriteRequest(new MXInterface.BufferWriteRequest(startIO, address, data, callback));
        _updated = true;
    }

    public void AddDeviceAddress(string address, Action<short> action)
    {
        if (string.IsNullOrEmpty(address) || address.Length < 2)
            return;

        address = address.ToUpper();
        DeviceSubscriber subscriber = _deviceList.Find(x => x.address == address);
        if (subscriber == null)
        {
            subscriber = new DeviceSubscriber(address);
            _deviceList.Add(subscriber);
            _addressList.Add(address);
        }


        if (action != null)
        {
            subscriber.callbacks += action;
            action.Invoke(subscriber.ReadValue);
        }

        _deviceList.Sort((x, y) => x.address.CompareTo(y.address));
        _addressList.Sort((x, y) => x.CompareTo(y));
        _changed = true;
    }
    public void RemoveDeviceAddress(string address, Action<short> action)
    {
        address = address.ToUpper();
        DeviceSubscriber subscriber = _deviceList.Find(x => x.address == address);
        if (subscriber == null)
            return;


        if (action != null)
            subscriber.callbacks -= action;

        if (subscriber.callbacks == null)
        {
            _deviceList.Remove(subscriber);
            _addressList.Remove(address);
        }

        _changed = true;
    }

    // --- Interface로부터 수신 (스레드에서 호출됨) ---
    public void OnReceivedGetDevice(MXInterface.GetDeviceRequest receive)
    {
        _getDeviceCallbackEnqueue.Enqueue(receive);
        _updated = true;
    }
    public void OnReceivedSetDevice(MXInterface.SetDeviceRequest receive)
    {
        _setCallbackEnqueue.Enqueue(receive);
        _updated = true;
    }
    public void OnReceiveReadDatas(MXInterface.ReadDeviceRequest receive)
    {
        _readDatasEnqueue.Enqueue(receive);
        _updated = true;
    }
    // 버퍼 메모리 수신
    public void OnReceivedBufferRead(MXInterface.BufferReadRequest receive)
    {
        _bufferReadCallbackQueue.Enqueue(receive);
        _updated = true;
    }
    public void OnReceivedBufferWrite(MXInterface.BufferWriteRequest receive)
    {
        _bufferWriteCallbackQueue.Enqueue(receive);
        _updated = true;
    }

    private void Awake()
    {
        _instance = this;
        _deviceList = new(_capacity);
        _addressList = new(_capacity);

        _mxComponent = new MXInterface(_interval, _capacity, _stationNumber, _password);

        if (_useAutoConnect)
            Open();
    }


    public void Open() => _mxComponent.Open();
    public void Close() => _mxComponent.Close();

    private void OnApplicationQuit() => Close();
    private void OnDestroy() => _mxComponent?.Dispose();

    private void Update()
    {
        if (_changed)
        {
            _mxComponent.SetAutoReadDevice(_addressList);
            _changed = false;
        }

        if (!_updated)
            return;

        //즉시 읽기 콜백
        while (_getDeviceCallbackEnqueue.TryDequeue(out MXInterface.GetDeviceRequest receive))
        {
            receive.callback?.Invoke(receive.readData);
        }
        //즉시 쓰기 콜백
        while (_setCallbackEnqueue.TryDequeue(out MXInterface.SetDeviceRequest receive))
        {
            receive.callback?.Invoke(receive.result);
        }

        //버퍼 읽기 콜백
        while (_bufferReadCallbackQueue.TryDequeue(out MXInterface.BufferReadRequest receive))
            receive.callback?.Invoke(receive.readData);

        //버퍼 쓰기 콜백
        while (_bufferWriteCallbackQueue.TryDequeue(out MXInterface.BufferWriteRequest receive))
            receive.callback?.Invoke(receive.result);

        //자동 읽기 데이터 갱신
        while (_readDatasEnqueue.TryDequeue(out MXInterface.ReadDeviceRequest receive))
        {
            for (int i = 0; i < _deviceList.Count; ++i)
            {
                _deviceList[i].ReadValue = receive.readDatas[i];
            }
        }

        _updated = false;
    }
}
