using RVP;
using UnityEngine;

public class Steering : MonoBehaviour
{
    #region Core Components
    public VehicleController vp {  get; private set; }
    public NewSuspension[] steeredWheels;
    #endregion

    #region Settings
    public SteeringSettings steeringSettings;
    public SteeringExtraValues extra { get; private set; }
    public SteeringControlValues control { get; private set; }
    public SteeringVisualValues visual { get; private set; }
    #endregion

    private float steerAmount;
    private float steerRot;

    private void Awake()
    {
        extra = steeringSettings.extra;
        control = steeringSettings.control;
        visual = steeringSettings.visual;
    }

    void Start()
    {
        vp = transform.GetTopmostParentComponent<VehicleController>();
        steerRot = visual.rotationOffset;
    }

    void FixedUpdate()
    {
        float rbSpeed = vp.localVelocity.z / control.steerCurveStretch;
        float steerLimit = extra.limitSteer ? control.steerCurve.Evaluate(extra.applyInReverse ? Mathf.Abs(rbSpeed) : rbSpeed) : 1;
        steerAmount = vp.steerInput * steerLimit;

        // Set steer angles in wheels
        foreach (NewSuspension curSus in steeredWheels)
        {
            curSus.steering.steerAngle = Mathf.Lerp(curSus.steering.steerAngle, steerAmount * curSus.steering.steerFactor * (curSus.steerEnabled ? 1 : 0) * (curSus.steerInverted ? -1 : 1), control.steerRate * TimeMaster.inverseFixedTimeFactor * Time.timeScale);
        }
    }

    void Update()
    {
        // Visual steering wheel rotation
        if (visual.rotate)
        {
            steerRot = Mathf.Lerp(steerRot, steerAmount * visual.maxDegreesRotation + visual.rotationOffset, control.steerRate * Time.timeScale);
            transform.localEulerAngles = new Vector3(transform.localEulerAngles.x, transform.localEulerAngles.y, steerRot);
        }
    }
}
