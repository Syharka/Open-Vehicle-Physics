using RVP;
using UnityEngine;

public class NewWheel : MonoBehaviour
{
    Rigidbody rb;
    [System.NonSerialized]
    public VehicleController vp;
    [System.NonSerialized]
    public NewSuspension suspensionParent;
    [System.NonSerialized]
    public Transform rim;
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
    Vector3 frictionForce = Vector3.zero;

    [Tooltip("X-axis = slip, y-axis = friction")]
    public AnimationCurve forwardFrictionCurve = AnimationCurve.Linear(0, 0, 1, 1);

    [Tooltip("X-axis = slip, y-axis = friction")]
    public AnimationCurve sidewaysFrictionCurve = AnimationCurve.Linear(0, 0, 1, 1);
    [System.NonSerialized]
    public float forwardSlip;
    [System.NonSerialized]
    public float sidewaysSlip;
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
    public float rimRadius;
    public float tireWidth;
    public float rimWidth;

    [System.NonSerialized]
    public float setTireWidth;
    [System.NonSerialized]
    public float tireWidthPrev;
    [System.NonSerialized]
    public float setTireRadius;
    [System.NonSerialized]
    public float tireRadiusPrev;

    [System.NonSerialized]
    public float setRimWidth;
    [System.NonSerialized]
    public float rimWidthPrev;
    [System.NonSerialized]
    public float setRimRadius;
    [System.NonSerialized]
    public float rimRadiusPrev;

    [System.NonSerialized]
    public float actualRadius;

    [Header("Tire")]

    [Range(0, 1)]
    public float tirePressure = 1;
    [System.NonSerialized]
    public float setTirePressure;
    [System.NonSerialized]
    public float tirePressurePrev;
    float initialTirePressure;
    public bool popped;
    [System.NonSerialized]
    public bool setPopped;
    [System.NonSerialized]
    public bool poppedPrev;
    public bool canPop;

    [Tooltip("Requires deform shader")]
    public float deformAmount;
    Material rimMat;
    Material tireMat;
    float airLeakTime = -1;

    [Range(0, 1)]
    public float rimGlow;
    float glowAmount;
    Color glowColor;

    [System.NonSerialized]
    public bool updatedSize;
    [System.NonSerialized]
    public bool updatedPopped;

    float currentRPM;
    [System.NonSerialized]
    public DriveForce targetDrive;
    [System.NonSerialized]
    public float rawRPM; // RPM based purely on velocity
    [System.NonSerialized]
    public WheelContact contactPoint = new WheelContact();
    [System.NonSerialized]
    public bool getContact = true; // Should the wheel try to get contact info?
    [System.NonSerialized]
    public bool grounded;
    float airTime;
    [System.NonSerialized]
    public float travelDist;
    Vector3 upDir; // Up direction
    float circumference;

    [System.NonSerialized]
    public Vector3 contactVelocity; // Velocity of contact point
    float actualEbrake;
    float actualTargetRPM;
    float actualTorque;

    [System.NonSerialized]
    public Vector3 forceApplicationPoint; // Point at which friction forces are applied

    [Tooltip("Apply friction forces at ground point")]
    public bool applyForceAtGroundContact;

    [Header("Audio")]

    public AudioSource impactSnd;
    public AudioClip[] tireHitClips;

    void Start()
    {
        rb = transform.GetTopmostParentComponent<Rigidbody>();
        vp = transform.GetTopmostParentComponent<VehicleController>();
        suspensionParent = transform.parent.GetComponent<NewSuspension>();
        travelDist = suspensionParent.targetCompression;
        initialTirePressure = tirePressure;

        if (transform.childCount > 0)
        {
            // Get rim
            rim = transform.GetChild(0);

            // Set up rim glow material
            if (rimGlow > 0 && Application.isPlaying)
            {
                rimMat = new Material(rim.GetComponent<MeshRenderer>().sharedMaterial);
                rimMat.EnableKeyword("_EMISSION");
                rim.GetComponent<MeshRenderer>().sharedMaterial = rimMat;
            }

            // Get tire
            if (rim.childCount > 0)
            {
                tire = rim.GetChild(0);
                if (deformAmount > 0 && Application.isPlaying)
                {
                    tireMat = new Material(tire.GetComponent<MeshRenderer>().sharedMaterial);
                    tire.GetComponent<MeshRenderer>().sharedMaterial = tireMat;
                }
            }

            if (Application.isPlaying)
            {
                // Generate hard collider
                if (generateHardCollider)
                {
                    GameObject sphereColNew = new GameObject("Rim Collider");
                    sphereColNew.layer = GlobalControl.ignoreWheelCastLayer;
                    sphereColTr = sphereColNew.transform;
                    sphereCol = sphereColNew.AddComponent<SphereCollider>();
                    sphereColTr.parent = transform;
                    sphereColTr.localPosition = Vector3.zero;
                    sphereColTr.localRotation = Quaternion.identity;
                    sphereCol.radius = Mathf.Min(rimWidth * 0.5f, rimRadius * 0.5f);
                    sphereCol.sharedMaterial = GlobalControl.frictionlessMatStatic;
                }
            }
        }

        targetDrive = GetComponent<DriveForce>();
        currentRPM = 0;
    }

