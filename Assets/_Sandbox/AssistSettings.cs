using System;
using UnityEngine;

[CreateAssetMenu(menuName ="OVP/Vehicle/New Assist")]
public class AssistSettings : ScriptableObject
{
    public AssistDriftValues drift;
    public AssistDownforceValues downforce;
    public AssistRolloverValues rollover;
    public AssistAirtimeValues airtime;
}

[Serializable]
public class AssistDriftValues
{
    [Tooltip("Variables are multiplied based on the number of wheels grounded out of the total number of wheels")]
    public bool basedOnWheelsGrounded;

    [Tooltip("How much to assist with spinning while drifting")]
    public float driftSpinAssist;
    public float driftSpinSpeed;
    public float driftSpinExponent = 1;

    [Tooltip("Automatically adjust drift angle based on steer input magnitude")]
    public bool autoSteerDrift;
    public float maxDriftAngle = 70;

    [Tooltip("Adjusts the force based on drift speed, x-axis = speed, y-axis = force")]
    public AnimationCurve driftSpinCurve = AnimationCurve.Linear(0, 0, 10, 1);

    [Tooltip("How much to push the vehicle forward while drifting")]
    public float driftPush;

    [Tooltip("Straighten out the vehicle when sliding slightly")]
    public bool straightenAssist;
}

[Serializable]
public class AssistDownforceValues
{
    public float downforceAmount = 1;
    public bool invertDownforceInReverse;
    public bool applyDownforceInAir;

    [Tooltip("X-axis = speed, y-axis = force")]
    public AnimationCurve downforceCurve = AnimationCurve.Linear(0, 0, 20, 1);
}

[Serializable]
public class AssistRolloverValues
{
    [Tooltip("Automatically roll over when rolled over")]
    public bool autoRollOver;

    [Tooltip("Roll over with steer input")]
    public bool steerRollOver;

    [Tooltip("Distance to check on sides to see if rolled over")]
    public float rollCheckDistance = 1;
    public float rollOverForce = 1;

    [Tooltip("Maximum speed at which vehicle can be rolled over with assists")]
    public float rollSpeedThreshold;
}

[Serializable]
public class AssistAirtimeValues
{
    [Tooltip("Increase angular drag immediately after jumping")]
    public bool angularDragOnJump;

    public float fallSpeedLimit = Mathf.Infinity;
    public bool applyFallLimitUpwards;
}