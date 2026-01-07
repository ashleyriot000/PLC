using System;
using UnityEngine;
using UnityEngine.Events;

[RequireComponent(typeof(Rigidbody)), RequireComponent(typeof(ConfigurableJoint))]
public class ServoAmplifier : MonoBehaviour
{
    #region Constants
    public enum ActuatorType
    {
        Linear,
        Rotary
    }
    public enum UnitType
    {
        MM,
        Degree,
        Pulse
    }
    public enum ServoState
    {
        Off = 0,
        Idle,
        Jogging,
        Positioning,
        Homing_Search,
        Homing_Retry,
        Homing_Creep,
        Error
    }

    // 상수 정의 (1mm = 1000 pulse/um)
    const double PLC_TO_UNITY_RATIO = 0.000001d; // 1/1,000,000
    const double UNITY_TO_PLC_RATIO = 1000000.0d;
    #endregion

    #region Variables
    public Rigidbody body;
    public ConfigurableJoint joint;

    public ActuatorType actuatorType = ActuatorType.Linear;
    public UnitType usedUnit;
    public bool useForceStop = false;
    public double motorResolution = 131072d;
    public double gearRatio = 1.0d;
    public double ballscrewLead = 2d;        //milimeter
    public double maxSpeed = 2000.0d;     //milimeter/min
    public double accelTime = 1000d;        //milisecond
    public double decelTime = 1000d;        //milisecond
    public int defaultHomingDirection = 1;
    public double jogSpeed = 1000d;
    public double homingHighSpeed = 200.0d;
    public double homingCreepSpeed = 20.0d;
    public double inPosWidth = 0.1d;
    public double inPositionDuration = 0.1d;
    

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

    private bool _isOnLimitSensorPositive = false;
    private bool _isOnLimitSensorNegative = false;
    private bool _isOnProximityDog = false;
    private bool _OPRComplete = false;
    private bool _isOnJogForward = false;
    private bool _isOnJogReverse = false;
    private bool _inPosition = false;
    private bool _isNotForceStop = false;


    private double _currentVelocity_Unit = 0d;
    private double _internalTarget_Unit = 0d;
    private double _targetUnit = 0d;
    private int _homingDir = 1;


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
        if(body == null)
            body = GetComponent<Rigidbody>();

        if (body != null)
        {
            body.automaticCenterOfMass = false;
            body.automaticInertiaTensor = false;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.Continuous;
            body.useGravity = false;
        }

        if(joint == null)
            joint = GetComponent<ConfigurableJoint>();

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
        double targetVelocity = _currentState switch
        {
            ServoState.Jogging => RunJogging(),
            ServoState.Positioning => RunPositioning(),
            ServoState.Homing_Search => throw new System.NotImplementedException(),
            ServoState.Homing_Retry => throw new System.NotImplementedException(),
            ServoState.Homing_Creep => throw new System.NotImplementedException(),
            _  => 0d
        };

        bool isHoming =
            (_currentState == ServoState.Homing_Search) ||
            (_currentState == ServoState.Homing_Retry) ||
            (_currentState == ServoState.Homing_Creep);

        if(_isOnLimitSensorPositive && targetVelocity > 0d)
        {
            targetVelocity = 0d;
            if(!isHoming)
            {
                IsError = true;
                _currentState = ServoState.Error;
                return;
            }
        }

        if(_isOnLimitSensorNegative && targetVelocity < 0d)
        {
            targetVelocity = 0d;
            if (!isHoming)
            {
                IsError = true;
                _currentState = ServoState.Error;
                return;
            }
        }

        double referenceSpeed = _currentState == ServoState.Jogging ? jogSpeed : maxSpeed;
        double accelRate = referenceSpeed / (accelTime * 0.001f);
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

    private double RunJogging()
    {
        if (_isOnJogForward)
            return jogSpeed;
        else if (_isOnJogReverse)
            return -jogSpeed;

        _currentState = ServoState.Idle;
        return 0d;
    }
    private double RunPositioning()
    {
        return 0;
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


    #endregion

    #region Public Method
    public void SetServoOn(bool isOn)
    {
        if(isOn)
        {
            IsReady = true;
            _currentState = ServoState.Idle;
            _homeOffset_Unit = 0;
        }
        else
        {
            IsReady = false;
            IsBusy = false;
            InPosition = false;
            IsError = false;
            OPRComplete = false;
            _currentState = ServoState.Off;
        }
    }    
    #endregion
}
