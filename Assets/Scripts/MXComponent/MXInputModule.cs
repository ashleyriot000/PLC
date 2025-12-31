using UnityEngine;


public class MXInputModule : MonoBehaviour
{
    //디바이스 주소
    public string address;
    //데이터 갱신상태의 알림을 받고 싶은지
    public bool needCallback = false;
   
    public void OnChangeValue(bool isOn)
    {
        if(needCallback)
            MXRequester.Get.AddSetDeviceRequest(address, (short)(isOn ? 1 : 0), OnChangedCallback);
        else
            MXRequester.Get.AddSetDeviceRequest(address, (short)(isOn ? 1 : 0));
    }

    public void OnChangedCallback(bool success)
    {
        Debug.Log($"{address}에 데이터 갱신을 {(success ? "성공했습니다." : "실패했습니다")}");
    }
}
