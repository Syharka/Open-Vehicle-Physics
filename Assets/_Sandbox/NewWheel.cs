using RVP;
using UnityEngine;

public class NewWheel : MonoBehaviour
{
    Rigidbody rb;
    public VehicleController vp { get; private set; }
    public NewSuspension suspensionParent { get; private set; }
    public Transform rim { get; private set; }
    Transform tire;
    Vector3 localVel;

    [Tooltip("Generate a sphere collider to represent the wheel for side collisions")]
    public bool generateHardCollider = true;
    SphereCollider sphereCol; // Hard collider
    Transform sphereColTr; // Hard collider transform

    [Header("Rotation")]

    [Tooltip("Bias for feedback RPM lerp between target RPM and raw RPM")]
    [Range(0, 1)]
    public float feedbackRpmBias;

    [Tooltip("Curve for setting final RPM of wheel based on driving torque/brake force, x-axis = torque/brake force, y-axis = lerp between raw RPM and target RPM")]
    public AnimationCurve rpmBiasCurve = AnimationCurve.Linear(0, 0, 1, 1);

    [Tooltip("As the RPM of the wheel approaches this value, the RPM bias curve is interpolated with the default linear curve")]
    public float rpmBiasCurveLimit = Mathf.Infinity;

    [Range(0, 10)]
    public float axleFriction;

    [Header("Friction")]

    [Range(0, 1)]
    public float frictionSmoothness = 0.5f;
    public float forwardFriction = 1;
    public float sidewaysFriction = 1;
    public float forwardRimFriction = 0.5f;
    public float sidewaysRimFriction = 0.5f;
    public float forwardCurveStretch = 1;
    public float sidewaysCurveStretch = 1;

    public float forwardSlip;
    public float sidewaysSlip;
    float forwardSlipFactor;
    float sidewaysSlipFactor;
    float forwardSlipDependenceFactor;
    float sidewaysSlipDependenceFactor;
    float targetForceX;
    float targetForceZ;
    Vector3 targetForce;
    float targetForceMultiplier;
    Vector3 frictionForce = Vector3.zero;

    [Tooltip("X-axis = slip, y-axis = friction")]
    public AnimationCurve forwardFrictionCurve = AnimationCurve.Linear(0, 0, 1, 1);

    [Tooltip("X-axis = slip, y-axis = friction")]
    public AnimationCurve sidewaysFrictionCurve = AnimationCurve.Linear(0, 0, 1, 1);
    public enum SlipDependenceMode { dependent, forward, sideways, independent };
    public SlipDependenceMode slipDependence = SlipDependenceMode.sideways;
    [Range(0, 2)]
    public float forwardSlipDependence = 2;
    [Range(0, 2)]
    public float sidewaysSlipDependence = 2;

    [Tooltip("Adjusts how much friction the wheel has based on the normal of the ground surface. X-axis = normal dot product, y-axis = friction multiplier")]
    public AnimationCurve normalFrictionCurve = AnimationCurve.Linear(0, 1, 1, 1);

    [Tooltip("How much the suspension compression affects the wheel friction")]
    [Range(0, 1)]
    public float compressionFrictionFactor = 0.5f;

    [Header("Size")]

    public float tireRadius;
    public float tireWidth;

    public float setTireWidth { get; private set; }
    public float tireWidthPrev { get; private set; }
    public float setTireRadius { get; private set; }
    public float tireRadiusPrev { get; private set; }

    float currentRPM;
    public DriveForce targetDrive { get; private set; }
    public float rawRPM { get; private set; }
    [System.NonSerialized]
    public WheelContact contactPoint = new WheelContact();
    [System.NonSerialized]
    public bool getContact = true; // Should the wheel try to get contact info?
    public bool grounded { get; private set; }
    float airTime;
    public float travelDist { get; private set; }
    float circumference => Mathf.PI * tireRadius * 2;

    public Vector3 contactVelocity { get; private set; }
    float actualEbrake;
    float actualTargetRPM;
    float actualTorque;

    public Vector3 forceApplicationPoint { get; private set; }

    [Tooltip("Apply friction forces at ground point")]
    public bool applyForceAtGroundContact;

    [Header("Audio")]

    public AudioSource impactSnd;
    public AudioClip[] tireHitClips;

