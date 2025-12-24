using Unity.VisualScripting.Antlr3.Runtime.Tree;
using UnityEngine;
using UnityEngine.Events;

public class InverterController : MonoBehaviour
{
    #region Variables
    [Tooltip("\"모터 회전에 사용할 샤프트 리지드바디를 연결해 주세요. 연결하지 않을 시 \n게임오브젝트 안에 어태치되어 있는 리지드바디를 자동으로 가져옵니다.")]
    public Rigidbody shaft;
    [Tooltip("\"모터 회전에 사용할 샤프트 컨피규러블조인트를 연결해 주세요. 연결하지 않을 시 \n게임오브젝트 안에 어태치되어 있는 컨피규러블조인트를 자동으로 가져옵니다.")]
    public ConfigurableJoint joint;

    [Header("모터 기본 성능")]
    [Delayed]
    public int poleCount = 4;                   //극수(짝수로만 가능함)
    [Delayed]
    public float maxFrequency = 60f;        //최대 주파수
    [Delayed]
    public float maxRPM = 1800f;             //정격 회전수    
    [Delayed]
    public float accelTime = 1.0f;              //가속시간(0 -> Max까지 도달하는데 걸리는 시간
    [Delayed]
    public float decelTime = 1.0f;              //감속시간(Max -> 0까지 도달하는데 걸리는 시간

    [Header("다단 속도 설정")]
    public bool useStep = false;                //단단 속도 제어. true로 할 경우 인버터 직접 제어함수가 작동하지 않게 바뀜
                                                            //SetTargetHz, IncreaseTargetHz, DecreaseTargetHz
    public float[] stepFrequencies = new float[8]
    {
        0f, 10f, 30f, 0f, 60f, 0f, 0f, 0f
    };

    [Header("아날로그 입력 설정")]
    public bool useAnalogInput = false;       //아날로그 신호로 제어. true로 할 경우 인버터 직접 제어함수가 작동하지 않게 바뀜
                                                              //SetTargetHz, IncreaseTargetHz, DecreaseTargetHz
    public int analogMaxResolution = 4000; //분해능(미쯔비시 : 4000, LS : 16000)

    [Header("상태 모니터링")]
    [Delayed, Range(0f, 240f), SerializeField]
    private float _targetHz = 0.0f;                //지령 주파수
    private int _analogInputValue = 0;          //아날로그 지령
    [SerializeField] float _currentHz = 0.0f;       //현재 주파수
    [SerializeField] float _currentRPM = 0.0f;     //현재 RPM

    private bool _isRun = false;
    private bool _isOnForward = false;      //정회전
    private bool _isOnReverse = false;      //역회전
    private bool _isOnLow = false;          //저속
    private bool _isOnMiddle = false;       //중속
    private bool _isOnHigh = false;         //고속

    //인버터의 상태 변화에 대한 델리게이트
    public UnityEvent<bool> onChangedRun;
    public UnityEvent<bool> onChangedForward;
    public UnityEvent<bool> onChangedReverse;
    public UnityEvent<bool> onChangedRL;
    public UnityEvent<bool> onChangedRM;
    public UnityEvent<bool> onChangedRH;
    public UnityEvent<int> onChangedAnalog;
    public UnityEvent<float> onChangedTargetHz;
    public UnityEvent<float> onChangedCurrentHz;
    public UnityEvent<float> onChangedCurrentRPM;
    #endregion

    #region Property
    public float GetCurrentHz => _currentHz;
    public float GetCurrentRPM => _currentRPM;

    //모터의 운전 상태 변화(On/Off)에 대한 프로퍼티
    public bool IsRun
    {
        get => _isRun;
        //내부에서만 변경 가능
        private set
        {
            //변경되지 않았으면 return
            if (_isRun == value)
                return;

            //최신 상태로 갱신
            _isRun = value;
            //갱신된 상태를 등록된 함수들에게 알림
            onChangedRun?.Invoke(value);
        }
    }
    //정방향 회전 상태 변화에 대한 프로퍼티
    public bool STF
    {
        get => _isOnForward;
        set
        {
            //변경되지 않았으면 return
            if (_isOnForward == value)
                return;

            //정회전 상태를 최신 상태로 갱신하고 그게 true라면
            if(_isOnForward = value)
            {
                //역회전 상태를 false로 만들고 역회전 상태를 등록된 함수들에게 알림
                onChangedReverse?.Invoke(_isOnReverse = false);
            }

            //정회전 상태를 등록된 함수들에게 알림
            onChangedForward?.Invoke(value);
            onChangedTargetHz?.Invoke(CalculateTargetHz());
        }
    }

