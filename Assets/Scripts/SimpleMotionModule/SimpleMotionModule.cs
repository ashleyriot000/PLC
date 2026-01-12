using UnityEngine;
using System;
using System.Collections.Generic;

public class SimpleMotionModule : MXObject
{
    #region Settings
    [Header("Network Settings")]
    [Tooltip("모듈의 선두 I/O 번호 (Hex String, 예: 00, 20, 50)")]
    public string startIO_Hex = "00";
    private int _startIO_Slot;

    [Header("PLC I/O Address (Auto Generated)")]
    [SerializeField] private string _ySystemCmdAddr;
    [SerializeField] private string _yAxisCmdAddr;
    [SerializeField] private string _xStartAddr;

    [Header("Buffer Memory Settings")]
    public int monitorAreaStartAddr = 800;
    public int commandAreaStartAddr = 1500;
    public int bufferSizePerAxis = 100;

    [Header("Communication Settings")]
    public float communicationInterval = 0.05f;
    private float _timer = 0f;

    [Header("Connected Axes")]
    public ServoAmp[] axes;
    #endregion

    #region Internal State
    private bool _isCommunicating = false;
    private short[] _readBufferCache;
    private short[] _writeBufferCache;

    private short _ySystemCache = 0;
    private short _yAxisCache = 0;

    private bool[] _isCommandExecuted;
    private bool _isModuleReady = false;
    #endregion

    private void Awake()
    {
        int rawAddress = System.Convert.ToInt32(startIO_Hex, 16);
        _startIO_Slot = rawAddress / 16;

        _ySystemCmdAddr = "K4Y" + startIO_Hex;

        int axisYStart = rawAddress + 0x10;
        _yAxisCmdAddr = "K4Y" + axisYStart.ToString("X");

        _xStartAddr = "K8X" + startIO_Hex;

        Debug.Log($"[Init] SysY:{_ySystemCmdAddr}, AxisY:{_yAxisCmdAddr}");

        int totalSize = axes.Length * bufferSizePerAxis;
        if (totalSize > 0)
        {
            _readBufferCache = new short[totalSize];
            _writeBufferCache = new short[totalSize];
        }
        _isCommandExecuted = new bool[axes.Length];
    }

    private void Start()
    {
        if (axes.Length > 0) Debug.Log(">>> 통신 시작 (2-Word Split Mode) <<<");
    }

    private void Update()
    {
        if (axes.Length == 0) return;
        _timer += Time.deltaTime;
        if (_timer >= communicationInterval && !_isCommunicating)
        {
            _timer = 0f;
            StartCommunicationCycle();
        }
    }

    #region Communication Cycle

    private void StartCommunicationCycle()
    {
        _isCommunicating = true;

        MXRequester.Get.AddGetDeviceRequest(_ySystemCmdAddr, OnReadSystemY);
        MXRequester.Get.AddGetDeviceRequest(_yAxisCmdAddr, OnReadAxisY);
    }

    private void OnReadSystemY(short value)
    {
        _ySystemCache = value;
    }

    private void OnReadAxisY(short value)
    {
        _yAxisCache = value;
        MXRequester.Get.AddBufferRead(_startIO_Slot, commandAreaStartAddr, _readBufferCache.Length, OnReadBufferCompleted);
    }

    private void OnReadBufferCompleted(short[] data)
    {
        if (data == null) { _isCommunicating = false; return; }

        // Y0 (Ready), Y1 (ServoOn)
        bool plcReady = (_ySystemCache & 1) != 0;
        bool servoOn = (_ySystemCache & 2) != 0;

        _isModuleReady = plcReady;

        for (int i = 0; i < axes.Length; i++)
        {
            if (axes[i] == null) continue;

            axes[i].SetServoOn(servoOn);

            if (!_isModuleReady) continue;

            int startBit = i;
            bool isStartOn = (_yAxisCache & (1 << startBit)) != 0;

            int jogFwdBit = 6 + (i * 2);
            int jogRevBit = 7 + (i * 2);

            bool isJogFwd = (_yAxisCache & (1 << jogFwdBit)) != 0;
            bool isJogRev = (_yAxisCache & (1 << jogRevBit)) != 0;

            axes[i].ProcessJog(isJogFwd, isJogRev);

            int offset = i * bufferSizePerAxis;
            int startNo = data[offset + 0];

            if (isStartOn)
            {
                if (!_isCommandExecuted[i])
                {
                    if (startNo > 0)
                    {
                        axes[i].StartPositioning(startNo);
                        _isCommandExecuted[i] = true;
                    }
                }
            }
            else
            {
                _isCommandExecuted[i] = false;
            }
        }

        PrepareWriteData();
    }

    private void PrepareWriteData()
    {
        int xSignalBitmap = 0;

        if (_isModuleReady) xSignalBitmap |= 1;

        for (int i = 0; i < axes.Length; i++)
        {
            if (axes[i] == null) continue;

            int offset = i * bufferSizePerAxis;
            int currentPulse = axes[i].CurrentPulse;

            _writeBufferCache[offset + 0] = (short)(currentPulse & 0xFFFF);
            _writeBufferCache[offset + 1] = (short)((currentPulse >> 16) & 0xFFFF);
            _writeBufferCache[offset + 6] = axes[i].ErrorCode;

            if (axes[i].IsBusy) xSignalBitmap |= (1 << (0x10 + i));
            if (axes[i].IsError) xSignalBitmap |= (1 << (0x18 + i));
        }

        MXRequester.Get.AddBufferWrite(_startIO_Slot, monitorAreaStartAddr, _writeBufferCache, null);
        MXRequester.Get.AddSetDeviceRequest(_xStartAddr, (short)xSignalBitmap, OnWriteCompleted);
    }

    private void OnWriteCompleted(bool success)
    {
        _isCommunicating = false;
    }
    #endregion
}