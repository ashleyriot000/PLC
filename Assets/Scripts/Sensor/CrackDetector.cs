using UnityEngine;
using UnityEngine.Events;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine.Animations;

public class CrackDetector : MonoBehaviour
{
    public enum ScanAxis
    {
        None = 0,
        XPlus,
        XMinus,
        YPlus,
        YMinus,
        ZPlus,
        ZMinus,
        Max
    }
    public enum DetectingType
    {
        BrighterThan,
        DarkerThan
    }

    public Transform scanner;
    public GameObject scanningEffect;
    public Transform scanCamera;
    public RectTransform scanImage;
    public float imageSize = 256f;
    public string MapName = "_BaseMap";
    public ScanAxis forwardAxis = ScanAxis.ZPlus;
    public ScanAxis scanlineAxis = ScanAxis.XPlus;
    public ScanAxis scanDirection = ScanAxis.YMinus;
    public LayerMask detectableLayer;
    public float detectableDistance = 1f;
    public DetectingType detectionType = DetectingType.DarkerThan;
    public float brightnessThreshold = 0.5f;
    public float scanDistance = 1f;
    public float scanSpeed = 1f;
    public bool needAllScan = false;

    public int count = 10;
    public float interval = 0.1f;

    public UnityEvent<bool> onChangedDetect;

    private bool _isScanning = false;

    public bool HasDetected
    {
        get => _hasFinalDetected;
        private set
        {
            if (_hasFinalDetected == value)
                return;

            _hasFinalDetected = value;
            onChangedDetect?.Invoke(value);
        }
    }

    private bool _hasDetected = false;
    private bool _hasFinalDetected = false;
    private bool[] _detectedLines;
    private Vector3[] _detectedPoints;

    private void Start()
    {
        _detectedLines = new bool[count];
        _detectedPoints = new Vector3[count];

        Vector3 forwardDir = forwardAxis switch
        {
            ScanAxis.XPlus => scanner.right,
            ScanAxis.XMinus => -scanner.right,
            ScanAxis.YPlus => scanner.up,
            ScanAxis.YMinus => -scanner.up,
            ScanAxis.ZPlus => scanner.forward,
            ScanAxis.ZMinus => -scanner.forward,
            _ => Vector3.zero
        };

        Vector3 lineAxis = scanlineAxis switch
        {
            ScanAxis.XPlus => scanner.right,
            ScanAxis.XMinus => -scanner.right,
            ScanAxis.YPlus => scanner.up,
            ScanAxis.YMinus => -scanner.up,
            ScanAxis.ZPlus => scanner.forward,
            ScanAxis.ZMinus => -scanner.forward,
            _ => Vector3.zero
        };

        Vector3 scanDir = scanDirection switch
        {
            ScanAxis.XPlus => Vector3.right,
            ScanAxis.XMinus => -Vector3.right,
            ScanAxis.YPlus => Vector3.up,
            ScanAxis.YMinus => -Vector3.up,
            ScanAxis.ZPlus => Vector3.forward,
            ScanAxis.ZMinus => -Vector3.forward,
            _ => Vector3.zero
        };


        scanCamera.localRotation = Quaternion.Euler(forwardDir);
        scanCamera.localPosition = Vector3.Lerp(scanner.localPosition, scanner.localPosition + (count - 1) * interval *lineAxis + scanDir * scanDistance, 0.5f);
        scanningEffect.SetActive(false);
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        _detectedLines = new bool[count];
        _detectedPoints = new Vector3[count];

        Vector3 forwardDir = forwardAxis switch
        {
            ScanAxis.XPlus => scanner.right,
            ScanAxis.XMinus => -scanner.right,
            ScanAxis.YPlus => scanner.up,
            ScanAxis.YMinus => -scanner.up,
            ScanAxis.ZPlus => scanner.forward,
            ScanAxis.ZMinus => -scanner.forward,
            _ => Vector3.zero
        };

        Vector3 lineAxis = scanlineAxis switch
        {
            ScanAxis.XPlus => scanner.right,
            ScanAxis.XMinus => -scanner.right,
            ScanAxis.YPlus => scanner.up,
            ScanAxis.YMinus => -scanner.up,
            ScanAxis.ZPlus => scanner.forward,
            ScanAxis.ZMinus => -scanner.forward,
            _ => Vector3.zero
        };

        Vector3 scanDir = scanDirection switch
        {
            ScanAxis.XPlus => Vector3.right,
            ScanAxis.XMinus => -Vector3.right,
            ScanAxis.YPlus => Vector3.up,
            ScanAxis.YMinus => -Vector3.up,
            ScanAxis.ZPlus => Vector3.forward,
            ScanAxis.ZMinus => -Vector3.forward,
            _ => Vector3.zero
        };


        scanCamera.localRotation = Quaternion.Euler(forwardDir);
        scanCamera.localPosition = Vector3.Lerp(scanner.localPosition, scanner.localPosition + (count - 1) * interval * lineAxis + scanDir * scanDistance, 0.5f);
    }
#endif

