using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEditor.PackageManager;
using UnityEngine;
using UnityEngine.Events;
using static ServoActuator;

[RequireComponent(typeof(Rigidbody)), RequireComponent(typeof(ConfigurableJoint))]
public class ServoAmplifier : MonoBehaviour
{
    #region Constants
    //연결된 액츄에이터의 구동 방식
    public enum ActuatorType
    {
        Linear,
        Rotary
    }
    //액츄에이터를 제어할 때 사용할 단위
    public enum UnitType
    {
        MM,         //밀리미터
        Degree,     //각도
        Pulse       //펄스값
    }
    //서보 동작 상태
    public enum ServoState
    {
        Off = 0,
        Idle,
        Jogging,
        Positioning,
        Homing_Search,
        Homing_Retry,
        Homing_Creep
    }

    public enum OperationPattern
    {
        End = 0,
        Continuos,
        Location
    }

    public enum ControlMethodType
    {
        ABS_Linear1 = 0,
        INC_Linear1
    }

    // 상수 정의 (1mm = 1000 pulse/um)
    const double PLC_TO_UNITY_RATIO = 0.000001d; // 1/1,000,000
    const double UNITY_TO_PLC_RATIO = 1000000.0d;
    #endregion

    #region Custom Type
    [Serializable]
    public struct PositioningData
    {
        public OperationPattern pattern;
        public ControlMethodType controlMethod;
        public double accelTime;
        public double decelTime;
        public double posAddress;
        public double commandSpeed;
        public double dwellTime;
    }
    #endregion

    #region Variables
    public Rigidbody body;          //물리 강체
    public ConfigurableJoint joint; //로컬 조인트

    [Header("액츄에이터 동작 방식")]
    public ActuatorType actuatorType = ActuatorType.Linear;
    [Header("Basic Parameter")]    
    public UnitType usedUnit = UnitType.MM;     //사용 단위
    public double motorResolution = 131072d;    //1회전당 펄스수(분해능)
    public double ballscrewLead = 2d;           //1회전당 이동 길이(mm)
    public double gearRatio = 1.0d;             //기어비
    public double speedLimit = 2000.0d;         //최대 속도(mm/min)
    public double accelTime = 1d;            //가속시간(초)
    public double decelTime = 1d;            //감속시간(초)

    [Header("Detail Parameter")]
    public double inPosWidth = 0.01d;           //인-포지션 폭 (오차 허용 범위:mm)
    public double jogSpeedLimit = 200d;         //JOG 기동 속도(mm/min)
    public double jogAccelTime = 500d;          //JOG 가속시간 
    public double jogdecelTime = 500d;          //JOG 감속시간
    public bool useForceStop = false;           //강제 정지 허용 여부(신호를 계속 주고 있어야 정지하지 않음)
    public int inPositionDuration = 100;    //목표 도달 신호 유지시간(millisecond)

    [Header("HPR(원점 복귀) 파라미터")]
    public int defaultHomingDirection = 1;      //원점복귀 기본 방향(1:정방향, -1:역방향)
    public double homingHighSpeed = 200.0d;     //원점복귀 최대속도
    public double homingCreepSpeed = 20.0d;     //원점복귀 정밀속도
    public double homingAccelTime = 1d;         //원점복귀 가속시간
    public double homingDecelTime = 1d;         //원점복귀 감속시간

    public List<PositioningData> positioningList; //포지셔닝 데이터 리스트

    public UnityEvent<bool> onReadyChanged;
    public UnityEvent<bool> onErrorChanged;
    public UnityEvent<bool> onBusyChanged;
    public UnityEvent<double> onUnitChanged;
    public UnityEvent<bool> onLSPChanged;
    public UnityEvent<bool> onLSNChanged;
    public UnityEvent<bool> onPDogChanged;
    public UnityEvent<bool> onOPRCompleted;
    public UnityEvent<bool> onInPosChanged;
    public UnityEvent<bool> onForceStopChanged;

    private ServoState _currentState = ServoState.Off;
    private double _currentUnit;
    private double _homeOffset_Unit = 0d;

    private bool _isReady = false;
    private bool _isBusy = false;
    private bool _isError = false;

    private bool _isOnJogForward = false;
    private bool _isOnJogReverse = false;
    private bool _isOnLimitSensorPositive = false;
    private bool _isOnLimitSensorNegative = false;
    private bool _isOnProximityDog = false;
    private bool _OPRComplete = false;
    private bool _inPosition = false;
    private bool _isNotForceStop = false;


    private double _currentVelocity_Unit = 0d;
    private double _internalTarget_Unit = 0d;
    private double _targetUnit = 0d;
    private int _currentHomingDir = 0;


    #endregion

    #region Property
    public bool IsReady
    {
        get => _isReady;
        private set
        {
            if (_isReady == value)
                return;

            _isReady = value;
            onReadyChanged?.Invoke(value);
        }
    }
    public bool IsError
    {
        get => _isError;
        private set
        {
            if (_isError == value)
                return;

            _isError = value;
            onErrorChanged?.Invoke(value);
        }
    }
    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (_isBusy == value)
                return;

