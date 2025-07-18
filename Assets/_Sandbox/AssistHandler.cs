using RVP;
using UnityEngine;

public class AssistHandler
{
    #region Settings
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

    public void Init(AssistSettings _assistSettings)
    {
        drift = _assistSettings.drift;
        downforce = _assistSettings.downforce;
        rollover = _assistSettings.rollover;
        airtime = _assistSettings.airtime;
    }

    public void UpdateAssists(VehicleController _vc)
    {
        forwardDot = Vector3.Dot(_vc.transform.forward, -Physics.gravity.normalized);
        rightDot = Vector3.Dot(_vc.transform.right, -Physics.gravity.normalized);
        upDot = Vector3.Dot(_vc.transform.up, -Physics.gravity.normalized);

        if (_vc.groundedWheels > 0)
        {
            groundedFactor = drift.basedOnWheelsGrounded ? _vc.groundedWheels / _vc.suspensions.Count : 1;

            angDragTime = 20;
            _vc.rb.angularDamping = initialAngularDrag;

            if (drift.driftSpinAssist > 0)
            {
                ApplySpinAssist(_vc);
            }

            if (drift.driftPush > 0)
            {
                ApplyDriftPush(_vc);
            }
        }
        else
        {
            if (airtime.angularDragOnJump)
            {
                angDragTime = Mathf.Max(0, angDragTime - Time.timeScale * TimeMaster.inverseFixedTimeFactor);
                _vc.rb.angularDamping = angDragTime > 0 && upDot > 0.5 ? 10 : initialAngularDrag;
            }
        }

        if (downforce.downforceAmount > 0)
        {
            ApplyDownforce(_vc);
        }

        if (rollover.autoRollOver || rollover.steerRollOver)
        {
            RollOver(_vc);
        }

        if (Mathf.Abs(_vc.localVelocity.y) > airtime.fallSpeedLimit && (_vc.localVelocity.y < 0 || airtime.applyFallLimitUpwards))
        {
            _vc.rb.AddRelativeForce(Vector3.down * _vc.localVelocity.y, ForceMode.Acceleration);
        }
    }

    // Apply assist for steering and drifting
    void ApplySpinAssist(VehicleController _vc)
    {
        // Get desired rotation speed
        float targetTurnSpeed = 0;

        // Auto steer drift
        if (drift.autoSteerDrift)
        {
            int steerSign = 0;
            if (_vc.steerInput != 0)
            {
                steerSign = (int)Mathf.Sign(_vc.steerInput);
            }

            targetDriftAngle = (steerSign != Mathf.Sign(_vc.localVelocity.x) ? _vc.steerInput : steerSign) * -drift.maxDriftAngle;
            Vector3 velDir = new Vector3(_vc.localVelocity.x, 0, _vc.localVelocity.z).normalized;
            Vector3 targetDir = new Vector3(Mathf.Sin(targetDriftAngle * Mathf.Deg2Rad), 0, Mathf.Cos(targetDriftAngle * Mathf.Deg2Rad)).normalized;
            Vector3 driftTorqueTemp = velDir - targetDir;
            targetTurnSpeed = driftTorqueTemp.magnitude * Mathf.Sign(driftTorqueTemp.z) * steerSign * drift.driftSpinSpeed - _vc.localAngularVel.y * Mathf.Clamp01(Vector3.Dot(velDir, targetDir)) * 2;
        }
        else
        {
            targetTurnSpeed = _vc.steerInput * drift.driftSpinSpeed * (_vc.localVelocity.z < 0 ? (_vc.extras.accelAxisIsBrake ? Mathf.Sign(_vc.accelInput) : Mathf.Sign(F.MaxAbs(_vc.accelInput, -_vc.brakeInput))) : 1);
        }

        _vc.rb.AddRelativeTorque(
            new Vector3(0, (targetTurnSpeed - _vc.localAngularVel.y) * drift.driftSpinAssist * drift.driftSpinCurve.Evaluate(Mathf.Abs(Mathf.Pow(_vc.localVelocity.x, drift.driftSpinExponent))) * groundedFactor, 0),
            ForceMode.Acceleration);

        float rightVelDot = Vector3.Dot(_vc.transform.right, _vc.rb.linearVelocity.normalized);

        if (drift.straightenAssist && _vc.steerInput == 0 && Mathf.Abs(rightVelDot) < 0.1f && _vc.rb.linearVelocity.sqrMagnitude > 5)
        {
            _vc.rb.AddRelativeTorque(
                new Vector3(0, rightVelDot * 100 * Mathf.Sign(_vc.localVelocity.z) * drift.driftSpinAssist, 0),
                ForceMode.Acceleration);
        }
    }

