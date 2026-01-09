
[System.Serializable]
public class DeviceAddress
{
    public bool useDevice;
    public string description;
    public string address;
    public string comment;

    public DeviceAddress(string description)
    {
        this.description = description;
    }
}