    //역방향 회전 상태 변화에 대한 프로퍼티
    public bool STR
    {
        get => _isOnReverse;
        
        set
        {
            //변화가 없으면 return
            if (_isOnReverse == value)
                return;

            //역회전 상태를 최신 상태로 갱신하고 그 값이 true면
            if (_isOnReverse = value)
            {
                //정회전 상태를 false로 갱신하고 등록된 함수들에게 알림
                onChangedForward?.Invoke(_isOnForward = false);
            }

            //역회전 갱신 상태를 등록된 함수들에게 알림
            onChangedReverse?.Invoke(value);
            onChangedTargetHz?.Invoke(CalculateTargetHz());
        }
    }
    //저속 회전 상태 변화에 대한 프로퍼티
    public bool RL
    {
        get => _isOnLow;

        set
        {
            //다단 속도를 사용하지 않는다면 return
            if (!useStep)
                return;

            //변하지 않았다면 return
            if (_isOnLow == value)
                return;

            //최신 상태로 갱신
            _isOnLow = value;
            //갱신된 상태를 등록된 함수들에게 알림.
            onChangedRL?.Invoke(value);
            onChangedTargetHz?.Invoke(CalculateTargetHz());
        }
    }

    //중속 회전 상태 변화에 대한 프로퍼티
    public bool RM
    {
        get => _isOnMiddle;
        set
        {
            //다단 속도를 사용하지 않는다면 return
            if (!useStep)
                return;

            //변화가 없다면 return
            if (_isOnMiddle == value)
                return;

            //최신 상태로 갱신
            _isOnMiddle = value;
            //갱신된 상태를 등록된 함수들에게 알림
            onChangedRM?.Invoke(value);
            onChangedTargetHz?.Invoke(CalculateTargetHz());
        }
    }

    //고속 회전 상태 변화에 대한 프로퍼티
    public bool RH
    {
        get => _isOnHigh;
        set
        {
            //다단 속도를 사용하지 않는다면 return
            if (!useStep)
                return;

            //값이 변경되지 않았다면 return
            if (_isOnHigh == value)
                return;

            //최신 상태로 갱신
            _isOnHigh = value;
            //갱신된 상태를 등록된 함수들에게 알림
            onChangedRH?.Invoke(value);
            onChangedTargetHz?.Invoke(CalculateTargetHz());
        }
    }

    //아날로그 입력값의 변화에 대한 프로퍼티
    public int AnalogInput
    {
        get => _analogInputValue;
        set
        {
            //아날로그 입력을 사용하지 않는다면 return
            if (!useAnalogInput)
                return;
            //아날로그 입력값이 동일하면 return
            if (_analogInputValue == value)
                return;

            //아날로그 입력값을 최신 상태로 갱신
            _analogInputValue = value;
            //갱신된 아날로그 값을 등록된 함수들에게 알림
            onChangedAnalog?.Invoke(value);

            //아날로그값을 주파수로 변환해 목표주파수 재설정
            _targetHz = (float)value / analogMaxResolution * maxFrequency; 
            //재설정된 목표 주파수를 등록된 함수들에게 알림
            onChangedTargetHz?.Invoke(_targetHz);
        }
    }

    //현재 주파수에 대한 프로퍼티
    public float CurrentHz
    {
        get => Mathf.Abs(_currentHz);
        //외부에서는 변경하지 못하고 내부에서만 변경가능
        private set
        {
            //최신 상태로 갱신후 등록된 함수들에게 알림
            _currentHz = value;
            onChangedCurrentHz?.Invoke(Mathf.Abs(value));
        }
    }

    //현재 RPM에 대한 프로퍼티
    public float CurrentRPM
    {
        get => Mathf.Abs(_currentRPM);
        //외부에서는 변경하지 못하고 내부에서만 변경가능
        private set
        {
            //최신 상태로 갱신 후, 등록된 함수들에게 알림
            _currentRPM = value;
            onChangedCurrentRPM?.Invoke(Mathf.Abs(value));
        }
    }
    #endregion

