using UnityEditor;
using UnityEngine;
using UnityEngine.Events;

public class OpticalSensor : MonoBehaviour
{
    public LayerMask detectableLayer;       //검출 가능한 레이어 - 반드시 설정해야 함.
    public string detectableTag;                //검출가능한 태그 - 입력하지 않으면 무시. 입력하면 해당 태그가 있어야만 검출됨.
    public string detectableName;            //검출가능한 게임오브젝트 이름. 입력하지 않으면 무시. 입력하면 해당 이름이 포함되어야 검출됨.
    public float detectableDistance = 1f;   //검출가능한 거리

    public string address;                        //디바이스 주소

    
    public UnityEvent<bool> onChangedDetect;    
    public UnityEvent<Rigidbody> onDetectedBody;

    //내부 변수
    private bool _hasDetected = false;
    private Rigidbody _detectedBody = null;
    private Vector3 _detectedPoint;

    //검출 결과의 변화에 대한 프로퍼티
    public bool HasDetected
    {
        get => _hasDetected;
        //내부에서만 수정가능하도록 
        private set
        {
            //결과가 이미 같을 경우에는 무시
            if (_hasDetected == value)
                return;

            //최신 상태로 갱신
            _hasDetected = value;
            //등록된 콜백함수들에게 최신 결과를 알림.
            onChangedDetect?.Invoke(value);
        }
    }

    private void Update()
    {
        //자신의 위치에서 바라보는 방향으로 Ray 생성
        Ray ray = new Ray(transform.position, transform.forward);
        //Ray를 발사해 충돌하는 게임오브젝트 확인
        //Physics.Raycast(레이, out 충돌정보, 확인거리, 확인할 레이어)
        if(Physics.Raycast(ray, out RaycastHit hit, detectableDistance, detectableLayer))
        {
            //검출한 위치 저장.
            _detectedPoint = hit.point;
            
            //태그가 비어있지 않을때 태그가 검출가능한 태그가 아닐 경우 검출하지 못한 것으로 간주하고 반환
            if (!string.IsNullOrEmpty(detectableTag) && hit.transform.gameObject.tag != detectableTag)
            {
                HasDetected = false;
                _detectedBody = null;
                return;
            }

            //검출가능한 이름이 비어있지 않을 때, 충돌한 게임오브젝트의 이름에 검출가능한 이름이 포함되어 있지 않으면 검출하지 못한
            //것으로 간주하고 반환
            if(!string.IsNullOrEmpty(detectableName) && !hit.transform.gameObject.name.Contains(detectableName))
            {
                HasDetected = false;
                _detectedBody = null;
                return;
            }

            //이전에 검출한 게임오브젝트와 현재 검출한 게임오브젝트가 서로 다르다면 
            if (_detectedBody != hit.rigidbody)
            {
                _detectedBody = hit.rigidbody;
                //새로운 검출인자가 있다는 것을 콜백 함수들에게 알림
                onDetectedBody?.Invoke(_detectedBody);
            }

            HasDetected = true;
        }
        else
        {
            HasDetected = false;
            _detectedBody = null;
        }
    }

    //씬뷰에서 그리고 싶은 것들이 있을 때 이 함수를 사용
#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        if (_hasDetected)
        {
            //검출될 경우 붉은 색 라인 그리기
            Handles.color = Color.red;
            Handles.DrawLine(transform.position, _detectedPoint);
        }
        else
        {
            //검출 안될 경우 녹색 라인 그리기
            Handles.color = Color.green;
            Handles.DrawLine(transform.position, transform.position + transform.forward * detectableDistance);
        }

    }
#endif
}
