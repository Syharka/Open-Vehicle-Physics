using RVP;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

public class NewSuspension : MonoBehaviour
{
    #region Core Components
    public Rigidbody rb { get; private set; }
    public VehicleController vp { get; private set; }
    public NewSuspension oppositeWheel;
    #endregion

    #region Settings
    public SuspensionSettings suspensionSettings;
    public AxleExtraValues extra { get; private set; }
    public AxleBrakeValues brake { get; private set; }
    public AxleSteeringValues steering { get; private set; }
    public AxleCamberValues camber { get; private set; }
    public AxleSpringValues spring { get; private set; }
    #endregion

    public bool flippedSide { get; private set; }
    public float flippedSideFactor { get; private set; }
    public Quaternion initialRotation { get; private set; }

    public NewWheel wheel { get; private set; }

    public List<SuspensionPart> movingParts { get; private set; } = new List<SuspensionPart>();
    public float steerDegrees { get; private set; }
    [NonSerialized]
    public float steerAngle;
    public float camberAngle { get; private set; }
    public float compression { get; private set; }
    public float penetration { get; private set; }
    public Vector3 maxCompressPoint { get; private set; }
    public Vector3 springDirection { get; private set; }
    public Vector3 upDir { get; private set; }
    public Vector3 forwardDir { get; private set; }
    public Drivetrain targetDrive { get; private set; }

    public SuspensionPropertyToggle properties { get; private set; }

    [Range(-1,1)]
    public float steerFactor;
    public bool driveEnabled = true;
    public bool ebrakeEnabled = true;
    public bool skidSteerBrake = false;

    #region Misc
    private CapsuleCollider compressCol; // The hard collider
    private Transform compressTr; // Transform component of the hard collider
    #endregion

    private void Awake()
    {
        extra = suspensionSettings.extra;
        brake = suspensionSettings.brake;
        steering = suspensionSettings.steering;
        camber = suspensionSettings.camber;
        spring = suspensionSettings.spring;
    }

    void Start()
    {
        rb = transform.GetTopmostParentComponent<Rigidbody>();
        vp = transform.GetTopmostParentComponent<VehicleController>();
        wheel = GetComponentInChildren<NewWheel>();
        flippedSide = Vector3.Dot(transform.forward, vp.transform.right) < 0;
        flippedSideFactor = flippedSide ? -1 : 1;
        initialRotation = transform.localRotation;

        targetDrive = new Drivetrain();

        if (Application.isPlaying)
        {
            GetCamber();

            // Generate the hard collider
            if (extra.generateHardCollider)
            {
                GameObject cap = new GameObject("Compress Collider");
                cap.layer = GlobalControl.ignoreWheelCastLayer;
                compressTr = cap.transform;
                compressTr.parent = transform;
                compressTr.localPosition = Vector3.zero;
                compressTr.localEulerAngles = new Vector3(camberAngle, 0, -camber.casterAngle * flippedSideFactor);
                compressCol = cap.AddComponent<CapsuleCollider>();
                compressCol.direction = 1;
                compressCol.radius = wheel.tireSize.tireWidth * extra.hardColliderRadiusFactor;
                compressCol.height = wheel.tireSize.tireRadius * 2;
                compressCol.sharedMaterial = GlobalControl.frictionlessMatStatic;
            }

            steering.steerRangeMax = Mathf.Max(steering.steerRangeMin, steering.steerRangeMax);
        }
    }

    void FixedUpdate()
    {
        upDir = transform.up;
        forwardDir = transform.forward;
        spring.targetCompression = 1;

        GetCamber();

        GetSpringVectors();

        compression = Mathf.Min(spring.targetCompression, spring.suspensionDistance > 0 ? Mathf.Clamp01(wheel.contactPoint.distance / spring.suspensionDistance) : 0);
        penetration = Mathf.Min(0, wheel.contactPoint.distance);

        if (spring.targetCompression > 0)
        {
            ApplySuspensionForce();
        }

        targetDrive.active = driveEnabled;
        targetDrive.feedbackRPM = wheel.targetDrive.feedbackRPM;
        wheel.targetDrive.SetDrive(targetDrive);

    }

    void Update()
    {
        GetCamber();

        if (!Application.isPlaying)
        {
            GetSpringVectors();
        }

        // Set steer angle for the wheel
        steerDegrees = Mathf.Abs(steerAngle) * (steerAngle > 0 ? steering.steerRangeMax : steering.steerRangeMin);
    }

