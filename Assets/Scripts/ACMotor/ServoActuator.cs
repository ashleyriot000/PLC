using UnityEngine;
using UnityEngine.Events;

[RequireComponent(typeof(ConfigurableJoint))]
public class ServoActuator : MonoBehaviour
{
    #region Enum Types
    //액츄에이터의 구동 방식
    public enum ActuatorType 
    { 
        Linear,
        Rotary
    }

    //컨트롤 유닛 타입
    public enum ControlUnit
    {
        mm,
        degree,
        pulse
    }

    //액츄에이터 상태
    public enum ActuatorState
    {
        Idle, 
        Jogging,
        Positioning,
        Homing_Search,
        Homing_Retry,
        Homing_Creep,
        Error
    }
    #endregion

    #region Variables
    [Header("선두 어드레스 및 축 번호")]
    public int headAddress;
    public int AxisNumber;

    [Header("필수 출력 디바이스 주소")]
    public string SONAddress;   //서보 ON
    public string ORGAddress;   //원점 복귀

    [Header("필수 입력 디바이스 주소")]
    public string BUSYAddress;      //Busy 신호
    public string INPOSAddress;     //목표 도착 신호
    public string PDOGAddress;      //근점 도그 신호
    public string LSPAddress;       //상한 리미트 신호
    public string LSNAddress;       //하한 리미트 신호

    [Header("[1] 기구 및 단위 설정")]
    public ActuatorType actuatorType = ActuatorType.Linear;
    public ControlUnit controlUnit = ControlUnit.mm;

    [Header("[2] 기구 파라미터")]
    
    public float motorResolution = 131072f;   //서보모터의 분해능
    public float gearRatio = 1.0f;                  //연결된 기어의 감속비
    public float ballscrewLead = 10f;             //1회전당 전진 길이

    [Header("[3] 속도 파라미터")]
    [Tooltip("위치 결정 운전시 최대 속도")]
    public float maxSpeed = 500.0f;

    [Tooltip("수동 JOG 운전시 속도")]
    public float jogSpeed = 100.0f;

    [Tooltip("원점 도그를 찾으러 갈 때 속도(고속)")]
    public float homingHighSpeed = 200.0f;

    [Tooltip("도그 감지 후 정밀하게 멈출 때 속도(저속)")]
    public float homingCreepSpeed = 20.0f;

    [Header("[4] 가감속 시간")]
    //가감속 시간
    public float accelTime = 100.0f;
    //타겟 포지션 도착 오차 
    public float inPosWidth = 0.1f;

    [Header("모니터링")]
    public float currentPos_Unit;
    public int currentPulse;

    //펄스값이 변경되면 그값을 받을 함수들을 넣을 델리게이트
    public UnityEvent<int> onChangedPulse;
    //준비 상태값이 변경되면 그 값을 받을 함수들을 넣을 델리게이트
    public UnityEvent<bool> onChangedReady;
    //에러 상태값이 변경되면 그 값을 받을 함수들을 넣을 델리게이트
    public UnityEvent<bool> onChangedError;
    //명령 수행 상태값이 변경되면 그 값을 받을 함수들을 넣을 델리게이트
    public UnityEvent<bool> onChangedBusy;


    [Header("센서 입력")]
    public bool isOnLimitSensorPositive = false;      //상한 리미트
    public bool isOnLimitSensorNegative = false;    //하한 리미트
    public bool isOnProximityDOG = false;             //근점 도그

    public bool IsReady = false;    //명령을 받을 수 있는 상태 여부
    public bool IsBusy = false;      //명령 수행중
    public bool InPosition = false; //명령 수행 완료 여부
    public bool IsError = false; //에러 경고
    public bool OPR_Complete = false;   //원점을 찾았다 여부

    //내부에서 처리하기 용이하게 하기 위한 
    private bool _cmd_ServoON = false; //서보 모터에 전원을 켜라는 명령이 들어온 상태
    private bool _cmd_StartPos = false;  //지정 위치로 이동하라는 명령이 들어온 상태
    private bool _cmd_StartOPR = false; //원점 복귀하라는 명령이 들어온 상태
    private bool _cmd_prev_StartOPR = false; //이전 원점 복귀 명령
    private bool _cmd_JogFwd = false;    //수동 조그 전진 명령이 들어온 상태
    private bool _cmd_JogRev = false;    //수동 조그 후퇴 명령이 들어온 상태
    private int _cmd_TargetPulse = 0;    //목표 위치 펄스값


    private ConfigurableJoint _joint;
    private float _currentVelocity_Unit = 0f;
    private float _internalTarget_Unit = 0f;

    [SerializeField]
    private ActuatorState _currentState = ActuatorState.Idle;
    private bool _homingHitDog = false;
    private int _homingDir = -1;
    private float _homeOffset_Unit = 0f;
    #endregion


