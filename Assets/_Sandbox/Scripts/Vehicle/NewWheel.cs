using RVP;
using System;
using System.Net.Mail;
using System.Net.NetworkInformation;
using UnityEngine;
using Random = UnityEngine.Random;

[RequireComponent(typeof(WheelVisualizer))]
public class NewWheel : MonoBehaviour
{
    #region Core Components
    public Rigidbody rb { get; private set; }
    public VehicleController vp { get; private set; }
    public NewSuspension suspensionParent { get; private set; }
    public Transform rim { get; private set; }
    public Transform attachments { get; private set; }
    #endregion

    #region Settings
    public WheelSettings wheelSettings;
    public WheelExtraValues extra => wheelSettings.extra;
    public WheelFrictionValues friction => wheelSettings.friction;
    public WheelRotationValues rotation => wheelSettings.rotation;
    public WheelBrakeValues brake => wheelSettings.brake;
    public WheelSizeValues tireSize => wheelSettings.size;
    #endregion

    #region Friction Values
    private float forwardSlip;
    private float sidewaysSlip;
    private float forwardSlipFactor;
    private float sidewaysSlipFactor;
    private float forwardSlipDependenceFactor;
    private float sidewaysSlipDependenceFactor;
    private float targetForceX;
    private float targetForceZ;
    private Vector3 targetForce;
    private float targetForceMultiplier;
    private Vector3 frictionForce = Vector3.zero;
    #endregion

    #region Contact Values
    private Vector3 localVel;
    public bool getContact { get; private set; } = true;
    public WheelContact contactPoint { get; private set; } = new WheelContact();
    public bool grounded { get; private set; }
    public Vector3 contactVelocity { get; private set; }
    public Vector3 forceApplicationPoint { get; private set; }
    private float airTime;
    #endregion

    #region Misc
    private SphereCollider sphereCol; // Hard collider
    private Transform sphereColTr; // Hard collider transform

    public float travelDist { get; private set; }
    private float circumference => Mathf.PI * tireSize.tireRadius * 2;

    private float actualEbrake;
    private float actualTargetRPM;
    private float actualTorque;
    #endregion

    // --- TO MOVE ELSEWHERE ---
    float currentRPM;
    public Drivetrain targetDrive { get; private set; }
    public float rawRPM { get; private set; }

    [Range(-1, 1)]
    public float steerFactor;
    public bool driveEnabled = true;
    public bool ebrakeEnabled = true;
    public bool skidSteerBrake = false;
    public float steerDegrees => Mathf.Abs(steerAngle) * (steerAngle > 0 ? vp.steeringHandler.control.steerRange : -vp.steeringHandler.control.steerRange);
    [NonSerialized]
    public float steerAngle;

    void Start()
    {
        //SetSettingsProfile(wheelSettings);
        GetCoreComponents();
        ResetDrivetrain();
        CreateWheelCollider();
        vp.RegisterWheel(this);
    }

    //public void SetSettingsProfile(WheelSettings _settings)
    //{
    //    extra = _settings.extra;
    //    friction = _settings.friction;
    //    rotation = _settings.rotation;
    //    brake = _settings.brake;
    //    tireSize = _settings.size;

    //    wheelSettings = _settings;
    //}

    private void GetCoreComponents()
    {
        rb = transform.GetTopmostParentComponent<Rigidbody>();
        vp = transform.GetTopmostParentComponent<VehicleController>();
        rim = transform.GetChild(0);
        attachments = transform.GetChild(1);
        suspensionParent = transform.parent.GetComponent<NewSuspension>();
        travelDist = suspensionParent.spring.targetCompression;
    }

    private void ResetDrivetrain()
    {
        targetDrive = new Drivetrain();
        targetDrive.active = driveEnabled;
        currentRPM = 0;
    }

    private void CreateWheelCollider()
    {
        if (!extra.generateHardCollider) return;

        GameObject sphereColNew = new GameObject("Rim Collider");
        sphereColNew.layer = GlobalControl.ignoreWheelCastLayer;
        sphereColTr = sphereColNew.transform;
        sphereCol = sphereColNew.AddComponent<SphereCollider>();
        sphereColTr.parent = transform;
        sphereColTr.localPosition = Vector3.zero;
        sphereColTr.localRotation = Quaternion.identity;
        sphereCol.radius = Mathf.Min(tireSize.tireWidth * 0.5f, tireSize.tireRadius * 0.5f);
        sphereCol.sharedMaterial = GlobalControl.frictionlessMatStatic;
    }

