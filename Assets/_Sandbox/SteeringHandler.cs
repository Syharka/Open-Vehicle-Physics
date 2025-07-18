using RVP;
using UnityEngine;

public class SteeringHandler
{
    #region Settings
    public SteeringExtraValues extra { get; private set; }
    public SteeringControlValues control { get; private set; }
    public SteeringVisualValues visual { get; private set; }
    #endregion

    private float steerAmount;
    private float steerRot;

    public void Init(SteeringSettings _steeringSettings)
    {
        extra = _steeringSettings.extra;
        control = _steeringSettings.control;
        visual = _steeringSettings.visual;
        steerRot = visual.rotationOffset;
    }

    public void UpdateSteering(VehicleController _vc)
    {
        float rbSpeed = _vc.localVelocity.z / control.steerCurveStretch;
        float steerLimit = extra.limitSteer ? control.steerCurve.Evaluate(extra.applyInReverse ? Mathf.Abs(rbSpeed) : rbSpeed) : 1;
        steerAmount = _vc.steerInput * steerLimit;

        // Set steer angles in wheels
        foreach (NewSuspension curSus in _vc.suspensions)
        {
            curSus.steerAngle = Mathf.Lerp(curSus.steerAngle, steerAmount * curSus.steerFactor * curSus.steerFactor, control.steerRate * TimeMaster.inverseFixedTimeFactor * Time.timeScale);
        }
    }

    void UpdateSteeringWheel()
    {
        // Visual steering wheel rotation
        if (visual.rotate)
        {
            steerRot = Mathf.Lerp(steerRot, steerAmount * visual.maxDegreesRotation + visual.rotationOffset, control.steerRate * Time.timeScale);
            //transform.localEulerAngles = new Vector3(transform.localEulerAngles.x, transform.localEulerAngles.y, steerRot);
        }
    }
}
