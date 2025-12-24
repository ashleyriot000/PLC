using UnityEngine;
using UnityEngine.Events;

public class MagneticSensor : MonoBehaviour
{
    #region Variables
    [Tooltip("감지에 사용할 콜라이더를 연결해 주세요. 연결하지 않을 시 \n게임오브젝트 안에 어태치되어 있는 콜라이더를 자동으로 가져옵니다.")]
    public Collider sensor;
    public LayerMask detectableLayer;       //감지 가능한 레이어
    public string detectableName;             //감지 가능한 이름

    public string sensorAddress;               //PLC 입출력 주소

    public UnityEvent<bool> onChangedDetect;

    private bool _hasDetected = false;
    #endregion

    #region Property
    //감지 결과에 대한 프로퍼티
    public bool HasDetected
    {
        get => _hasDetected;
        set
        {
            if (_hasDetected == value)
                return;

            _hasDetected = value;
            onChangedDetect?.Invoke(value);
        }
    }
    #endregion

    #region Unity event method
    private void Awake()
    {
        if (sensor == null)
            sensor = GetComponent<Collider>();

        if (sensor != null)
            sensor.isTrigger = true;

        if (string.IsNullOrEmpty(sensorAddress))
        {
            Debug.LogWarning($"{gameObject.name} PLC에 할당될 어드레스 값이 비어있습니다.");
        }

        HasDetected = false;
    }

    private void OnTriggerEnter(Collider other)
    {
        //감지 가능한 레이어들 중에서 트리거된 게임오브젝트의 레이어가 포함되는지 확인
        if ((detectableLayer.value & 1 << other.gameObject.layer) == 0)
            return;

        //감지 가능한 이름이 채워져 있을 경우, 트리거된 게임오브젝트의 이름과 비교해 같지 않으면 return
        if (!string.IsNullOrEmpty(detectableName) && !other.gameObject.name.Contains(detectableName))
            return;

        //레이어와 이름 모두 인정되어 감지대상이 들어왔음을 알림.
        HasDetected = true;
    }

    private void OnTriggerExit(Collider other)
    {
        //감지 가능한 레이어들 중에서 트리거된 게임오브젝트의 레이어가 포함되는지 확인해 포함되지 않으면 return
        if ((detectableLayer.value & 1 << other.gameObject.layer) == 0)
            return;

        //감지 가능한 이름이 채워져 있을 경우, 트리거된 게임오브젝트의 이름과 비교해 같지 않으면 return
        if (!string.IsNullOrEmpty(detectableName) && !other.gameObject.name.Contains(detectableName))
            return;

        //레이어와 이름 모두 인정되어 감지된 대상이 나갔음을 알림.
        HasDetected = false;
    }

    #endregion
}