    #region Property
    //현재 펄스값에 대한 프로퍼티
    public int GetCurrentPulse
    {
        get => currentPulse;
        set
        {
            if (currentPulse == value)
                return;

            currentPulse = value;
            onChangedPulse?.Invoke(value);
        }
    }



    public bool IsOnLSP
    {
        get => isOnLimitSensorPositive;
        set => isOnLimitSensorPositive = value;
    }
    public bool IsONLSN
    {
        get => isOnLimitSensorNegative;
        set => isOnLimitSensorNegative = value;
    }
    public bool IsOnPDOG
    {
        get => isOnProximityDOG;
        set => isOnProximityDOG = value;
    }



    public bool Ready
    {
        get => IsReady;
        set
        {
            if (IsReady == value)
                return;

            IsReady = value;
            onChangedReady?.Invoke(value);
        }
    }

    public bool Error
    {
        get => IsError;
        set
        {
            if (IsError == value)
                return;

            IsError = value;
            onChangedError?.Invoke(value);
        }
    }

    public bool Busy
    {
        get => IsBusy;
        set
        {
            if (IsBusy == value)
                return;

            IsBusy = value;
            onChangedBusy?.Invoke(value);
        }
    }
    #endregion

    #region UNITY EVENT METHOD
    private void Awake()
    {
        _joint = GetComponent<ConfigurableJoint>();

        //시작 위치 계산
        float startPos = (actuatorType == ActuatorType.Linear) ?
            -transform.localPosition.z * 1000.0f : transform.localRotation.eulerAngles.x;

        currentPulse = PhysToPulse(startPos);
        _internalTarget_Unit = PulseToUnit(currentPulse);
        _homeOffset_Unit = 0f;
    }

    private void Start()
    {
        
    }
    #endregion

    //모터의 힘(Torque)를 조절하는 함수
    private void SetJointForce(float force)
    {
        JointDrive drive = new JointDrive
        {
            positionSpring = force,
            positionDamper = 100,
            maximumForce = float.MaxValue
        };

        if (actuatorType == ActuatorType.Linear)
        {
            _joint.zDrive = drive;
        }
        else
        {
            _joint.angularXDrive = drive;
        }
    }

    //펄스 -> 단위(mm/degree/pulse) 변환
    public float PulseToUnit(int pulse)
    {
        //입력받은 펄스값 / 모터의 1회전당 펄스값 => 회전수
        float revs = (float)pulse / motorResolution;
        //서보모터의 회전수 * 기어감속비 => 샤프트 회전수
        float shaftRevs = revs * gearRatio;
        float unitValue = 0f;

        //리니어 타입일 경우
        if(controlUnit == ControlUnit.mm)
        {
            //전진 길이로 변환. 샤프트 회전수 * 볼스크류리드 => 전진 길이
            unitValue = shaftRevs * ballscrewLead;
        }
        else if(controlUnit == ControlUnit.degree) //회전 타입일 경우
        {
            //샤프트 회전수 * 360도 => 회전각도
            unitValue = shaftRevs * 360f;
        }
        else
        {
            //펄스 -> 펄스
            unitValue = pulse;
        }

        return unitValue + _homeOffset_Unit;
    }

    //단위(mm/degree) -> 펄스(Pulse) 변환
    public int PhysToPulse(float physValocity)
    {
        //현재 이동값 - 원점 보정값 => 원점 기준 위치 
        float relativePos = physValocity - _homeOffset_Unit;
        //각 제어방식에 맞게 샤프트의 회전수를 구하고 
        float shaftRevs = (controlUnit == ControlUnit.mm) ? relativePos / ballscrewLead : relativePos / 360f;
        //샤프트 회전수 / 기어비 => 서보모터의 회전수
        float motorRevs = shaftRevs / gearRatio;
        //모터 회전수 * 모터의 분해능 => 현재 이동한 펄스값
        return (int)(motorRevs * motorResolution);
    }


    //조인트에 물리적인 힘(Torque)을 가하는 함수
    private void ApplyPhysics(float value)
    {

        //작동 방식이 리니어일 경우
        if (actuatorType == ActuatorType.Linear)
        {
            float mmValue = value;
            //컨트롤 유닛이 mm단위일 때
            if (controlUnit == ControlUnit.pulse)
            {
                mmValue = (value / motorResolution) * gearRatio * ballscrewLead;
            }

            //조인트에 목표 위치값을 입력
            _joint.targetPosition = new Vector3(0, 0, mmValue / 1000.0f);
        }
        else   //작동방식이 회전일 경우
        {
            //회전 각을 대입했는데
            float degValue = value;
            //컨트롤 유닛이 펄스일 경우
            if(controlUnit == ControlUnit.pulse)
            {
                //입력받은 펄스값을 회전 각도 값으로 변환
                degValue = (value / motorResolution) * gearRatio * 360.0f;
            }

            //조인트에 목표 회전값을 입력
            _joint.targetRotation = Quaternion.Euler(degValue, 0, 0);
        }
    }

