using RVP;
using UnityEngine;
using System.Collections;

public class VehicleController : MonoBehaviour
{
    [System.NonSerialized]
    public Rigidbody rb;
    [System.NonSerialized]
    public Transform norm; // Normal orientation object

    [System.NonSerialized]
    public float accelInput;
    [System.NonSerialized]
    public float brakeInput;
    [System.NonSerialized]
    public float steerInput;
    [System.NonSerialized]
    public float ebrakeInput;
    [System.NonSerialized]
    public bool boostButton;
    [System.NonSerialized]
    public bool upshiftPressed;
    [System.NonSerialized]
    public bool downshiftPressed;
    [System.NonSerialized]
    public float upshiftHold;
    [System.NonSerialized]
    public float downshiftHold;
    [System.NonSerialized]
    public float pitchInput;
    [System.NonSerialized]
    public float yawInput;
    [System.NonSerialized]
    public float rollInput;

    [Tooltip("Accel axis is used for brake input")]
    public bool accelAxisIsBrake;

    [Tooltip("Brake input will act as reverse input")]
    public bool brakeIsReverse;

    [Tooltip("Automatically hold ebrake if it's pressed while parked")]
    public bool holdEbrakePark;

    public float burnoutThreshold = 0.9f;
    [System.NonSerialized]
    public float burnout;
    public float burnoutSpin = 5;
    [Range(0, 0.9f)]
    public float burnoutSmoothness = 0.5f;
    public NewMotor engine;

    bool stopUpshift;
    bool stopDownShift;

    [System.NonSerialized]
    public Vector3 localVelocity; // Local space velocity
    [System.NonSerialized]
    public Vector3 localAngularVel; // Local space angular velocity
    [System.NonSerialized]
    public float forwardDot; // Dot product between forwardDir and GlobalControl.worldUpDir
    [System.NonSerialized]
    public float rightDot; // Dot product between rightDir and GlobalControl.worldUpDir
    [System.NonSerialized]
    public float upDot; // Dot product between upDir and GlobalControl.worldUpDir

    [System.NonSerialized]
    public bool reversing;

    public NewWheel[] wheels;
    [System.NonSerialized]
    public int groundedWheels; // Number of wheels grounded
    [System.NonSerialized]
    public Vector3 wheelNormalAverage; // Average normal of the wheel contact points
    Vector3 wheelContactsVelocity; // Average velocity of wheel contact points

    [Tooltip("Lower center of mass by suspension height")]
    public bool suspensionCenterOfMass;
    public Vector3 centerOfMassOffset;

    public ForceMode wheelForceMode = ForceMode.Acceleration;
    public ForceMode suspensionForceMode = ForceMode.Acceleration;

    void Start()
    {
        rb = GetComponent<Rigidbody>();

        CreateNormalOrientation();
        SetCenterOfMass();
    }

    void Update()
    {
        // Shift single frame pressing logic
        if (stopUpshift)
        {
            upshiftPressed = false;
            stopUpshift = false;
        }

        if (stopDownShift)
        {
            downshiftPressed = false;
            stopDownShift = false;
        }

        if (upshiftPressed)
        {
            stopUpshift = true;
        }

        if (downshiftPressed)
        {
            stopDownShift = true;
        }
    }

    void FixedUpdate()
    {
        GetGroundedWheels();

        localVelocity = transform.InverseTransformDirection(rb.linearVelocity - wheelContactsVelocity);
        localAngularVel = transform.InverseTransformDirection(rb.angularVelocity);
        forwardDot = Vector3.Dot(transform.forward, GlobalControl.worldUpDir);
        rightDot = Vector3.Dot(transform.right, GlobalControl.worldUpDir);
        upDot = Vector3.Dot(transform.up, GlobalControl.worldUpDir);
        norm.transform.position = transform.position;
        norm.transform.rotation = Quaternion.LookRotation(groundedWheels == 0 ? transform.up : wheelNormalAverage, transform.forward);

        SetBurnoutInputs();
        SetReversing();
    }

    private void CreateNormalOrientation()
    {
        if (norm != null) return;

        GameObject normTemp = new GameObject(transform.name + "'s Normal Orientation");
        norm = normTemp.transform;
    }

