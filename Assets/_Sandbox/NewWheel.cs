using RVP;
using UnityEngine;

public class NewWheel : MonoBehaviour
{
    #region Core Components
    public Rigidbody rb { get; private set; }
    public VehicleController vp { get; private set; }
    public NewSuspension suspensionParent { get; private set; }
    public Transform rim { get; private set; }
    #endregion

    #region Settings
    public WheelSettings wheelSettings;
    public WheelExtraValues extra { get; private set; }
    public WheelFrictionValues friction { get; private set; }
    public WheelRotationValues rotation { get; private set; }
    public WheelSizeValues tireSize { get; private set; }
    public WheelAudioValues tireAudio { get; private set; }
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
    public DriveForce targetDrive { get; private set; }
    public float rawRPM { get; private set; }

    private void Awake()
    {
        extra = wheelSettings.extra;
        friction = wheelSettings.friction;
        rotation = wheelSettings.rotation;
        tireSize = wheelSettings.size;
        tireAudio = wheelSettings.audio;
    }

    void Start()
    {
        rb = transform.GetTopmostParentComponent<Rigidbody>();
        vp = transform.GetTopmostParentComponent<VehicleController>();
        rim = transform.GetChild(0);
        suspensionParent = transform.parent.GetComponent<NewSuspension>();
        travelDist = suspensionParent.spring.targetCompression;

        targetDrive = GetComponent<DriveForce>();
        currentRPM = 0;

        CreateWheelCollider();
    }