    public bool DetectCrack()
    {
        bool detected = false;

        Vector3 forwardDir = forwardAxis switch
        {
            ScanAxis.XPlus => scanner.right,
            ScanAxis.XMinus => -scanner.right,
            ScanAxis.YPlus => scanner.up,
            ScanAxis.YMinus => -scanner.up,
            ScanAxis.ZPlus => scanner.forward,
            ScanAxis.ZMinus => -scanner.forward,
            _ => Vector3.zero
        };

        Vector3 lineAxis = scanlineAxis switch
        {
            ScanAxis.XPlus => scanner.right,
            ScanAxis.XMinus => -scanner.right,
            ScanAxis.YPlus => scanner.up,
            ScanAxis.YMinus => -scanner.up,
            ScanAxis.ZPlus => scanner.forward,
            ScanAxis.ZMinus => -scanner.forward,
            _ => Vector3.zero
        };

        for (int i = 0; i < count; ++i)
        {
            Ray ray = new Ray(scanner.position + lineAxis * interval * i, forwardDir);
            if (Physics.Raycast(ray, out RaycastHit hit, detectableDistance, detectableLayer))
            {
                _detectedPoints[i] = hit.point;
                if (hit.collider is not MeshCollider)
                {
                    _detectedLines[i] = false;
                    continue;
                }

                Renderer render = hit.collider.GetComponent<Renderer>();
                if (render == null)
                {
                    _detectedLines[i] = false;
                    continue;
                }

                Texture2D texture = render.material.GetTexture(MapName) as Texture2D;
                Debug.Log(texture);
                Vector2 rawUV = hit.textureCoord;
                Vector2 tiling = render.material.GetTextureScale(MapName);
                Vector2 offset = render.material.GetTextureOffset(MapName);
                Vector2 final = (rawUV * tiling) + offset;
                final.x %= 1.0f;
                final.y %= 1.0f;
                Color pixelColor = texture.GetPixelBilinear(final.x, final.y);


                if (!CheckBright(pixelColor.grayscale, brightnessThreshold))
                {
                    _detectedLines[i] = false;
                    continue;
                }

                _detectedLines[i] = true;
                detected = true;
            }
            else
            {
                _detectedPoints[i] = scanner.position + forwardDir * detectableDistance;
                _detectedLines[i] = false;
                continue;
            }
        }

        return detected;
    }


    private void Update()
    {
        if(!_isScanning)
        {
            scanner.localPosition = Vector3.MoveTowards(scanner.localPosition, Vector3.zero, scanSpeed * Time.deltaTime);
            return;
        }

        Vector3 scanDir = scanDirection switch
        {
            ScanAxis.XPlus => Vector3.right,
            ScanAxis.XMinus => -Vector3.right,
            ScanAxis.YPlus => Vector3.up,
            ScanAxis.YMinus => -Vector3.up,
            ScanAxis.ZPlus => Vector3.forward,
            ScanAxis.ZMinus => -Vector3.forward,
            _ => Vector3.zero
        };

        Vector3 destination = scanDir * scanDistance;
        scanner.localPosition = Vector3.MoveTowards(scanner.localPosition, destination, scanSpeed * Time.deltaTime);
        scanImage.anchoredPosition = Vector2.MoveTowards(scanImage.anchoredPosition, Vector2.zero, imageSize / scanDistance * scanSpeed * Time.deltaTime);
        if(DetectCrack())
        {
            _hasDetected = true;
            if(!needAllScan)
            {
                ScanEnd(_hasDetected);
                return;
            }
        }
        
        if (scanner.localPosition == destination)
        {
            ScanEnd(_hasDetected);
        }
    }


    public void Scan()
    {
        _hasDetected = false;
        HasDetected = false;
        _isScanning = true;
        scanningEffect.SetActive(true);
        scanImage.anchoredPosition = Vector2.up * -imageSize;
    }

    private void ScanEnd(bool result)
    {
        HasDetected = result;
        _isScanning = false;
        scanningEffect.SetActive(false);
    }

    private bool CheckBright(float color, float threshold)
    {
        if (detectionType == DetectingType.BrighterThan)
        {
            return color > threshold;
        }

        return color < threshold;
    }

    private void OnDrawGizmos()
    {
        if (!_isScanning)
        {
            return;
        }

        Vector3 forwardDir = forwardAxis switch
        {
            ScanAxis.XPlus => scanner.right,
            ScanAxis.XMinus => -scanner.right,
            ScanAxis.YPlus => scanner.up,
            ScanAxis.YMinus => -scanner.up,
            ScanAxis.ZPlus => scanner.forward,
            ScanAxis.ZMinus => -scanner.forward,
            _ => Vector3.zero
        };

        Vector3 lineAxis = scanlineAxis switch
        {
            ScanAxis.XPlus => scanner.right,
            ScanAxis.XMinus => -scanner.right,
            ScanAxis.YPlus => scanner.up,
            ScanAxis.YMinus => -scanner.up,
            ScanAxis.ZPlus => scanner.forward,
            ScanAxis.ZMinus => -scanner.forward,
            _ => Vector3.zero
        };        

        for (int i = 0; i < count; ++i)
        {
            //검출될 경우 붉은 색 라인 그리기
            Handles.color = _detectedLines[i] ? Color.red : Color.green;
            Handles.DrawLine(scanner.position + lineAxis * interval * i, _detectedPoints[i]);
        }
    }
}
