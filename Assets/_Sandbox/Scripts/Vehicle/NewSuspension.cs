using RVP;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

/* TO DO */
/* 
 * Refactor ApplySuspensionForce
*/

public class NewSuspension : MonoBehaviour
{
    #region Core Components
    public Rigidbody rb { get; private set; }
    public VehicleController vp { get; private set; }
    public NewSuspension oppositeAxle;
    #endregion

    #region Settings
    public SuspensionSettings suspensionSettings;
    public AxleExtraValues extra { get; private set; }
    public AxleCamberValues camber { get; private set; }
    public AxleSpringValues spring { get; private set; }
    #endregion

    public bool flippedSide { get; private set; }
    public float flippedSideFactor { get; private set; }
    public Quaternion initialRotation { get; private set; }

    public NewWheel wheel { get; private set; }
    public float camberAngle { get; private set; }
    public float compression => Mathf.Min(spring.targetCompression, spring.suspensionDistance > 0 ? 
                                Mathf.Clamp01(wheel.contactPoint.distance / spring.suspensionDistance) : 0);
    public float penetration => Mathf.Min(0, wheel.contactPoint.distance);
    public Vector3 maxCompressPoint { get; private set; }
    public Vector3 springDirection { get; private set; }

    #region Misc
    private CapsuleCollider compressCol; // The hard collider
    private Transform compressTr; // Transform component of the hard collider
    #endregion

    private void Start()
    {
        SetSettingsProfile(suspensionSettings);
        GetCoreComponents();
        GetPositionFactor();
        GenerateCollider();
    }

    public void SetSettingsProfile(SuspensionSettings _settings)
    {
        extra = _settings.extra;
        camber = _settings.camber;
        spring = _settings.spring;

        suspensionSettings = _settings;
    }

    private void GetCoreComponents()
    {
        rb = transform.GetTopmostParentComponent<Rigidbody>();
        vp = transform.GetTopmostParentComponent<VehicleController>();
        wheel = GetComponentInChildren<NewWheel>();
    }

    private void GetPositionFactor()
    {
        flippedSide = Vector3.Dot(transform.forward, vp.transform.right) < 0;
        flippedSideFactor = flippedSide ? -1 : 1;
        initialRotation = transform.localRotation;
    }

    private void GenerateCollider()
    {
        if (!extra.generateHardCollider) return;

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

    void FixedUpdate()
    {
        GetCamber();
        GetSpringVectors();
        ApplySuspensionForce();
    }

    // TO REFACTOR AND INHERIT FROM WHEEL
    private void ApplySuspensionForce()
    {
        if (!wheel.grounded) return;

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
        Vector3 appliedSuspensionForce = (extra.leaningForce ? Vector3.Lerp(transform.up, vp.norm.forward, Mathf.Abs(Mathf.Pow(Vector3.Dot(vp.norm.forward, vp.transform.up), 5))) : vp.norm.forward) *
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

        // Apply hard contact force
        if (compression == 0 && !extra.generateHardCollider && extra.applyHardContactForce)
        {
            rb.AddForceAtPosition(
                -vp.norm.TransformDirection(0, 0, Mathf.Clamp(travelVel, -spring.hardContactSensitivity * TimeMaster.fixedTimeFactor, 0) + penetration) * spring.hardContactForce * Mathf.Clamp01(TimeMaster.fixedTimeFactor),
                extra.applyForceAtGroundContact ? wheel.contactPoint.point : wheel.transform.position,
                vp.extras.suspensionForceMode);
        }
    }

    private void GetSpringVectors()
    {
        maxCompressPoint = transform.position;

        float casterDir = -Mathf.Sin(camber.casterAngle * Mathf.Deg2Rad) * flippedSideFactor;
        float sideDir = -Mathf.Sin(camber.sideAngle * Mathf.Deg2Rad);

        springDirection = transform.TransformDirection(casterDir, Mathf.Max(Mathf.Abs(casterDir), Mathf.Abs(sideDir)) - 1, sideDir).normalized;
    }

    private void GetCamber()
    {
        if (camber.solidAxleCamber && oppositeAxle)
        {
                Vector3 axleDir = transform.InverseTransformDirection((oppositeAxle.wheel.rim.position - wheel.rim.position).normalized);
                camberAngle = Mathf.Atan2(axleDir.z, axleDir.y) * Mathf.Rad2Deg + 90 + camber.camberOffset;
        }
        else
        {
            camberAngle = camber.camberCurve.Evaluate(wheel.travelDist) + camber.camberOffset;
        }
    }
}