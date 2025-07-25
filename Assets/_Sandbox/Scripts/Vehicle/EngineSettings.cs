using System;
using UnityEngine;

[CreateAssetMenu (menuName = "OVP/Vehicle/New Engine")]
public class EngineSettings : ScriptableObject
{
    public EngineTempValues temp;
    public EngineBoostValues boost;
    public EnginePerformanceValues performance;
}

[Serializable]
public class EngineTempValues
{
    [Tooltip("Throttle curve, x-axis = input, y-axis = output")]
    public AnimationCurve inputCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);
}

[Serializable]
public class EngineBoostValues
{
    public bool canBoost = true;
    public float boostPower = 1;

    [Tooltip("X-axis = local z-velocity, y-axis = power")]
    public AnimationCurve boostPowerCurve = AnimationCurve.EaseInOut(0, 0.1f, 50, 0.2f);
    public float maxBoost = 1;
    public float boostBurnRate = 0.01f;
}

[Serializable]
public class EnginePerformanceValues
{
    [Tooltip("X-axis = RPM in thousands, y-axis = torque.  The rightmost key represents the maximum RPM")]
    public AnimationCurve torqueCurve = AnimationCurve.EaseInOut(0, 0, 8, 1);

    [Range(0, 0.99f)]
    [Tooltip("How quickly the engine adjusts its RPMs")]
    public float inertia;

    [Tooltip("Can the engine turn backwards?")]
    public bool canReverse;

    [Tooltip("Exponent for torque output on each wheel")]
    public float driveDividePower = 3;
}