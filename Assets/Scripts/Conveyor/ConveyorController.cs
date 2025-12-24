using UnityEngine;
using realvirtual;
using System.Collections.Generic;
using UnityEngine.Events;

public class ConveyorController : MonoBehaviour
{
    #region Variables
    public ConveyorBelt belt;                   //컨베이어 벨트
    public LayerMask movableLayer;        //이동가능한 오브젝트 레이어(Nothing 혹은 Everything 이면 검사안함)
    public string movableTag;                  //이동가능한 오브젝트 태그(비어 있으면 검사 안함)
    public string movableName;              //이동가능한 오브젝트 이름(비어 있으면 검사 안함)

    public string forwardAddress;
    public string reverseAddress;

    public float maxSpeed = 10f;       //초당 최대 이동 속도  

    public UnityEvent<bool> onChangedForward;
    public UnityEvent<bool> onChangedReverse;

    private bool _isOnForward = false;      //정방향 전류 On
    private bool _isOnReverse = false;      //역방향 전류 On

    private float _targetSpeed = 0f;
    private float _currentSpeed = 0f;

    private List<Rigidbody> _productList;
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
            SetMoveDirection(_isOnForward, _isOnReverse);
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
            SetMoveDirection(_isOnForward, _isOnReverse);
        }
    }

    #endregion

    #region Unity event method
    private void Awake()
    {
        //트리거된 제품들을 담아 놓을 리스트
        _productList = new List<Rigidbody>();

        //컨베이어 벨트가 비어 있다면 찾아서 넣어라.
        if(belt == null)
            belt = GetComponent<ConveyorBelt>();
    }

    private void FixedUpdate()
    {
        _currentSpeed = Mathf.MoveTowards(_currentSpeed, _targetSpeed, maxSpeed * Time.fixedDeltaTime);
        belt.speed = _currentSpeed;
        Vector3 moveDelta = _currentSpeed * Time.fixedDeltaTime * transform.forward ;
        foreach (Rigidbody rb in _productList)
        {
            rb.MovePosition(rb.position + moveDelta);
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (movableLayer.value != 0 && (movableLayer.value & 1 << other.gameObject.layer) == 0)
            return;

        if (!string.IsNullOrEmpty(movableTag) && movableTag != other.gameObject.tag)
            return;

        if (!string.IsNullOrEmpty(movableName) && !other.gameObject.name.Contains(movableName))
            return;

        if (other.attachedRigidbody != null)
            _productList.Add(other.attachedRigidbody);
    }

    private void OnTriggerExit(Collider other)
    {
        if (movableLayer.value != 0 && (movableLayer.value & 1 << other.gameObject.layer) == 0)
            return;

        if (!string.IsNullOrEmpty(movableTag) && movableTag != other.gameObject.tag)
            return;

        if (!string.IsNullOrEmpty(movableName) &&  !other.gameObject.name.Contains(movableName))
            return;

        if (other.attachedRigidbody != null)
            _productList.Remove(other.attachedRigidbody);
    }
    #endregion


    private void SetMoveDirection(bool isOnForward, bool isOnReverse)
    {
        float speed = 0f;

        if (isOnForward)
            speed = maxSpeed;

        else if (isOnReverse)
            speed += -maxSpeed;

        _targetSpeed = speed;
    }
}