    void FixedUpdate()
    {
        upDir = transform.up;
        actualRadius = popped ? rimRadius : Mathf.Lerp(rimRadius, tireRadius, tirePressure);
        circumference = Mathf.PI * actualRadius * 2;
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

            // Update hard collider size upon changed radius or width
            if (generateHardCollider)
            {
                setRimWidth = rimWidth;
                setRimRadius = rimRadius;
                setTireWidth = tireWidth;
                setTireRadius = tireRadius;
                setTirePressure = tirePressure;

                if (rimWidthPrev != setRimWidth || rimRadiusPrev != setRimRadius)
                {
                    sphereCol.radius = Mathf.Min(rimWidth * 0.5f, rimRadius * 0.5f);
                    updatedSize = true;
                }
                else if (tireWidthPrev != setTireWidth || tireRadiusPrev != setTireRadius || tirePressurePrev != setTirePressure)
                {
                    updatedSize = true;
                }
                else
                {
                    updatedSize = false;
                }

                rimWidthPrev = setRimWidth;
                rimRadiusPrev = setRimRadius;
                tireWidthPrev = setTireWidth;
                tireRadiusPrev = setTireRadius;
                tirePressurePrev = setTirePressure;
            }

            GetSlip();
            ApplyFriction();

            // Burnout spinning
            if (vp.burnout > 0 && targetDrive.rpm != 0 && actualEbrake * vp.ebrakeInput == 0 && grounded)
            {
                rb.AddForceAtPosition(suspensionParent.forwardDir * -suspensionParent.flippedSideFactor * (vp.steerInput * vp.burnoutSpin * currentRPM * Mathf.Min(0.1f, targetDrive.torque) * 0.001f) * vp.burnout * (popped ? 0.5f : 1) * contactPoint.surfaceFriction, suspensionParent.transform.position, vp.wheelForceMode);
            }

            // Popping logic
            setPopped = popped;

            if (poppedPrev != setPopped)
            {
                if (tire)
                {
                    tire.gameObject.SetActive(!popped);
                }

                updatedPopped = true;
            }
            else
            {
                updatedPopped = false;
            }

            poppedPrev = setPopped;
            
    }

    void Update()
    {
        RotateWheel();

        if (!Application.isPlaying)
        {
            PositionWheel();
        }
        else
        {
            // Update tire and rim materials
            if (deformAmount > 0 && tireMat)
            {
                if (tireMat.HasProperty("_DeformNormal"))
                {
                    // Deform tire (requires deform shader)
                    Vector3 deformNormal = grounded ? contactPoint.normal * Mathf.Max(-suspensionParent.penetration * (1 - suspensionParent.compression) * 10, 1 - tirePressure) * deformAmount : Vector3.zero;
                    tireMat.SetVector("_DeformNormal", new Vector4(deformNormal.x, deformNormal.y, deformNormal.z, 0));
                }
            }

            if (rimMat)
            {
                if (rimMat.HasProperty("_EmissionColor"))
                {
                    // Make the rim glow
                    float targetGlow = GroundSurfaceMaster.surfaceTypesStatic[contactPoint.surfaceType].leaveSparks ? Mathf.Abs(F.MaxAbs(forwardSlip, sidewaysSlip)) : 0;
                    glowAmount = popped ? Mathf.Lerp(glowAmount, targetGlow, (targetGlow > glowAmount ? 2 : 0.2f) * Time.deltaTime) : 0;
                    glowColor = new Color(glowAmount, glowAmount * 0.5f, 0);
                    rimMat.SetColor("_EmissionColor", popped ? Color.Lerp(Color.black, glowColor, glowAmount * rimGlow) : Color.black);
                }
            }
        }
    }