    void FixedUpdate()
    {
        localVel = rb.GetPointVelocity(forceApplicationPoint);

        // Get proper inputs
        actualEbrake = ebrakeEnabled ? brake.ebrakeForce : 0;
        actualTargetRPM = targetDrive.rpm;
        actualTorque = driveEnabled ? Mathf.Lerp(targetDrive.torque, Mathf.Abs(vp.accelInput), vp.burnout) : 0;

        if (getContact)
        {
            GetWheelContact();
        }
        else if (grounded)
        {
            contactPoint.point += localVel * Time.fixedDeltaTime;
        }

        airTime = grounded ? 0 : airTime + Time.fixedDeltaTime;
        forceApplicationPoint = extra.applyForceAtGroundContact ? contactPoint.point : transform.position;

        GetRawRPM();
        ApplyDrive();

        // Get travel distance
        travelDist = suspensionParent.compression < travelDist || grounded ? suspensionParent.compression : Mathf.Lerp(travelDist, suspensionParent.compression, suspensionParent.spring.extendSpeed * Time.fixedDeltaTime);

        PositionWheel();
        RotateWheel();

        GetSlip();
        ApplyFriction();

        // Handle Burnout
        if (vp.burnout > 0 && targetDrive.rpm != 0 && actualEbrake * vp.ebrakeInput == 0 && grounded)
        {
            rb.AddForceAtPosition(suspensionParent.transform.forward * -suspensionParent.flippedSideFactor * (vp.steerInput * vp.extras.burnoutSpin * currentRPM * Mathf.Min(0.1f, targetDrive.torque) * 0.001f) * vp.burnout * 1 * contactPoint.surfaceFriction, suspensionParent.transform.position, vp.extras.wheelForceMode);
        }
    }

    // Use raycasting to find the current contact point for the wheel
    private void GetWheelContact()
    {
        float castDist = Mathf.Max(suspensionParent.spring.suspensionDistance * Mathf.Max(0.001f, suspensionParent.spring.targetCompression) + tireSize.tireRadius, 0.001f);
        RaycastHit hit;
        if (Physics.Raycast(suspensionParent.maxCompressPoint, suspensionParent.springDirection, out hit, castDist, GlobalControl.wheelCastMaskStatic))
        {
            grounded = true;
            contactPoint.distance = hit.distance - tireSize.tireRadius;
            contactPoint.point = hit.point + localVel * Time.fixedDeltaTime;
            contactPoint.grounded = true;
            contactPoint.normal = hit.normal;
            contactPoint.relativeVelocity = transform.InverseTransformDirection(localVel);
            contactPoint.col = hit.collider;

            if (hit.collider.attachedRigidbody)
            {
                contactVelocity = hit.collider.attachedRigidbody.GetPointVelocity(contactPoint.point);
                contactPoint.relativeVelocity -= transform.InverseTransformDirection(contactVelocity);
            }
            else
            {
                contactVelocity = Vector3.zero;
            }

            GroundSurfaceInstance curSurface = hit.collider.GetComponent<GroundSurfaceInstance>();
            TerrainSurface curTerrain = hit.collider.GetComponent<TerrainSurface>();

            if (curSurface)
            {
                contactPoint.surfaceFriction = curSurface.friction;
                contactPoint.surfaceType = curSurface.surfaceType;
            }
            else if (curTerrain)
            {
                contactPoint.surfaceType = curTerrain.GetDominantSurfaceTypeAtPoint(contactPoint.point);
                contactPoint.surfaceFriction = curTerrain.GetFriction(contactPoint.surfaceType);
            }
            else
            {
                contactPoint.surfaceFriction = hit.collider.sharedMaterial != null ? hit.collider.sharedMaterial.dynamicFriction * 2 : 1.0f;
                contactPoint.surfaceType = 0;
            }
        }
        else
        {
            grounded = false;
            contactPoint.distance = suspensionParent.spring.suspensionDistance;
            contactPoint.point = Vector3.zero;
            contactPoint.grounded = false;
            contactPoint.normal = transform.up;
            contactPoint.relativeVelocity = Vector3.zero;
            contactPoint.col = null;
            contactVelocity = Vector3.zero;
            contactPoint.surfaceFriction = 0;
            contactPoint.surfaceType = 0;
        }
    }

