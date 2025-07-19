using UnityEngine;
using System;

[CreateAssetMenu(menuName = "OVP/Vehicle/New Vehicle")]
public class VehicleSettings : ScriptableObject
{
    [Header ("Setup")]
    public EngineSettings motor;
    public TransmissionSettings transmission;
    public AssistSettings assists;

    [Space]
    public VehicleExtraValues extras;
}

[Serializable]
public class VehicleExtraValues
{
    [Tooltip("Accel axis is used for brake input")]
    public bool accelAxisIsBrake;

    [Tooltip("Brake input will act as reverse input")]
    public bool brakeIsReverse;

    [Tooltip("Automatically hold ebrake if it's pressed while parked")]
    public bool holdEbrakePark;

    public float burnoutThreshold = 0.9f;
    public float burnoutSpin = 5;
    [Range(0, 0.9f)]
    public float burnoutSmoothness = 0.5f;

    [Tooltip("Lower center of mass by suspension height")]
    public bool suspensionCenterOfMass;
    public Vector3 centerOfMassOffset;

    public ForceMode wheelForceMode = ForceMode.Acceleration;
    public ForceMode suspensionForceMode = ForceMode.Acceleration;
}