    // Use raycasting to find the current contact point for the wheel
    void GetWheelContact()
    {
        float castDist = Mathf.Max(suspensionParent.suspensionDistance * Mathf.Max(0.001f, suspensionParent.targetCompression) + actualRadius, 0.001f);
        RaycastHit[] wheelHits = Physics.RaycastAll(suspensionParent.maxCompressPoint, suspensionParent.springDirection, castDist, GlobalControl.wheelCastMaskStatic);
        RaycastHit hit;
        int hitIndex = 0;
        bool validHit = false;
        float hitDist = Mathf.Infinity;

            for (int i = 0; i < wheelHits.Length; i++)
            {
                if (!wheelHits[i].transform.IsChildOf(vp.transform) && wheelHits[i].distance < hitDist)
                {
                    hitIndex = i;
                    hitDist = wheelHits[i].distance;
                    validHit = true;
                }
            }
        

        // Set contact point variables
        if (validHit)
        {
            hit = wheelHits[hitIndex];

            if (!grounded && impactSnd && (tireHitClips.Length > 0))
            {
                impactSnd.PlayOneShot(tireHitClips[Mathf.RoundToInt(Random.Range(0, tireHitClips.Length - 1))], Mathf.Clamp01(airTime * airTime));
                impactSnd.pitch = Mathf.Clamp(airTime * 0.2f + 0.8f, 0.8f, 1);
            }

            grounded = true;
            contactPoint.distance = hit.distance - actualRadius;
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
            contactPoint.normal = upDir;
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
            float forwardSlipFactor = (int)slipDependence == 0 || (int)slipDependence == 1 ? forwardSlip - sidewaysSlip : forwardSlip;
            float sidewaysSlipFactor = (int)slipDependence == 0 || (int)slipDependence == 2 ? sidewaysSlip - forwardSlip : sidewaysSlip;
            float forwardSlipDependenceFactor = Mathf.Clamp01(forwardSlipDependence - Mathf.Clamp01(Mathf.Abs(sidewaysSlip)));
            float sidewaysSlipDependenceFactor = Mathf.Clamp01(sidewaysSlipDependence - Mathf.Clamp01(Mathf.Abs(forwardSlip)));

            float targetForceX = forwardFrictionCurve.Evaluate(Mathf.Abs(forwardSlipFactor)) * -System.Math.Sign(forwardSlip) * (popped ? forwardRimFriction : forwardFriction) * forwardSlipDependenceFactor * -suspensionParent.flippedSideFactor;
            float targetForceZ = sidewaysFrictionCurve.Evaluate(Mathf.Abs(sidewaysSlipFactor)) * -System.Math.Sign(sidewaysSlip) * (popped ? sidewaysRimFriction : sidewaysFriction) * sidewaysSlipDependenceFactor *
                normalFrictionCurve.Evaluate(Mathf.Clamp01(Vector3.Dot(contactPoint.normal, GlobalControl.worldUpDir))) *
                (vp.burnout > 0 && Mathf.Abs(targetDrive.rpm) != 0 && actualEbrake * vp.ebrakeInput == 0 && grounded ? (1 - vp.burnout) * (1 - Mathf.Abs(vp.accelInput)) : 1);

            Vector3 targetForce = transform.TransformDirection(targetForceX, 0, targetForceZ);
            float targetForceMultiplier = ((1 - compressionFrictionFactor) + (1 - suspensionParent.compression) * compressionFrictionFactor * Mathf.Clamp01(Mathf.Abs(suspensionParent.transform.InverseTransformDirection(localVel).z) * 10)) * contactPoint.surfaceFriction;
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

        currentRPM = Mathf.Lerp(rawRPM,
            Mathf.Lerp(
            Mathf.Lerp(rawRPM, actualTargetRPM, validTorque ? EvaluateTorque(actualTorque) : actualTorque),
            0, Mathf.Max(brakeForce, actualEbrake * vp.ebrakeInput)),
        validTorque ? EvaluateTorque(actualTorque + brakeForce + actualEbrake * vp.ebrakeInput) : actualTorque + brakeForce + actualEbrake * vp.ebrakeInput);

        targetDrive.feedbackRPM = Mathf.Lerp(currentRPM, rawRPM, feedbackRpmBias);
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
                suspensionParent.upDir * Mathf.Pow(Mathf.Max(Mathf.Abs(Mathf.Sin(suspensionParent.sideAngle * Mathf.Deg2Rad)), Mathf.Abs(Mathf.Sin(suspensionParent.casterAngle * Mathf.Deg2Rad))), 2) * actualRadius +
                suspensionParent.pivotOffset * suspensionParent.transform.TransformDirection(Mathf.Sin(transform.localEulerAngles.y * Mathf.Deg2Rad), 0, Mathf.Cos(transform.localEulerAngles.y * Mathf.Deg2Rad))
                - suspensionParent.pivotOffset * (Application.isPlaying ? suspensionParent.forwardDir : suspensionParent.transform.forward);
        }

        if (Application.isPlaying && generateHardCollider)
        {
            sphereColTr.position = rim.position;
        }
    }