    #region Unity Event Method
    private void Awake()
    {
        //회전시킬 샤프트가 비어 있다면
        if(shaft == null)
        {
            //게임 오브젝트에 어태치되어 있는 리지드 바디를 찾아 넣는다.
            shaft = GetComponent<Rigidbody>();
        }

        //리지드바디를 찾았다면
        if(shaft != null)
        {
            //자동 무게 중심을 해제하고
            shaft.automaticCenterOfMass = false;
            shaft.automaticInertiaTensor = false;
            //중력 해제
            shaft.useGravity = false;
        }

        //컨피규러블조인트가 비어있다면
        if(joint == null)
        {
            //게임오브젝트에 어태치된 컨피규러블조인트를 찾아 넣는다.
            joint = GetComponent<ConfigurableJoint>();
        }

        //컨피규러블 조인트를 찾았다면
        if(joint != null)
        {
            //x,y,z축 이동을 금지하고
            joint.xMotion = ConfigurableJointMotion.Locked;
            joint.yMotion = ConfigurableJointMotion.Locked;
            joint.zMotion = ConfigurableJointMotion.Locked;
            //x,y축 회전 금지
            joint.angularXMotion = ConfigurableJointMotion.Locked;
            joint.angularYMotion = ConfigurableJointMotion.Locked;  
            //z축 회전만 자유롭게 설정
            joint.angularZMotion = ConfigurableJointMotion.Free;
        }

    }

    private void FixedUpdate()
    {
        float finalTargetHz = GetFinalTargetHz();
        shaft.angularVelocity = -transform.forward * CalculateCurrentSpeed(finalTargetHz);
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        maxRPM = 120f * maxFrequency / 4f;
    }
#endif
#endregion

    #region Private Method
    private float GetFinalTargetHz()
    {
        //운전신호가 없으면 정지
        if (!STF && !STR)
        {
            IsRun = false;

            return 0f;
        }

        IsRun = true;
        float direction = STF ? 1f : -1f;

        //다단 속도 확인
        int hzStep = 0;
        if (RL) hzStep += 1;
        if(RM) hzStep += 2;
        if(RH) hzStep += 4;


        if(hzStep > 0)
        {
            
            return direction * stepFrequencies[hzStep];
        }

        return direction * _targetHz;
    }
    private float CalculateCurrentSpeed(float targetHz)
    {
        float rampRate = maxFrequency / (targetHz != 0 ? accelTime : decelTime);
        CurrentHz = Mathf.MoveTowards(_currentHz, targetHz, rampRate * Time.fixedDeltaTime);
        CurrentRPM = (_currentHz / maxFrequency) * maxRPM;

        return _currentRPM * 0.10472f;
    }

    private float CalculateTargetHz()
    {
        if (!STF && !STR)
            return 0f;


        //다단 속도 확인
        int hzStep = 0;
        if (RL) hzStep += 1;
        if (RM) hzStep += 2;
        if (RH) hzStep += 4;


        if (hzStep > 0)
        {
            return stepFrequencies[hzStep];
        }

        return _targetHz;
    }
    #endregion

    #region Public Method
    public void SetTargetHz(float target)
    {
        if (useStep)
            return;

        if (useAnalogInput)
            return;

        if (_targetHz == target)
            return;

        _targetHz = target;
        onChangedTargetHz?.Invoke(target);
    }
    public void IncreaseTargetHz(float increase)
    {
        if (useStep)
            return;

        if (useAnalogInput)
            return;

        _targetHz = Mathf.Clamp(_targetHz + increase, 0f, maxFrequency);
        onChangedTargetHz?.Invoke(_targetHz);
    }
    public void DecreaseTargetHz(float decrease)
    {
        if (useStep)
            return;

        if (useAnalogInput)
            return;

        _targetHz = Mathf.Clamp(_targetHz - decrease, 0f, maxFrequency);
        onChangedTargetHz?.Invoke(_targetHz);
    }
    public void SetAnalogInputValue(float value)
    {
        AnalogInput = (int)value;
    }
    #endregion
}

