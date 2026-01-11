using System;
using System.Text;
using System.Linq;
using System.Threading;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using ActUtlType64Lib;
using UnityEngine;

public sealed class MXInterface : IDisposable
{
    public struct SetDeviceRequest
    {
        public string deviceAddress;
        public short writeValue;
        public Action<bool> callback;
        public bool result;

        public SetDeviceRequest(string deviceAddress, short writeValue, Action<bool> callback = null)
        {
            this.deviceAddress = deviceAddress;
            this.writeValue = writeValue;
            this.callback = callback;
            result = false;
        }
    }
    public struct GetDeviceRequest
    {
        public string deviceAddress;
        public Action<short> callback;
        public short readData;

        public GetDeviceRequest(string deviceAddress, Action<short> callback = null)
        {
            this.deviceAddress = deviceAddress;
            this.callback = callback;
            this.readData = 0;
        }
    }
    public struct ReadDeviceRequest
    {
        public short[] readDatas;

        public ReadDeviceRequest(short[] readDatas)
        {
            this.readDatas = readDatas;
        }
    }    
    public struct BufferReadRequest
    {
        public int startIO;         // 모듈 선두 I/O 번호
        public int address;         // 버퍼 메모리 주소
        public int size;            // 읽을 워드 수
        public short[] readData;    // 읽어온 데이터 저장소
        public Action<short[]> callback;

        public BufferReadRequest(int startIO, int address, int size, Action<short[]> callback)
        {
            this.startIO = startIO;
            this.address = address;
            this.size = size;
            this.readData = new short[size];
            this.callback = callback;
        }
    }

    public struct BufferWriteRequest
    {
        public int startIO;
        public int address;
        public short[] writeData;   // 쓸 데이터
        public Action<bool> callback;
        public bool result;

        public BufferWriteRequest(int startIO, int address, short[] writeData, Action<bool> callback)
        {
            this.startIO = startIO;
            this.address = address;
            this.writeData = writeData;
            this.callback = callback;
            this.result = false;
        }
    }

    private readonly StringBuilder _sb = new();
    private Thread _worker;
    private readonly AutoResetEvent _resetEvent = new(false);
    private ActUtlType64 _communicator;

    private readonly int _interval;
    private readonly int _stationNumber;
    private readonly string _password;

    private bool _isRunning = false;

    private int _autoReadCount;
    private short[] _autoReadDatas;

    private readonly ConcurrentQueue<string> _readRequestQueue = new();
    private readonly ConcurrentQueue<SetDeviceRequest> _setRequestQueue = new();
    private readonly ConcurrentQueue<GetDeviceRequest> _getRequestQueue = new();

    private readonly ConcurrentQueue<BufferReadRequest> _bufferReadQueue = new();
    private readonly ConcurrentQueue<BufferWriteRequest> _bufferWriteQueue = new();

    public MXInterface(int interval, int capacity, int stationNumber, string password = null)
    {
        _interval = interval;
        _stationNumber = stationNumber;
        _password = password;
        _autoReadDatas = new short[capacity];        
        
    }
    ~MXInterface()
    {
        Close();
        Dispose();
    }

    public void Open()
    {
        if (_worker != null)
            return;

        Thread thread = new(Run);
        _worker = thread;
        _worker.IsBackground = true;
        _worker.SetApartmentState(ApartmentState.STA);
        _worker.Start();
    }
    public void Close()
    {
        if (_worker == null)
            return;

        _isRunning = false;
        _resetEvent.Set();
    }
    public void Dispose()
    {
        _worker.Abort();
        _worker = null;
        _autoReadDatas = null;
        GC.SuppressFinalize(this);
    }

    //요청 메서드
    public void AddGetDeviceRequest(GetDeviceRequest request)
    {
        _getRequestQueue.Enqueue(request);
        _resetEvent.Set();
    }
    public void AddSetDeviceRequest(SetDeviceRequest request)
    {
        _setRequestQueue.Enqueue(request);
        _resetEvent.Set();
    }    
    public void AddBufferReadRequest(BufferReadRequest request)
    {
        _bufferReadQueue.Enqueue(request);
        _resetEvent.Set();
    }
    public void AddBufferWriteRequest(BufferWriteRequest request)
    {
        _bufferWriteQueue.Enqueue(request);
        _resetEvent.Set();
    }
    public void SetAutoReadDevice(IEnumerable<string> deviceArray)
    {
        Thread thread = new(() =>
        {
            _autoReadCount = deviceArray.Count();
            if (_autoReadCount > _autoReadDatas.Length)
            {
                _autoReadDatas = new short[_autoReadDatas.Length * 2];
            }

            _sb.Clear();
            _readRequestQueue.Clear();

            var enumerator = deviceArray.GetEnumerator();
            enumerator.MoveNext();
            _sb.Append(enumerator.Current);
            while (enumerator.MoveNext())
            {
                _sb.AppendFormat("\n{0}", enumerator.Current);
            }

            if (_autoReadCount > 0)
            {
                _readRequestQueue.Enqueue(_sb.ToString());
                _resetEvent.Set();
            }
        });
        thread.Start();
    }
    public void SetAutoReadDevice(params string[] devices)
    {
        SetAutoReadDevice(deviceArray: devices);
    }