    void FixedUpdate()
    {
        localVel = rb.GetPointVelocity(forceApplicationPoint);

        // Get proper inputs
        actualEbrake = suspensionParent.ebrakeEnabled ? suspensionParent.brake.ebrakeForce : 0;
        actualTargetRPM = targetDrive.rpm * (suspensionParent.driveInverted ? -1 : 1);
        actualTorque = suspensionParent.driveEnabled ? Mathf.Lerp(targetDrive.torque, Mathf.Abs(vp.accelInput), vp.burnout) : 0;

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
            rb.AddForceAtPosition(suspensionParent.forwardDir * -suspensionParent.flippedSideFactor * (vp.steerInput * vp.burnoutSpin * currentRPM * Mathf.Min(0.1f, targetDrive.torque) * 0.001f) * vp.burnout * 1 * contactPoint.surfaceFriction, suspensionParent.transform.position, vp.wheelForceMode);
        }
    }

    void CreateWheelCollider()
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

    // Use raycasting to find the current contact point for the wheel
    void GetWheelContact()
    {
        float castDist = Mathf.Max(suspensionParent.spring.suspensionDistance * Mathf.Max(0.001f, suspensionParent.spring.targetCompression) + tireSize.tireRadius, 0.001f);
        RaycastHit hit;
        if (Physics.Raycast(suspensionParent.maxCompressPoint, suspensionParent.springDirection, out hit, castDist, GlobalControl.wheelCastMaskStatic))
        {
            if (!grounded && tireAudio.impactSnd && (tireAudio.tireHitClips.Length > 0))
            {
                tireAudio.impactSnd.PlayOneShot(tireAudio.tireHitClips[Mathf.RoundToInt(Random.Range(0, tireAudio.tireHitClips.Length - 1))], Mathf.Clamp01(airTime * airTime));
                tireAudio.impactSnd.pitch = Mathf.Clamp(airTime * 0.2f + 0.8f, 0.8f, 1);
            }

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
    void GetRawRPM()
    {
        if (grounded)
        {
            rawRPM = (contactPoint.relativeVelocity.x / circumference) * (Mathf.PI * 100) * -suspensionParent.flippedSideFactor;
        }
        else
        {
            rawRPM = Mathf.Lerp(rawRPM, actualTargetRPM, (actualTorque + suspensionParent.brake.brakeForce * vp.brakeInput + actualEbrake * vp.ebrakeInput) * Time.timeScale);
        }
    }

    // Calculate the current slip amount
    void GetSlip()
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
    void ApplyFriction()
    {
        if (grounded)
        {
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
            rb.AddForceAtPosition(frictionForce, forceApplicationPoint, vp.wheelForceMode);

            // If resting on a rigidbody, apply opposing force to it
            if (contactPoint.col.attachedRigidbody)
            {
                contactPoint.col.attachedRigidbody.AddForceAtPosition(-frictionForce, contactPoint.point, vp.wheelForceMode);
            }
        }
    }

    // Do torque and RPM calculations/simulation
    void ApplyDrive()
    {
        float brakeForce = 0;
        float brakeCheckValue = suspensionParent.skidSteerBrake ? vp.localAngularVel.y : vp.localVelocity.z;

        // Set brake force
        if (vp.brakeIsReverse)
        {
            if (brakeCheckValue > 0)
            {
                brakeForce = suspensionParent.brake.brakeForce * vp.brakeInput;
            }
            else if (brakeCheckValue <= 0)
            {
                brakeForce = suspensionParent.brake.brakeForce * Mathf.Clamp01(vp.accelInput);
            }
        }
        else
        {
            brakeForce = suspensionParent.brake.brakeForce * vp.brakeInput;
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

    float CalculateRPM(bool validTorque, float brakeForce)
    {
        float torqueOutput = validTorque ?
            EvaluateTorque(actualTorque + brakeForce + actualEbrake * vp.ebrakeInput) : actualTorque + brakeForce + actualEbrake * vp.ebrakeInput;

        float evaluatedTorque = validTorque ? EvaluateTorque(actualTorque) : actualTorque;

        return Mathf.Lerp(rawRPM, Mathf.Lerp( Mathf.Lerp(rawRPM, actualTargetRPM, evaluatedTorque), 0, Mathf.Max(brakeForce, actualEbrake * vp.ebrakeInput)), torqueOutput);
    }

    // Extra method for evaluating torque to make the ApplyDrive method more readable
    float EvaluateTorque(float t)
    {
        float torque = Mathf.Lerp(rotation.rpmBiasCurve.Evaluate(t), t, rawRPM / (rotation.rpmBiasCurveLimit * Mathf.Sign(actualTargetRPM)));
        return torque;
    }

    // Visual wheel positioning
    void PositionWheel()
    {
        if (suspensionParent)
        {
            rim.position = suspensionParent.maxCompressPoint + suspensionParent.springDirection * suspensionParent.spring.suspensionDistance * (Application.isPlaying ? travelDist : suspensionParent.spring.targetCompression) +
                suspensionParent.upDir * Mathf.Pow(Mathf.Max(Mathf.Abs(Mathf.Sin(suspensionParent.camber.sideAngle * Mathf.Deg2Rad)), Mathf.Abs(Mathf.Sin(suspensionParent.camber.casterAngle * Mathf.Deg2Rad))), 2) * tireSize.tireRadius +
                suspensionParent.camber.pivotOffset * suspensionParent.transform.TransformDirection(Mathf.Sin(transform.localEulerAngles.y * Mathf.Deg2Rad), 0, Mathf.Cos(transform.localEulerAngles.y * Mathf.Deg2Rad))
                - suspensionParent.camber.pivotOffset * (Application.isPlaying ? suspensionParent.forwardDir : suspensionParent.transform.forward);
        }

            sphereColTr.position = rim.position;
    }

    // Visual wheel rotation
    void RotateWheel()
    {
        if (suspensionParent)
        {
            float ackermannVal = Mathf.Sign(suspensionParent.steering.steerAngle) == suspensionParent.flippedSideFactor ? 1 + suspensionParent.steering.ackermannFactor : 1 - suspensionParent.steering.ackermannFactor;
            transform.localEulerAngles = new Vector3(
                suspensionParent.camberAngle + suspensionParent.camber.casterAngle * suspensionParent.steering.steerAngle * suspensionParent.flippedSideFactor,
                -suspensionParent.camber.toeAngle * suspensionParent.flippedSideFactor + suspensionParent.steerDegrees * ackermannVal,
                0);
        }

        rim.Rotate(Vector3.forward, currentRPM * suspensionParent.flippedSideFactor * Time.deltaTime);

        if (rim.localEulerAngles.x != 0 || rim.localEulerAngles.y != 0)
        {
            rim.localEulerAngles = new Vector3(0, 0, rim.localEulerAngles.z);
        }
    }


    // visualize wheel
    void OnDrawGizmosSelected()
    {
        WheelSizeValues activeSettings = Application.isPlaying ? tireSize : wheelSettings.size;
        if (transform.childCount > 0)
        {
            // Rim is the first child of this object
            rim = transform.GetChild(0);
        }

        float tireActualRadius = activeSettings.tireRadius;

        Gizmos.color = Color.white;
        GizmosExtra.DrawWireCylinder(rim.position, rim.forward, activeSettings.tireRadius, activeSettings.tireWidth * 2);

        Gizmos.color = new Color(1, 1, 1, 1);
        GizmosExtra.DrawWireCylinder(rim.position, rim.forward, activeSettings.tireRadius, activeSettings.tireWidth * 2);
    }
}

// Contact point class
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
