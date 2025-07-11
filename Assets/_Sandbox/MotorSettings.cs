using System;
using UnityEngine;

[CreateAssetMenu (menuName = "OVP/Vehicle/New Motor")]
public class MotorSettings : ScriptableObject
{
    public MotorTempValues temp;
    public MotorBoostValues boost;
    public MotorPerformanceValues performance;
}

[Serializable]
public class MotorTempValues
{
    [Tooltip("Throttle curve, x-axis = input, y-axis = output")]
    public AnimationCurve inputCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);
    public float minPitch;
    public float maxPitch;
    [Tooltip("Increase sound pitch between shifts")]
    public bool pitchIncreaseBetweenShift;
    [Tooltip("Decrease sound pitch when the throttle is released")]
    public bool pitchDecreaseWithoutThrottle = true;
}

[Serializable]
public class MotorBoostValues
{
    public bool canBoost = true;
    public float boostPower = 1;

    [Tooltip("X-axis = local z-velocity, y-axis = power")]
    public AnimationCurve boostPowerCurve = AnimationCurve.EaseInOut(0, 0.1f, 50, 0.2f);
    public float maxBoost = 1;
    public float boostBurnRate = 0.01f;

    public AudioClip boostStart;
    public AudioClip boostEnd;
}

[Serializable]
public class MotorPerformanceValues
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