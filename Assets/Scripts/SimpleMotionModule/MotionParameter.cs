using UnityEngine;
using System;

#region Enums
public enum ActuatorType { Linear, Rotary }
public enum UnitType { MM, Degree, Pulse }
public enum HomingType { DoNotRetry, Retry }
public enum OperationPattern { End = 0, Continuos, Location }
public enum ControlMethodType { None = 0, ABS_Linear1, INC_Linear1 }

public enum AxisState
{
    Off, Standby, Positioning, Jogging, Homing, Error
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
#endregion