    public int defaultHomingDirection = 1;
    //원점 복귀 명령
    private void SetupHoming()
    {
        //원점 복귀하기 위해 세팅되어야 하는 값들.
        _currentState = ActuatorState.Homing_Search;
        _homingDir = defaultHomingDirection;
        OPR_Complete = false;
        InPosition = false;
        _homingHitDog = false;
        Busy = true;
    }

    //원점 복귀 완료
    private void CompletedHoming()
    {
        //1. 현재 속도를 0으로 내려서 위치 고정
        _currentVelocity_Unit = 0f;

        //2. 현재 위치를 기억해서 여기부터 원점이라는 걸 저장.
        _homeOffset_Unit = _internalTarget_Unit;

        //3. 내외부에 원점 확인했음을 알림.
        OPR_Complete = true;
        _cmd_StartOPR = false;
        _currentState = ActuatorState.Idle;
        Busy = false;
    }

    //외부 제어 함수
    public void Cmd_SetServoON(bool isON)
    {
        _cmd_ServoON = isON;
        if(!isON)
        {
            Ready = false;
            Busy = false;
            InPosition = false;
            Error = false;
            OPR_Complete = false;
            _cmd_ServoON = false; 
            _cmd_StartPos = false;
            _cmd_StartOPR = false; 
            _cmd_prev_StartOPR = false;
            _cmd_JogFwd = false;
            _cmd_JogRev = false;
            _cmd_TargetPulse = 0;  
        }
        else
        {
            _homeOffset_Unit = 0f;
        }
    }
    public void Cmd_Positioning(int pulse)
    {
        if (!IsReady || IsBusy)
            return;

        _cmd_TargetPulse = pulse;
        _cmd_StartPos = true;
        Busy = true;
    }
    public void Cmd_JogForward(bool isOn)
    {
        if (!IsReady || IsBusy)
            return;

        _cmd_JogFwd = isOn;
    }
    public void Cmd_JogReverse(bool isOn)
    {
        if (!IsReady || IsBusy)
            return;

        _cmd_JogRev = isOn;
    }
    public void Cmd_Homing()
    {
        if (!IsReady || IsBusy)
            return;
        
        _cmd_StartOPR = true;
    }
    public void Cmd_Reset()
    {
        //에러 상태이면
        if(Error)
        {
            //에러를 없애고
            Error = false;
            //다시 대기모드로 전환해라
            _currentState = ActuatorState.Idle;

            _cmd_StartOPR = false;
            _cmd_JogFwd = false;
            _cmd_JogRev = false;
            _cmd_StartPos = false;
            _cmd_TargetPulse = 0;
        }
    }

