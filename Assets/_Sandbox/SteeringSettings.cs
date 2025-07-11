using System;
using UnityEngine;

[CreateAssetMenu(menuName = "OVP/Vehicle/New Steering")]
public class SteeringSettings : ScriptableObject
{
    public SteeringExtraValues extra;
    public SteeringControlValues control;
    public SteeringVisualValues visual;
}

[Serializable]
public class SteeringExtraValues
{
    public bool limitSteer = true;
    public bool applyInReverse = true; // Limit steering in reverse?
}

[Serializable]
public class SteeringControlValues
{
    public float steerRate = 0.1f;
    [Tooltip("Curve for limiting steer range based on speed, x-axis = speed, y-axis = multiplier")]
    public AnimationCurve steerCurve = AnimationCurve.Linear(0, 1, 30, 0.1f);

    [Tooltip("Horizontal stretch of the steer curve")]
    public float steerCurveStretch = 1;
}

[Serializable]
public class SteeringVisualValues
{
    public bool rotate;
    public float maxDegreesRotation;
    public float rotationOffset;
}
