using RVP;
using UnityEngine;

public class NewAssist : MonoBehaviour
{
    #region Core Components
    public Rigidbody rb { get; private set; }
    public VehicleController vp { get; private set; }
    #endregion

    #region Settings
    public AssistSettings assistSettings;
    public AssistDriftValues drift { get; private set; }
    public AssistDownforceValues downforce { get; private set; }
    public AssistRolloverValues rollover { get; private set; }
    public AssistAirtimeValues airtime { get; private set; }
    #endregion

    public float forwardDot { get; private set; }
    public float rightDot { get; private set; }
    public float upDot { get; private set; }

    [Header("Drift")]
    private float groundedFactor;
    private float targetDriftAngle;

    [Header("Downforce")]
    public bool rolledOver { get; private set; }

    [Header("Air")]
    private float initialAngularDrag;
    private float angDragTime = 0;

    //[Tooltip("Variables are multiplied based on the number of wheels grounded out of the total number of wheels")]
    //public bool basedOnWheelsGrounded;

    //[Tooltip("How much to assist with spinning while drifting")]
    //public float driftSpinAssist;
    //public float driftSpinSpeed;
    //public float driftSpinExponent = 1;

    //[Tooltip("Automatically adjust drift angle based on steer input magnitude")]
    //public bool autoSteerDrift;
    //public float maxDriftAngle = 70;

    //[Tooltip("Adjusts the force based on drift speed, x-axis = speed, y-axis = force")]
    //public AnimationCurve driftSpinCurve = AnimationCurve.Linear(0, 0, 10, 1);

    //[Tooltip("How much to push the vehicle forward while drifting")]
    //public float driftPush;

    //[Tooltip("Straighten out the vehicle when sliding slightly")]
    //public bool straightenAssist;

    //public float downforce = 1;
    //public bool invertDownforceInReverse;
    //public bool applyDownforceInAir;

    //[Tooltip("X-axis = speed, y-axis = force")]
    //public AnimationCurve downforceCurve = AnimationCurve.Linear(0, 0, 20, 1);

    //[Header("Roll Over")]

    //[Tooltip("Automatically roll over when rolled over")]
    //public bool autoRollOver;

    //[Tooltip("Roll over with steer input")]
    //public bool steerRollOver;

    //[Tooltip("Distance to check on sides to see if rolled over")]
    //public float rollCheckDistance = 1;
    //public float rollOverForce = 1;

    //[Tooltip("Maximum speed at which vehicle can be rolled over with assists")]
    //public float rollSpeedThreshold;

    //[Tooltip("Increase angular drag immediately after jumping")]
    //public bool angularDragOnJump;

    //public float fallSpeedLimit = Mathf.Infinity;
    //public bool applyFallLimitUpwards;

    private void Awake()
    {
        drift = assistSettings.drift;
        downforce = assistSettings.downforce;
        rollover = assistSettings.rollover;
        airtime = assistSettings.airtime;
    }

    void Start()
    {
        rb = GetComponent<Rigidbody>();
        vp = GetComponent<VehicleController>();
        initialAngularDrag = rb.angularDamping;
    }

    void FixedUpdate()
    {
        forwardDot = Vector3.Dot(transform.forward, -Physics.gravity.normalized);
        rightDot = Vector3.Dot(transform.right, -Physics.gravity.normalized);
        upDot = Vector3.Dot(transform.up, -Physics.gravity.normalized);

        if (vp.groundedWheels > 0)
        {
            groundedFactor = drift.basedOnWheelsGrounded ? vp.groundedWheels / vp.wheels.Length : 1;

            angDragTime = 20;
            rb.angularDamping = initialAngularDrag;

            if (drift.driftSpinAssist > 0)
            {
                ApplySpinAssist();
            }

            if (drift.driftPush > 0)
            {
                ApplyDriftPush();
            }
        }
        else
        {
            if (airtime.angularDragOnJump)
            {
                angDragTime = Mathf.Max(0, angDragTime - Time.timeScale * TimeMaster.inverseFixedTimeFactor);
                rb.angularDamping = angDragTime > 0 && upDot > 0.5 ? 10 : initialAngularDrag;
            }
        }

        if (downforce.downforceAmount > 0)
        {
            ApplyDownforce();
        }

        if (rollover.autoRollOver || rollover.steerRollOver)
        {
            RollOver();
        }

        if (Mathf.Abs(vp.localVelocity.y) > airtime.fallSpeedLimit && (vp.localVelocity.y < 0 || airtime.applyFallLimitUpwards))
        {
            rb.AddRelativeForce(Vector3.down * vp.localVelocity.y, ForceMode.Acceleration);
        }
    }

