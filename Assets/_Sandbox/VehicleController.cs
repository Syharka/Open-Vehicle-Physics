using RVP;
using UnityEngine;
using System.Collections;

public class VehicleController : MonoBehaviour
{
    [Header("Old Stuff")]
    public Rigidbody rb { get; private set; }
    public Transform norm { get; private set; }

    public float accelInput { get; private set; }
    public float brakeInput { get; private set; }
    public float steerInput { get; private set; }
    public float ebrakeInput { get; private set; }
    public bool boostButton { get; private set; }
    public bool upshiftPressed { get; private set; }
    public bool downshiftPressed { get; private set; }
    public float upshiftHold { get; private set; }
    public float downshiftHold { get; private set; }
    public float pitchInput { get; private set; }
    public float yawInput { get; private set; }
    public float rollInput { get; private set; }

    [Tooltip("Accel axis is used for brake input")]
    public bool accelAxisIsBrake;

    [Tooltip("Brake input will act as reverse input")]
    public bool brakeIsReverse;

    [Tooltip("Automatically hold ebrake if it's pressed while parked")]
    public bool holdEbrakePark;

    public float burnoutThreshold = 0.9f;
    public float burnout { get; private set; }
    public float burnoutSpin = 5;
    [Range(0, 0.9f)]
    public float burnoutSmoothness = 0.5f;
    public NewMotor engine;

    private bool stopUpshift;
    private bool stopDownShift;

    public Vector3 localVelocity { get; private set; }
    public Vector3 localAngularVel { get; private set; }

    public bool reversing { get; private set; }

    public NewWheel[] wheels;
    public int groundedWheels { get; private set; }
    public Vector3 wheelNormalAverage { get; private set; }
    private Vector3 wheelsAverageVelocity; // Average velocity of wheel contact points

    [Tooltip("Lower center of mass by suspension height")]
    public bool suspensionCenterOfMass;
    public Vector3 centerOfMassOffset;

    public ForceMode wheelForceMode = ForceMode.Acceleration;
    public ForceMode suspensionForceMode = ForceMode.Acceleration;

    [Header("New Stuff")]
    public EngineSettings _engine;
    public float rawRpm;
    public float rpm;

    public TransmissionSettings _transmission;
    public float gear;

    void Start()
    {
        rb = GetComponent<Rigidbody>();

        CreateNormalOrientation();
        SetCenterOfMass();
    }

    void Update()
    {
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

        SetVelocities();
        SetOrientNormal();

        SetBurnoutInputs();
        SetReversing();

        _engine.UpdateEngine(rpm, accelInput, false, false, (groundedWheels == 0), 0, 0);

        rawRpm = wheels[3].rawRPM;
        rpm = _engine.RPM;
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

    void SetVelocities()
    {
        if (rb == null) return;
        localVelocity = transform.InverseTransformDirection(rb.linearVelocity - wheelsAverageVelocity);
        localAngularVel = transform.InverseTransformDirection(rb.angularVelocity);
    }

    void SetOrientNormal()
    {
        if (norm == null) return;
        norm.transform.position = transform.position;
        norm.transform.rotation = Quaternion.LookRotation(groundedWheels == 0 ? transform.up : wheelNormalAverage, transform.forward);
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
        wheelsAverageVelocity = Vector3.zero;

        for (int i = 0; i < wheels.Length; i++)
        {
            if (!wheels[i].grounded) break;

            wheelsAverageVelocity = i == 0 ? wheels[i].contactVelocity : (wheelsAverageVelocity + wheels[i].contactVelocity) * 0.5f;
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
