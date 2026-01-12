using UnityEngine;
using System;
using System.Collections.Generic;
using System.Linq;

public class ServoAmp : MonoBehaviour
{
    #region 1. Hardware Setup
    [Header("Physics Components")]
    public Rigidbody movingPart;
    public ConfigurableJoint driveJoint;

    [Header("Sensors")]
    public MagneticSensor sensorFLS;
    public MagneticSensor sensorRLS;
    public MagneticSensor sensorDOG;

    [Header("Axis Settings")]
    public int axisNo = 1;
    public MotionParameter parameter = new ();
    public List<PositioningData> positioningDataList = new();
    #endregion

    #region 2. Internal State
    [Header("Status Monitor")]
    [SerializeField] private bool _isServoOn = false; // 서보 상태
    [SerializeField] private AxisState _currentState = AxisState.Standby;
    [SerializeField] private MotionError _lastError = MotionError.None;

    [SerializeField] private int _currentPositionRaw = 0;
    [SerializeField] private int _currentSpeedRaw = 0;

    // 내부 물리 연산용 (mm 단위)
    private double _currentPositionMM = 0d;
    private double _currentVelocityMM = 0d;

    private double _commandPositionMM = 0d;
    private double _finalTargetPosMM = 0d;
    private double _targetSpeedMM = 0d;

    private double _activeAccelTime = 0d;
    private double _activeDecelTime = 0d;
    private int _homingSequenceStep = 0;

    // 센서 상태 캐싱
    private bool _signalFLS = false;
    private bool _signalRLS = false;
    private bool _signalDOG = false;
    #endregion

    #region 3. Properties
    public int CurrentPulse => _currentPositionRaw;
    public bool IsBusy => _currentState != AxisState.Standby && _currentState != AxisState.Error;
    public bool IsError => _currentState == AxisState.Error;
    public short ErrorCode => (short)_lastError;
    public bool IsServoOn => _isServoOn;
    #endregion

    #region 4. Unity Methods
    private void Awake()
    {
        if (movingPart == null) 
            movingPart = GetComponent<Rigidbody>();

        if(movingPart != null)
        {
            movingPart.automaticCenterOfMass = false;
            movingPart.automaticInertiaTensor = false;
        }


        if (driveJoint == null) 
            driveJoint = GetComponent<ConfigurableJoint>();

        if(driveJoint != null)
        {
            driveJoint.xMotion = parameter.actuatorType == ActuatorType.Linear ? 
                ConfigurableJointMotion.Free : ConfigurableJointMotion.Locked;

            driveJoint.yMotion = ConfigurableJointMotion.Locked;
            driveJoint.zMotion = ConfigurableJointMotion.Locked;

            driveJoint.angularXMotion = parameter.actuatorType == ActuatorType.Rotary ?
                ConfigurableJointMotion.Free : ConfigurableJointMotion.Locked;
            driveJoint.angularYMotion = ConfigurableJointMotion.Locked;
            driveJoint.angularZMotion = ConfigurableJointMotion.Locked;
        }

        UnlockJointMotion();
        SetupJointPhysics();

        // 런타임 초기화
        _currentPositionMM = GetPhysicalPositionMM();
        _currentPositionRaw = MMToRaw(_currentPositionMM);
        _commandPositionMM = _currentPositionMM;
    }

    private void Start()
    {
        if (sensorFLS != null) sensorFLS.onChangedDetect.AddListener(OnChangedFLS);
        if (sensorRLS != null) sensorRLS.onChangedDetect.AddListener(OnChangedRLS);
        if (sensorDOG != null) sensorDOG.onChangedDetect.AddListener(OnChangedDOG);
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

        _isServoOn = isOn;
        if (_isServoOn)
        {
            Debug.Log($"[Axis {axisNo}] Servo ON");
            _commandPositionMM = _currentPositionMM; // 켜지는 순간 위치 고정
            _currentState = AxisState.Standby;
            _lastError = MotionError.None;
            if (movingPart != null) movingPart.isKinematic = false;
        }
        else
        {
            Debug.Log($"[Axis {axisNo}] Servo OFF");
            _currentState = AxisState.Standby;
            _targetSpeedMM = 0;
        }
    }
    #endregion

    #region 6. Helper Functions
    // [단위 변환] Property 사용 (자동 계산)
    private double RawToMM(double rawValue) => rawValue * parameter.UnitMultiplier;

    private int MMToRaw(double mmValue)
    {
        if (parameter.UnitMultiplier <= 1e-9) return 0;
        return (int)(mmValue / parameter.UnitMultiplier);
    }

    private double GetPhysicalPositionMM()
    {
        if (driveJoint == null) return 0;
        float val = (parameter.actuatorType == ActuatorType.Linear) ?
            driveJoint.transform.localPosition.x : driveJoint.transform.localEulerAngles.x;
        return (parameter.actuatorType == ActuatorType.Linear) ? val * 1000d : val;
    }

    private void ApplyPhysicsTarget(double posMM)
    {
        if (driveJoint == null) return;
        if (movingPart.IsSleeping()) movingPart.WakeUp();

        if (parameter.actuatorType == ActuatorType.Linear)
        {
            float unityTarget = (float)(posMM / 1000d);
            driveJoint.targetPosition = new Vector3(unityTarget, 0, 0);
        }
        else
        {
            driveJoint.targetRotation = Quaternion.Euler((float)posMM, 0, 0);
        }
    }

