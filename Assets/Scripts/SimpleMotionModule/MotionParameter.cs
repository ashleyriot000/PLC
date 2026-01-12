using UnityEngine;
using System;

#region Enums
public enum ActuatorType { Linear, Rotary }
public enum UnitType { MM, Degree, Pulse }
public enum HomingType { DoNotRetry, Retry }
public enum OperationPattern { End = 0, Continuos, Location }
public enum ControlMethodType { ABS_Linear1 = 0, INC_Linear1 }

public enum AxisState
{
    Standby, Positioning, Jogging, Homing, Error
}

public enum MotionError
{
    None = 0,
    OverSpeed = 101,
    DriveNotReady = 102,
    HomingTimeout = 103,
    SoftwareStrokeLimit = 104,
    HardwareStrokeLimit = 105,
    StartDuringOperation = 201
}
#endregion

#region Structs & Classes
[Serializable]
public struct PositioningData
{
    public int stepNo;                                      //스텝 No.
    public OperationPattern pattern;                 //포지셔닝 패턴
    public ControlMethodType controlMethod;    //제어 방식
    public double accelTime;                            //가속시간(ms) => 초(/1000)
    public double decelTime;                            //감속시간(ms) => 초(/1000)
    public double posAddress;                           //목적위치(um) => m(/unitMagnification/1000)
    public double arcAddress;                           //아크위치(um) => m(/unitMagnification/1000)
    public double commandSpeed;                    //지령속도(mm/분) => m/초(/60000)
    public double dwellTime;                            //대기시간(ms) => 초(/1000)
    public double mCode;
}

[Serializable]
public class MotionParameter
{
    [Header("Unit System")]    
    public int unitMagnification = 1; //단위 배율 - 1(0.1um:x10), 10(1um), 100(10um:/10), 1000(100um:/100)

    public double UnitMultiplier
    {
        get
        {
            return 10.0d / unitMagnification;
        }
    }

    [Header("Basic Parameter")]
    public ActuatorType actuatorType = ActuatorType.Linear;     //액츄에이터 타입
    public UnitType usedUnit = UnitType.MM;                     //기본 단위
    public double motorResolution = 4194304d;                 //분해능
    public double ballscrewLead = 2000.0;                        //1회전당 전진 길이(um)
    public double gearRatio = 1.0d;                                  //기어비
    public double speedLimit = 2000.0d;                      //최대 스피드(mm/분) -> m/초(/60000)

    [Header("Detail Parameter")]
    public double inPosWidth = 10.0d;                               //도착 허용 범위(um) -> m(/1000000)
    public double jogSpeedLimit = 2000.0d;                       //JOG 최대 스피드(mm/분) -> m/초(/60000)
    public double jogAccelTime = 500d;                              //JOG 가속속도(ms) -> 초(/1000)
    public double jogdecelTime = 500d;                              //JOG 감속속도(ms) -> 초(/1000)

    [Header("HPR Parameter")]
    public int defaultHomingDirection = 1;                          //기본 원점 복귀 방향 1:정방향, -1:역방향
    public double homingHighSpeed = 2000.0d;                 //원점 복귀 최대 속도(mm/분) -> m/초(/60000)
    public double homingCreepSpeed = 2000.0d;               //원점 복귀 정밀 속도(mm/분) -> m/초(/60000)
    public double homingAccelTime = 1000d;                     //원점 복귀 가속 시간(ms) -> 초(/1000)
    public double homingDecelTime = 1000d;                     //원점 복귀 감속 시간(ms) -> 초(/1000)

    public double GetPulseRatio()
    {
        if (usedUnit == UnitType.Pulse) return 1.0d;
        double limitVal = ballscrewLead;
        if (actuatorType == ActuatorType.Rotary && usedUnit == UnitType.Degree) limitVal = 360.0d;
        return (motorResolution * gearRatio) / limitVal;
    }
}
#endregion