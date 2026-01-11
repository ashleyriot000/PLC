using UnityEngine;
using System;
using System.Collections.Generic;

public static class QD77MSAddress
{
    public static int Da_Start(int axisNo) => 2000 + (axisNo - 1) * 6000;
    public static int Md_FeedCurrentValue(int axisNo) => 800 + (axisNo - 1) * 100;
    public static int Md_AxisStatus(int axisNo) => 817 + (axisNo - 1) * 100;
    public static int Md_ErrorNo(int axisNo) => 806 + (axisNo - 1) * 100;
    public static int Cd_PositioningStartNo(int axisNo) => 1500 + (axisNo - 1) * 100;
}

public class SimpleMotionModule : MXObject
{
    #region Settings
    [Header("Network Settings")]
    [Tooltip("모듈의 선두 I/O 번호 (Hex String, 예: 00, 20, 50)")]
    public string startIO_Hex = "00";
    private int _startIO_Slot;

    [Header("PLC I/O Address")]
    [SerializeField] private string _yStartCmdAddr;
    [SerializeField] private string _xStartAddr;

    [Header("Buffer Memory Settings")]
    public int monitorAreaStartAddr = 800;

    // [수정 완료] 기본값을 1500(Cd 영역)으로 정확하게 설정
    public int commandAreaStartAddr = 1500;

    public int bufferSizePerAxis = 100;

    [Header("Connected Axes")]
    public ServoAmp[] axes;
    #endregion

    #region Internal State
    private bool _isLoopRunning = false;
    private short[] _readBufferCache;
    private short[] _writeBufferCache;

    private int _ySignalCache = 0;

    // [래치] 명령 중복 실행 방지용 플래그
    private bool[] _isCommandExecuted;
    #endregion

    private void Awake()
    {
        // 1. 주소 및 슬롯 계산
        int rawAddress = System.Convert.ToInt32(startIO_Hex, 16);
        _startIO_Slot = rawAddress / 16;

        // Y신호: 선두번지 + 10H (예: 50H -> Y60)
        int yStartInt = rawAddress + 0x10;
        _yStartCmdAddr = "K4Y" + yStartInt.ToString("X");
        _xStartAddr = "K4X" + startIO_Hex;

        Debug.Log($"[Init] Hex:{startIO_Hex} -> Slot:{_startIO_Slot}");
        Debug.Log($"[Addr] Y-Cmd:{_yStartCmdAddr}, BufferStart:{commandAreaStartAddr}");

        // 2. 버퍼 초기화
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
        if (axes.Length > 0)
        {
            Debug.Log(">>> 통신 자동 루프 시작 (Auto Loop + Latch) <<<");
            RequestReadCycle();
        }
    }

    #region Communication Loop (Chain Reaction)

    // 1. 읽기 요청 (Cycle Start)
    private void RequestReadCycle()
    {
        _isLoopRunning = true;
        // Y신호부터 읽기
        MXRequester.Get.AddGetDeviceRequest(_yStartCmdAddr, OnReadYSignal);
    }

    // 2. Y신호 수신 -> 버퍼 읽기
    private void OnReadYSignal(short value)
    {
        _ySignalCache = value;
        // Y신호 수신 후 버퍼 읽기 (순서 보장)
        MXRequester.Get.AddBufferRead(_startIO_Slot, commandAreaStartAddr, _readBufferCache.Length, OnReadBufferCompleted);
    }

    // 3. 버퍼 수신 -> 로직 수행 -> 쓰기 준비
    private void OnReadBufferCompleted(short[] data)
    {
        if (data == null)
        {
            Debug.LogError("통신 실패. 1초 후 재시도.");
            Invoke(nameof(RequestReadCycle), 1.0f);
            return;
        }

        // --- 데이터 처리 ---
        for (int i = 0; i < axes.Length; i++)
        {
            if (axes[i] == null) continue;

            // Y신호 확인 (Bit check)
            bool isYOn = (_ySignalCache & (1 << i)) != 0;

            int offset = i * bufferSizePerAxis;
            int startNo = data[offset + 0];

            // [래치 로직 적용]
            if (isYOn)
            {
                // Y신호는 켜져 있는데, 이번 턴에 아직 실행하지 않았다면?
                if (!_isCommandExecuted[i])
                {
                    if (startNo > 0)
                    {
                        // 데이터가 0보다 클 때만 실행 (안전장치)
                        Debug.Log($"[Axis {i + 1}] 명령 실행! StartNo: {startNo}");
                        ProcessAxisCommand(axes[i], startNo);

                        // 실행 완료 플래그 설정 (Y가 꺼질 때까지 재실행 안함)
                        _isCommandExecuted[i] = true;
                    }
                    else
                    {
                        // Y는 켜졌는데 데이터가 아직 0임 -> 다음 루프까지 대기 (Logs skip to prevent spam)
                    }
                }
            }
            else
            {
                // Y신호가 꺼지면 래치 해제 (다음 명령 대기)
                if (_isCommandExecuted[i])
                {
                    // Debug.Log($"[Axis {i+1}] 래치 해제 (Y OFF)");
                    _isCommandExecuted[i] = false;
                }
            }
        }

        PrepareWriteData();
    }

    private void PrepareWriteData()
    {
        int xSignalBitmap = 0;

        for (int i = 0; i < axes.Length; i++)
        {
            if (axes[i] == null) continue;

            int offset = i * bufferSizePerAxis;
            int currentPulse = axes[i].CurrentPulse;

            // Md.20 (현재 위치), Md.23 (에러) 쓰기
            _writeBufferCache[offset + 0] = (short)(currentPulse & 0xFFFF);
            _writeBufferCache[offset + 1] = (short)((currentPulse >> 16) & 0xFFFF);
            _writeBufferCache[offset + 6] = axes[i].ErrorCode;

            // X 신호 (Ready, Busy, Error)
            if (i == 0) xSignalBitmap |= 1;
            if (axes[i].IsBusy) xSignalBitmap |= (1 << (0x10 + i));
            if (axes[i].IsError) xSignalBitmap |= (1 << (0x18 + i));
        }

        // 쓰기 요청
        MXRequester.Get.AddBufferWrite(_startIO_Slot, monitorAreaStartAddr, _writeBufferCache, null);
        // 마지막 Device 쓰기 후 -> 다시 읽기 요청 (Loop)
        MXRequester.Get.AddSetDeviceRequest(_xStartAddr, (short)xSignalBitmap, OnWriteCycleCompleted);
    }

    // 4. 쓰기 완료 -> 다시 시작 (Recursive)
    private void OnWriteCycleCompleted(bool success)
    {
        // 즉시 다음 사이클 요청 (딜레이 0)
        RequestReadCycle();
    }
    #endregion

    private void ProcessAxisCommand(ServoAmp axis, int startNo)
    {
        // 이미 래치 로직에서 필터링되었으므로 바로 실행
        if (axis.IsBusy) axis.RaiseWarning(MotionError.StartDuringOperation);
        else if (!axis.IsError) axis.StartPositioning(startNo);
    }
}