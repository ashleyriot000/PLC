using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class ServoAmp : MonoBehaviour
{
    #region Struct
    public enum ActuatorType { Linear, Rotary }

    public enum Direction
    {
        X,
        Y,
        Z
    }
    public enum UnitType { MM, Degree, Pulse }
    public enum HomingType { DoNotRetry, Retry }
    public enum OperationPattern { End = 0, Continuos, Location }
    public enum ControlMethodType { None = 0, ABS_Linear1, INC_Linear1 }

    public enum AxisState
    {
        Off, Standby, Positioning, Jogging, Homing, Error
    }

    /*
    1004,하드웨어 스트로크 상한 감지,   정방향 끝(FLS) 센서가 OFF됨 (선이 끊기거나 침범함),역회전(JOG)으로 이동하여 탈출 후 에러 리셋
    1005,하드웨어 스트로크 하한 감지,   역방향 끝(RLS) 센서가 OFF됨,정회전(JOG)으로 이동하여 탈출 후 에러 리셋
    1205,서보 READY OFF 기동,           서보 앰프 전원이 안 켜졌는데 이동 명령을 내림,서보 앰프 전원 투입 및 All Axis Servo ON (Y1) 확인
    1201,서보 앰프 에러,                      "서보 모터 과부하, 케이블 단선 등 앰프 자체 에러",앰프의 LED 번호 확인 필요 (물리적 점검 필요)
    2001,운전 중 기동,                         포지셔닝 중인데 다른 포지셔닝 명령을 또 내림,[동작 유지] 기존 동작이 끝날 때까지 대기(Busy OFF 확인) 후 명령
    2005,운전 중 JOG 기동,                  움직이고 있는데 JOG(수동) 버튼을 누름 (혹은 동시 입력),[동작 유지 or 정지] JOG 정/역 동시 입력 시에는 정지함
    2003,정지 신호 ON 중 기동,             '축 정지' 신호(Cd.180)가 켜져 있는데 출발하라고 함,정지 신호를 끄고(0) 다시 기동 명령 입력
    2004,외부 정지 신호 ON,                 외부 비상정지 버튼(EMI)이 눌려 있음,비상정지 버튼 해제
    524,속도 제한값 초과,                      설정된 '속도 제한(Pr.8)'보다 더 빠른 속도를 명령함,"명령 속도를 줄이거나, 파라미터의 속도 제한을 높임"
    529,지령 속도 0,                            """가라!""고 했는데 속도를 0으로 설정해서 보냄",속도 값(Da.8 또는 Cd.17)에 0이 아닌 값 입력
    5001,소프트웨어 스트로크 에러,         "센서는 안 쳤지만, 설정된 소프트웨어(가상) 한계를 넘으려 함",목표 위치 좌표를 범위 내로 수정
    */
    public enum MotionError
    {
        None = 0,
        [InspectorName("지령속도 0")]               ZeroSpeed                = 529,
        [InspectorName("제한속도 초과")]               OverSpeed                = 1001,
        [InspectorName("드라이브 준비 안됨")]          DriveNotReady            = 1002,
        [InspectorName("HPR 타임아웃")]                HomingTimeout            = 1003,
        [InspectorName("소프트웨어 제한범위 초과")]    SoftwareStrokeLimit      = 1004,
        [InspectorName("하드웨어 제한범위 초과")]      HardwareStrokeLimit      = 1005,
        [InspectorName("운행중 지령 시작")]            StartDuringOperation     = 2001,
        [InspectorName("운행중 지령 시작(JOG)")]       StartDuringOperationJOG  = 2005,
    }
    [Serializable]
    public struct PositioningData
    {
        
        [Label("No.")] public int stepNo;                                           //스텝 No.
        [Label("포지셔닝 패턴")] public OperationPattern pattern;             //포지셔닝 패턴
        [Label("제어 종류")] public ControlMethodType controlMethod;    //제어 방식
        [Label("가속 시간")] public double accelTime;                               //가속시간(ms) => 초(/1000)
        [Label("감속 시간")] public double decelTime;                               //감속시간(ms) => 초(/1000)
        [Label("지령 위치")] public double posAddress;                              //목적위치(um) => m(/unitMagnification/1000)
        [Label("호 위치")] public double arcAddress;                                   //아크위치(um) => m(/unitMagnification/1000)
        [Label("지령 속도")] public double commandSpeed;                            //지령속도(mm/분) => m/초(/60000)
        [Label("대기 시간")] public double dwellTime;                               //대기시간(ms) => 초(/1000)
        [Label("M코드")] public double mCode;                                     //M코드
    }
    #endregion

    #region Hardware Setup
    [Header("Physics Components")]
    public Rigidbody movingPart;
    public ConfigurableJoint driveJoint;

    [Header("Axis Settings")]
    [Label("축 No.")]
    public int axisNo = 1;
    [Tooltip("PLC 단위 배율 => x1(0.1um), x10(1um), x100(10um), x1000(100um)")]
    //단위 배율 - x1(0.1um), x10(1um), x100(10um), x1000(100um)
    //설정값에 따라 PLC에서 보내고 피드백 받는 값이 달라짐.(배율이 높을 수록 정밀도가 낮아짐)
    //300000.0um 이동시 -> x1    (0.1um까지 표현) 3000000 값을 보냄,    mm단위로 바꾸려면x 0.0001 => 300mm,    M단위로 x 0.001 => 0.3m
    //                  -> x10   (1um까지 표현)    300000 값을 보냄,    mm단위로 바꾸려면x 0.001               M단위로 x 0.001
    //                  -> x100  (10um까지 표현)    30000 값을 보냄,    mm단위로 바꾸려면x 0.01                M단위로 x 0.001
    //                  -> x1000 (100um까지 표현)    3000 값을 보냄,    mm단위로 바꾸려면x 0.1                 M단위로 x 0.001    
    [SerializeField][Label("단위 배율")] private int _unitMagnification = 1;
    [SerializeField][Label("액츄에이터 종류")] private ActuatorType _actuatorType = ActuatorType.Linear;   //액츄에이터 타입
    [SerializeField][Label("축 방향")] private Direction forwardDirection = Direction.X;
    [SerializeField][Label("기본 단위")] private UnitType _unitSetting = UnitType.MM;                      //기본 단위
    [SerializeField][Label("1회전당 펄스 수")] private double _motorResolution = 4194304d;                 //분해능
    [SerializeField][Label("1회전당 이동 거리")] private double _ballscrewLead = 2000.0;                   //1회전당 전진 길이(um)
    [SerializeField][Label("기어비")] private double _gearRatio = 1.0d;                                    //기어비
    [SerializeField][Label("최대 속도(mm/Min)")] private double _speedLimit = 2000.0d;                     //최대 스피드(mm/분) -> m/초(/60000)
    [SerializeField][Label("위치결정 완료 폭(um)")] private double _inPosWidth = 10.0d;                        //도착 허용 범위(um) -> m(/1000000)
    [SerializeField][Label("JOG 최대 속도")] private double _jogSpeedLimit = 200.0d;                      //JOG 최대 스피드(mm/분) -> m/초(/60000)
    [SerializeField][Label("JOG 가속시간")] private double _jogAccelTime = 1000d;                           //JOG 가속속도(ms) -> 초(/1000)
    [SerializeField][Label("JOG 감속시간")] private double _jogdecelTime = 1000d;                           //JOG 감속속도(ms) -> 초(/1000)
    [SerializeField][Label("원점복귀 재시도")] private HomingType _hprRetryType = HomingType.DoNotRetry;   //원점 복귀 재시도 여부
    [SerializeField][Label("원점복귀 기본 방향")] private int _defaultHprDirection = 1;                    //원점 복귀 시작 방향 1:정방향, -1:역방향
    [SerializeField][Label("원점복귀 최대 속도")] private double _hprHighSpeed = 0.01;                  //원점 복귀 최대 속도(mm/분) -> m/초(/60000)
    [SerializeField][Label("원점복귀 정밀 속도")] private double _hprCreepSpeed = 0.01d;                 //원점 복귀 정밀 속도(mm/분) -> m/초(/60000)
    [SerializeField][Label("원점복귀 가속 시간")] private double _hprAccelTime = 1000d;                    //원점 복귀 가속 시간(ms) -> 초(/1000)
    [SerializeField][Label("원점복귀 감속 시간")] private double _hprDecelTime = 1000d;                    //원점 복귀 감속 시간(ms) -> 초(/1000)
    #endregion    

    #region 2. State
    [Header("Status Monitor")]
    [SerializeField] [Label("서보 레디")] private bool _isServoOn = false;                                  //서보 준비 여부
    [SerializeField] [Label("현재 서보 상태")] private AxisState _currentState = AxisState.Off;             //현재 서보의 상태
    [SerializeField] [Label("에러 상태(에러 코드)")] private MotionError _lastError = MotionError.None;     //에러 종류
    [SerializeField] [Label("현재 위치(mm)")] private double _currentPositionMM = 0d;         //현재 위치(mm)
    [SerializeField] [Label("현재 위치")] private int _currentPositionRaw = 0;                              //현재 위치(PLC기준)
    [SerializeField] [Label("현재 속도(mm/Min)")] private double _currentVelocityMM = 0d;         //현재 속도(mm/Min)
    [SerializeField] [Label("현재 속도")] private int _currentSpeedRaw = 0;                                 //현재 속도(PLC기준)
    [SerializeField] [Label("포지셔닝 데이터")] private List<PositioningData> positioningDataList = new();  //포지셔닝 데이터 리스트

    // 내부 물리 연산용 (mm 단위)    
    private double _unitMultiplier = 0d;            //단위 배율 실적용치(Raw <-> mm)
    private double _internalPositionMM = 0d;
    private double _hprOffsetMM = 0d;               //원점 보정(mm)
    private double _jogTargetSpeed = 0d;            //JOG 지령 속도(mm/Min)
    private double _commandPositionMM = 0d;         //포지셔닝 명령 위치(mm)
    private double _posTargetSpeed = 0d;               //지령 속도(m/Sec)
    private int _homingSequenceStep = 0;

    private bool _isOnJogForward = false;
    private bool _isOnJogReverse = false;

    private bool _isOnFLS = false;
    private bool _isOnRLS = false;
    private bool _isOnDOG = false;
    #endregion

    #region Properties
    public int CurrentPulse => _currentPositionRaw;
    public double CurrentPosition => _currentPositionMM;
    public bool IsBusy => _currentState != AxisState.Standby && _currentState != AxisState.Error && _lastError == MotionError.None;
    public bool IsError => _currentState == AxisState.Error || _lastError != MotionError.None;
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
    }
    public bool IsOnRLS
    {
        get => _isOnRLS;
        set
        {
            _isOnRLS = value;
        }
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
        _unitMultiplier = 10000.0d / _unitMagnification;

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
            driveJoint.autoConfigureConnectedAnchor = false;
            driveJoint.connectedAnchor = Vector3.zero;
            driveJoint.axis = -Vector3.right;
                
            driveJoint.xMotion = _actuatorType == ActuatorType.Linear ? 
                ConfigurableJointMotion.Free : ConfigurableJointMotion.Locked;
            driveJoint.yMotion = ConfigurableJointMotion.Locked;
            driveJoint.zMotion = ConfigurableJointMotion.Locked;
                
            driveJoint.angularXMotion = _actuatorType == ActuatorType.Rotary ? 
                ConfigurableJointMotion.Free : ConfigurableJointMotion.Locked;
            driveJoint.angularYMotion = ConfigurableJointMotion.Locked;
            driveJoint.angularZMotion = ConfigurableJointMotion.Locked;

            driveJoint.enableCollision = true;

        }
        // 런타임 초기화
        _hprOffsetMM = 0d;
        _internalPositionMM = GetPhysicalPositionMM();
        _currentPositionMM = _internalPositionMM - _hprOffsetMM;
        _currentPositionRaw = MMToRaw(_currentPositionMM);
        _jogTargetSpeed = 0d;
        _posTargetSpeed = 0d;
        SetupJointPhysics(20f);
    }    

    private void FixedUpdate()
    {
        if (!_isServoOn)
        {
            // 1. 상태 업데이트
            double prevPos = _internalPositionMM;
            _internalPositionMM = GetPhysicalPositionMM();
            _currentPositionMM = _internalPositionMM - _hprOffsetMM;
            _currentPositionRaw = MMToRaw(_currentPositionMM);
            _currentVelocityMM = Math.Abs(prevPos - _internalPositionMM) * 60d / Time.fixedDeltaTime ;
            _currentSpeedRaw = (int)_currentVelocityMM * 100;
            return;
        }

        // 2. 센서 체크
        CheckHardwareLimits();

        // 3. 에러 시 정지
        if (_currentState == AxisState.Error)
        {
            _jogTargetSpeed = 0d;
            return;
        }

        // 4. 동작 로직
        switch (_currentState)
        {
            case AxisState.Positioning:
                break;
            case AxisState.Jogging:
                ProcessJog();
                break;
            case AxisState.Homing:
                break;
            default:
                break;
        }
    }
    #endregion

    #region 5. Control Signals (System)
    public void SetServoOn(bool isOn)
    {
        if (_isServoOn == isOn) return;

        if (_isServoOn = isOn)
        {
            Debug.Log($"[Axis {axisNo}] Servo ON");
            _currentState = AxisState.Standby;
            _lastError = MotionError.None;
            _internalPositionMM = GetPhysicalPositionMM();
            _currentPositionMM = _internalPositionMM - _hprOffsetMM;
            _currentPositionRaw = MMToRaw(_currentPositionMM);
            ApplyPhysicsTarget(_internalPositionMM);
            SetupJointPhysics(100000f);
        }
        else
        {
            Debug.Log($"[Axis {axisNo}] Servo OFF");
            _currentState = AxisState.Off;
            _hprOffsetMM = 0f;
            _internalPositionMM = GetPhysicalPositionMM();
            _currentPositionMM = _internalPositionMM - _hprOffsetMM;
            _currentPositionRaw = MMToRaw(_currentPositionMM);
            _jogTargetSpeed = 0d;
            _posTargetSpeed = 0d;
            SetupJointPhysics(20f);
        }
    }

    private void SetupJointPhysics(float spring)
    {
        JointDrive drive = new()
        {
            positionSpring = spring,
            positionDamper = 2000f,
            maximumForce = float.MaxValue
        };

        if (_actuatorType == ActuatorType.Linear)
        {
            driveJoint.xDrive = drive;
        }
        else
        {
            driveJoint.angularXDrive = drive;
        }
    }


    #endregion

    #region 6. Helper Functions
    // [단위 변환] Property 사용 (자동 계산)
    private int MMToRaw(double mmValue) => (int)(mmValue * _unitMultiplier);
    private double ToMeterPerSeconds(double MmPerMin) => MmPerMin * 60000d;

    //전자기어비 구하는 메서드(1펄스당 이동거리 혹은 이동각)
    public double GetPulseRatio()
    {
        if (_unitSetting == UnitType.Pulse) return 1.0d;
        double limitVal = _ballscrewLead;
        if (_actuatorType == ActuatorType.Rotary && _unitSetting == UnitType.Degree) limitVal = 360.0d;
        return (_motorResolution * _gearRatio) / limitVal;
    }

    //유니티에서의 실제 위치를 구한다.(로컬x축 위치기준)
    private double GetPhysicalPositionMM()
    {
        if(_actuatorType == ActuatorType.Rotary)
            return driveJoint.transform.eulerAngles.x;
        
        if(forwardDirection == Direction.X)
            return driveJoint.transform.localPosition.x * 1000d;
        else if (forwardDirection == Direction.Y)
            return driveJoint.transform.localPosition.y * 1000d;
        else
            return driveJoint.transform.localPosition.z * 1000d;
    }

    //현재 값(current)을 목표 값(target)으로 maxDelta만큼 이동시킵니다. (Double 정밀도)
    private double MoveTowards(double current, double target, double maxDelta)
    {
        // 1. 이동할 수 있는 거리(maxDelta)가 음수면 이동하지 않음 (Unity 동작과 동일)
        if (maxDelta < 0)
        {
            return current;
        }

        // 2. 목표까지의 거리(절대값) 계산
        double difference = target - current;

        // 3. 남은 거리가 이번에 이동할 거리(maxDelta)보다 작거나 같다면
        // 즉시 목표값에 도착한 것으로 처리 (오버슈트 방지)
        if (Math.Abs(difference) <= maxDelta)
        {
            return target;
        }

        // 4. 목표 방향(부호)으로 maxDelta만큼 이동
        // Math.Sign은 양수면 1, 음수면 -1, 0이면 0을 반환
        return current + Math.Sign(difference) * maxDelta;
    }
    
    //두 값이 거의 같은지 비교합니다. 허용 오차(tolerance) 지정
    private bool Approximately(double a, double b, double tolerance)
    {
        return Math.Abs(a - b) < tolerance;
    }

    private void ApplyPhysicsTarget(double positionMM)
    {
        if (_actuatorType == ActuatorType.Linear)
        {
            float unityTarget = (float)(positionMM / 1000d);
            Debug.Log($"Apply {positionMM} => {unityTarget}");
            driveJoint.targetPosition = new Vector3(unityTarget, 0, 0);
        }
        else
        {
            driveJoint.targetRotation = Quaternion.Euler((float)positionMM, 0, 0);
        }
    }
    #endregion

    #region 7. Motion Logic
    private void CheckHardwareLimits()
    {
        if (_currentState == AxisState.Homing && _hprRetryType == HomingType.Retry)
            return;

        if (_posTargetSpeed > 0 && _isOnFLS) RaiseError(MotionError.HardwareStrokeLimit);
        if (_posTargetSpeed < 0 && _isOnRLS) RaiseError(MotionError.HardwareStrokeLimit);
    }

    public void ResetAxis()
    {
        _currentState = AxisState.Standby;
        _lastError = MotionError.None;
    }

    public void CommandJog(bool isOnForward, bool isOnReverse, int targetSpeed)
    {
        //동일 명령 무시
        if (_isOnJogForward == isOnForward && _isOnJogReverse == isOnReverse)
            return;


        //에러상태일 때 지령 무시.
        if (_currentState == AxisState.Error || _lastError != MotionError.None)
        {
            Debug.LogWarning($"[동작 거부] 현재 에러 상태입니다. 리셋해주세요. {_lastError}(Code: {(int)_lastError})");
            return;
        }

        //최신 상태로 갱신
        _isOnJogForward = isOnForward;
        _isOnJogReverse = isOnReverse;

        //두 개의 신호가 같을 경우
        if(isOnForward == isOnReverse)
        {
            //양방향 모두 신호가 들어오면 에러
            if(isOnForward)
            {
                RaiseError(MotionError.StartDuringOperationJOG);
                return;
            }
            else
            {
                //그게 아니면 정지
                _jogTargetSpeed = 0d;
                return;
            }
        }

        if (_currentState != AxisState.Jogging && IsBusy)
        {
            RaiseError(MotionError.StartDuringOperationJOG);
            return;
        }

        double scaledJogSpeed = targetSpeed / 0.01d;
        if (scaledJogSpeed > _jogSpeedLimit)
        {
            RaiseError(MotionError.OverSpeed);
            return;
        }

        _currentState = AxisState.Jogging;
        _jogTargetSpeed = isOnForward ? scaledJogSpeed : -scaledJogSpeed;
    }

    //JOG상태 진행과정
    private void ProcessJog()
    {
        double accelRate = 0d;
        if(_isOnJogForward || _isOnJogReverse)
            accelRate = _speedLimit / _jogAccelTime * 0.001d;
        else
            accelRate = _speedLimit / _jogdecelTime * 0.001d;

        _currentVelocityMM = MoveTowards(_currentVelocityMM, _jogTargetSpeed, accelRate * Time.fixedDeltaTime);
        _internalPositionMM += _currentVelocityMM * Time.fixedDeltaTime;
        _currentPositionMM = _internalPositionMM - _hprOffsetMM;
        Debug.Log($"Update Jog => {_internalPositionMM}");
        ApplyPhysicsTarget(_internalPositionMM);

        if(!_isOnJogForward && !_isOnJogReverse && Approximately(Math.Abs(_currentVelocityMM), 0d, 0.0001d))
        {
            if(_lastError == MotionError.None)
                _currentState = AxisState.Standby;
            else
                _currentState = AxisState.Error;
        }
    }

    private void UpdateProfileLogic()
    {
        //double targetSpeedSec = _posTargetSpeed / 60d;
        //double speedLimitSec = (_speedLimit * _unitMultiplier) / 60d;

        //if (_currentState == AxisState.Positioning)
        //{
        //    double distToEnd = Math.Abs(_finalTargetPosMM - _commandPositionMM);
        //    double decelRate = speedLimitSec / (Math.Max(_activeDecelTime, 1) / 1000d);
        //    double stoppingDist = (_currentVelocityMM * _currentVelocityMM) / (2 * decelRate);
        //    if (distToEnd <= stoppingDist) targetSpeedSec = 0;
        //}

        //double accelStep = (speedLimitSec / (Math.Max(_activeAccelTime, 1) / 1000d)) * Time.fixedDeltaTime;
        //double decelStep = (speedLimitSec / (Math.Max(_activeDecelTime, 1) / 1000d)) * Time.fixedDeltaTime;
        //double maxChange = (Math.Abs(targetSpeedSec) > Math.Abs(_currentVelocityMM)) ? accelStep : decelStep;

        //_currentVelocityMM = Mathf.MoveTowards((float)_currentVelocityMM, (float)targetSpeedSec, (float)maxChange);

        //double inPosMM = _inPosWidth * _unitMultiplier;
        //if (_currentState == AxisState.Positioning &&
        //    Math.Abs(_finalTargetPosMM - _commandPositionMM) < inPosMM &&
        //    Math.Abs(_currentVelocityMM) < 0.01d)
        //{
        //    _commandPositionMM = _finalTargetPosMM;
        //    _currentVelocityMM = 0;
        //    _currentState = AxisState.Standby;
        //}
        //else
        //{
        //    _commandPositionMM += _currentVelocityMM * Time.fixedDeltaTime;
        //}
        //ApplyPhysicsTarget(_commandPositionMM);
    }

    private void UpdateHomingLogic()
    {
        double highSpeed = ToMeterPerSeconds(_hprHighSpeed);
        double creepSpeed = ToMeterPerSeconds(_hprCreepSpeed);
        int dir = _defaultHprDirection;

        switch (_homingSequenceStep)
        {
            case 0:
                _posTargetSpeed = highSpeed * dir;
                UpdateVelocityAndPos(highSpeed * dir, _hprAccelTime);
                if (_isOnDOG) _homingSequenceStep = 1;
                break;
            case 1:
                _posTargetSpeed = creepSpeed * dir;
                UpdateVelocityAndPos(creepSpeed * dir, _hprDecelTime);
                if (!_isOnDOG) _homingSequenceStep = 2;
                break;
            case 2:
                _posTargetSpeed = 0;
                double limitSec = ToMeterPerSeconds(_speedLimit);
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
        double limitSec = ToMeterPerSeconds(_speedLimit);
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
        double scaledLimit = _speedLimit * _unitMultiplier;

        Debug.Log($"[Axis {axisNo}] Start Pos No.{stepNo} (Raw:{data.posAddress} -> MM:{scaledPos:F3})");

        //_finalTargetPosMM = (data.controlMethod == ControlMethodType.INC_Linear1)
        //    ? _commandPositionMM + scaledPos : scaledPos;

        //double cmdSpeed = Math.Min(scaledSpeed, scaledLimit);
        //_posTargetSpeed = (_finalTargetPosMM > _commandPositionMM) ? cmdSpeed : -cmdSpeed;
        //_activeAccelTime = data.accelTime;
        //_activeDecelTime = data.decelTime;
    }

    

    private void StartHoming()
    {
        Debug.Log($"[Axis {axisNo}] Start Homing (9001)");
        _currentState = AxisState.Homing;
        _homingSequenceStep = 0;
        _currentVelocityMM = 0;
    }


    public void RaiseError(MotionError error) 
    { 
        _lastError = error;
        Debug.LogWarning($"[Axis {axisNo}] Error 발생 => {error}(Code: {ErrorCode})");
    }

    #endregion

    // [Unity Editor Magic] OnValidate: 인스펙터 값이 변경될 때 호출됨
#if UNITY_EDITOR
       
    private void OnValidate()
    {
        if (_unitMagnification >= 1000)
            _unitMagnification = 1000;
        else if (_unitMagnification >= 100)
            _unitMagnification = 100;
        else if (_unitMagnification >= 10)
            _unitMagnification = 10;
        else
            _unitMagnification = 1;

        _unitMultiplier = 10000.0d / _unitMagnification;        
    }
#endif
}