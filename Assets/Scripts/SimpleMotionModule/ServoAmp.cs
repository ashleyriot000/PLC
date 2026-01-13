using UnityEngine;
using System;
using System.Collections.Generic;
using System.Linq;

public class ServoAmp : MonoBehaviour
{
    #region Custom Struck
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
    #endregion

    #region Hardware Setup
    [Header("Physics Components")]
    public Rigidbody movingPart;
    public ConfigurableJoint driveJoint;

    [Header("Axis Settings")]
    public int axisNo = 1;
    [Tooltip("PLC 단위 배율 => x1(0.1um), x10(1um), x100(10um), x1000(100um)")]
    [SerializeField] private int unitMagnification = 1; //단위 배율 - x1(0.1um), x10(1um), x100(10um), x1000(100um)
    [SerializeField] private ActuatorType actuatorType = ActuatorType.Linear;     //액츄에이터 타입
    [SerializeField] private UnitType usedUnit = UnitType.MM;                     //기본 단위
    [SerializeField] private double motorResolution = 4194304d;                 //분해능
    [SerializeField] private double ballscrewLead = 2000.0;                        //1회전당 전진 길이(um)
    [SerializeField] private double gearRatio = 1.0d;                                  //기어비
    [SerializeField] private double speedLimit = 2000.0d;                             //최대 스피드(mm/분) -> m/초(/60000)
    [SerializeField] private double inPosWidth = 10.0d;                               //도착 허용 범위(um) -> m(/1000000)
    [SerializeField] private double jogSpeedLimit = 2000.0d;                       //JOG 최대 스피드(mm/분) -> m/초(/60000)
    [SerializeField] private double jogAccelTime = 500d;                              //JOG 가속속도(ms) -> 초(/1000)
    [SerializeField] private double jogdecelTime = 500d;                              //JOG 감속속도(ms) -> 초(/1000)
    [SerializeField] private int defaultHomingDirection = 1;                          //기본 원점 복귀 방향 1:정방향, -1:역방향
    [SerializeField] private double homingHighSpeed = 2000.0d;                 //원점 복귀 최대 속도(mm/분) -> m/초(/60000)
    [SerializeField] private double homingCreepSpeed = 2000.0d;               //원점 복귀 정밀 속도(mm/분) -> m/초(/60000)
    [SerializeField] private double homingAccelTime = 1000d;                     //원점 복귀 가속 시간(ms) -> 초(/1000)
    [SerializeField] private double homingDecelTime = 1000d;                     //원점 복귀 감속 시간(ms) -> 초(/1000)
    #endregion    

    #region 2. State
    [Header("Status Monitor")]
    [SerializeField] private bool _isServoOn = false;                                       //서보 준비 상태
    [SerializeField] private AxisState _currentState = AxisState.Off;               //현재 서보 상태
    [SerializeField] private MotionError _lastError = MotionError.None;           //최근 에러
    [SerializeField] private int _currentPositionRaw = 0;                               //현재 위치(PLC기준)
    [SerializeField] private int _currentSpeedRaw = 0;                                 //현재 속도(PLC기준)
    [SerializeField] private List<PositioningData> positioningDataList = new();

    // 내부 물리 연산용 (mm 단위)
    private double _unitMultiplier = 0d;
    private double _currentPositionMM = 0d;
    private double _homingPositionOffset = 0d;
    private double _currentVelocityMM = 0d;

    private double _commandPositionMM = 0d;
    private double _finalTargetPosMM = 0d;
    private double _targetSpeedMM = 0d;

    private double _activeAccelTime = 0d;
    private double _activeDecelTime = 0d;
    private int _homingSequenceStep = 0;

    private bool _isOnFLS = false;
    private bool _isOnRLS = false;
    private bool _isOnDOG = false;
    #endregion

    #region Properties
    public int CurrentPulse => _currentPositionRaw;
    public double CurrentPosition => _currentPositionMM + _homingPositionOffset;
    public bool IsBusy => _currentState != AxisState.Standby && _currentState != AxisState.Error;
    public bool IsError => _currentState == AxisState.Error;
    public short ErrorCode => (short)_lastError;
    public bool IsServoOn => _isServoOn;
    public double RawToMeter(double rawValue) => rawValue * _unitMultiplier * 0.001d;

    