    // Visual wheel rotation
    void RotateWheel()
    {
        if (transform && suspensionParent)
        {
            float ackermannVal = Mathf.Sign(suspensionParent.steerAngle) == suspensionParent.flippedSideFactor ? 1 + suspensionParent.ackermannFactor : 1 - suspensionParent.ackermannFactor;
            transform.localEulerAngles = new Vector3(
                suspensionParent.camberAngle + suspensionParent.casterAngle * suspensionParent.steerAngle * suspensionParent.flippedSideFactor,
                -suspensionParent.toeAngle * suspensionParent.flippedSideFactor + suspensionParent.steerDegrees * ackermannVal,
                0);
        }

        if (Application.isPlaying)
        {
            rim.Rotate(Vector3.forward, currentRPM * suspensionParent.flippedSideFactor * Time.deltaTime);

            if (rim.localEulerAngles.x != 0 || rim.localEulerAngles.y != 0)
            {
                rim.localEulerAngles = new Vector3(0, 0, rim.localEulerAngles.z);
            }
        }
    }

    // Automatically sets wheel dimensions based on rim/tire meshes
    public void GetWheelDimensions(float radiusMargin, float widthMargin)
    {
        Mesh rimMesh = null;
        Mesh tireMesh = null;
        Mesh checker;
        Transform scaler = transform;

        if (transform.childCount > 0)
        {
            if (transform.GetChild(0).GetComponent<MeshFilter>())
            {
                rimMesh = transform.GetChild(0).GetComponent<MeshFilter>().sharedMesh;
                scaler = transform.GetChild(0);
            }

            if (transform.GetChild(0).childCount > 0)
            {
                if (transform.GetChild(0).GetChild(0).GetComponent<MeshFilter>())
                {
                    tireMesh = transform.GetChild(0).GetChild(0).GetComponent<MeshFilter>().sharedMesh;
                }
            }

            checker = tireMesh ? tireMesh : rimMesh;

            if (checker)
            {
                float maxWidth = 0;
                float maxRadius = 0;

                foreach (Vector3 curVert in checker.vertices)
                {
                    if (new Vector2(curVert.x * scaler.localScale.x, curVert.y * scaler.localScale.y).magnitude > maxRadius)
                    {
                        maxRadius = new Vector2(curVert.x * scaler.localScale.x, curVert.y * scaler.localScale.y).magnitude;
                    }

                    if (Mathf.Abs(curVert.z * scaler.localScale.z) > maxWidth)
                    {
                        maxWidth = Mathf.Abs(curVert.z * scaler.localScale.z);
                    }
                }

                tireRadius = maxRadius + radiusMargin;
                tireWidth = maxWidth + widthMargin;

                if (tireMesh && rimMesh)
                {
                    maxWidth = 0;
                    maxRadius = 0;

                    foreach (Vector3 curVert in rimMesh.vertices)
                    {
                        if (new Vector2(curVert.x * scaler.localScale.x, curVert.y * scaler.localScale.y).magnitude > maxRadius)
                        {
                            maxRadius = new Vector2(curVert.x * scaler.localScale.x, curVert.y * scaler.localScale.y).magnitude;
                        }

                        if (Mathf.Abs(curVert.z * scaler.localScale.z) > maxWidth)
                        {
                            maxWidth = Mathf.Abs(curVert.z * scaler.localScale.z);
                        }
                    }

                    rimRadius = maxRadius + radiusMargin;
                    rimWidth = maxWidth + widthMargin;
                }
                else
                {
                    rimRadius = maxRadius * 0.5f + radiusMargin;
                    rimWidth = maxWidth * 0.5f + widthMargin;
                }
            }
            else
            {
                Debug.LogError("No rim or tire meshes found for getting wheel dimensions.", this);
            }
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

        float tireActualRadius = Mathf.Lerp(rimRadius, tireRadius, tirePressure);

        if (tirePressure < 1 && tirePressure > 0)
        {
            Gizmos.color = new Color(1, 1, 0, popped ? 0.5f : 1);
            GizmosExtra.DrawWireCylinder(rim.position, rim.forward, tireActualRadius, tireWidth * 2);
        }

        Gizmos.color = Color.white;
        GizmosExtra.DrawWireCylinder(rim.position, rim.forward, tireRadius, tireWidth * 2);

        Gizmos.color = tirePressure == 0 || popped ? Color.green : Color.cyan;
        GizmosExtra.DrawWireCylinder(rim.position, rim.forward, rimRadius, rimWidth * 2);

        Gizmos.color = new Color(1, 1, 1, tirePressure < 1 ? 0.5f : 1);
        GizmosExtra.DrawWireCylinder(rim.position, rim.forward, tireRadius, tireWidth * 2);

        Gizmos.color = tirePressure == 0 || popped ? Color.green : Color.cyan;
        GizmosExtra.DrawWireCylinder(rim.position, rim.forward, rimRadius, rimWidth * 2);
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
