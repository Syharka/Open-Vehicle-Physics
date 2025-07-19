using RVP;
using UnityEngine;
using System.Collections;
using System.Linq;
using UnityEditor;
using NUnit.Framework;
using System.Collections.Generic;

/* TO DO */
/* 
 * Refactor to connect vehicle functions as manager
 * Design custom inspectors to obfuscate component info
 * Set scriptable data from here per object
 * Refactor functions with Delegates to subscribe child components as actions
 * Split visual and audio into seperate handlers
 * Test input system as replacement to current inputs
*/

/* GOAL */
/* 
 * This script should merely setup the child components and manage their actions/values as a public source.
 * This script should not actually 'do' any work, only call and receive from functions on components that do.
*/


public class VehicleController : MonoBehaviour
{
    #region Core Components
    public Rigidbody rb { get; private set; }
    public Transform norm { get; private set; }
    #endregion

    #region Required Setup
    public VehicleSettings vehicleSettings;
    public VehicleExtraValues extras => vehicleSettings.extras;
    public EngineHandler engineHandler { get; private set; } = new EngineHandler();
    public MotorSettings engineSettings;
    public NewTransmission transmission;
    public AssistHandler assistsHandler { get; private set; } = new AssistHandler();
    public AssistSettings assistSettings;
    public SteeringHandler steeringHandler { get; private set; } = new SteeringHandler();
    public SteeringSettings steeringSettings;
    public List<NewSuspension> suspensions { get; private set; } = new List<NewSuspension>();
    #endregion

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
    public float burnout { get; private set; }
    public Vector3 localVelocity { get; private set; }
    public Vector3 localAngularVel { get; private set; }
    public bool reversing { get; private set; }
    public int groundedWheels { get; private set; }
    public Vector3 wheelNormalAverage { get; private set; }

    private Vector3 wheelsAverageVelocity;
    private bool stopUpshift;
    private bool stopDownShift;

    void Start()
    {
        rb = GetComponent<Rigidbody>();

        engineHandler.Init(engineSettings);
        engineHandler.GetMaxRPM(this);
        assistsHandler.Init(assistSettings);
        steeringHandler.Init(steeringSettings);

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

        engineHandler.UpdateMotor(this);
        assistsHandler.UpdateAssists(this);
        steeringHandler.UpdateSteering(this);
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
        brakeInput = extras.accelAxisIsBrake ? -Mathf.Clamp(accelInput, -1, 0) : Mathf.Clamp(f, -1, 1);
    }

    public void SetSteer(float f)
    {
        steerInput = Mathf.Clamp(f, -1, 1);
    }

    public void SetEbrake(float f)
    {
        if ((f > 0 || ebrakeInput > 0) && extras.holdEbrakePark && rb.linearVelocity.magnitude < 1 && accelInput == 0 && (brakeInput == 0 || !extras.brakeIsReverse))
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
        if (extras.suspensionCenterOfMass)
        {
            for (int i = 0; i < suspensions.Count; i++)
            {
                //float newSusDist = wheels[i].transform.parent.GetComponent<NewSuspension>().spring.suspensionDistance;
                float suspensionDistance = suspensions[i].spring.suspensionDistance;
                susAverage = i == 0 ? suspensionDistance : (susAverage + suspensionDistance) * 0.5f;
            }
        }

        rb.centerOfMass = extras.centerOfMassOffset + new Vector3(0, -susAverage, 0);
        rb.inertiaTensor = rb.inertiaTensor; // This is required due to decoupling of inertia tensor from center of mass in Unity 5.3
    }

    void SetBurnoutInputs()
    {
        if (groundedWheels > 0 && !extras.accelAxisIsBrake && extras.burnoutThreshold >= 0 && accelInput > extras.burnoutThreshold && brakeInput > extras.burnoutThreshold)
        {
            burnout = Mathf.Lerp(burnout, ((5 - Mathf.Min(5, Mathf.Abs(localVelocity.z))) / 5) * Mathf.Abs(accelInput), Time.fixedDeltaTime * (1 - extras.burnoutSmoothness) * 10);
        }
        else if (burnout > 0.01f)
        {
            burnout = Mathf.Lerp(burnout, 0, Time.fixedDeltaTime * (1 - extras.burnoutSmoothness) * 10);
        }
        else
        {
            burnout = 0;
        }
    }

    void SetReversing()
    {
        if (extras.brakeIsReverse && brakeInput > 0 && localVelocity.z < 1 && burnout == 0)
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

        for (int i = 0; i < suspensions.Count; i++)
        {
            NewWheel wheel = suspensions[i].wheel;
            if (!wheel.grounded) break;

            wheelsAverageVelocity = i == 0 ? wheel.contactVelocity : (wheelsAverageVelocity + wheel.contactVelocity) * 0.5f;
            wheelNormalAverage = i == 0 ? wheel.contactPoint.normal : (wheelNormalAverage + wheel.contactPoint.normal).normalized;
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

    public void RegisterSuspension(NewSuspension _suspension)
    {
        if (suspensions.Contains(_suspension))
            { return; }

        suspensions.Add(_suspension);
    }

    public void RemoveSuspension(NewSuspension _suspension)
    {
        if (!suspensions.Contains(_suspension))
        { return; }

        suspensions.Remove(_suspension);
    }
}
