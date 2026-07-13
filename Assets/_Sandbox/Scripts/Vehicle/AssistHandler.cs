using RVP;
using UnityEngine;

public class AssistHandler
{
    #region Settings
    public AssistSettings assistSettings;
    public AssistDriftValues drift => assistSettings.drift;
    public AssistDownforceValues downforce => assistSettings.downforce;
    public AssistRolloverValues rollover => assistSettings.rollover;
    public AssistAirtimeValues airtime => assistSettings.airtime;
    public AssistAircontrolValues aircontrol => assistSettings.aircontrol;
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

    private Quaternion velDir;

    public void Init(AssistSettings _assistSettings) => assistSettings = _assistSettings;

    public void UpdateAssists(VehicleController _vc)
    {
        forwardDot = Vector3.Dot(_vc.transform.forward, -Physics.gravity.normalized);
        rightDot = Vector3.Dot(_vc.transform.right, -Physics.gravity.normalized);
        upDot = Vector3.Dot(_vc.transform.up, -Physics.gravity.normalized);

        if (_vc.groundedWheels > 0)
        {
            groundedFactor = drift.basedOnWheelsGrounded ? _vc.groundedWheels / _vc.wheels.Count : 1;

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

            velDir = Quaternion.LookRotation(GlobalControl.worldUpDir, _vc.rb.linearVelocity);

            if (aircontrol.flipPower != Vector3.zero)
            {
                ApplyFlip(_vc);
            }

            if (aircontrol.stopFlip)
            {
                ApplyStopFlip(_vc);
            }

            if (aircontrol.rotationCorrection != Vector3.zero)
            {
                ApplyRotationCorrection(_vc);
            }

            if (aircontrol.diveFactor > 0)
            {
                Dive(_vc);
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

        if (drift.straightenAssist && _vc.steerInput == 0 && Mathf.Abs(rightVelDot) < 0.1f && _vc.rb.linearVelocity.sqrMagnitude > 10)
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

    // Apply flip forces
    void ApplyFlip(VehicleController _vc)
    {
        Vector3 flipTorque;

        if (aircontrol.freeSpinFlip)
        {
            flipTorque = new Vector3(
                _vc.pitchInput * aircontrol.flipPower.x,
                _vc.rollInput * aircontrol.flipPower.y,
                _vc.yawInput * aircontrol.flipPower.z
                );
        }
        else
        {
            flipTorque = new Vector3(
                _vc.pitchInput != 0 && Mathf.Abs(_vc.localAngularVel.x) > 1 && System.Math.Sign(_vc.pitchInput * Mathf.Sign(aircontrol.flipPower.x)) != System.Math.Sign(_vc.localAngularVel.x) ? -_vc.localAngularVel.x * Mathf.Abs(aircontrol.flipPower.x) : _vc.pitchInput * aircontrol.flipPower.x - _vc.localAngularVel.x * (1 - Mathf.Abs(_vc.pitchInput)) * Mathf.Abs(aircontrol.flipPower.x),
                _vc.rollInput != 0 && Mathf.Abs(_vc.localAngularVel.y) > 1 && System.Math.Sign(_vc.rollInput * Mathf.Sign(aircontrol.flipPower.y)) != System.Math.Sign(_vc.localAngularVel.y) ? -_vc.localAngularVel.y * Mathf.Abs(aircontrol.flipPower.y) : _vc.rollInput * aircontrol.flipPower.y - _vc.localAngularVel.y * (1 - Mathf.Abs(_vc.rollInput)) * Mathf.Abs(aircontrol.flipPower.y),
                _vc.yawInput != 0 && Mathf.Abs(_vc.localAngularVel.z) > 1 && System.Math.Sign(_vc.yawInput * Mathf.Sign(aircontrol.flipPower.z)) != System.Math.Sign(_vc.localAngularVel.z) ? -_vc.localAngularVel.z * Mathf.Abs(aircontrol.flipPower.z) : _vc.yawInput * aircontrol.flipPower.z - _vc.localAngularVel.z * (1 - Mathf.Abs(_vc.yawInput)) * Mathf.Abs(aircontrol.flipPower.z)
                );
        }

        _vc.rb.AddRelativeTorque(flipTorque, ForceMode.Acceleration);
    }

    // Counteract flipping with forces
    void ApplyStopFlip(VehicleController _vc)
    {
        Vector3 stopFlipFactor = Vector3.zero;

        stopFlipFactor.x = _vc.pitchInput * aircontrol.flipPower.x == 0 ? Mathf.Pow(Mathf.Clamp01(upDot), Mathf.Clamp(10 - Mathf.Abs(_vc.localAngularVel.x), 2, 10)) * 10 : 0;
        stopFlipFactor.y = _vc.yawInput * aircontrol.flipPower.y == 0 && _vc.rb.linearVelocity.sqrMagnitude > 5 ? Mathf.Pow(Mathf.Clamp01(Vector3.Dot(_vc.transform.forward, velDir * Vector3.up)), Mathf.Clamp(10 - Mathf.Abs(_vc.localAngularVel.y), 2, 10)) * 10 : 0;
        stopFlipFactor.z = _vc.rollInput * aircontrol.flipPower.z == 0 ? Mathf.Pow(Mathf.Clamp01(upDot), Mathf.Clamp(10 - Mathf.Abs(_vc.localAngularVel.z), 2, 10)) * 10 : 0;

        _vc.rb.AddRelativeTorque(new Vector3(-_vc.localAngularVel.x * stopFlipFactor.x, -_vc.localAngularVel.y * stopFlipFactor.y, -_vc.localAngularVel.z * stopFlipFactor.z), ForceMode.Acceleration);
    }

    // Apply forces to align vehicle with normal of ground surface that it will land on
    void ApplyRotationCorrection(VehicleController _vc)
    {
        float actualForwardDot = forwardDot;
        float actualRightDot = rightDot;
        float actualUpDot = upDot;

        if (aircontrol.groundCheckDistance > 0)
        {
            RaycastHit groundHit;

            if (Physics.Raycast(_vc.transform.position, (-GlobalControl.worldUpDir + _vc.rb.linearVelocity).normalized, out groundHit, aircontrol.groundCheckDistance, GlobalControl.groundMaskStatic))
            {
                if (Vector3.Dot(groundHit.normal, GlobalControl.worldUpDir) >= aircontrol.groundSteepnessLimit)
                {
                    actualForwardDot = Vector3.Dot(_vc.transform.forward, groundHit.normal);
                    actualRightDot = Vector3.Dot(_vc.transform.right, groundHit.normal);
                    actualUpDot = Vector3.Dot(_vc.transform.up, groundHit.normal);
                }
            }
        }

        _vc.rb.AddRelativeTorque(new Vector3(
            _vc.pitchInput * aircontrol.flipPower.x == 0 ? actualForwardDot * (1 - Mathf.Abs(actualRightDot)) * aircontrol.rotationCorrection.x - _vc.localAngularVel.x * Mathf.Pow(actualUpDot, 2) * 10 : 0,
            _vc.yawInput * aircontrol.flipPower.y == 0 && _vc.rb.linearVelocity.sqrMagnitude > 10 ? Vector3.Dot(_vc.transform.forward, velDir * Vector3.right) * Mathf.Abs(actualUpDot) * aircontrol.rotationCorrection.y - _vc.localAngularVel.y * Mathf.Pow(actualUpDot, 2) * 10 : 0,
            _vc.rollInput * aircontrol.flipPower.z == 0 ? -actualRightDot * (1 - Mathf.Abs(actualForwardDot)) * aircontrol.rotationCorrection.z - _vc.localAngularVel.z * Mathf.Pow(actualUpDot, 2) * 10 : 0
            ), ForceMode.Acceleration);
    }

    // Apply diving force
    void Dive(VehicleController _vc)
    {
        _vc.rb.AddTorque(velDir * Vector3.left * Mathf.Clamp01(_vc.rb.linearVelocity.magnitude * 0.01f) * Mathf.Clamp01(upDot) * aircontrol.diveFactor, ForceMode.Acceleration);
    }
}