    public void SetAccel(float f)
    {
        f = Mathf.Clamp(f, -1, 1);
        accelInput = f;
    }

    public void SetBrake(float f)
    {
        brakeInput = accelAxisIsBrake ? -Mathf.Clamp(accelInput, -1, 0) : Mathf.Clamp(f, -1, 1);
    }

    public void SetSteer(float f)
    {
        steerInput = Mathf.Clamp(f, -1, 1);
    }

    public void SetEbrake(float f)
    {
        if ((f > 0 || ebrakeInput > 0) && holdEbrakePark && rb.linearVelocity.magnitude < 1 && accelInput == 0 && (brakeInput == 0 || !brakeIsReverse))
        {
            ebrakeInput = 1;
        }
        else
        {
            ebrakeInput = Mathf.Clamp01(f);
        }
    }

    public void SetBoost(bool b)
    {
        boostButton = b;
    }

    public void SetPitch(float f)
    {
        pitchInput = Mathf.Clamp(f, -1, 1);
    }

    public void SetYaw(float f)
    {
        yawInput = Mathf.Clamp(f, -1, 1);
    }

    public void SetRoll(float f)
    {
        rollInput = Mathf.Clamp(f, -1, 1);
    }

    public void PressUpshift()
    {
        upshiftPressed = true;
    }

    public void PressDownshift()
    {
        downshiftPressed = true;
    }

    public void SetUpshift(float f)
    {
        upshiftHold = f;
    }

    public void SetDownshift(float f)
    {
        downshiftHold = f;
    }

    void SetCenterOfMass()
    {
        float susAverage = 0;

        // Get average suspension height
        if (suspensionCenterOfMass)
        {
            for (int i = 0; i < wheels.Length; i++)
            {
                float newSusDist = wheels[i].transform.parent.GetComponent<NewSuspension>().suspensionDistance;
                susAverage = i == 0 ? newSusDist : (susAverage + newSusDist) * 0.5f;
            }
        }

        rb.centerOfMass = centerOfMassOffset + new Vector3(0, -susAverage, 0);
        rb.inertiaTensor = rb.inertiaTensor; // This is required due to decoupling of inertia tensor from center of mass in Unity 5.3
    }

    void SetBurnoutInputs()
    {
        if (groundedWheels > 0 && !accelAxisIsBrake && burnoutThreshold >= 0 && accelInput > burnoutThreshold && brakeInput > burnoutThreshold)
        {
            burnout = Mathf.Lerp(burnout, ((5 - Mathf.Min(5, Mathf.Abs(localVelocity.z))) / 5) * Mathf.Abs(accelInput), Time.fixedDeltaTime * (1 - burnoutSmoothness) * 10);
        }
        else if (burnout > 0.01f)
        {
            burnout = Mathf.Lerp(burnout, 0, Time.fixedDeltaTime * (1 - burnoutSmoothness) * 10);
        }
        else
        {
            burnout = 0;
        }
    }

    void SetReversing()
    {
        if (brakeIsReverse && brakeInput > 0 && localVelocity.z < 1 && burnout == 0)
        {
            reversing = true;
        }
        else if (localVelocity.z >= 0 || burnout > 0)
        {
            reversing = false;
        }
    }

    // Get the number of grounded wheels and the normals and velocities of surfaces they're sitting on
    void GetGroundedWheels()
    {
        groundedWheels = 0;
        wheelContactsVelocity = Vector3.zero;

        for (int i = 0; i < wheels.Length; i++)
        {
            if (!wheels[i].grounded) break;

            wheelContactsVelocity = i == 0 ? wheels[i].contactVelocity : (wheelContactsVelocity + wheels[i].contactVelocity) * 0.5f;
            wheelNormalAverage = i == 0 ? wheels[i].contactPoint.normal : (wheelNormalAverage + wheels[i].contactPoint.normal).normalized;
            groundedWheels++;
        }
    }

    void OnDestroy()
    {
        if (norm)
        {
            Destroy(norm.gameObject);
        }
    }
}
