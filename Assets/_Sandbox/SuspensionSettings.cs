using UnityEngine;
using System;

[CreateAssetMenu(menuName = "OVP/Vehicle/New Suspension")]
public class SuspensionSettings : ScriptableObject
{
    public AxleExtraValues extra;
    public AxleBrakeValues brake;
    public AxleSteeringValues steering;
    public AxleCamberValues camber;
    public AxleSpringValues spring;
}

[Serializable]
public class AxleExtraValues
{
    [Tooltip("Generate a capsule collider for hard compressions")]
    public bool generateHardCollider = true;

    [Tooltip("Multiplier for the radius of the hard collider")]
    public float hardColliderRadiusFactor = 1;

    [Tooltip("Apply forces to prevent the wheel from intersecting with the ground, not necessary if generating a hard collider")]
    public bool applyHardContactForce = true;

    [Tooltip("Apply suspension forces at ground point")]
    public bool applyForceAtGroundContact = true;

    [Tooltip("Apply suspension forces along local up direction instead of ground normal")]
    public bool leaningForce;
}

[Serializable]
public class AxleBrakeValues
{
    public float brakeForce;
    public float ebrakeForce;
}

[Serializable]
public class AxleSteeringValues
{
    [Range(-180, 180)]
    public float steerRangeMin;
    [Range(-180, 180)]
    public float steerRangeMax;

    [Tooltip("How much the wheel is steered")]
    public float steerFactor = 1;
    [Range(-1, 1)]
    public float steerAngle;

    [Tooltip("Effect of Ackermann steering geometry")]
    public float ackermannFactor;
}

[Serializable]
public class AxleCamberValues
{
    [Tooltip("The camber of the wheel as it travels, x-axis = compression, y-axis = angle")]
    public AnimationCurve camberCurve = AnimationCurve.Linear(0, 0, 1, 0);
    [Range(-89.999f, 89.999f)]
    public float camberOffset;

    [Tooltip("Adjust the camber as if it was connected to a solid axle, opposite wheel must be set")]
    public bool solidAxleCamber;

    [Tooltip("Angle at which the suspension points out to the side")]
    [Range(-89.999f, 89.999f)]
    public float sideAngle;
    [Range(-89.999f, 89.999f)]
    public float casterAngle;
    [Range(-89.999f, 89.999f)]
    public float toeAngle;

    [Tooltip("Wheel offset from its pivot point")]
    public float pivotOffset;
}

[Serializable]
public class AxleSpringValues
{
    public float suspensionDistance;

    [Tooltip("Should be left at 1 unless testing suspension travel")]
    [Range(0, 1)]
    public float targetCompression;
    public float springForce;

    [Tooltip("Force of the curve depending on it's compression, x-axis = compression, y-axis = force")]
    public AnimationCurve springForceCurve = AnimationCurve.Linear(0, 0, 1, 1);

    [Tooltip("Exponent for spring force based on compression")]
    public float springExponent = 1;
    public float springDampening;

    [Tooltip("How quickly the suspension extends if it's not grounded")]
    public float extendSpeed = 20;

    public float hardContactForce = 50;
    public float hardContactSensitivity = 2;
}

