using RVP;
using System.Collections.Generic;
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
    public AxleExtraValues extra => suspensionSettings.extra; //{ get; private set; }
    public AxleBrakeValues brake => suspensionSettings.brake; //{ get; private set; }
    public AxleSteeringValues steering => suspensionSettings.steering; //{ get; private set; }
    public AxleCamberValues camber => suspensionSettings.camber; //{ get; private set; }
    public AxleSpringValues spring => suspensionSettings.spring; //{ get; private set; }
    #endregion

    public bool flippedSide { get; private set; }
    public float flippedSideFactor { get; private set; }
    public Quaternion initialRotation { get; private set; }

    public NewWheel wheel;

    
    [Header("Brakes and Steering")]
    public float brakeForce;
    public float ebrakeForce;
    

    
    [Range(-180, 180)]
    public float steerRangeMin;
    [Range(-180, 180)]
    public float steerRangeMax;

    [Tooltip("How much the wheel is steered")]
    public float steerFactor = 1;
    [Range(-1, 1)]
    public float steerAngle;

    [Tooltip("Effect of Ackermann steering geometry")]
    public float ackermannFactor;
    

    
    [Tooltip("The camber of the wheel as it travels, x-axis = compression, y-axis = angle")]
    public AnimationCurve camberCurve = AnimationCurve.Linear(0, 0, 1, 0);
    [Range(-89.999f, 89.999f)]
    public float camberOffset;

    [Tooltip("Adjust the camber as if it was connected to a solid axle, opposite wheel must be set")]
    public bool solidAxleCamber;

    [Tooltip("Angle at which the suspension points out to the side")]
    [Range(-89.999f, 89.999f)]
    public float sideAngle;
    [Range(-89.999f, 89.999f)]
    public float casterAngle;
    [Range(-89.999f, 89.999f)]
    public float toeAngle;

    [Tooltip("Wheel offset from its pivot point")]
    public float pivotOffset;
    

    
    [Header("Spring")]
    public float suspensionDistance;

    [Tooltip("Should be left at 1 unless testing suspension travel")]
    [Range(0, 1)]
    public float targetCompression;
    public float springForce;

    [Tooltip("Force of the curve depending on it's compression, x-axis = compression, y-axis = force")]
    public AnimationCurve springForceCurve = AnimationCurve.Linear(0, 0, 1, 1);

    [Tooltip("Exponent for spring force based on compression")]
    public float springExponent = 1;
    public float springDampening;

    [Tooltip("How quickly the suspension extends if it's not grounded")]
    public float extendSpeed = 20;

    [Tooltip("Apply forces to prevent the wheel from intersecting with the ground, not necessary if generating a hard collider")]
    public bool applyHardContactForce = true;
    public float hardContactForce = 50;
    public float hardContactSensitivity = 2;

    [Tooltip("Apply suspension forces at ground point")]
    public bool applyForceAtGroundContact = true;

    [Tooltip("Apply suspension forces along local up direction instead of ground normal")]
    public bool leaningForce;
    

    public List<SuspensionPart> movingParts { get; private set; } = new List<SuspensionPart>();
    public float steerDegrees { get; private set; }
    public float camberAngle { get; private set; }
    public float compression { get; private set; }
    public float penetration { get; private set; }
    public Vector3 maxCompressPoint { get; private set; }
    public Vector3 springDirection { get; private set; }
    public Vector3 upDir { get; private set; }
    public Vector3 forwardDir { get; private set; }

    public DriveForce targetDrive { get; private set; }

    public SuspensionPropertyToggle properties { get; private set; }
    public bool steerEnabled { get; private set; } = true;
    public bool steerInverted { get; private set; }
    public bool driveEnabled { get; private set; } = true;
    public bool driveInverted { get; private set; }
    public bool ebrakeEnabled { get; private set; } = true;
    public bool skidSteerBrake { get; private set; }

    #region Misc
    [Tooltip("Generate a capsule collider for hard compressions")]
    public bool generateHardCollider = true;

    [Tooltip("Multiplier for the radius of the hard collider")]
    public float hardColliderRadiusFactor = 1;
    float setHardColliderRadiusFactor;
    CapsuleCollider compressCol; // The hard collider
    Transform compressTr; // Transform component of the hard collider
    #endregion

    //private void Awake()
    //{
    //    extra = suspensionSettings.extra;
    //    brake = suspensionSettings.brake;
    //    steering = suspensionSettings.steering;
    //    camber = suspensionSettings.camber;
    //    spring = suspensionSettings.spring;
    //}

    void Start()
    {
        rb = transform.GetTopmostParentComponent<Rigidbody>();
        vp = transform.GetTopmostParentComponent<VehicleController>();
        targetDrive = GetComponent<DriveForce>();
        flippedSide = Vector3.Dot(transform.forward, vp.transform.right) < 0;
        flippedSideFactor = flippedSide ? -1 : 1;
        initialRotation = transform.localRotation;

        if (Application.isPlaying)
        {
            GetCamber();

            // Generate the hard collider
            if (generateHardCollider)
            {
                GameObject cap = new GameObject("Compress Collider");
                cap.layer = GlobalControl.ignoreWheelCastLayer;
                compressTr = cap.transform;
                compressTr.parent = transform;
                compressTr.localPosition = Vector3.zero;
                compressTr.localEulerAngles = new Vector3(camberAngle, 0, -camber.casterAngle * flippedSideFactor);
                compressCol = cap.AddComponent<CapsuleCollider>();
                compressCol.direction = 1;
                compressCol.radius = wheel.tireSize.tireWidth * hardColliderRadiusFactor;
                compressCol.height = wheel.tireSize.tireRadius * 2;
                compressCol.sharedMaterial = GlobalControl.frictionlessMatStatic;
            }

            steering.steerRangeMax = Mathf.Max(steering.steerRangeMin, steering.steerRangeMax);

            properties = GetComponent<SuspensionPropertyToggle>();
            if (properties)
            {
                UpdateProperties();
            }
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
        if (wheel.targetDrive)
        {
            targetDrive.active = driveEnabled;
            targetDrive.feedbackRPM = wheel.targetDrive.feedbackRPM;
            wheel.targetDrive.SetDrive(targetDrive);
        }
    }

    void Update()
    {
        GetCamber();

        if (!Application.isPlaying)
        {
            GetSpringVectors();
        }

        // Set steer angle for the wheel
        steerDegrees = Mathf.Abs(steering.steerAngle) * (steering.steerAngle > 0 ? steering.steerRangeMax : steering.steerRangeMin);
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
                    vp.suspensionForceMode);

                // If wheel is resting on a rigidbody, apply opposing force to it
                if (groundBody)
                {
                    groundBody.AddForceAtPosition(
                        -appliedSuspensionForce,
                        wheel.contactPoint.point,
                        vp.suspensionForceMode);
                }
            }

            // Apply hard contact force
            if (compression == 0 && !generateHardCollider && extra.applyHardContactForce)
            {
                rb.AddForceAtPosition(
                    -vp.norm.TransformDirection(0, 0, Mathf.Clamp(travelVel, -spring.hardContactSensitivity * TimeMaster.fixedTimeFactor, 0) + penetration) * spring.hardContactForce * Mathf.Clamp01(TimeMaster.fixedTimeFactor),
                    extra.applyForceAtGroundContact ? wheel.contactPoint.point : wheel.transform.position,
                    vp.suspensionForceMode);
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

    // Update the toggleable properties
    public void UpdateProperties()
    {
        if (properties)
        {
            foreach (SuspensionToggledProperty curProperty in properties.properties)
            {
                switch ((int)curProperty.property)
                {
                    case 0:
                        steerEnabled = curProperty.toggled;
                        break;
                    case 1:
                        steerInverted = curProperty.toggled;
                        break;
                    case 2:
                        driveEnabled = curProperty.toggled;
                        break;
                    case 3:
                        driveInverted = curProperty.toggled;
                        break;
                    case 4:
                        ebrakeEnabled = curProperty.toggled;
                        break;
                    case 5:
                        skidSteerBrake = curProperty.toggled;
                        break;
                }
            }
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
                float steerSin = Mathf.Sin(Mathf.Lerp(activeSteerSettings.steerRangeMin, activeSteerSettings.steerRangeMax, (activeSteerSettings.steerAngle + 1) * 0.5f) * Mathf.Deg2Rad);
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
