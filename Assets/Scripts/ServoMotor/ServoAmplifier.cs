using UnityEngine;

[RequireComponent(typeof(Rigidbody)), RequireComponent(typeof(ConfigurableJoint))]
public class ServoAmplifier : MonoBehaviour
{
    public enum ActuatorType
    {
        Linear,
        Rotary
    }
    public enum UnitType
    {
        MM,
        Degree,
        Pulse
    }
    public enum ServoState
    {
        Off = 0,
        Idle,
        Jogging,
        Positioning,
        Homing_Search,
        Homing_Retry,
        Homing_Creep,
        Error
    }

    public Rigidbody body;
    public ConfigurableJoint joint;

    public ActuatorType actuatorType = ActuatorType.Linear;
    public UnitType usedUnit;


}