    // Apply suspension forces to support vehicles
    void ApplySuspensionForce()
    {
        if (wheel.grounded)
        {
            // Get velocity of ground to offset from local vertical velocity
            Rigidbody groundBody = wheel.contactPoint.col.attachedRigidbody;
            Vector3 groundVel = Vector3.zero;
            if (groundBody)
            {
                groundVel = groundBody.linearVelocity;
            }

            // Get the local vertical velocity
            float travelVel = vp.norm.InverseTransformDirection(rb.GetPointVelocity(transform.position) - groundVel).z;

            // Apply the suspension force
            if (spring.suspensionDistance > 0 && spring.targetCompression > 0)
            {
                Vector3 appliedSuspensionForce = (extra.leaningForce ? Vector3.Lerp(upDir, vp.norm.forward, Mathf.Abs(Mathf.Pow(Vector3.Dot(vp.norm.forward, vp.transform.up), 5))) : vp.norm.forward) *
                    spring.springForce * (Mathf.Pow(spring.springForceCurve.Evaluate(1 - compression), Mathf.Max(1, spring.springExponent)) - (1 - spring.targetCompression) - spring.springDampening * Mathf.Clamp(travelVel, -1, 1));

                rb.AddForceAtPosition(
                    appliedSuspensionForce,
                    extra.applyForceAtGroundContact ? wheel.contactPoint.point : wheel.transform.position,
                    vp.extras.suspensionForceMode);

                // If wheel is resting on a rigidbody, apply opposing force to it
                if (groundBody)
                {
                    groundBody.AddForceAtPosition(
                        -appliedSuspensionForce,
                        wheel.contactPoint.point,
                        vp.extras.suspensionForceMode);
                }
            }

            // Apply hard contact force
            if (compression == 0 && !extra.generateHardCollider && extra.applyHardContactForce)
            {
                rb.AddForceAtPosition(
                    -vp.norm.TransformDirection(0, 0, Mathf.Clamp(travelVel, -spring.hardContactSensitivity * TimeMaster.fixedTimeFactor, 0) + penetration) * spring.hardContactForce * Mathf.Clamp01(TimeMaster.fixedTimeFactor),
                    extra.applyForceAtGroundContact ? wheel.contactPoint.point : wheel.transform.position,
                    vp.extras.suspensionForceMode);
            }
        }
    }

    // Calculate the direction of the spring
    void GetSpringVectors()
    {
        if (!Application.isPlaying)
        {
            flippedSide = Vector3.Dot(transform.forward, vp.transform.right) < 0;
            flippedSideFactor = flippedSide ? -1 : 1;
        }

        maxCompressPoint = transform.position;

        float casterDir = -Mathf.Sin(camber.casterAngle * Mathf.Deg2Rad) * flippedSideFactor;
        float sideDir = -Mathf.Sin(camber.sideAngle * Mathf.Deg2Rad);

        springDirection = transform.TransformDirection(casterDir, Mathf.Max(Mathf.Abs(casterDir), Mathf.Abs(sideDir)) - 1, sideDir).normalized;
    }

    // Calculate the camber angle
    void GetCamber()
    {
        if (camber.solidAxleCamber && oppositeWheel)
        {
            if (oppositeWheel.wheel.rim && wheel.rim)
            {
                Vector3 axleDir = transform.InverseTransformDirection((oppositeWheel.wheel.rim.position - wheel.rim.position).normalized);
                camberAngle = Mathf.Atan2(axleDir.z, axleDir.y) * Mathf.Rad2Deg + 90 + camber.camberOffset;
            }
        }
        else
        {
            camberAngle = camber.camberCurve.Evaluate((Application.isPlaying ? wheel.travelDist : spring.targetCompression)) + camber.camberOffset;
        }
    }

    // Visualize steer range
    void OnDrawGizmosSelected()
    {
        if (wheel)
        {
            if (wheel.rim)
            {
                AxleSteeringValues activeSteerSettings = Application.isPlaying ? steering : suspensionSettings.steering;

                Vector3 wheelPoint = wheel.rim.position;

                float camberSin = -Mathf.Sin(camberAngle * Mathf.Deg2Rad);
                float steerSin = Mathf.Sin(Mathf.Lerp(activeSteerSettings.steerRangeMin, activeSteerSettings.steerRangeMax, (steerAngle + 1) * 0.5f) * Mathf.Deg2Rad);
                float minSteerSin = Mathf.Sin(activeSteerSettings.steerRangeMin * Mathf.Deg2Rad);
                float maxSteerSin = Mathf.Sin(activeSteerSettings.steerRangeMax * Mathf.Deg2Rad);

                Gizmos.color = Color.magenta;

                Gizmos.DrawWireSphere(wheelPoint, 0.05f);

                Gizmos.DrawLine(wheelPoint, wheelPoint + transform.TransformDirection(minSteerSin,
                    camberSin * (1 - Mathf.Abs(minSteerSin)),
                    Mathf.Cos(activeSteerSettings.steerRangeMin * Mathf.Deg2Rad) * (1 - Mathf.Abs(camberSin))
                    ).normalized);

                Gizmos.DrawLine(wheelPoint, wheelPoint + transform.TransformDirection(maxSteerSin,
                    camberSin * (1 - Mathf.Abs(maxSteerSin)),
                    Mathf.Cos(activeSteerSettings.steerRangeMax * Mathf.Deg2Rad) * (1 - Mathf.Abs(camberSin))
                    ).normalized);

                Gizmos.DrawLine(wheelPoint + transform.TransformDirection(minSteerSin,
                    camberSin * (1 - Mathf.Abs(minSteerSin)),
                    Mathf.Cos(activeSteerSettings.steerRangeMin * Mathf.Deg2Rad) * (1 - Mathf.Abs(camberSin))
                    ).normalized * 0.9f,
                wheelPoint + transform.TransformDirection(maxSteerSin,
                    camberSin * (1 - Mathf.Abs(maxSteerSin)),
                    Mathf.Cos(activeSteerSettings.steerRangeMax * Mathf.Deg2Rad) * (1 - Mathf.Abs(camberSin))
                    ).normalized * 0.9f);

                Gizmos.DrawLine(wheelPoint, wheelPoint + transform.TransformDirection(steerSin,
                    camberSin * (1 - Mathf.Abs(steerSin)),
                    Mathf.Cos(activeSteerSettings.steerRangeMin * Mathf.Deg2Rad) * (1 - Mathf.Abs(camberSin))
                    ).normalized);
            }
        }

        Gizmos.color = Color.red;
    }
}