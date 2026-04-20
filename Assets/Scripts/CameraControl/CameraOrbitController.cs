using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;

public class CameraOrbitController : MonoBehaviour
{

    [Header("Target Settings")]
    public Transform target;
    public float distance = 10f;

    [Header("Speed Settings")]
    public float panSpeed = 0.5f;
    public float xSpeed = 120f;
    public float ySpeed = 120;
    public float zoomSpeed = 2.0f;

    [Header("Limit Settings")]
    [Range(-90f, 0f)]
    public float yMinLimit = -20f; //아래로 내려다 볼 때 한계각도
    [Range(0, 90)]
    public float yMaxLimit = 80f; //위로 올려다 볼 때 한계각도
    public float minDistance = 2.0f;
    public float maxDistance = 50f;

    [Header("Smoothness Settings")]
    [Range(1f, 20f)]
    public float smoothness = 10f;              //값이 클수록 빠릿한 움직임, 작을 수록 부드러운 움직임
    public float panningSmoothness = 10f;   //패닝 전용 

    public UnityEvent<bool> onPressedPan;
    public UnityEvent<bool> onPressedOrbit;
    public UnityEvent<bool> onPressedFreeLook;

    //제어할 카메라
    private Camera _cam;

    //유저의 입력 정보 저장 변수들
    private bool _isPressedPan = false;
    private bool _isPressedOrbit = false;
    private bool _isPressedFreeLook = false;
    private float _deltaScroll = 0f;
    private float _x = 0f;
    private float _y = 0f;

    //부드럽게 보간하기 위해 중간값 저장 변수들
    private float _currentX = 0f;
    private float _currentY = 0f;
    private float _currentDistance = 0f;
    private Vector3 _targetPosition = Vector3.zero;

    void Start()
    {
        //타겟이 비어 있으면
        if(target == null)
        {
            //타겟 게임오브젝트를 생성하고 카메라가 바라보는 방향의 지정된 거리 위치에 배치.
            GameObject go = new GameObject("Cam Target");
            target = go.transform;
            _targetPosition = target.position = transform.position + (transform.forward * distance);
            _currentDistance = distance;
        }
        else //타겟이 있다면
        {
            //게임오브젝트가 타겟을 향해 바라보도록 방향과 거리를 계산해 알아내 적용.
            Vector3 direction = target.position - transform.position;
            transform.rotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
            _targetPosition = target.position;
            _currentDistance = distance = direction.magnitude;
        }

        //카메라가 비어져 있다면 카메라 찾아서 넣어준다.
        if (_cam == null)
            _cam = GetComponent<Camera>();

        //시작시 카메라의 상하 앵글이 범위 안에서 시작할 수 있도록 각도 수정.
        Vector3 angles = transform.eulerAngles;
        if(angles.x > yMaxLimit)
            angles.x = yMaxLimit;
        if(angles.x < yMinLimit)
            angles.x = yMinLimit;

        transform.eulerAngles = angles;
        
        //중간값과 최종값을 동일하게 시작하도록 만듬.
        _x = _currentX = angles.y;
        _y = _currentY = angles.x;        
    }

    void LateUpdate()
    {
        //이전 프레임동안 지나간 시간을 기록.
        float dt = Time.deltaTime;

        //마우스 왼쪽 버튼을 누르고 있을 때
        if(_isPressedPan)
        {
            Vector2 delta = Mouse.current.delta.ReadValue() * panSpeed * 0.01f;
            //목표위치값 설정
            _targetPosition -= (transform.right * delta.x) + (transform.up * delta.y);
        }

        //마우스 휠 버튼을 누르고 있을 때
        if(_isPressedOrbit)
        {
            Vector2 delta = Mouse.current.delta.ReadValue();
            delta.x *= xSpeed * dt;
            delta.y *= ySpeed * dt;

            //목표 방향 설정
            _x += delta.x;
            _y -= delta.y;
            _y = Mathf.Clamp(_y, yMinLimit, yMaxLimit);
        }


        if(_isPressedFreeLook)
        {
            Vector2 delta = Mouse.current.delta.ReadValue();
            delta.x *= xSpeed * dt;
            delta.y *= ySpeed * dt;

            //목표 방향 설정
            _x += delta.x;
            _y -= delta.y;
            _y = Mathf.Clamp(_y, yMinLimit, yMaxLimit);
        }

        //현재 휠 스크롤값을 기준으로 줌 목표값 설정
        distance -= _deltaScroll * zoomSpeed;
        distance = Mathf.Clamp(distance, minDistance, maxDistance);

        //현재 중간값에서 목표값으로 자연스럽게 보간해 움직이도록 중간값 수정.
        _currentX = Mathf.Lerp(_currentX, _x, smoothness * dt);
        _currentY = Mathf.Lerp(_currentY, _y, smoothness * dt);
        _currentDistance = Mathf.Lerp(_currentDistance, distance, smoothness * dt);


        if(_isPressedFreeLook)
        {
            Quaternion rot = Quaternion.Euler(_currentY, _currentX, 0f);
            _targetPosition = target.position = transform.position + (rot * Vector3.forward * distance);
        }
        else
        {
            target.position = Vector3.Lerp(target.position, _targetPosition, dt * panningSmoothness);
        }

        //보간된 중간값을 적용.
        Quaternion rotation = Quaternion.Euler(_currentY, _currentX, 0f);
        Vector3 backward = new Vector3(0f, 0f, -_currentDistance);
        transform.SetPositionAndRotation(rotation * backward + target.position, rotation);
    }

    //마우스 휠 버튼을 눌러 카메라 이동
    public void OnPan(InputValue value)
    {
        _isPressedPan = value.isPressed;
        onPressedPan?.Invoke(_isPressedPan);
    }

    //마우스 왼쪽 버튼을 눌러 포커싱된 오브젝트 돌려보기 
    public void OnOrbit(InputValue value)
    {
        _isPressedOrbit = value.isPressed;
        onPressedOrbit?.Invoke(_isPressedOrbit);
    }

    //마우스 오른쪽 버튼을 눌러 주변 둘러보기.
    public void OnFreeLook(InputValue value)
    {
        _isPressedFreeLook = value.isPressed;
        onPressedFreeLook?.Invoke(_isPressedFreeLook);
    }

    //휠를 스크롤해 줌 Up/Down
    public void OnZoom(InputValue value)
    {
        _deltaScroll = value.Get<float>();
    }

    //더블 클릭해 원하는 오브젝트를 포커싱.
    public void OnFocus()
    {
        if (_cam == null)
            return;

        Ray ray = _cam.ScreenPointToRay(Mouse.current.position.ReadValue());
        if(Physics.Raycast(ray, out RaycastHit hit))
        {
            Debug.Log(hit.transform.gameObject);
            _targetPosition = hit.transform.position;
        }
    }
}
