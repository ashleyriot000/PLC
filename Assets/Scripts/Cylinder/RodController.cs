using UnityEngine;
using UnityEngine.Events;

public class RodController : MonoBehaviour
{
    [Tooltip("전후진할 Rod를 연결해 주세요. 연결하지 않을 시 \n게임오브젝트 안에 어태치되어 있는 리지드바디를 자동으로 가져옵니다.")]
    public Rigidbody rod;

    public string forwardAddress;
    public string reverseAddress;

    public float pressure = 10f;

    public UnityEvent<bool> onChangedForward;
    public UnityEvent<bool> onChangedReverse;

    private bool _isOnForward;
    private bool _isOnReverse;
    private float _direction = 0f;

    private void Awake()
    {
        if(rod == null)
            rod = GetComponent<Rigidbody>();

        if(rod != null)
        {
            rod.useGravity = false;
            rod.constraints = RigidbodyConstraints.FreezePositionX | RigidbodyConstraints.FreezePositionY | RigidbodyConstraints.FreezeRotation;
            rod.collisionDetectionMode = CollisionDetectionMode.Continuous;
        }


        if(string.IsNullOrEmpty(forwardAddress) || string.IsNullOrEmpty(reverseAddress))
        {
            Debug.LogWarning($"{gameObject.name} PLC에 할당될 어드레스 값이 비어있습니다.");
        }

        IsOnForward = false;
        IsOnReverse = false;
    }

    private void FixedUpdate()
    {
        rod.AddRelativeForce(_direction * pressure * Vector3.forward);
    }

    private float SetDirection(bool isOnForward, bool isOnReverse)
    {
        if (isOnForward == IsOnReverse)
            return 0f;

        if (isOnForward)
            return 1f;
            
        return -1f;
    }

    public bool IsOnForward
    {
        get => _isOnForward;
        set
        {
            if (_isOnForward == value)
                return;

            _isOnForward = value;
            onChangedForward?.Invoke(value);
            _direction = SetDirection(_isOnForward, _isOnReverse);
        }
    }

    public bool IsOnReverse
    {
        get => _isOnReverse;
        set
        {
            if (_isOnReverse == value)
                return;

            _isOnReverse = value;
            onChangedReverse?.Invoke(value);
            _direction = SetDirection(_isOnForward, _isOnReverse);
        }
    }
}