    private void Run()
    {
        try
        {
            _communicator = new ActUtlType64();
            Debug.Log("MX Component 객체가 성공적으로 생성되었습니다.");
            _communicator.ActLogicalStationNumber = _stationNumber;
            _communicator.ActPassword = _password;
        }
        catch (COMException e)
        {
            Debug.LogError($"MX Component 객체가 생성에 실패했습니다.\n{e.Message}");
            return;
        }

        int ret = _communicator.Open();
        if (ret == 0)
            Debug.Log("시뮬레이터와 성공적으로 연결되었습니다.");
        else
            Debug.LogError($"시뮬레이터와의 연결에 실패하였습니다.오류코드(0x{ret:X8})");

        _isRunning = true;

        while (_isRunning)
        {
            if (_readRequestQueue.IsEmpty)
                _resetEvent.WaitOne();
            else
                _resetEvent.WaitOne(_interval);

            //즉시 쓰기 요청
            while (_setRequestQueue.TryDequeue(out SetDeviceRequest request))
            {
                ret = _communicator.SetDevice2(request.deviceAddress, request.writeValue);
                if (ret != 0)
                {
                    Debug.LogError($"{request.deviceAddress}에 {request.writeValue}를 덮어쓰지 못했습니다. 오류코드(0x{ret:X8})");
                    continue;
                }

                if (request.callback == null)
                    continue;

                request.result = ret == 0;
                MXRequester.Get.OnReceivedSetDevice(request);
            }

            //즉시 읽기 요청
            while (_getRequestQueue.TryDequeue(out GetDeviceRequest request))
            {
                ret = _communicator.GetDevice2(request.deviceAddress, out request.readData);
                if (ret != 0)
                {
                    Debug.LogError($"{request.deviceAddress}의 데이터를 읽지 못했습니다. 오류코드(0x{ret:X8})");
                    continue;
                }

                if (request.callback == null)
                    continue;

                MXRequester.Get.OnReceivedGetDevice(request);
            }

            // 버퍼 메모리 쓰기 (WriteBuffer)
            while (_bufferWriteQueue.TryDequeue(out BufferWriteRequest request))
            {
                // WriteBuffer(StartIO, Address, Size, ref Data)
                ret = _communicator.WriteBuffer(request.startIO, request.address, request.writeData.Length, ref request.writeData[0]);

                if (ret != 0) Debug.LogError($"Buffer Write 실패 (IO:{request.startIO:X}, Addr:{request.address}). 오류(0x{ret:X8})");

                if (request.callback == null) continue;
                request.result = ret == 0;
                MXRequester.Get.OnReceivedBufferWrite(request);
            }

            // 버퍼 메모리 읽기 (ReadBuffer)
            while (_bufferReadQueue.TryDequeue(out BufferReadRequest request))
            {

                // ReadBuffer(StartIO, Address, Size, out Data)
                //Debug.Log($"ReadBuffer => {request.startIO}, {request.address}, {request.size}");
                ret = _communicator.ReadBuffer(request.startIO, request.address, request.size, out request.readData[0]);

                if (ret != 0) Debug.LogError($"Buffer Read 실패 (IO:{request.startIO:X}, Addr:{request.address}). 오류(0x{ret:X8})");

                if (request.callback == null) continue;
                MXRequester.Get.OnReceivedBufferRead(request);
            }


            lock (_readRequestQueue)
            {
                if (!_readRequestQueue.IsEmpty)
                {
                    ret = _communicator.ReadDeviceRandom2(_readRequestQueue.ElementAt(0), _autoReadCount, out _autoReadDatas[0]);

                    if (ret != 0)
                    {
                        Debug.LogError($"지정된 주소들로 부터 데이터 읽기에 실패하였습니다.오류코드(0x{ret:X8})");
                        continue;
                    }
                    MXRequester.Get.OnReceiveReadDatas(new ReadDeviceRequest(_autoReadDatas));
                }
            }
        }

        try
        {
            if(_communicator != null)
            {
                ret = _communicator.Close();
                if (ret == 0)
                {
                    Debug.Log("시뮬레이터와 성공적으로 연결 해제되었습니다.");
                    _worker = null;
                }
                else
                    Debug.LogError($"시뮬레이터와의 연결해제에 실패하였습니다.오류코드(0x{ret:X8})");
            }
        }
        catch (COMException e)
        {
            Debug.LogError(e.Message);
        }
    }
}