    public bool IsOnFLS
    {
        get => _isOnFLS;
        set
        {
            _isOnFLS = value;
        }
        //if (isOn && _currentVelocityMM > 0)
        //    RaiseError(MotionError.HardwareStrokeLimit);
    }
    public bool IsOnRLS
    {
        get => _isOnRLS;
        set
        {
            _isOnRLS = value;
        }
        //if (isOn && _currentVelocityMM < 0)
        //    RaiseError(MotionError.HardwareStrokeLimit);
    }
    public bool IsOnDOG
    {
        get => _isOnDOG;
        set
        {
            _isOnDOG = value;
        }
    }
    #endregion

    #region 4. Unity Methods
    private void Awake()
    {
        _unitMultiplier = 10000.0d / unitMagnification;
        if (movingPart == null) 
            movingPart = GetComponent<Rigidbody>();

        if(movingPart != null)
        {
            movingPart.automaticCenterOfMass = false;
            movingPart.automaticInertiaTensor = false;
            movingPart.mass = 10f;
            movingPart.collisionDetectionMode = CollisionDetectionMode.Continuous;
            movingPart.interpolation = RigidbodyInterpolation.Interpolate;
        }

        if (driveJoint == null) 
            driveJoint = GetComponent<ConfigurableJoint>();

        if (driveJoint != null)
        {
            driveJoint.secondaryAxis = -Vector3.up;

            driveJoint.xMotion = actuatorType == ActuatorType.Linear ? 
                ConfigurableJointMotion.Free : ConfigurableJointMotion.Locked;

            driveJoint.yMotion = ConfigurableJointMotion.Locked;
            driveJoint.zMotion = ConfigurableJointMotion.Locked;

            driveJoint.angularXMotion = actuatorType == ActuatorType.Rotary ?
                ConfigurableJointMotion.Free : ConfigurableJointMotion.Locked;
            driveJoint.angularYMotion = ConfigurableJointMotion.Locked;
            driveJoint.angularZMotion = ConfigurableJointMotion.Locked;

        }

        // 런타임 초기화
        SetServoOn(false);        
    }    

    private void FixedUpdate()
    {
        // 1. 상태 업데이트
        _currentPositionMM = GetPhysicalPositionMM();
        _currentPositionRaw = MMToRaw(_currentPositionMM);
        _currentSpeedRaw = MMToRaw(_currentVelocityMM * 60);

        // 2. 센서 체크
        CheckHardwareLimits();

        // [서보 OFF] 물리 제어 중단 (Free)
        if (!_isServoOn) return;

        // 3. 에러 시 정지
        if (_currentState == AxisState.Error)
        {
            _targetSpeedMM = 0;
            _commandPositionMM = _currentPositionMM;
            ApplyPhysicsTarget(_commandPositionMM);
            return;
        }

        // 4. 동작 로직
        if (_currentState == AxisState.Homing) UpdateHomingLogic();
        else if (_currentState == AxisState.Positioning || _currentState == AxisState.Jogging) UpdateProfileLogic();
        else ApplyPhysicsTarget(_commandPositionMM);
    }
    #endregion

    #region 5. Control Signals (System)
    public void SetServoOn(bool isOn)
    {
        if (_isServoOn == isOn) return;

        if (_isServoOn = isOn)
        {
            Debug.Log($"[Axis {axisNo}] Servo ON");
            _commandPositionMM = _currentPositionMM; // 켜지는 순간 위치 고정
            _currentState = AxisState.Standby;
            _lastError = MotionError.None;
            SetupJointPhysics(100000f);
        }
        else
        {
            Debug.Log($"[Axis {axisNo}] Servo OFF");
            _currentState = AxisState.Off;
            _currentPositionMM = GetPhysicalPositionMM();
            _homingPositionOffset = 0f;
            _currentPositionRaw = MMToRaw(_currentPositionMM);
            _commandPositionMM = _currentPositionMM;
            _targetSpeedMM = 0;
            SetupJointPhysics(20f);
        }
    }
    #endregion

    #region 6. Helper Functions
    // [단위 변환] Property 사용 (자동 계산)
    private double RawToMM(double rawValue) => rawValue / _unitMultiplier;
    private int MMToRaw(double mmValue) => (int)(mmValue * _unitMultiplier);
    private double MmToUm(double mmValue) => mmValue * 1000d;
    private double UmToMm(double umValue) => umValue * 0.001d;
    private double ToMeterPerSeconds(double MmPerMin) => MmPerMin * 60000d;