    void Start()
    {
        rb = transform.GetTopmostParentComponent<Rigidbody>();
        vp = transform.GetTopmostParentComponent<VehicleController>();
        rim = transform.GetChild(0);
        suspensionParent = transform.parent.GetComponent<NewSuspension>();
        travelDist = suspensionParent.targetCompression;

        targetDrive = GetComponent<DriveForce>();
        currentRPM = 0;

        CreateWheelCollider();
    }

    void FixedUpdate()
    {
        localVel = rb.GetPointVelocity(forceApplicationPoint);

        // Get proper inputs
        actualEbrake = suspensionParent.ebrakeEnabled ? suspensionParent.ebrakeForce : 0;
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
        forceApplicationPoint = applyForceAtGroundContact ? contactPoint.point : transform.position;

        GetRawRPM();
        ApplyDrive();

        // Get travel distance
        travelDist = suspensionParent.compression < travelDist || grounded ? suspensionParent.compression : Mathf.Lerp(travelDist, suspensionParent.compression, suspensionParent.extendSpeed * Time.fixedDeltaTime);

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
        if (!generateHardCollider) return;

        GameObject sphereColNew = new GameObject("Rim Collider");
        sphereColNew.layer = GlobalControl.ignoreWheelCastLayer;
        sphereColTr = sphereColNew.transform;
        sphereCol = sphereColNew.AddComponent<SphereCollider>();
        sphereColTr.parent = transform;
        sphereColTr.localPosition = Vector3.zero;
        sphereColTr.localRotation = Quaternion.identity;
        sphereCol.radius = Mathf.Min(tireWidth * 0.5f, tireRadius * 0.5f);
        sphereCol.sharedMaterial = GlobalControl.frictionlessMatStatic;
    }

    // Use raycasting to find the current contact point for the wheel
    void GetWheelContact()
    {
        float castDist = Mathf.Max(suspensionParent.suspensionDistance * Mathf.Max(0.001f, suspensionParent.targetCompression) + tireRadius, 0.001f);
        RaycastHit hit;
        if (Physics.Raycast(suspensionParent.maxCompressPoint, suspensionParent.springDirection, out hit, castDist, GlobalControl.wheelCastMaskStatic))
        {
            if (!grounded && impactSnd && (tireHitClips.Length > 0))
            {
                impactSnd.PlayOneShot(tireHitClips[Mathf.RoundToInt(Random.Range(0, tireHitClips.Length - 1))], Mathf.Clamp01(airTime * airTime));
                impactSnd.pitch = Mathf.Clamp(airTime * 0.2f + 0.8f, 0.8f, 1);
            }

            grounded = true;
            contactPoint.distance = hit.distance - tireRadius;
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
            contactPoint.distance = suspensionParent.suspensionDistance;
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
            rawRPM = Mathf.Lerp(rawRPM, actualTargetRPM, (actualTorque + suspensionParent.brakeForce * vp.brakeInput + actualEbrake * vp.ebrakeInput) * Time.timeScale);
        }
    }

    // Calculate the current slip amount
    void GetSlip()
    {
        if (grounded)
        {
            sidewaysSlip = (contactPoint.relativeVelocity.z * 0.1f) / sidewaysCurveStretch;
            forwardSlip = (0.01f * (rawRPM - currentRPM)) / forwardCurveStretch;
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
            forwardSlipFactor = (int)slipDependence == 0 || (int)slipDependence == 1 ? forwardSlip - sidewaysSlip : forwardSlip;
            sidewaysSlipFactor = (int)slipDependence == 0 || (int)slipDependence == 2 ? sidewaysSlip - forwardSlip : sidewaysSlip;
            forwardSlipDependenceFactor = Mathf.Clamp01(forwardSlipDependence - Mathf.Clamp01(Mathf.Abs(sidewaysSlip)));
            sidewaysSlipDependenceFactor = Mathf.Clamp01(sidewaysSlipDependence - Mathf.Clamp01(Mathf.Abs(forwardSlip)));

            targetForceX = forwardFrictionCurve.Evaluate(Mathf.Abs(forwardSlipFactor)) * -System.Math.Sign(forwardSlip) * forwardFriction * forwardSlipDependenceFactor * -suspensionParent.flippedSideFactor;
            targetForceZ = sidewaysFrictionCurve.Evaluate(Mathf.Abs(sidewaysSlipFactor)) * -System.Math.Sign(sidewaysSlip) * sidewaysFriction * sidewaysSlipDependenceFactor *
                normalFrictionCurve.Evaluate(Mathf.Clamp01(Vector3.Dot(contactPoint.normal, GlobalControl.worldUpDir))) *
                (vp.burnout > 0 && Mathf.Abs(targetDrive.rpm) != 0 && actualEbrake * vp.ebrakeInput == 0 && grounded ? (1 - vp.burnout) * (1 - Mathf.Abs(vp.accelInput)) : 1);

            targetForce = transform.TransformDirection(targetForceX, 0, targetForceZ);
            targetForceMultiplier = ((1 - compressionFrictionFactor) + (1 - suspensionParent.compression) * compressionFrictionFactor * Mathf.Clamp01(Mathf.Abs(suspensionParent.transform.InverseTransformDirection(localVel).z) * 10)) * contactPoint.surfaceFriction;
            frictionForce = Vector3.Lerp(frictionForce, targetForce * targetForceMultiplier, 1 - frictionSmoothness);
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
                brakeForce = suspensionParent.brakeForce * vp.brakeInput;
            }
            else if (brakeCheckValue <= 0)
            {
                brakeForce = suspensionParent.brakeForce * Mathf.Clamp01(vp.accelInput);
            }
        }
        else
        {
            brakeForce = suspensionParent.brakeForce * vp.brakeInput;
        }

