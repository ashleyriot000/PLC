using UnityEngine;
using System;

#region Enums
// 액추에이터 구동 방식
public enum ActuatorType { Linear, Rotary }

// 사용 단위
public enum UnitType { MM, Degree, Pulse }

// 원점 복귀 재시도 방식
public enum HomingType { DoNotRetry, Retry }

// 포지셔닝 동작 패턴
public enum OperationPattern { End = 0, Continuos, Location }

// 제어 방식 (절대/상대)
public enum ControlMethodType { ABS_Linear1 = 0, INC_Linear1 }

// 서보 앰프 운전 상태
public enum AxisState
{
    Standby,        // 대기 (Servo ON)
    Positioning,    // 위치결정 운전 중
    Jogging,        // JOG 운전 중
    Homing,         // 원점복귀 중
    Error           // 에러 발생 (Servo OFF/정지)
}

// 에러 및 경고 코드 (QD77MS 호환)
public enum MotionError
{
    None = 0,

    // --- Critical Errors (운전 즉시 정지) ---
    OverSpeed = 101,            // 과속
    DriveNotReady = 102,        // 드라이브 준비 안됨
    HomingTimeout = 103,        // 원점 복귀 타임아웃
    SoftwareStrokeLimit = 104,  // 소프트웨어 리미트 초과
    HardwareStrokeLimit = 105,  // 하드웨어 리미트(FLS/RLS) 감지

    // --- Warnings (운전 계속) ---
    StartDuringOperation = 201  // 운전 중 기동 요청 (명령 무시됨)
}
#endregion

#region Structs & Classes
[Serializable]
public struct PositioningData
{
    public int stepNo;              // 데이터 번호 (1~600)
    public OperationPattern pattern;
    public ControlMethodType controlMethod;
    public double accelTime;        // 가속 시간 (ms)
    public double decelTime;        // 감속 시간 (ms)
    public double posAddress;       // 목표 주소 (Unit)
    public double commandSpeed;     // 지령 속도
    public double dwellTime;        // 드웰 타임 (ms)
    public double mCode;            // M코드
}

[Serializable]
public class MotionParameter
{
    [Header("액츄에이터 동작 방식")]
    public ActuatorType actuatorType = ActuatorType.Linear;

    [Header("Basic Parameter")]
    public UnitType usedUnit = UnitType.MM;
    public double motorResolution = 131072d; // 모터 분해능 (Pulse/Rev)
    public double ballscrewLead = 10d;       // 리드 (mm) 또는 1회전 각도
    public double gearRatio = 1.0d;          // 기어비
    public double speedLimit = 2000.0d;      // 속도 제한 (Unit/min)

    public double[] accelTimes = new double[4] { 1000d, 1000d, 1000d, 1000d };
    public double[] decelTimes = new double[4] { 1000d, 1000d, 1000d, 1000d };

    [Header("Detail Parameter")]
    public double inPosWidth = 0.01d;        // 인포지션 폭 (완료 판정 범위)
    public double jogSpeedLimit = 200d;      // JOG 속도 제한
    public double jogAccelTime = 500d;       // JOG 가속 시간
    public double jogdecelTime = 500d;       // JOG 감속 시간
    public bool useForceStop = false;
    public double inPositionDuration = 0.1d; // 도달 유지 시간

    [Header("HPR(원점 복귀) 파라미터")]
    public HomingType homingType = HomingType.DoNotRetry;
    public int defaultHomingDirection = 1;   // 1: 정방향, -1: 역방향
    public double homingHighSpeed = 200.0d;  // 고속 이동 속도
    public double homingCreepSpeed = 20.0d;  // 크리프 속도 (정밀)
    public double homingAccelTime = 1000d;
    public double homingDecelTime = 1000d;

    /// <summary>
    /// 단위 변환 비율 계산 (Unit -> Pulse)
    /// </summary>
    public double GetPulseRatio()
    {
        if (usedUnit == UnitType.Pulse) return 1.0d;

        double limitVal = ballscrewLead;

        // 로터리 타입이고 단위가 도(Degree)인 경우 1회전 = 360도로 계산
        if (actuatorType == ActuatorType.Rotary && usedUnit == UnitType.Degree)
        {
            limitVal = 360.0d;
        }

        // 공식: (분해능 * 기어비) / (1회전 이동량)
        return (motorResolution * gearRatio) / limitVal;
    }
}
#endregion