    // Apply assist for steering and drifting
    void ApplySpinAssist()
    {
        // Get desired rotation speed
        float targetTurnSpeed = 0;

        // Auto steer drift
        if (drift.autoSteerDrift)
        {
            int steerSign = 0;
            if (vp.steerInput != 0)
            {
                steerSign = (int)Mathf.Sign(vp.steerInput);
            }

            targetDriftAngle = (steerSign != Mathf.Sign(vp.localVelocity.x) ? vp.steerInput : steerSign) * -drift.maxDriftAngle;
            Vector3 velDir = new Vector3(vp.localVelocity.x, 0, vp.localVelocity.z).normalized;
            Vector3 targetDir = new Vector3(Mathf.Sin(targetDriftAngle * Mathf.Deg2Rad), 0, Mathf.Cos(targetDriftAngle * Mathf.Deg2Rad)).normalized;
            Vector3 driftTorqueTemp = velDir - targetDir;
            targetTurnSpeed = driftTorqueTemp.magnitude * Mathf.Sign(driftTorqueTemp.z) * steerSign * drift.driftSpinSpeed - vp.localAngularVel.y * Mathf.Clamp01(Vector3.Dot(velDir, targetDir)) * 2;
        }
        else
        {
            targetTurnSpeed = vp.steerInput * drift.driftSpinSpeed * (vp.localVelocity.z < 0 ? (vp.accelAxisIsBrake ? Mathf.Sign(vp.accelInput) : Mathf.Sign(F.MaxAbs(vp.accelInput, -vp.brakeInput))) : 1);
        }

        rb.AddRelativeTorque(
            new Vector3(0, (targetTurnSpeed - vp.localAngularVel.y) * drift.driftSpinAssist * drift.driftSpinCurve.Evaluate(Mathf.Abs(Mathf.Pow(vp.localVelocity.x, drift.driftSpinExponent))) * groundedFactor, 0),
            ForceMode.Acceleration);

        float rightVelDot = Vector3.Dot(transform.right, rb.linearVelocity.normalized);

        if (drift.straightenAssist && vp.steerInput == 0 && Mathf.Abs(rightVelDot) < 0.1f && vp.rb.linearVelocity.sqrMagnitude > 5)
        {
            rb.AddRelativeTorque(
                new Vector3(0, rightVelDot * 100 * Mathf.Sign(vp.localVelocity.z) * drift.driftSpinAssist, 0),
                ForceMode.Acceleration);
        }
    }

    // Apply downforce
    void ApplyDownforce()
    {
        if (vp.groundedWheels > 0 || downforce.applyDownforceInAir)
        {
            rb.AddRelativeForce(
                new Vector3(0, downforce.downforceCurve.Evaluate(Mathf.Abs(vp.localVelocity.z)) * -downforce.downforceAmount * (downforce.applyDownforceInAir ? 1 : groundedFactor) * (downforce.invertDownforceInReverse ? Mathf.Sign(vp.localVelocity.z) : 1), 0),
                ForceMode.Acceleration);

            // Reverse downforce
            if (downforce.invertDownforceInReverse && vp.localVelocity.z < 0)
            {
                rb.AddRelativeTorque(
                    new Vector3(downforce.downforceCurve.Evaluate(Mathf.Abs(vp.localVelocity.z)) * downforce.downforceAmount * (downforce.applyDownforceInAir ? 1 : groundedFactor), 0, 0),
                    ForceMode.Acceleration);
            }
        }
    }

    // Assist with rolling back over if upside down or on side
    void RollOver()
    {
        RaycastHit rollHit;

        // Check if rolled over
        if (vp.groundedWheels == 0 && vp.rb.linearVelocity.magnitude < rollover.rollSpeedThreshold && upDot < 0.8 && rollover.rollCheckDistance > 0)
        {
            if (Physics.Raycast(transform.position, vp.transform.up, out rollHit, rollover.rollCheckDistance, GlobalControl.groundMaskStatic)
                || Physics.Raycast(transform.position, vp.transform.right, out rollHit, rollover.rollCheckDistance, GlobalControl.groundMaskStatic)
                || Physics.Raycast(transform.position, -vp.transform.right, out rollHit, rollover.rollCheckDistance, GlobalControl.groundMaskStatic))
            {
                rolledOver = true;
            }
            else
            {
                rolledOver = false;
            }
        }
        else
        {
            rolledOver = false;
        }

        // Apply roll over force
        if (rolledOver)
        {
            if (rollover.steerRollOver && vp.steerInput != 0)
            {
                rb.AddRelativeTorque(
                    new Vector3(0, 0, -vp.steerInput * rollover.rollOverForce),
                    ForceMode.Acceleration);
            }
            else if (rollover.autoRollOver)
            {
                rb.AddRelativeTorque(
                    new Vector3(0, 0, -Mathf.Sign(rightDot) * rollover.rollOverForce),
                    ForceMode.Acceleration);
            }
        }
    }

    // Assist for accelerating while drifting
    void ApplyDriftPush()
    {
        float pushFactor = (vp.accelAxisIsBrake ? vp.accelInput : vp.accelInput - vp.brakeInput) * Mathf.Abs(vp.localVelocity.x) * drift.driftPush * groundedFactor * (1 - Mathf.Abs(Vector3.Dot(vp.transform.forward, rb.linearVelocity.normalized)));

        rb.AddForce(
            vp.norm.TransformDirection(new Vector3(Mathf.Abs(pushFactor) * Mathf.Sign(vp.localVelocity.x), Mathf.Abs(pushFactor) * Mathf.Sign(vp.localVelocity.z), 0)),
            ForceMode.Acceleration);
    }
}
