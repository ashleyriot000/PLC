using UnityEngine;
using UnityEngine.Events;

public class RollerConveyorController : MXObject
{
    #region Variables
    public HingeJoint[] rollers;        //돌리고 싶은 롤러 연결(반드시 힌지조인트가 있어야 함)

    public DeviceAddress forwardAddress = new("롤러 정회전");
    public DeviceAddress reverseAddress = new("롤러 역회전");

    
    public int maxRPM = 60;     //정격 회전수(분당 회전수)
    [Min(0.1f)]
    public float accelTime = 1f;    //가감속 시간

    
    public UnityEvent<bool> onForwardChanged;       //정회전 신호 변화에 대한 델리게이트
    public UnityEvent<bool> onReverseChanged;       //역회전 신호 변화에 대한 델리게이트

    //내부 연산용 변수 
    private bool _isOnForward = false;
    private bool _isOnReverse = false;
    private float _maxVelocity = 0;
    private float _targetVelocity = 0f;
    private float _currentVelocity = 0f;
    #endregion

    #region Property
    public bool IsOnForward
    {
        get => _isOnForward;
        //외부에서 변경하지 못하도록 private 추가
        private set
        {
            //동일한 값이면 무시하고 넘어감
            if (_isOnForward == value)
                return;

            //최신 값으로 갱신
            _isOnForward = value;
            //갱신된 값을 등록된 콜백함수들에게 알림
            onForwardChanged?.Invoke(value);
            //목표 속도 계산
            CalculateTargetSpeed(_isOnForward, _isOnReverse);
        }
    }

    public bool IsOnReverse
    {
        get => _isOnReverse;
        //외부에서 변경하지 못하도록 private 추가
        private set
        {
            //동일한 값이면 무시하도록 함.
            if (_isOnReverse == value)
                return;

            //최신 값으로 갱신            
            _isOnReverse = value;
            //갱신된 값을 등록된 콜백함수들에게 알림
            onReverseChanged?.Invoke(value);
            //목표 속도 계산
            CalculateTargetSpeed(_isOnForward, _isOnReverse);
        }
    }
    #endregion

    #region UNITY EVENT METHOD
    private void Awake()
    {
        //시작시 모든 롤러의 설정을 동일하게 설정.
        foreach(var roller in rollers)
        {
            roller.useMotor = true;
            var motor = roller.motor;
            motor.force = 1000f;
            motor.freeSpin = false;
            motor.targetVelocity = 0f;
            roller.motor = motor;
        }
    }

    private void Start()
    {
        //정회전 디바이스를 사용하고, 어드레스가 채워져 있으면 
        if (forwardAddress.useDevice && !string.IsNullOrEmpty(forwardAddress.address))
        {
            //PLC 데이터 자동 읽어오기 등록
            MXRequester.Get.AddDeviceAddress(forwardAddress.address, OnForwardRead);
        }
        //역회전 디바이스를 사용하고, 어드레스가 채워져 있으면 
        if (forwardAddress.useDevice && !string.IsNullOrEmpty(forwardAddress.address))
        {
            //PLC 데이터 자동 읽어오기 등록
            MXRequester.Get.AddDeviceAddress(reverseAddress.address, OnReverseRead);
        }
    }


    private void FixedUpdate()
    {
        //현재 회전각속도 구하기.
        _currentVelocity = Mathf.MoveTowards(_currentVelocity, _targetVelocity,  
            (_maxVelocity / accelTime) * Time.fixedDeltaTime);


        //모든 롤러에 동일한 각속도 적용하기
        foreach (var roller in rollers)
        {
            var motor = roller.motor;
            motor.targetVelocity = _currentVelocity;
            roller.motor = motor;
        }
    }

#if UNITY_EDITOR
    //인스펙터창에서 변수를 수정할 경우 자동으로 호출됨. 
    private void OnValidate()
    {
        //초당 최대 각속도를 구한다.
        _maxVelocity = maxRPM * 6f;
    }
#endif
    #endregion

    #region Private method
    //목표 속도 구하는 메서드
    private void CalculateTargetSpeed(bool isOnForward, bool isOnReverse)
    {
        //둘다 동일한 신호일 경우(둘다 On, 둘다 Off)
        if (isOnForward == isOnReverse)
        {
            _targetVelocity = 0f;
            return;
        }

        //정회전만 On일 경우
        if (isOnForward)
        {
            //목표 속도 설정
            _targetVelocity = _maxVelocity;
            return;
        }
        //목표 속도 설정
        _targetVelocity = -_maxVelocity;
    }
    #endregion

    #region Public method
    //PLC로부터 받은 신호 처리 함수들
    public void OnForwardRead(short readValue)
    {
        IsOnForward = readValue == 0 ? false : true;
    }
    public void OnReverseRead(short readValue)
    {
        IsOnReverse = readValue == 0 ? false : true;
    }
    #endregion
}
