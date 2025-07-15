using System;
using UnityEngine;

[CreateAssetMenu(menuName = "OVP/Vehicle/New Transmission")]
public class TransmissionSettings : ScriptableObject
{
    public TransmissionExtraValues extra;
    public TransmissionGearValues gear;
    public TransmissionClutchValues clutch;
}

[Serializable]
public class TransmissionExtraValues
{
    [Tooltip("Calculate the RPM ranges of the gears in play mode.  This will overwrite the current values")]
    public bool autoCalculateRpmRanges = true;

    [Tooltip("Apply special drive to wheels for skid steering")]
    public bool skidSteerDrive;

    [Tooltip("Exponent for torque output on each wheel")]
    public float driveDividePower = 3;
}

[Serializable]
public class TransmissionGearValues
{
    public bool skipNeutral;
    public int startGear;
    public Gear[] gears;
}

[Serializable]
public class TransmissionClutchValues
{

    [Tooltip("Number of physics steps a shift should last")]
    public float shiftDelay;

    [Tooltip("Multiplier for comparisons in automatic shifting calculations, should be 2 in most cases")]
    public float shiftThreshold;
}

[Serializable]
public class Gear
{
    public float ratio;
    public float minRPM;
    public float maxRPM;
}
