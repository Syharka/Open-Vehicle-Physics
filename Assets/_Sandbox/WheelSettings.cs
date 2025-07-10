using UnityEngine;

[CreateAssetMenu(menuName = "Settings/Wheel")]
public class WheelSettings : ScriptableObject
{
    public ExtraValues extra;
    public FrictionValues friction;
    public RotationValues rotation;
    public TireSizeValues tireSize;
    public TireAudioValues tireAudio;
}

[System.Serializable]
public class ExtraValues
{
    [Tooltip("Generate a sphere collider to represent the wheel for side collisions")]
    public bool generateHardCollider = true;

    [Tooltip("Apply friction forces at ground point")]
    public bool applyForceAtGroundContact;
}

[System.Serializable]
public class FrictionValues
{
    [Range(0, 1)]
    public float frictionSmoothness = 0.5f;
    public float forwardFriction = 1;
    public float sidewaysFriction = 1;
    public float forwardRimFriction = 0.5f;
    public float sidewaysRimFriction = 0.5f;
    public float forwardCurveStretch = 1;
    public float sidewaysCurveStretch = 1;

    [Tooltip("X-axis = slip, y-axis = friction")]
    public AnimationCurve forwardFrictionCurve, sidewaysFrictionCurve = AnimationCurve.Linear(0, 0, 1, 1);

    public enum SlipDependenceMode { dependent, forward, sideways, independent };
    public SlipDependenceMode slipDependence = SlipDependenceMode.sideways;

    [Range(0, 2)]
    public float forwardSlipDependence, sidewaysSlipDependence = 2;

    [Tooltip("Adjusts friction based on the normal of the ground surface. X-axis = normal dot product, y-axis = friction multiplier")]
    public AnimationCurve normalFrictionCurve = AnimationCurve.Linear(0, 1, 1, 1);

    [Tooltip("How much the suspension compression affects the wheel friction")]
    [Range(0, 1)]
    public float compressionFrictionFactor = 0.5f;
}

[System.Serializable]
public class RotationValues
{
    [Tooltip("Bias for feedback RPM lerp between target RPM and raw RPM")]
    [Range(0, 1)]
    public float feedbackRpmBias;

    [Tooltip("Curve for setting final RPM of wheel based on driving torque/brake force, x-axis = torque/brake force, y-axis = lerp between raw RPM and target RPM")]
    public AnimationCurve rpmBiasCurve = AnimationCurve.Linear(0, 0, 1, 1);

    [Tooltip("As the RPM of the wheel approaches this value, the RPM bias curve is interpolated with the default linear curve")]
    public float rpmBiasCurveLimit = Mathf.Infinity;

    [Range(0, 10)]
    public float axleFriction;
}

[System.Serializable]
public class TireSizeValues
{
    public float tireRadius;
    public float tireWidth;
}

[System.Serializable]
public class TireAudioValues
{
    [Header("Audio")]
    public AudioSource impactSnd;
    public AudioClip[] tireHitClips;
}