    //전자기어비 구하는 메서드(1펄스당 이동거리 혹은 이동각)
    public double GetPulseRatio()
    {
        if (usedUnit == UnitType.Pulse) return 1.0d;
        double limitVal = ballscrewLead;
        if (actuatorType == ActuatorType.Rotary && usedUnit == UnitType.Degree) limitVal = 360.0d;
        return (motorResolution * gearRatio) / limitVal;
    }

    private double GetPhysicalPositionMM()
    {
        return (actuatorType == ActuatorType.Linear) ? 
            driveJoint.transform.localPosition.x * 1000d : 
            driveJoint.transform.localEulerAngles.x;
    }

    private void ApplyPhysicsTarget(double posMM)
    {
        if (actuatorType == ActuatorType.Linear)
        {
            float unityTarget = (float)(posMM / 1000d);
            driveJoint.targetPosition = new Vector3(unityTarget, 0, 0);
        }
        else
        {
            driveJoint.targetRotation = Quaternion.Euler((float)posMM, 0, 0);
        }
    }

    private void SetupJointPhysics(float spring)
    {
        JointDrive drive = new JointDrive 
        { 
            positionSpring = spring, 
            positionDamper = 1000f, 
            maximumForce = float.MaxValue 
        };

        if (actuatorType == ActuatorType.Linear) 
            driveJoint.xDrive = drive;
        else 
            driveJoint.angularXDrive = drive;
    }
    #endregion

    #region 7. Motion Logic
    private void CheckHardwareLimits()
    {
        if (_targetSpeedMM > 0 && _isOnFLS) RaiseError(MotionError.HardwareStrokeLimit);
        if (_targetSpeedMM < 0 && _isOnRLS) RaiseError(MotionError.HardwareStrokeLimit);
    }

    private void UpdateProfileLogic()
    {
        double targetSpeedSec = _targetSpeedMM / 60d;
        double speedLimitSec = (speedLimit * _unitMultiplier) / 60d;

        if (_currentState == AxisState.Positioning)
        {
            double distToEnd = Math.Abs(_finalTargetPosMM - _commandPositionMM);
            double decelRate = speedLimitSec / (Math.Max(_activeDecelTime, 1) / 1000d);
            double stoppingDist = (_currentVelocityMM * _currentVelocityMM) / (2 * decelRate);
            if (distToEnd <= stoppingDist) targetSpeedSec = 0;
        }

        double accelStep = (speedLimitSec / (Math.Max(_activeAccelTime, 1) / 1000d)) * Time.fixedDeltaTime;
        double decelStep = (speedLimitSec / (Math.Max(_activeDecelTime, 1) / 1000d)) * Time.fixedDeltaTime;
        double maxChange = (Math.Abs(targetSpeedSec) > Math.Abs(_currentVelocityMM)) ? accelStep : decelStep;

        _currentVelocityMM = Mathf.MoveTowards((float)_currentVelocityMM, (float)targetSpeedSec, (float)maxChange);

        double inPosMM = inPosWidth * _unitMultiplier;
        if (_currentState == AxisState.Positioning &&
            Math.Abs(_finalTargetPosMM - _commandPositionMM) < inPosMM &&
            Math.Abs(_currentVelocityMM) < 0.01d)
        {
            _commandPositionMM = _finalTargetPosMM;
            _currentVelocityMM = 0;
            _currentState = AxisState.Standby;
        }
        else
        {
            _commandPositionMM += _currentVelocityMM * Time.fixedDeltaTime;
        }
        ApplyPhysicsTarget(_commandPositionMM);
    }

    private void UpdateHomingLogic()
    {
        double highSpeed = ToMeterPerSeconds(homingHighSpeed);
        double creepSpeed = ToMeterPerSeconds(homingCreepSpeed);
        int dir = defaultHomingDirection;

        switch (_homingSequenceStep)
        {
            case 0:
                _targetSpeedMM = highSpeed * dir;
                UpdateVelocityAndPos(highSpeed * dir, homingAccelTime);
                if (_isOnDOG) _homingSequenceStep = 1;
                break;
            case 1:
                _targetSpeedMM = creepSpeed * dir;
                UpdateVelocityAndPos(creepSpeed * dir, homingDecelTime);
                if (!_isOnDOG) _homingSequenceStep = 2;
                break;
            case 2:
                _targetSpeedMM = 0;
                double limitSec = ToMeterPerSeconds(speedLimit);
                double stopDecel = (limitSec / 0.05d) * Time.fixedDeltaTime;
                _currentVelocityMM = Mathf.MoveTowards((float)_currentVelocityMM, 0f, (float)stopDecel);
                _commandPositionMM += _currentVelocityMM * Time.fixedDeltaTime;
                if (Math.Abs(_currentVelocityMM) < 0.01d)
                {
                    _currentVelocityMM = 0; _commandPositionMM = 0; ApplyPhysicsTarget(0);
                    _currentState = AxisState.Standby;
                }
                break;
        }
        ApplyPhysicsTarget(_commandPositionMM);
    }