    // Apply downforce
    void ApplyDownforce(VehicleController _vc)
    {
        if (_vc.groundedWheels > 0 || downforce.applyDownforceInAir)
        {
            _vc.rb.AddRelativeForce(
                new Vector3(0, downforce.downforceCurve.Evaluate(Mathf.Abs(_vc.localVelocity.z)) * -downforce.downforceAmount * (downforce.applyDownforceInAir ? 1 : groundedFactor) * (downforce.invertDownforceInReverse ? Mathf.Sign(_vc.localVelocity.z) : 1), 0),
                ForceMode.Acceleration);

            // Reverse downforce
            if (downforce.invertDownforceInReverse && _vc.localVelocity.z < 0)
            {
                _vc.rb.AddRelativeTorque(
                    new Vector3(downforce.downforceCurve.Evaluate(Mathf.Abs(_vc.localVelocity.z)) * downforce.downforceAmount * (downforce.applyDownforceInAir ? 1 : groundedFactor), 0, 0),
                    ForceMode.Acceleration);
            }
        }
    }

    // Assist with rolling back over if upside down or on side
    void RollOver(VehicleController _vc)
    {
        RaycastHit rollHit;

        // Check if rolled over
        if (_vc.groundedWheels == 0 && _vc.rb.linearVelocity.magnitude < rollover.rollSpeedThreshold && upDot < 0.8 && rollover.rollCheckDistance > 0)
        {
            if (Physics.Raycast(_vc.transform.position, _vc.transform.up, out rollHit, rollover.rollCheckDistance, GlobalControl.groundMaskStatic)
                || Physics.Raycast(_vc.transform.position, _vc.transform.right, out rollHit, rollover.rollCheckDistance, GlobalControl.groundMaskStatic)
                || Physics.Raycast(_vc.transform.position, -_vc.transform.right, out rollHit, rollover.rollCheckDistance, GlobalControl.groundMaskStatic))
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
            if (rollover.steerRollOver && _vc.steerInput != 0)
            {
                _vc.rb.AddRelativeTorque(
                    new Vector3(0, 0, -_vc.steerInput * rollover.rollOverForce),
                    ForceMode.Acceleration);
            }
            else if (rollover.autoRollOver)
            {
                _vc.rb.AddRelativeTorque(
                    new Vector3(0, 0, -Mathf.Sign(rightDot) * rollover.rollOverForce),
                    ForceMode.Acceleration);
            }
        }
    }

    // Assist for accelerating while drifting
    void ApplyDriftPush(VehicleController _vc)
    {
        float pushFactor = (_vc.extras.accelAxisIsBrake ? _vc.accelInput : _vc.accelInput - _vc.brakeInput) * Mathf.Abs(_vc.localVelocity.x) * drift.driftPush * groundedFactor * (1 - Mathf.Abs(Vector3.Dot(_vc.transform.forward, _vc.rb.linearVelocity.normalized)));

        _vc.rb.AddForce(
            _vc.norm.TransformDirection(new Vector3(Mathf.Abs(pushFactor) * Mathf.Sign(_vc.localVelocity.x), Mathf.Abs(pushFactor) * Mathf.Sign(_vc.localVelocity.z), 0)),
            ForceMode.Acceleration);
    }
}
