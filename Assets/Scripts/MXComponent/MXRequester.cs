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
    public static MXRequester Get
    {
        get
        {
            return _instance;
        }
    }

    private MXInterface _mxComponent;
    private ConcurrentQueue<MXInterface.GetDeviceRequest> _getDeviceCallbackEnqueue = new();
    private ConcurrentQueue<MXInterface.SetDeviceRequest> _setCallbackEnqueue = new();
    private ConcurrentQueue<MXInterface.ReadDeviceRequest> _readDatasEnqueue = new();


    [SerializeField] private int _interval = 100;
    [SerializeField] private int _capacity = 100;
    [SerializeField] private int _stationNumber = 0;
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

    private void Awake()
    {
        _instance = this;
        _mxComponent = new MXInterface(_interval, _capacity, _stationNumber, _password);

        if (_useAutoConnect)
            Open();
    }


    public void Open()
    {
        _mxComponent.Open();
    }

    public void Close()
    {
        _mxComponent.Close();
    }


    private void OnApplicationQuit()
    {
        Close();
    }

    private void OnDestroy()
    {
        _mxComponent?.Dispose();
    }

    void Update()
    {
        if (_changed)
        {
            _mxComponent.SetAutoReadDevice(_addressList);
            _changed = false;
        }

        if (!_updated)
            return;



        while (_getDeviceCallbackEnqueue.TryDequeue(out MXInterface.GetDeviceRequest receive))
        {
            receive.callback?.Invoke(receive.readData);
        }

        while (_setCallbackEnqueue.TryDequeue(out MXInterface.SetDeviceRequest receive))
        {
            receive.callback?.Invoke(receive.result);
        }

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
