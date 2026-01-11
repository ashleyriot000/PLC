using UnityEngine;
using System;
using System.Collections.Generic;
using System.Linq;

public class ServoAmp : MXObject
{
    #region Hardware Setup
    [Header("Physics Components")]
    [Tooltip("실제 움직이는 파트의 리지드바디")]
    public Rigidbody movingPartRb;
    [Tooltip("구동을 담당할 조인트")]
    public ConfigurableJoint driveJoint;

    [Header("Sensors (Magnetic Sensor)")]
    [Tooltip("MagneticSensor 스크립트가 붙은 오브젝트를 연결하세요.")]
    public MagneticSensor sensorFLS; // 정방향 리미트 (Upper Limit)
    public MagneticSensor sensorRLS; // 역방향 리미트 (Lower Limit)
    public MagneticSensor sensorDOG; // 근점 도그 (Near Dog)

    [Header("Axis Settings")]
    public int axisNo = 1;
    public MotionParameter parameter = new MotionParameter();
    public List<PositioningData> positioningDataList = new List<PositioningData>();
    #endregion

    #region Internal State
    [Header("Monitoring")]
    [SerializeField] private AxisState _currentState = AxisState.Standby;
    [SerializeField] private MotionError _lastError = MotionError.None;

    // 센서 상태 캐싱
    [SerializeField] private bool _signalFLS = false;
    [SerializeField] private bool _signalRLS = false;
    [SerializeField] private bool _signalDOG = false;

    // 물리적 상태 (User Unit 기준: mm, degree 등)
    [SerializeField] private double _currentPositionUser = 0d;
    [SerializeField] private double _currentVelocityUser = 0d;
    [SerializeField] private int _currentPulse = 0;

    // 제어 프로파일링 변수
    private double _commandPositionUser = 0d; // 내부 목표 위치 (프로파일러 계산값)
    private double _finalTargetPosUser = 0d;  // 최종 목표 위치
    private double _targetSpeedUser = 0d;     // 목표 속도 (UserUnit/min)
    private double _activeAccelTime = 0d;     // 가속 시간 (ms)
    private double _activeDecelTime = 0d;     // 감속 시간 (ms)

    // 원점 복귀 시퀀스 스텝
    private int _homingSequenceStep = 0;
    #endregion

    #region Properties
    public int CurrentPulse => _currentPulse;

    // [IsComplete] 에러 없음 && 이동 중 아님 && 목표 위치 도달 (인포지션 내)
    public bool IsComplete
    {
        get
        {
            if (_currentState == AxisState.Error) return false;
            if (IsBusy) return false;
            return Math.Abs(_finalTargetPosUser - _currentPositionUser) <= parameter.inPosWidth;
        }
    }

    public bool IsBusy => _currentState != AxisState.Standby && _currentState != AxisState.Error;
    public bool IsError => _currentState == AxisState.Error;
    public short ErrorCode => (short)_lastError;

    // 외부 확인용 센서 프로퍼티
    public bool SignalFLS => _signalFLS;
    public bool SignalRLS => _signalRLS;
    public bool SignalDOG => _signalDOG;
    #endregion

    #region Unity Methods
    private void Awake()
    {
        if (movingPartRb == null) movingPartRb = GetComponent<Rigidbody>();
        if (driveJoint == null) driveJoint = GetComponent<ConfigurableJoint>();

        // [중요] 조인트 잠금 해제 (Locked 상태에서는 TargetPosition 작동 안함)
        UnlockJointMotion();

        // 물리 스프링 설정
        SetupJointPhysics();

        // 초기 위치 동기화
        _currentPositionUser = GetPositionUserUnit();
        _commandPositionUser = _currentPositionUser;
    }

    private void Start()
    {
        // MagneticSensor 이벤트 리스너 등록
        if (sensorFLS != null)
        {
            _signalFLS = sensorFLS.HasDetected;
            sensorFLS.onChangedDetect.AddListener(OnChangedFLS);
        }
        if (sensorRLS != null)
        {
            _signalRLS = sensorRLS.HasDetected;
            sensorRLS.onChangedDetect.AddListener(OnChangedRLS);
        }
        if (sensorDOG != null)
        {
            _signalDOG = sensorDOG.HasDetected;
            sensorDOG.onChangedDetect.AddListener(OnChangedDOG);
        }
    }