            _isBusy = value;
            onBusyChanged?.Invoke(value);
        }
    }
    public bool LSP
    {
        get => _isOnLimitSensorPositive;
        set
        {
            if (_isOnLimitSensorPositive == value)
                return;

            _isOnLimitSensorPositive = value;
            onLSPChanged?.Invoke(value);
        }
    }
    public bool LSN
    {
        get => _isOnLimitSensorNegative;
        set
        {
            if (_isOnLimitSensorNegative == value)
                return;

            _isOnLimitSensorNegative = value;
            onLSNChanged?.Invoke(value);
        }
    }
    public bool PDog
    {
        get => _isOnProximityDog;
        set
        {
            if(_isOnProximityDog == value) return;

            _isOnProximityDog = value;
            onPDogChanged?.Invoke(value);
        }
    }
    public bool OPRComplete
    {
        get => _OPRComplete;
        private set
        {
            if (_OPRComplete == value)
                return;

            _OPRComplete = value;
            onOPRCompleted?.Invoke(value);
        }
    }

    public bool InPosition
    {
        get => _inPosition;
        set
        {
            if (_inPosition == value)
                return;

            _inPosition = value;
            onInPosChanged?.Invoke(value);
        }
    }

    public bool IsNotForceStop
    {
        get => _isNotForceStop;

    }

    public double CurrentUnit
    {
        get => _currentUnit;
        set
        {
            if (_currentUnit == value)
                return;

            _currentUnit = value;
            onUnitChanged?.Invoke(value);
        }
    }
    #endregion

    #region UNITY EVENT METHOD
    private void Awake()
    {
        //리지드 바디가 연결되어 있지 않으면 
        if(body == null)
            body = GetComponent<Rigidbody>(); //게임오브젝트 안에 어태치된 리지드바디를 찾아서 넣어라.

        //리지드바디를 찾았다면
        if (body != null)
        {
            body.automaticCenterOfMass = false;
            body.automaticInertiaTensor = false;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.Continuous;
            body.useGravity = false;
        }

        //컨피규러블 조인트가 연결되어 있지 않다면
        if(joint == null)
            joint = GetComponent<ConfigurableJoint>();  //게임오브젝트 안에 어태치된 조인트를 찾아서 넣어라

        //조인트를 찾았다면
        if(joint != null)
        {
            joint.xMotion = ConfigurableJointMotion.Locked;
            joint.yMotion = ConfigurableJointMotion.Locked;
            joint.zMotion = actuatorType == ActuatorType.Linear ? ConfigurableJointMotion.Free : ConfigurableJointMotion.Locked;
            joint.angularXMotion = ConfigurableJointMotion.Locked;
            joint.angularYMotion = ConfigurableJointMotion.Locked;
            joint.angularZMotion = actuatorType == ActuatorType.Rotary ? ConfigurableJointMotion.Free : ConfigurableJointMotion.Locked;
        }
    }

    private void FixedUpdate()
    {
        double accTime;
        double targetVelocity = _currentState switch
        {
            ServoState.Jogging=> ProcessJog(out accTime),
            ServoState.Positioning => ProcessPositioning(out accTime),
            ServoState.Homing_Search => throw new System.NotImplementedException(),
            ServoState.Homing_Retry => throw new System.NotImplementedException(),
            ServoState.Homing_Creep => throw new System.NotImplementedException(),
            _ => 0d
        };             

        double limitSpeed = _currentState == ServoState.Positioning ? speedLimit : jogSpeedLimit;
        double accelRate = limitSpeed / accelTime;
        _currentVelocity_Unit = MoveTowards(_currentVelocity_Unit, targetVelocity, accelRate * Time.fixedDeltaTime);

        _internalTarget_Unit += _currentVelocity_Unit * Time.fixedDeltaTime;
        ApplyPhysics(_internalTarget_Unit);
        _currentUnit = _internalTarget_Unit;

        CurrentUnit = PhysToPulse(_internalTarget_Unit);
    }

    #endregion

    #region Private Method    
    private double SetJointForce(float force)
    {
        JointDrive drive = new JointDrive
        {
            positionSpring = force,
            positionDamper = 100,
            maximumForce = float.MaxValue
        };

        if (actuatorType == ActuatorType.Linear)
        {
            joint.zDrive = drive;
        }
        else
        {
            joint.angularXDrive = drive;
        }

        return 0d;
    }
    private double MoveTowards(double current, double target, double maxDelta)
    {
        // 1. 현재 값과 목표 값의 차이(절대값)가 최대 변화량보다 작거나 같으면
        //    바로 목표 값에 도달한 것으로 처리 (오버슈팅 방지)
        if (Math.Abs(target - current) <= maxDelta)
        {
            return target;
        }

        // 2. 목표 값으로 다가가기
        //    Math.Sign: 목표가 더 크면 1, 작으면 -1 반환
        return current + Math.Sign(target - current) * maxDelta;
    }    
    private double PulseToUnit(int pulse)
    {
        double revs = (double)pulse / motorResolution;
        double shaftRevs = revs * gearRatio;
        double unitValue = 0f;

        if (usedUnit == UnitType.MM)
        {
            unitValue = shaftRevs * ballscrewLead;
        }
        else if (usedUnit == UnitType.Degree)
        {
            unitValue = shaftRevs * 360d;
        }
        else
        {
            unitValue = pulse;
        }
            
        return unitValue + _homeOffset_Unit;
    }
    private int PhysToPulse(double physValocity)
    {
        double relativePos = physValocity - _homeOffset_Unit;
        double shaftRevs = (usedUnit == UnitType.MM) ? relativePos / ballscrewLead : relativePos / 360d;
        double motorRevs = shaftRevs / gearRatio;
        return (int)(motorRevs * motorResolution);
    }
    private void ApplyPhysics(double value)
    {
        if(actuatorType == ActuatorType.Linear)
        {
            double mmValue = value;

            if (usedUnit == UnitType.Pulse)
                mmValue = (value / motorResolution) * gearRatio * ballscrewLead;

            joint.targetPosition = new Vector3(0, 0, (float)(mmValue / 1000.0d));
        }
        else
        {
            double degValue = value;
            if(usedUnit == UnitType.Pulse)
            {
                degValue = (value / motorResolution) * gearRatio * 360.0d;
            }

            joint.targetRotation = Quaternion.Euler((float)degValue, 0, 0);
        }
    }
    private async Task CompletePositioning()
    {
        InPosition = true;

        if (inPositionDuration > 0)
        {
            try
            {
                await Task.Delay(inPositionDuration, destroyCancellationToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            InPosition = false;
        }
    }

    private double ProcessJog(out double accelTime)
    {
        accelTime = 0d;
        double direction = 0;
        if (_isOnJogForward)
        {
            if (_isOnLimitSensorPositive)
            {
                _currentVelocity_Unit = 0d;
                IsError = true;
                return 0d;
            }

            accelTime = jogAccelTime;
            direction = 1d;
        }

        if (_isOnJogReverse)
        {
            if (_isOnLimitSensorNegative)
            {
                _currentVelocity_Unit = 0d;
                IsError = true;
                return 0d;
            }
            direction += -1d;
            accelTime += jogdecelTime;
        }

        return jogSpeedLimit * direction;
    }
    private double ProcessHomingSearch(out double accelTime)
    {
        accelTime = 0d;
        return homingHighSpeed * _currentHomingDir;
    }

    private double ProcessPositioning(out double accelTime)
    {
        accelTime = 0d;
        if(_isOnLimitSensorPositive || _isOnLimitSensorNegative)
        {
            _currentVelocity_Unit = 0d;
            IsError = true;
            return 0d;
        }

        double distance = _targetUnit - _internalTarget_Unit;
        if(Math.Abs(distance) <= inPosWidth)
        {
            _currentVelocity_Unit = 0d;
            _internalTarget_Unit = _targetUnit;
            ApplyPhysics(_internalTarget_Unit);
            return 0d;
        }

        return 1d;
    }
    #endregion

    #region Public Method
    public void CommandPLCReady(bool isOn)
    {
        if(isOn)
        {
            IsReady = true;
            _currentState = ServoState.Idle;
            _homeOffset_Unit = 0;
            SetJointForce(100000f);
        }
        else
        {
            IsReady = false;
            IsBusy = false;
            InPosition = false;
            IsError = false;
            OPRComplete = false;
            _currentState = ServoState.Off;
            SetJointForce(0f);
        }
    }

    public void SetForceStop(bool isOn)
    {
        if (!useForceStop)
            return;

        //true: 일반
        //false: 강제 정지상태
    }


    public void CommandJogForward(bool isOn)
    {
        if(_currentState == ServoState.Idle || 
            _currentState == ServoState.Jogging)
        {
            _isOnJogForward = isOn;

            _currentState = !_isOnJogForward && !_isOnJogReverse ?
            ServoState.Idle : ServoState.Jogging;

            IsBusy = _currentState == ServoState.Jogging;
        }
        else
        {
            IsError = true;
        }
    }
    public void CommandJogReverse(bool isOn)
    {
        if(_currentState == ServoState.Idle ||
            _currentState == ServoState.Jogging)
        {
            _isOnJogReverse = isOn;
        }

        _currentState = !_isOnJogForward && !_isOnJogReverse ?
            ServoState.Idle : ServoState.Jogging;

        IsBusy = _currentState == ServoState.Jogging;
    }
    public void CommandPositioning(int num)
    {
        if (IsError)
            return;

        if (_currentState != ServoState.Idle)
        {
            IsError = true;
            return;
        }

        PositioningData data = positioningList[num - 1];
        if (data.controlMethod == ControlMethodType.ABS_Linear1)
            _targetUnit = data.posAddress;
        else if (data.controlMethod == ControlMethodType.INC_Linear1)
            _targetUnit += data.posAddress;
    }




    #endregion
}
