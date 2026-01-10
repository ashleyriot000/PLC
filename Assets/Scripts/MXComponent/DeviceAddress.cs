using UnityEngine;

[System.Serializable]
public class DeviceAddress
{   
    public bool useDevice = true;
    public bool useDoubleWord = false;
    public bool isLocked = false;
    public string label = "";
    public string address = "";

    [TextArea] public string description = "";
    [TextArea] public string comment = "";

    public DeviceAddress(string description)
    {
        this.description = description;
    }
}