        brakeForce += axleFriction * 0.1f * (Mathf.Approximately(actualTorque, 0) ? 1 : 0);

        if (targetDrive.rpm != 0)
        {
            brakeForce *= (1 - vp.burnout);
        }
        bool validTorque = (!(Mathf.Approximately(actualTorque, 0) && Mathf.Abs(actualTargetRPM) < 0.01f) && !Mathf.Approximately(actualTargetRPM, 0)) || brakeForce + actualEbrake * vp.ebrakeInput > 0;

        currentRPM = CalculateRPM(validTorque, brakeForce);

        targetDrive.feedbackRPM = Mathf.Lerp(currentRPM, rawRPM, feedbackRpmBias);
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
        float torque = Mathf.Lerp(rpmBiasCurve.Evaluate(t), t, rawRPM / (rpmBiasCurveLimit * Mathf.Sign(actualTargetRPM)));
        return torque;
    }

    // Visual wheel positioning
    void PositionWheel()
    {
        if (suspensionParent)
        {
            rim.position = suspensionParent.maxCompressPoint + suspensionParent.springDirection * suspensionParent.suspensionDistance * (Application.isPlaying ? travelDist : suspensionParent.targetCompression) +
                suspensionParent.upDir * Mathf.Pow(Mathf.Max(Mathf.Abs(Mathf.Sin(suspensionParent.sideAngle * Mathf.Deg2Rad)), Mathf.Abs(Mathf.Sin(suspensionParent.casterAngle * Mathf.Deg2Rad))), 2) * tireRadius +
                suspensionParent.pivotOffset * suspensionParent.transform.TransformDirection(Mathf.Sin(transform.localEulerAngles.y * Mathf.Deg2Rad), 0, Mathf.Cos(transform.localEulerAngles.y * Mathf.Deg2Rad))
                - suspensionParent.pivotOffset * (Application.isPlaying ? suspensionParent.forwardDir : suspensionParent.transform.forward);
        }

            sphereColTr.position = rim.position;
    }

    // Visual wheel rotation
    void RotateWheel()
    {
        if (suspensionParent)
        {
            float ackermannVal = Mathf.Sign(suspensionParent.steerAngle) == suspensionParent.flippedSideFactor ? 1 + suspensionParent.ackermannFactor : 1 - suspensionParent.ackermannFactor;
            transform.localEulerAngles = new Vector3(
                suspensionParent.camberAngle + suspensionParent.casterAngle * suspensionParent.steerAngle * suspensionParent.flippedSideFactor,
                -suspensionParent.toeAngle * suspensionParent.flippedSideFactor + suspensionParent.steerDegrees * ackermannVal,
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
        if (transform.childCount > 0)
        {
            // Rim is the first child of this object
            rim = transform.GetChild(0);

            // Tire mesh should be first child of rim
            if (rim.childCount > 0)
            {
                tire = rim.GetChild(0);
            }
        }

        float tireActualRadius = tireRadius;

        Gizmos.color = Color.white;
        GizmosExtra.DrawWireCylinder(rim.position, rim.forward, tireRadius, tireWidth * 2);

        Gizmos.color = new Color(1, 1, 1, 1);
        GizmosExtra.DrawWireCylinder(rim.position, rim.forward, tireRadius, tireWidth * 2);
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