    private void UnlockJointMotion()
    {
        if (driveJoint == null) return;
        if (parameter.actuatorType == ActuatorType.Linear)
        {
            driveJoint.xMotion = ConfigurableJointMotion.Free;
            driveJoint.yMotion = ConfigurableJointMotion.Locked;
            driveJoint.zMotion = ConfigurableJointMotion.Locked;
            driveJoint.angularXMotion = ConfigurableJointMotion.Locked;
            driveJoint.angularYMotion = ConfigurableJointMotion.Locked;
            driveJoint.angularZMotion = ConfigurableJointMotion.Locked;
        }
        else
        {
            driveJoint.angularXMotion = ConfigurableJointMotion.Free;
            driveJoint.xMotion = ConfigurableJointMotion.Locked;
        }
        if (movingPart != null) movingPart.isKinematic = false;
    }

    private void SetupJointPhysics()
    {
        if (driveJoint == null) return;
        JointDrive drive = new JointDrive { positionSpring = 1000000f, positionDamper = 1000f, maximumForce = float.MaxValue };
        if (parameter.actuatorType == ActuatorType.Linear) driveJoint.xDrive = drive;
        else driveJoint.angularXDrive = drive;
    }
    #endregion

    #region 7. Motion Logic
    private void CheckHardwareLimits()
    {
        if (_targetSpeedMM > 0 && _signalFLS) RaiseError(MotionError.HardwareStrokeLimit);
        if (_targetSpeedMM < 0 && _signalRLS) RaiseError(MotionError.HardwareStrokeLimit);
    }

    private void UpdateProfileLogic()
    {
        double targetSpeedSec = _targetSpeedMM / 60d;
        double speedLimitSec = (parameter.speedLimit * parameter.UnitMultiplier) / 60d;

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

        double inPosMM = parameter.inPosWidth * parameter.UnitMultiplier;
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
        double highSpeed = parameter.homingHighSpeed * parameter.UnitMultiplier;
        double creepSpeed = parameter.homingCreepSpeed * parameter.UnitMultiplier;
        double highSpeedSec = highSpeed / 60d;
        double creepSpeedSec = creepSpeed / 60d;
        int dir = parameter.defaultHomingDirection;

        switch (_homingSequenceStep)
        {
            case 0:
                _targetSpeedMM = highSpeed * dir;
                UpdateVelocityAndPos(highSpeedSec * dir, parameter.homingAccelTime);
                if (_signalDOG) _homingSequenceStep = 1;
                break;
            case 1:
                _targetSpeedMM = creepSpeed * dir;
                UpdateVelocityAndPos(creepSpeedSec * dir, parameter.homingDecelTime);
                if (!_signalDOG) _homingSequenceStep = 2;
                break;
            case 2:
                _targetSpeedMM = 0;
                double limitSec = (parameter.speedLimit * parameter.UnitMultiplier) / 60d;
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
        double limitSec = (parameter.speedLimit * parameter.UnitMultiplier) / 60d;
        double step = (limitSec / (timeMs / 1000d)) * Time.fixedDeltaTime;
        _currentVelocityMM = Mathf.MoveTowards((float)_currentVelocityMM, (float)targetVelSec, (float)step);
        _commandPositionMM += _currentVelocityMM * Time.fixedDeltaTime;
    }

    public void OnChangedFLS(bool d) { _signalFLS = d; if (d && _currentVelocityMM > 0) RaiseError(MotionError.HardwareStrokeLimit); }
    public void OnChangedRLS(bool d) { _signalRLS = d; if (d && _currentVelocityMM < 0) RaiseError(MotionError.HardwareStrokeLimit); }
    public void OnChangedDOG(bool d) { _signalDOG = d; }
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

        double scaledPos = data.posAddress * parameter.UnitMultiplier;
        double scaledSpeed = data.commandSpeed * parameter.UnitMultiplier;
        double scaledLimit = parameter.speedLimit * parameter.UnitMultiplier;

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
        double scaledJogSpeed = parameter.jogSpeedLimit * parameter.UnitMultiplier;
        _targetSpeedMM = isForward ? scaledJogSpeed : -scaledJogSpeed;
        _activeAccelTime = parameter.jogAccelTime;
        _activeDecelTime = parameter.jogdecelTime;
    }

    private void StopJog() { if (_currentState == AxisState.Jogging) _targetSpeedMM = 0; }
    public void RaiseError(MotionError e) { _lastError = e; _currentState = AxisState.Error; Debug.LogError($"[Axis {axisNo}] Error: {e}"); }
    #endregion

    // [Unity Editor Magic] OnValidate: 인스펙터 값이 변경될 때 호출됨
#if UNITY_EDITOR
    private void OnValidate()
    {
        // MotionParameter는 MonoBehaviour가 아니므로 자동 호출되지 않음.
        // ServoAmp가 변화를 감지하여 parameter의 값을 강제로 보정(Snap)함.
        if (parameter != null)
        {
            if (parameter.unitMagnification >= 1000)
                parameter.unitMagnification = 1000;
            else if (parameter.unitMagnification >= 100)
                parameter.unitMagnification = 100;
            else if (parameter.unitMagnification >= 10)
                parameter.unitMagnification = 10;
            else
                parameter.unitMagnification = 1;
        }
    }
#endif
}