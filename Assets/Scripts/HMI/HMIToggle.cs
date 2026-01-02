using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

public class HMIToggle : MonoBehaviour
{
    //연결될 토글
    public Toggle connectedToggle;
    //isOn => true일 때 호출해야되는 함수 델리게이트
    public UnityEvent<bool> onChangedTrue;
    //isOn => false일 때 호출해야되는 함수 델리게이트
    public UnityEvent<bool> onChangedFalse;

    private void Start()
    {
        //연결할 토글이 비어있으면 게임오브젝트에 어태치된 토글을 찾아서 넣는다
        if(connectedToggle == null)
            connectedToggle = GetComponent<Toggle>();

        //찾아낸 토글이 있을 경우
        if(connectedToggle != null)
        {
            //토글 이벤트에 OnChangedValue 함수를 등록한다.
            connectedToggle.onValueChanged.AddListener(OnChangedValue);
        }
    }

    //IsOn 값에 따라 다르게 호출함.
    private void OnChangedValue(bool isOn)
    {
        if (isOn)
            onChangedTrue.Invoke(isOn);
        else
            onChangedFalse.Invoke(isOn);
    }
}
