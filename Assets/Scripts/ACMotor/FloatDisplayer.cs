using UnityEngine;
using UnityEngine.UI;

public class FloatDisplayer : MonoBehaviour
{
    public Text floatText;
    public int digitCount = 0;
    public string unit = "hz";

    private void Awake()
    {
        if (floatText == null)
        {
            floatText = GetComponent<Text>();
        }
    }

    public void OnChangedValue(float value)
    {
        floatText.text = value.ToString("F"+digitCount.ToString()) + unit;
    }
}