    // Calculate what the RPM of the wheel would be based purely on its velocity
    private void GetRawRPM()
    {
        if (grounded)
        {
            rawRPM = (contactPoint.relativeVelocity.x / circumference) * (Mathf.PI * 100) * -suspensionParent.flippedSideFactor;
        }
        else
        {
            rawRPM = Mathf.Lerp(rawRPM, actualTargetRPM, (actualTorque + brake.brakeForce * vp.brakeInput + actualEbrake * vp.ebrakeInput) * Time.timeScale);
        }
    }

    // Calculate the current slip amount
    private void GetSlip()
    {
        if (grounded)
        {
            sidewaysSlip = (contactPoint.relativeVelocity.z * 0.1f) / friction.sidewaysCurveStretch;
            forwardSlip = (0.01f * (rawRPM - currentRPM)) / friction.forwardCurveStretch;
        }
        else
        {
            sidewaysSlip = 0;
            forwardSlip = 0;
        }
    }

    // Apply actual forces to rigidbody based on wheel simulation
    private void ApplyFriction()
    {
        if (!grounded) return;

        forwardSlipFactor = (int)friction.slipDependence == 0 || (int)friction.slipDependence == 1 ? forwardSlip - sidewaysSlip : forwardSlip;
        sidewaysSlipFactor = (int)friction.slipDependence == 0 || (int)friction.slipDependence == 2 ? sidewaysSlip - forwardSlip : sidewaysSlip;
        forwardSlipDependenceFactor = Mathf.Clamp01(friction.forwardSlipDependence - Mathf.Clamp01(Mathf.Abs(sidewaysSlip)));
        sidewaysSlipDependenceFactor = Mathf.Clamp01(friction.sidewaysSlipDependence - Mathf.Clamp01(Mathf.Abs(forwardSlip)));

        targetForceX = friction.forwardFrictionCurve.Evaluate(Mathf.Abs(forwardSlipFactor)) * -System.Math.Sign(forwardSlip) * friction.forwardFriction * forwardSlipDependenceFactor * -suspensionParent.flippedSideFactor;
        targetForceZ = friction.sidewaysFrictionCurve.Evaluate(Mathf.Abs(sidewaysSlipFactor)) * -System.Math.Sign(sidewaysSlip) * friction.sidewaysFriction * sidewaysSlipDependenceFactor *
            friction.normalFrictionCurve.Evaluate(Mathf.Clamp01(Vector3.Dot(contactPoint.normal, GlobalControl.worldUpDir))) *
            (vp.burnout > 0 && Mathf.Abs(targetDrive.rpm) != 0 && actualEbrake * vp.ebrakeInput == 0 && grounded ? (1 - vp.burnout) * (1 - Mathf.Abs(vp.accelInput)) : 1);

        targetForce = transform.TransformDirection(targetForceX, 0, targetForceZ);
        targetForceMultiplier = ((1 - friction.compressionFrictionFactor) + (1 - suspensionParent.compression) * friction.compressionFrictionFactor * Mathf.Clamp01(Mathf.Abs(suspensionParent.transform.InverseTransformDirection(localVel).z) * 10)) * contactPoint.surfaceFriction;
        frictionForce = Vector3.Lerp(frictionForce, targetForce * targetForceMultiplier, 1 - friction.frictionSmoothness);
        rb.AddForceAtPosition(frictionForce, forceApplicationPoint, vp.extras.wheelForceMode);

        // If resting on a rigidbody, apply opposing force to it
        if (contactPoint.col.attachedRigidbody)
        {
            contactPoint.col.attachedRigidbody.AddForceAtPosition(-frictionForce, contactPoint.point, vp.extras.wheelForceMode);
        }
    }

    // Do torque and RPM calculations/simulation
    private void ApplyDrive()
    {
        float brakeForce = 0;
        float brakeCheckValue = skidSteerBrake ? vp.localAngularVel.y : vp.localVelocity.z;

        // Set brake force
        if (vp.extras.brakeIsReverse)
        {
            if (brakeCheckValue > 0)
            {
                brakeForce = brake.brakeForce * vp.brakeInput;
            }
            else if (brakeCheckValue <= 0)
            {
                brakeForce = brake.brakeForce * Mathf.Clamp01(vp.accelInput);
            }
        }
        else
        {
            brakeForce = brake.brakeForce * vp.brakeInput;
        }

        brakeForce += rotation.axleFriction * 0.1f * (Mathf.Approximately(actualTorque, 0) ? 1 : 0);

        if (targetDrive.rpm != 0)
        {
            brakeForce *= (1 - vp.burnout);
        }
        bool validTorque = (!(Mathf.Approximately(actualTorque, 0) && Mathf.Abs(actualTargetRPM) < 0.01f) && !Mathf.Approximately(actualTargetRPM, 0)) || brakeForce + actualEbrake * vp.ebrakeInput > 0;

        currentRPM = CalculateRPM(validTorque, brakeForce);

        targetDrive.feedbackRPM = Mathf.Lerp(currentRPM, rawRPM, rotation.feedbackRpmBias);
    }