    private void FixedUpdate()
    {
        if (_joint == null)
            return;

        if (!_cmd_ServoON && !IsError)
        {
            Ready = false;
            _currentVelocity_Unit = 0f;
            //모터에 토크를 0으로 없앰
            SetJointForce(0);
            return;
        }

        Ready = true;
        //모터에 강한 토크를 줘서 고정
        SetJointForce(100000);

        bool isRisingEdge_OPR = (_cmd_StartOPR && !_cmd_prev_StartOPR);
        _cmd_prev_StartOPR = _cmd_StartOPR;

        float targetVelocity = 0f;

        switch (_currentState)
        {
            case ActuatorState.Idle:
                if (_cmd_JogFwd || _cmd_JogRev)
                {
                    _currentState = ActuatorState.Jogging;
                }
                else if (_cmd_StartPos)
                {
                    _currentState = ActuatorState.Positioning;
                    Busy = true;
                }
                else if (isRisingEdge_OPR)
                {
                    if (OPR_Complete)
                    {
                        //이미 원점은 찾았고, 원점 위치로 고속 이동.
                        Cmd_Positioning(0);
                    }
                    else
                    {
                        SetupHoming();
                    }
                }                    
                break;
            case ActuatorState.Jogging:         //수동 운전
                //수동 전진 On일 때
                if (_cmd_JogFwd)
                {
                    //정방향 조그 이동 속도 적용.
                    targetVelocity = jogSpeed;
                }
                else if (_cmd_JogRev)   //수동 후퇴 On일때
                {
                    //역방향 조그 이동 속도 적용.
                    targetVelocity = -jogSpeed;
                }
                else
                {
                    //대기상태로 돌아감
                    _currentState = ActuatorState.Idle;
                }                    
                break;
            case ActuatorState.Positioning:
                //펄스 명령을 단위(mm)로 변환
                float targetPos = PulseToUnit(_cmd_TargetPulse);
                //남은 거리를 계산
                float distance = targetPos - _internalTarget_Unit;
                //도착 여부 판정
                if(Mathf.Abs(distance) <= inPosWidth)
                {
                    targetVelocity = 0f;

                    //강제로 위치와 속도를 고정(떨림 방지)
                    _currentVelocity_Unit = 0f;
                    _internalTarget_Unit = targetPos;
                    ApplyPhysics(_internalTarget_Unit);


                    Busy = false;
                    InPosition = true;
                    _cmd_StartPos = false;
                    _currentState = ActuatorState.Idle;
                }
                else
                {
                    InPosition = false;
                    if(Mathf.Abs(distance) < 2.0f)
                    {
                        targetVelocity = distance > 1f ?
                            Mathf.Sign(distance) * 10f :
                            Mathf.Sign(distance);
                    }
                    else
                    {
                        
                        //제동 거리 예측
                        float stopDist = (Mathf.Abs(_currentVelocity_Unit) * accelTime * 0.001f);
                        //제동 거리보다 남은 거리가 짧으면 속도 대폭 감소
                        if (Mathf.Abs(distance) < stopDist)
                        {
                            targetVelocity = distance * 2.0f;
                        }
                        else
                        {
                            targetVelocity = Mathf.Sign(distance) * maxSpeed;
                        }
                    }   
                }
                break;
            case ActuatorState.Homing_Search:
                //원점을 모르는 상태 -> 찾아다녀야 함.
                targetVelocity = homingHighSpeed * _homingDir; //설정된 방향으로 고속이동

                //근점 도그 발견
                if(isOnProximityDOG)
                {
                    _currentState = ActuatorState.Homing_Creep; //저속모드로 변환해 원점을 찾아라.
                    _homingHitDog = true;
                }
                else if((_homingDir == -1 && isOnLimitSensorNegative) ||(_homingDir == 1 && isOnLimitSensorPositive))
                {
                    _currentVelocity_Unit = 0f;
                    _homingDir = -_homingDir; //방향을 반전
                    _currentState = ActuatorState.Homing_Retry;
                }  
                break;
            case ActuatorState.Homing_Retry:
                targetVelocity = homingHighSpeed * _homingDir;
                if(isOnProximityDOG)
                {
                    _currentState = ActuatorState.Homing_Creep;
                    _homingHitDog = true;
                }
                break;
            case ActuatorState.Homing_Creep:
                targetVelocity = -homingCreepSpeed;

                //도그를 밟으면 기본 원점 복귀 방향으로 저속 이동해서 원점을 찾는다.
                if(!isOnProximityDOG && _homingHitDog)
                {
                    CompletedHoming();
                    targetVelocity = 0f;
                }
                break;
            case ActuatorState.Error:
                targetVelocity = 0f;
                break;
        }


        bool isHoming =
            (_currentState == ActuatorState.Homing_Search) ||
            (_currentState == ActuatorState.Homing_Retry) ||
            (_currentState == ActuatorState.Homing_Creep);

        //정방향으로 이동하는데 상한 리미트 센서가 켜지면
        if(isOnLimitSensorPositive && targetVelocity > 0f)
        {
            //정지시키고 
            targetVelocity = 0f;
            //원점복귀 중이 아니라면
            if(!isHoming)
            {
                //에러로 판단
                Error = true;
                _currentState = ActuatorState.Error;
            }
        }

        //역방향으로 이동하는데 하한 리미트 센서가 켜지면
        if (isOnLimitSensorNegative && targetVelocity < 0f)
        {
            //정지시키고 
            targetVelocity = 0f;
            //원점복귀 중이 아니라면
            if (!isHoming)
            {
                //에러로 판단
                Error = true;
                _currentState = ActuatorState.Error;
            }
        }

        //가감속 적용하기
        //공식 : 현재 속도 = 이전 속도 + (가속도 * 시간)
        float referenceSpeed = (_currentState == ActuatorState.Jogging) ? jogSpeed : maxSpeed;
        float accelRate = referenceSpeed / (accelTime * 0.001f); //초당 속도 변화량
        //현재 초당 이동 속도
        _currentVelocity_Unit = Mathf.MoveTowards(_currentVelocity_Unit, targetVelocity, accelRate * Time.fixedDeltaTime);
        
        //위치 적분
        _internalTarget_Unit += _currentVelocity_Unit * Time.fixedDeltaTime;
        //물리적인 위치로 보내기        
        ApplyPhysics(_internalTarget_Unit);

        currentPos_Unit = _internalTarget_Unit;

        //현재 물리 위치를 펄스로 변환해서 피드백용으로 보내기
        GetCurrentPulse = PhysToPulse(_internalTarget_Unit);
    }
}