    private void OnDestroy()
    {
        if (sensorFLS != null) sensorFLS.onChangedDetect.RemoveListener(OnChangedFLS);
        if (sensorRLS != null) sensorRLS.onChangedDetect.RemoveListener(OnChangedRLS);
        if (sensorDOG != null) sensorDOG.onChangedDetect.RemoveListener(OnChangedDOG);
    }

    private void FixedUpdate()
    {
        // 1. 현재 물리 위치 측정 (Unity -> User Unit 변환)
        _currentPositionUser = GetPositionUserUnit();
        _currentPulse = (int)(_currentPositionUser * parameter.GetPulseRatio());

        // 2. 하드웨어 리미트 체크 (인터락)
        CheckHardwareLimits();

        // 3. 에러 발생 시 위치 고수 (급정지)
        if (_currentState == AxisState.Error)
        {
            _targetSpeedUser = 0;
            _commandPositionUser = _currentPositionUser; // 현재 위치 유지
            ApplyPhysicsTarget(_commandPositionUser);
            return;
        }

        // 4. 모션 로직 수행
        if (_currentState == AxisState.Homing)
        {
            UpdateHomingLogic();
        }
        else if (_currentState == AxisState.Positioning || _currentState == AxisState.Jogging)
        {
            UpdateProfileLogic();
        }
        else // Standby
        {
            // 대기 상태에서도 현재 위치 유지를 위해 Target 갱신 (외력 방지)
            ApplyPhysicsTarget(_commandPositionUser);
        }
    }
    #endregion

    #region Sensor Callbacks
    public void OnChangedFLS(bool detected)
    {
        _signalFLS = detected;
        // 정방향 이동 중 센서 감지 시 즉시 에러
        if (_signalFLS && _currentVelocityUser > 0.1d) RaiseError(MotionError.HardwareStrokeLimit);
    }
    public void OnChangedRLS(bool detected)
    {
        _signalRLS = detected;
        // 역방향 이동 중 센서 감지 시 즉시 에러
        if (_signalRLS && _currentVelocityUser < -0.1d) RaiseError(MotionError.HardwareStrokeLimit);
    }
    public void OnChangedDOG(bool detected)
    {
        _signalDOG = detected;
    }
    #endregion

    #region Physics & Unit Conversion
    private void UnlockJointMotion()
    {
        if (driveJoint == null) return;

        if (parameter.actuatorType == ActuatorType.Linear)
        {
            driveJoint.xMotion = ConfigurableJointMotion.Free; // X축 이동 허용
            // 나머지 축 고정
            driveJoint.yMotion = ConfigurableJointMotion.Locked;
            driveJoint.zMotion = ConfigurableJointMotion.Locked;
            driveJoint.angularXMotion = ConfigurableJointMotion.Locked;
            driveJoint.angularYMotion = ConfigurableJointMotion.Locked;
            driveJoint.angularZMotion = ConfigurableJointMotion.Locked;
        }
        else // Rotary
        {
            driveJoint.angularXMotion = ConfigurableJointMotion.Free; // X축 회전 허용
            driveJoint.xMotion = ConfigurableJointMotion.Locked;
            driveJoint.yMotion = ConfigurableJointMotion.Locked;
            driveJoint.zMotion = ConfigurableJointMotion.Locked;
        }

        if (movingPartRb != null) movingPartRb.isKinematic = false;
    }

    private void SetupJointPhysics()
    {
        if (driveJoint == null) return;

        // 서보 모터의 강한 토크를 시뮬레이션하기 위해 높은 Spring 값 사용
        JointDrive drive = new JointDrive
        {
            positionSpring = 1000000f, // 강성 (Stiffness)
            positionDamper = 1000f,    // 감쇠 (Damping)
            maximumForce = float.MaxValue
        };

        if (parameter.actuatorType == ActuatorType.Linear)
        {
            driveJoint.xDrive = drive;
        }
        else
        {
            driveJoint.angularXDrive = drive;
        }
    }

    private double GetPositionUserUnit()
    {
        if (driveJoint == null) return 0;

        if (parameter.actuatorType == ActuatorType.Linear)
        {
            // Unity(m) -> User(mm)
            float val = driveJoint.transform.localPosition.x;
            return (parameter.usedUnit == UnitType.MM) ? val * 1000d : val;
        }
        else
        {
            // Unity(Deg) -> User(Deg)
            return driveJoint.transform.localEulerAngles.x;
        }
    }

