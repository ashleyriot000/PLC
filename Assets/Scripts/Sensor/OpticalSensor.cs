using UnityEditor;
using UnityEngine;
using UnityEngine.Events;

public class OpticalSensor : MonoBehaviour
{
    public LayerMask detectableLayer;
    public string detectableTag;
    public string detectableName;
    public float detectableDistance = 1f;

    public UnityEvent<bool> onChangedDetect;
    public UnityEvent<Rigidbody> onDetectedBody;

    private bool _hasDetected = false;
    private Rigidbody _detectedBody = null;
    private Vector3 _detectedPoint;

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

    private void Update()
    {
        Ray ray = new Ray(transform.position, transform.forward);
        if(Physics.Raycast(ray, out RaycastHit hit, detectableDistance, detectableLayer))
        {
            _detectedPoint = hit.point;
            if (!string.IsNullOrEmpty(detectableTag) && hit.transform.gameObject.tag != detectableTag)
            {
                HasDetected = false;
                _detectedBody = null;
                return;
            }

            if(!string.IsNullOrEmpty(detectableName) && !hit.transform.gameObject.name.Contains(detectableName))
            {
                HasDetected = false;
                _detectedBody = null;
                return;
            }

            if (_detectedBody != hit.rigidbody)
            {
                _detectedBody = hit.rigidbody;
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
}
