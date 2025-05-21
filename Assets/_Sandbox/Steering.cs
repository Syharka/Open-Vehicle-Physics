using RVP;
using UnityEngine;

public class Steering : MonoBehaviour
{
    VehicleController vp;
    public float steerRate = 0.1f;
    float steerAmount;

    [Tooltip("Curve for limiting steer range based on speed, x-axis = speed, y-axis = multiplier")]
    public AnimationCurve steerCurve = AnimationCurve.Linear(0, 1, 30, 0.1f);
    public bool limitSteer = true;

    [Tooltip("Horizontal stretch of the steer curve")]
    public float steerCurveStretch = 1;
    public bool applyInReverse = true; // Limit steering in reverse?
    public NewSuspension[] steeredWheels;

    [Header("Visual")]

    public bool rotate;
    public float maxDegreesRotation;
    public float rotationOffset;
    float steerRot;

    void Start()
    {
        vp = transform.GetTopmostParentComponent<VehicleController>();
        steerRot = rotationOffset;
    }

    void FixedUpdate()
    {
        float rbSpeed = vp.localVelocity.z / steerCurveStretch;
        float steerLimit = limitSteer ? steerCurve.Evaluate(applyInReverse ? Mathf.Abs(rbSpeed) : rbSpeed) : 1;
        steerAmount = vp.steerInput * steerLimit;

        // Set steer angles in wheels
        foreach (NewSuspension curSus in steeredWheels)
        {
            curSus.steerAngle = Mathf.Lerp(curSus.steerAngle, steerAmount * curSus.steerFactor * (curSus.steerEnabled ? 1 : 0) * (curSus.steerInverted ? -1 : 1), steerRate * TimeMaster.inverseFixedTimeFactor * Time.timeScale);
        }
    }

    void Update()
    {
        // Visual steering wheel rotation
        if (rotate)
        {
            steerRot = Mathf.Lerp(steerRot, steerAmount * maxDegreesRotation + rotationOffset, steerRate * Time.timeScale);
            transform.localEulerAngles = new Vector3(transform.localEulerAngles.x, transform.localEulerAngles.y, steerRot);
        }
    }
}