    private void ApplyPhysicsTarget(double userPos)
    {
        if (driveJoint == null) return;

        // [디버그] 물리 엔진 깨우기
        if (movingPartRb.IsSleeping()) movingPartRb.WakeUp();

        if (parameter.actuatorType == ActuatorType.Linear)
        {
            // User(mm) -> Unity(m)
            float unityTarget = (float)((parameter.usedUnit == UnitType.MM) ? userPos / 1000d : userPos);
            driveJoint.targetPosition = new Vector3(unityTarget, 0, 0);
        }
        else
        {
            // User(Deg) -> Unity(Deg)
            driveJoint.targetRotation = Quaternion.Euler((float)userPos, 0, 0);
        }
    }
    #endregion

    #region Motion Logic
    private void CheckHardwareLimits()
    {
        if (_targetSpeedUser > 0 && _signalFLS) RaiseError(MotionError.HardwareStrokeLimit);
        if (_targetSpeedUser < 0 && _signalRLS) RaiseError(MotionError.HardwareStrokeLimit);
    }

    private void UpdateProfileLogic()
    {
        // 단위 변환: mm/min -> mm/sec
        double speedLimitSec = parameter.speedLimit / 60d;
        double targetSpeedSec = _targetSpeedUser / 60d;

        // 1. 감속 거리 계산
        if (_currentState == AxisState.Positioning)
        {
            double distToEnd = Math.Abs(_finalTargetPosUser - _commandPositionUser);
            // 감속도 = Speed / Time(s)
            double decelRate = speedLimitSec / (Math.Max(_activeDecelTime, 1) / 1000d);

            // 정지거리 = v^2 / 2a
            double stoppingDist = (_currentVelocityUser * _currentVelocityUser) / (2 * decelRate);

            if (distToEnd <= stoppingDist) targetSpeedSec = 0;
        }

        // 2. 속도 프로파일 갱신
        double accelStep = (speedLimitSec / (Math.Max(_activeAccelTime, 1) / 1000d)) * Time.fixedDeltaTime;
        double decelStep = (speedLimitSec / (Math.Max(_activeDecelTime, 1) / 1000d)) * Time.fixedDeltaTime;

        double maxChange = (Math.Abs(targetSpeedSec) > Math.Abs(_currentVelocityUser)) ? accelStep : decelStep;

        _currentVelocityUser = Mathf.MoveTowards((float)_currentVelocityUser, (float)targetSpeedSec, (float)maxChange);

        // 3. 위치 적분
        if (_currentState == AxisState.Positioning &&
            Math.Abs(_finalTargetPosUser - _commandPositionUser) < parameter.inPosWidth &&
            Math.Abs(_currentVelocityUser) < 0.01d)
        {
            _commandPositionUser = _finalTargetPosUser;
            _currentVelocityUser = 0;
            _currentState = AxisState.Standby;
        }
        else
        {
            _commandPositionUser += _currentVelocityUser * Time.fixedDeltaTime;
        }

        ApplyPhysicsTarget(_commandPositionUser);
    }

    private void UpdateHomingLogic()
    {
        int dir = parameter.defaultHomingDirection;

        // Homing 속도 변환 (/60)
        double highSpeedSec = parameter.homingHighSpeed / 60d;
        double creepSpeedSec = parameter.homingCreepSpeed / 60d;

        switch (_homingSequenceStep)
        {
            case 0: // 고속 이동 (Dog 찾기)
                _targetSpeedUser = parameter.homingHighSpeed * dir; // 모니터링용
                UpdateVelocityAndPos(highSpeedSec * dir, parameter.homingAccelTime);

                if (_signalDOG) // Dog 감지
                {
                    Debug.Log($"[Axis {axisNo}] Near Dog Detected -> Creep Speed");
                    _homingSequenceStep = 1;
                }
                break;

            case 1: // 크리프 속도 이동 (Dog 통과 대기)
                _targetSpeedUser = parameter.homingCreepSpeed * dir;
                UpdateVelocityAndPos(creepSpeedSec * dir, parameter.homingDecelTime);

                if (!_signalDOG) // Dog 통과 (OFF)
                {
                    Debug.Log($"[Axis {axisNo}] Near Dog Passed -> Stopping");
                    _homingSequenceStep = 2;
                }
                break;

            case 2: // 정지 및 원점 확정
                _targetSpeedUser = 0;
                double speedLimitSec = parameter.speedLimit / 60d;
                double stopDecel = (speedLimitSec / 0.05d) * Time.fixedDeltaTime; // 50ms 급제동

                _currentVelocityUser = Mathf.MoveTowards((float)_currentVelocityUser, 0f, (float)stopDecel);
                _commandPositionUser += _currentVelocityUser * Time.fixedDeltaTime;

                if (Math.Abs(_currentVelocityUser) < 0.01d)
                {
                    _currentVelocityUser = 0;
                    _commandPositionUser = 0; // 내부 좌표 리셋
                    ApplyPhysicsTarget(0); // 물리 좌표 리셋
                    _currentState = AxisState.Standby;
                    Debug.Log($"[Axis {axisNo}] Homing Completed.");
                }
                break;
        }
        ApplyPhysicsTarget(_commandPositionUser);
    }