    private float CalculateRPM(bool validTorque, float brakeForce)
    {
        float torqueOutput = validTorque ?
            EvaluateTorque(actualTorque + brakeForce + actualEbrake * vp.ebrakeInput) : actualTorque + brakeForce + actualEbrake * vp.ebrakeInput;

        float evaluatedTorque = validTorque ? EvaluateTorque(actualTorque) : actualTorque;

        return Mathf.Lerp(rawRPM, Mathf.Lerp( Mathf.Lerp(rawRPM, actualTargetRPM, evaluatedTorque), 0, Mathf.Max(brakeForce, actualEbrake * vp.ebrakeInput)), torqueOutput);
    }

    // Extra method for evaluating torque to make the ApplyDrive method more readable
    private float EvaluateTorque(float t)
    {
        float torque = Mathf.Lerp(rotation.rpmBiasCurve.Evaluate(t), t, rawRPM / (rotation.rpmBiasCurveLimit * Mathf.Sign(actualTargetRPM)));
        return torque;
    }

    // Visual wheel positioning
    private void PositionWheel()
    {
        if (suspensionParent)
        {
            rim.position = suspensionParent.maxCompressPoint + suspensionParent.springDirection * suspensionParent.spring.suspensionDistance * (Application.isPlaying ? travelDist : suspensionParent.spring.targetCompression) +
                suspensionParent.transform.up * Mathf.Pow(Mathf.Max(Mathf.Abs(Mathf.Sin(suspensionParent.camber.sideAngle * Mathf.Deg2Rad)), Mathf.Abs(Mathf.Sin(suspensionParent.camber.casterAngle * Mathf.Deg2Rad))), 2) * tireSize.tireRadius +
                suspensionParent.camber.pivotOffset * suspensionParent.transform.TransformDirection(Mathf.Sin(transform.localEulerAngles.y * Mathf.Deg2Rad), 0, Mathf.Cos(transform.localEulerAngles.y * Mathf.Deg2Rad))
                - suspensionParent.camber.pivotOffset * (Application.isPlaying ? suspensionParent.transform.forward : suspensionParent.transform.forward);
        }

        attachments.position = rim.position;
        sphereColTr.position = rim.position;
    }

    // Visual wheel rotation
    private void RotateWheel()
    {
        if (suspensionParent)
        {
            float ackermannVal = Mathf.Sign(steerAngle) == suspensionParent.flippedSideFactor ? 1 + vp.steeringHandler.extra.ackermannFactor : 1 - vp.steeringHandler.extra.ackermannFactor;
            transform.localEulerAngles = new Vector3(
                suspensionParent.camberAngle + suspensionParent.camber.casterAngle * steerAngle * suspensionParent.flippedSideFactor,
                -suspensionParent.camber.toeAngle * suspensionParent.flippedSideFactor + steerDegrees * ackermannVal,
                0);
        }

        rim.Rotate(Vector3.forward, currentRPM * suspensionParent.flippedSideFactor * Time.deltaTime);

        if (rim.localEulerAngles.x != 0 || rim.localEulerAngles.y != 0)
        {
            rim.localEulerAngles = new Vector3(0, 0, rim.localEulerAngles.z);
        }

        Vector3 rimRot = rim.localEulerAngles;
        rimRot.z = 0;
        attachments.localEulerAngles = rimRot;
    }
}

// Contact point class
[System.Serializable]
public class WheelContact
{
    public bool grounded; // Is the contact point grounded?
    public Collider col; // The collider of the contact point
    public Vector3 point; // The position of the contact point
    public Vector3 normal; // The normal of the contact point
    public Vector3 relativeVelocity; // Relative velocity between the wheel and the contact point object
    public float distance; // Distance from the suspension to the contact point minus the wheel radius
    public float surfaceFriction; // Friction of the contact surface
    public int surfaceType; // The surface type identified by the surface types array of GroundSurfaceMaster
}
