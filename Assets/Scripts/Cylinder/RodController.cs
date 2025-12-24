using UnityEngine;
using UnityEngine.Events;

public class RodController : MonoBehaviour
{
    #region Variables
    [Tooltip("연결하지 않을 시 게임오브젝트 안에 어태치되어 있는 리지드바디를 자동으로 가져옵니다.")]
    public Rigidbody rod;
    [Tooltip("연결하지 않을 시 게임오브젝트 안에 어태치되어 있는 리지드바디를 자동으로 가져옵니다.")]
    public ConfigurableJoint joint;

    public string forwardAddress;
    public string reverseAddress;
    
    public float pressure = 10f;            //피스톤 압력

    public UnityEvent<bool> onChangedForward;
    public UnityEvent<bool> onChangedReverse;

    private bool _isOnForward;      //전진 밸브 On/Off
    private bool _isOnReverse;      //후퇴 밸브 On/Off
    private float _direction = 0f;    //이동 방향(1: 전진, -1:후퇴)
    #endregion

    #region Property
    //전진 밸브의 상태 변화에 대한 프로퍼티
    public bool IsOnForward
    {
        get => _isOnForward;
        set
        {
            //변화가 없으면 return
            if (_isOnForward == value)
                return;

            //최신 상태로 갱신
            _isOnForward = value;
            //갱신상태를 등록된 함수들에게 알림.
            onChangedForward?.Invoke(value);
            //전후진 방향 재설정
            _direction = SetDirection(_isOnForward, _isOnReverse);
        }
    }

    //후퇴 밸브의 상태 변화에 대한 프로퍼티
    public bool IsOnReverse
    {
        get => _isOnReverse;
        set
        {
            //변화가 없으면 return
            if (_isOnReverse == value)
                return;

            //최신 상태로 갱신
            _isOnReverse = value;
            //갱신 상태를 등록된 함수들에게 알림
            onChangedReverse?.Invoke(value);
            //전후진 방향 재설정
            _direction = SetDirection(_isOnForward, _isOnReverse);
        }
    }
    #endregion

    #region Unity event method
    private void Awake()
    {
        //Rod가 비어 있으면 게임오브젝트에 어태치된 리지드바디 찾아서 넣기
        if(rod == null)
            rod = GetComponent<Rigidbody>();

        //리지드바디를 찾았으면 
        if(rod != null)
        {
            //중력 제거.
            rod.useGravity = false;
            //회전값 고정
            rod.constraints = RigidbodyConstraints.FreezeRotation;
            //충돌 감지 방식 Continuous로 변경
            rod.collisionDetectionMode = CollisionDetectionMode.Continuous;
        }

        //조인트가 비어있으면 게임오브젝트에 어태치된 컨피규러블조인트를 찾아 넣기
        if (joint == null)
        {
            joint = GetComponent<ConfigurableJoint>();
        }

        //컨피규러블조인트를 찾았으면
        if (joint != null)
        {
            //연결된 바디가 있으면 해제.
            joint.connectedBody = null;
            //x,y축 이동 모션을 락을 걸고, z축만 자유롭게
            joint.xMotion = ConfigurableJointMotion.Locked;
            joint.yMotion = ConfigurableJointMotion.Locked;
            joint.zMotion = ConfigurableJointMotion.Free;
            //x,y,z축 회전 모션을 모두 락을 걸기
            joint.angularXMotion = ConfigurableJointMotion.Locked;
            joint.angularYMotion = ConfigurableJointMotion.Locked;
            joint.angularZMotion = ConfigurableJointMotion.Locked;
        }

        if (string.IsNullOrEmpty(forwardAddress) || string.IsNullOrEmpty(reverseAddress))
        {
            Debug.LogWarning($"{gameObject.name} PLC에 할당될 어드레스 값이 비어있습니다.");
        }

        IsOnForward = false;
        IsOnReverse = false;
    }

    private void FixedUpdate()
    {
        //Z축을 기준으로 전진,후퇴방향으로 압력값만큼 밀어낸다.
        rod.AddRelativeForce(_direction * pressure * Vector3.forward);
    }
    #endregion

    #region Private method
    //방향 설정 함수
    private float SetDirection(bool isOnForward, bool isOnReverse)
    {
        //전진밸브와 후퇴 밸브가 둘다 같은 On이거나 Off이면 방향이 0반환
        if (isOnForward == IsOnReverse)
            return 0f;

        //전진 밸브가 On이면 1 반환
        if (isOnForward)
            return 1f;
            
        //모두 아니면 -1 반환
        return -1f;
    }
    #endregion

}
