using UnityEngine;
using System;
using System.Collections.Generic;

public class SimpleMotionModule : MXObject
{
    [System.Flags]
    public enum SystemFeedback : ushort
    {
        None = 0,
        READY = 1,
        SyncFLAG = 1 << 1,
        MCode_Axis1 = 1 << 4,
        MCode_Axis2 = 1 << 5,
        MCode_Axis3 = 1 << 6,
        MCode_Axis4 = 1 << 7,
        ERROR_Axis1 = 1 << 8,
        ERROR_Axis2 = 1 << 9,
        ERROR_Axis3 = 1 << 10,
        ERROR_Axis4 = 1 << 11,
        BUSY_Axis1 = 1 << 12,
        BUSY_Axis2 = 1 << 13,
        BUSY_Axis3 = 1 << 14,
        BUSY_Axis4 = 1 << 15,
    }

    [System.Flags]
    public enum AxisFeedback : ushort
    {
        None = 0,        
        READY_Axis1 = 1,
        READY_Axis2 = 1 << 1,
        READY_Axis3 = 1 << 2,
        READY_Axis4 = 1 << 3,
        InPosition_Axis1 = 1 << 4,
        InPosition_Axis2 = 1 << 5,
        InPosition_Axis3 = 1 << 6,
        InPosition_Axis4 = 1 << 7,
    }

    #region Settings
    [Header("Network Settings")]
    [Tooltip("모듈의 선두 I/O 번호 (Hex String, 예: 00, 20, 50)")]
    public string startIO_Hex = "00";
    private int _startIO_Slot;

    [Header("PLC I/O Address (Auto Generated)")]
    [SerializeField] private string _ySystemCmdAddr;
    [SerializeField] private string _yAxisCmdAddr;
    [SerializeField] private string _xSystemFeedbackAddr;
    [SerializeField] private string _xAxisFeedbackAddr;

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

    private SystemFeedback _xSystemFeedback = SystemFeedback.SyncFLAG;
    private AxisFeedback _xAxisFeedback = AxisFeedback.None;

    private bool[] _isCommandExecuted;
    #endregion

    private void Awake()
    {
        int rawAddress = System.Convert.ToInt32(startIO_Hex, 16);
        _startIO_Slot = rawAddress / 16;

        _ySystemCmdAddr = "K4Y" + startIO_Hex;

        int axisYStart = rawAddress + 0x10;
        _yAxisCmdAddr = "K4Y" + axisYStart.ToString("X");

        Debug.Log($"[Init] SysY:{_ySystemCmdAddr}, AxisY:{_yAxisCmdAddr}");

        _xSystemFeedbackAddr = "K4X" + startIO_Hex;
        int axisXStart = rawAddress + 0x10;
        _xAxisFeedbackAddr = "K4X" + axisXStart.ToString("X");
        Debug.Log($"[Init] SysX:{_xSystemFeedbackAddr},  AxisX:{_xAxisFeedbackAddr}");

        int totalSize = axes.Length * bufferSizePerAxis;
        if (totalSize > 0)
        {
            _readBufferCache = new short[totalSize];
            _writeBufferCache = new short[totalSize];
        }
        _isCommandExecuted = new bool[axes.Length];
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
        if (data == null) 
        { 
            _isCommunicating = false; 
            return; 
        }

        // Y0 (Ready), Y1 (ServoOn)
        bool plcReady = (_ySystemCache & 1) != 0;
        bool servoOn = (_ySystemCache & 2) != 0;
       
        if (plcReady)
            _xSystemFeedback |= SystemFeedback.READY;
        else
            _xSystemFeedback &= ~SystemFeedback.READY;


        for (int i = 0; i < axes.Length; i++)
        {
            if (axes[i] == null) continue;

            axes[i].SetServoOn(servoOn);

            if (!axes[i].IsReady)
                continue;

            int startBit = i;
            int offset = i * bufferSizePerAxis;
            int startNo = data[offset + 0];

            int jogFwdBit = 8 + (i * 2);
            int jogRevBit = 9 + (i * 2);

            bool isJogFwd = (_ySystemCache & (1 << jogFwdBit)) != 0;
            bool isJogRev = (_ySystemCache & (1 << jogRevBit)) != 0;

            int jogTargetSpeed = (data[offset + 19] << 16) | (ushort)data[offset + 18];

            axes[i].CommandJog(isJogFwd, isJogRev, jogTargetSpeed);

            bool isStartOn = (_yAxisCache & (1 << startBit)) != 0;
            if (isStartOn)
            {
                if (!_isCommandExecuted[i])
                {
                    if (startNo > 0)
                    {
                        Debug.Log($"Executed => {startNo}");
                        axes[i].StartPositioning(startNo);
                        _isCommandExecuted[i] = true;
                    }
                }
            }
            else
            {
                _isCommandExecuted[i] = false;
            }
            

            int resetNo = data[offset + 2];
            if (resetNo > 0)
            {
                axes[i].ResetAxis();
            }
        }

        PrepareWriteData();
    }

    private void PrepareWriteData()
    {
        for (int i = 0; i < axes.Length; i++)
        {
            if (axes[i] == null) continue;

            int offset = i * bufferSizePerAxis;
            int currentPulse = axes[i].CurrentPulse;

            _writeBufferCache[offset + 0] = (short)(currentPulse & 0xFFFF);
            _writeBufferCache[offset + 1] = (short)((currentPulse >> 16) & 0xFFFF);
            _writeBufferCache[offset + 6] = axes[i].ErrorCode;

            if (axes[i].IsReady)
                AddAxisFeedback((int)AxisFeedback.READY_Axis1 << i);
            else
                RemoveAxisFeedback(1 << i);

            if (axes[i].IsError)
                AddSystemFeedback((int)SystemFeedback.ERROR_Axis1 << i);
            else
                RemoveSystemFeedback((int)SystemFeedback.ERROR_Axis1 << i);

            if (axes[i].IsBusy)
                AddSystemFeedback((int)SystemFeedback.BUSY_Axis1 << i);
            else
                RemoveSystemFeedback((int)SystemFeedback.BUSY_Axis1 << i);

            if (axes[i].InPosition)
                AddAxisFeedback((int)AxisFeedback.InPosition_Axis1 << i);
            else
                RemoveAxisFeedback((int)AxisFeedback.InPosition_Axis1 << i);
        }

        MXRequester.Get.AddSetDeviceRequest(_xSystemFeedbackAddr, (short)_xSystemFeedback, null);
        MXRequester.Get.AddSetDeviceRequest(_xAxisFeedbackAddr, (short)_xAxisFeedback, null);
        MXRequester.Get.AddBufferWrite(_startIO_Slot, monitorAreaStartAddr, _writeBufferCache, OnWriteCompleted);
    }

    private void OnWriteCompleted(bool success)
    {
        _isCommunicating = false;
    }
    #endregion

    public void AddSystemFeedback(int flags)
    {
        _xSystemFeedback |= (SystemFeedback)flags;
    }
    public void RemoveSystemFeedback(int flags)
    {
        _xSystemFeedback &= ~(SystemFeedback)flags;
    }

    public void AddAxisFeedback(int flags)
    {
        _xAxisFeedback |= (AxisFeedback)flags;
    }
    public void RemoveAxisFeedback(int flags)
    {
        _xAxisFeedback &= ~(AxisFeedback)flags;
    }
}