    private void UpdateVelocityAndPos(double targetVelSec, double timeMs)
    {
        double limitSec = ToMeterPerSeconds(speedLimit);
        double step = (limitSec / (timeMs / 1000d)) * Time.fixedDeltaTime;
        _currentVelocityMM = Mathf.MoveTowards((float)_currentVelocityMM, (float)targetVelSec, (float)step);
        _commandPositionMM += _currentVelocityMM * Time.fixedDeltaTime;
    }

    
    #endregion

    #region 8. Commands (External)
    public void StartPositioning(int stepNo)
    {
        if (!_isServoOn) { RaiseError(MotionError.DriveNotReady); return; }
        if (IsError) return;

        if (stepNo == 9001)
        {
            StartHoming();
            return;
        }

        var data = positioningDataList.FirstOrDefault(x => x.stepNo == stepNo);
        if (data.stepNo == 0) { RaiseError(MotionError.DriveNotReady); return; }

        _currentState = AxisState.Positioning;

        double scaledPos = data.posAddress * _unitMultiplier;
        double scaledSpeed = data.commandSpeed * _unitMultiplier;
        double scaledLimit = speedLimit * _unitMultiplier;

        Debug.Log($"[Axis {axisNo}] Start Pos No.{stepNo} (Raw:{data.posAddress} -> MM:{scaledPos:F3})");

        _finalTargetPosMM = (data.controlMethod == ControlMethodType.INC_Linear1)
            ? _commandPositionMM + scaledPos : scaledPos;

        double cmdSpeed = Math.Min(scaledSpeed, scaledLimit);
        _targetSpeedMM = (_finalTargetPosMM > _commandPositionMM) ? cmdSpeed : -cmdSpeed;
        _activeAccelTime = data.accelTime;
        _activeDecelTime = data.decelTime;
    }

    public void ProcessJog(bool fwdOn, bool revOn)
    {
        if (!_isServoOn || IsError)
        {
            if (_currentState == AxisState.Jogging) StopJog();
            return;
        }

        if (fwdOn && revOn) StopJog();
        else if (fwdOn) { if (_currentState != AxisState.Jogging || _targetSpeedMM <= 0) StartJog(true); }
        else if (revOn) { if (_currentState != AxisState.Jogging || _targetSpeedMM >= 0) StartJog(false); }
        else { if (_currentState == AxisState.Jogging) StopJog(); }
    }

    private void StartHoming()
    {
        Debug.Log($"[Axis {axisNo}] Start Homing (9001)");
        _currentState = AxisState.Homing;
        _homingSequenceStep = 0;
        _currentVelocityMM = 0;
    }

    private void StartJog(bool isForward)
    {
        _currentState = AxisState.Jogging;
        double scaledJogSpeed = jogSpeedLimit;
        _targetSpeedMM = isForward ? scaledJogSpeed : -scaledJogSpeed;
        _activeAccelTime = jogAccelTime;
        _activeDecelTime = jogdecelTime;
    }

    private void StopJog() { if (_currentState == AxisState.Jogging) _targetSpeedMM = 0; }
    public void RaiseError(MotionError e) { _lastError = e; _currentState = AxisState.Error; Debug.LogError($"[Axis {axisNo}] Error: {e}"); }

    #endregion

    // [Unity Editor Magic] OnValidate: 인스펙터 값이 변경될 때 호출됨
#if UNITY_EDITOR
    private void OnValidate()
    {
        if (unitMagnification >= 1000)
            unitMagnification = 1000;
        else if (unitMagnification >= 100)
            unitMagnification = 100;
        else if (unitMagnification >= 10)
            unitMagnification = 10;
        else
            unitMagnification = 1;

        _unitMultiplier = 10000.0d / unitMagnification;
    }
#endif
}