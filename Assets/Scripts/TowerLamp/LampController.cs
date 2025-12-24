using UnityEngine;
using UnityEngine.Events;

public class LampController : MonoBehaviour
{
    #region Variables
    [Tooltip("램프에 불이 들어오는 매터리얼이 반드시 첫번째 매터리얼이어야 합니다.")]
    public MeshRenderer lamp;           //램프 렌더러

    [Tooltip("PLC와 연결하려면 반드시 어드레스를 입력해주세요.")]
    public string address;                  //PLC 디바이스 주소

    public UnityEvent<bool> onChangedLamp;

    private Material _lampMaterial;    //램프 매터리얼
    private bool _isOn = false;
    #endregion

    #region Property
    //램프를 On/Off 시키는 함수
    public bool TurnOn
    {
        get => _isOn;
        set
        {
            if (_isOn == value) return;

            _isOn = value;
            onChangedLamp?.Invoke(value);

            //램프 매터리얼이 비어있으면 켜고/끄기 기능을 할 수 없으니 Return.
            if (_lampMaterial == null)
            {
                Debug.LogError("램프를 제어할 매터리얼이 비어있습니다.");
                return;
            }

            //true면 Emission(발광) 체크
            if (_isOn)
                _lampMaterial.EnableKeyword("_EMISSION");
            //false면 Emission(발광) 체크 해제.
            else
                _lampMaterial.DisableKeyword("_EMISSION");
        }
    }
    #endregion

    #region Unity event method
    private void Awake()
    {
        //인스펙터에 lamp가 비어있으면
        if (lamp == null)
        {
            //게임 오브젝트에 어태치되어 있는 매쉬렌더러를 찾아 넣어준다.
            lamp = GetComponent<MeshRenderer>();
        }

        //램프를 찾은 상태라면
        if (lamp != null)
        {
            //램프의 매터리얼을 _lampMaterial에 넣어준다.
            _lampMaterial = lamp.materials[0];
        }

        if (string.IsNullOrEmpty(address))
            Debug.LogWarning($"{gameObject.name} PLC에 할당될 어드레스 값이 비어있습니다.");

        _isOn = _lampMaterial.IsKeywordEnabled("_EMISSION");

        TurnOn = false;
    }
    #endregion
}