    private void UpdateVelocityAndPos(double targetVelSec, double timeMs)
    {
        double speedLimitSec = parameter.speedLimit / 60d;
        double step = (speedLimitSec / (timeMs / 1000d)) * Time.fixedDeltaTime;

        _currentVelocityUser = Mathf.MoveTowards((float)_currentVelocityUser, (float)targetVelSec, (float)step);
        _commandPositionUser += _currentVelocityUser * Time.fixedDeltaTime;
    }
    #endregion

    #region Command Interface
    public void StartPositioning(int stepNo)
    {
        if (IsError) return;
        var data = positioningDataList.FirstOrDefault(x => x.stepNo == stepNo);
        if (data.stepNo == 0) { RaiseError(MotionError.DriveNotReady); return; }

        Debug.Log($"[Axis {axisNo}] Start Positioning: {data.posAddress} (Speed: {data.commandSpeed})");

        _currentState = AxisState.Positioning;
        _finalTargetPosUser = (data.controlMethod == ControlMethodType.INC_Linear1) ? _commandPositionUser + data.posAddress : data.posAddress;

        double limit = parameter.speedLimit;
        double cmdSpeed = Math.Min(data.commandSpeed, limit);

        _targetSpeedUser = (_finalTargetPosUser > _commandPositionUser) ? cmdSpeed : -cmdSpeed;
        _activeAccelTime = data.accelTime;
        _activeDecelTime = data.decelTime;
    }

    public void StartJog(bool isForward)
    {
        if (IsError) return;
        Debug.Log($"[Axis {axisNo}] Start JOG {(isForward ? "Fwd" : "Rev")}");
        _currentState = AxisState.Jogging;
        double limit = parameter.jogSpeedLimit;
        _targetSpeedUser = isForward ? limit : -limit;
        _activeAccelTime = parameter.jogAccelTime;
        _activeDecelTime = parameter.jogdecelTime;
    }

    public void StopJog()
    {
        if (_currentState == AxisState.Jogging)
        {
            Debug.Log($"[Axis {axisNo}] Stop JOG");
            _targetSpeedUser = 0;
            CancelInvoke(nameof(CheckStopState));
            InvokeRepeating(nameof(CheckStopState), 0f, 0.1f);
        }
    }
    private void CheckStopState()
    {
        if (Math.Abs(_currentVelocityUser) < 0.1d)
        {
            _currentState = AxisState.Standby;
            CancelInvoke(nameof(CheckStopState));
        }
    }

    public void StartHoming()
    {
        if (IsError) return;
        Debug.Log($"[Axis {axisNo}] Start Homing");
        _currentState = AxisState.Homing;
        _homingSequenceStep = 0;
        _currentVelocityUser = 0;
    }

    public void RaiseError(MotionError error)
    {
        _lastError = error;
        _currentState = AxisState.Error;
        Debug.LogError($"[Axis {axisNo}] Error: {error}");
    }

    public void RaiseWarning(MotionError warning)
    {
        if (IsError) return;
        _lastError = warning;
        Debug.LogWarning($"[Axis {axisNo}] Warning: {warning}");
    }

    public void ErrorReset()
    {
        Debug.Log($"[Axis {axisNo}] Error Reset");
        _lastError = MotionError.None;
        _currentState = AxisState.Standby;
        _targetSpeedUser = 0;
        _currentVelocityUser = 0;
    }
    #endregion
}