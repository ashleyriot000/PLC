using UnityEngine;
using UnityEngine.Events;

public class ACMotorController : MonoBehaviour
{
    #region Variables
    [Tooltip("회전시킬 샤프트를 연결해 주세요. 연결하지 않을 시 \n게임오브젝트 안에 어태치되어 있는 리지드바디를 자동으로 가져옵니다.")]
    public Rigidbody shaft;     //샤프트 리지드바디
    public string forwardAddress;
    public string reverseAddress;
    
    public float maxVelocity = 10f;     //초당 최대 회전 속도    
    public float torquePerSec = 1f;     //초당 토크

    public UnityEvent<bool> onChangedForward;
    public UnityEvent<bool> onChangedReverse;

    private bool _isOnForward = false;      //정방향 전류 On
    private bool _isOnReverse = false;    //역방향 전류 On
    private float _torqueDirection = 0f;     //회전 방향    
    #endregion

    #region Property
    //정방향 회전On/Off 프로퍼티
    public bool IsOnForward
    {
        get => _isOnForward;
        set
        {
            if (_isOnForward == value)
                return;

            if (_isOnForward = value)
            {
                _isOnReverse = false;
                onChangedReverse?.Invoke(_isOnReverse);
            }

            onChangedForward?.Invoke(_isOnForward);
            _torqueDirection = SetRotationDirection(_isOnForward, _isOnReverse);
        }
    }

    //역방향 회전 On/Off 프로퍼티
    public bool IsOnReverse
    {
        get => _isOnReverse;
        set
        {
            if (_isOnReverse == value)
                return;

            if (_isOnReverse = value)
            {
                _isOnForward = false;
                onChangedForward?.Invoke(_isOnForward);
            }

            onChangedReverse?.Invoke(_isOnReverse);
            _torqueDirection = SetRotationDirection(_isOnForward, _isOnReverse);
        }
    }
    #endregion

    #region Unity Event Method
    private void Awake()
    {
        //샤프트가 비어 있으면 자동으로 게임오브젝트에 어태치되어 있는 리지드바디를 가져온다.
        if(shaft == null)
        {
            shaft = GetComponent<Rigidbody>();
        }

        //샤프트가 들어 있으면
        if(shaft != null)
        {
            //초당 최대 회전 속도를 적용한다.
            shaft.maxAngularVelocity = maxVelocity;
        }

        if(string.IsNullOrEmpty(forwardAddress) || string.IsNullOrEmpty(reverseAddress))
        {
            Debug.LogWarning($"{gameObject.name} PLC에 할당될 어드레스 값이 비어있습니다.");
        }
        
        //처음 시작시 모두 OFF로 설정
        IsOnForward = IsOnReverse = false;
    }

    private void FixedUpdate()
    {
        //Z축 방향 기준으로 회전 토크를 발생시킨다.
        shaft.AddRelativeTorque(_torqueDirection * torquePerSec * Time.fixedDeltaTime * Vector3.forward);
    }
    #endregion

    #region Private Method
    private float SetRotationDirection(bool isOnForward, bool isOnReverse)
    {
        if (isOnForward)
            return -1f;

        else if (isOnReverse)
            return 1f;

        return 0f;
    }
    #endregion
}
