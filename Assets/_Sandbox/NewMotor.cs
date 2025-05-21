using RVP;
using UnityEngine;

public class NewMotor : MonoBehaviour
{
    protected VehicleController vp;
    public bool ignition;
    public float power = 1;

    [Tooltip("Throttle curve, x-axis = input, y-axis = output")]
    public AnimationCurve inputCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);
    protected float actualInput; // Input after applying the input curve

    protected AudioSource snd;

    [Header("Engine Audio")]

    public float minPitch;
    public float maxPitch;
    [System.NonSerialized]
    public float targetPitch;
    protected float pitchFactor;
    protected float airPitch;

    [Header("Nitrous Boost")]

    public bool canBoost = true;
    [System.NonSerialized]
    public bool boosting;
    public float boost = 1;
    bool boostReleased;
    bool boostPrev;

    [Tooltip("X-axis = local z-velocity, y-axis = power")]
    public AnimationCurve boostPowerCurve = AnimationCurve.EaseInOut(0, 0.1f, 50, 0.2f);
    public float maxBoost = 1;
    public float boostBurnRate = 0.01f;
    public AudioSource boostLoopSnd;
    AudioSource boostSnd; // AudioSource for boostStart and boostEnd
    public AudioClip boostStart;
    public AudioClip boostEnd;
    public ParticleSystem[] boostParticles;

    public void Start()
    {
        vp = transform.GetTopmostParentComponent<VehicleController>();

        // Get engine sound
        snd = GetComponent<AudioSource>();
        if (snd)
        {
            snd.pitch = minPitch;
        }

        // Get boost sound
        if (boostLoopSnd)
        {
            GameObject newBoost = Instantiate(boostLoopSnd.gameObject, boostLoopSnd.transform.position, boostLoopSnd.transform.rotation) as GameObject;
            boostSnd = newBoost.GetComponent<AudioSource>();
            boostSnd.transform.parent = boostLoopSnd.transform;
            boostSnd.transform.localPosition = Vector3.zero;
            boostSnd.transform.localRotation = Quaternion.identity;
            boostSnd.loop = false;
        }

        targetDrive = GetComponent<DriveForce>();
        // Get maximum possible RPM
        GetMaxRPM();
    }

    public void FixedUpdate()
    {
        // Boost logic
        boost = Mathf.Clamp(boosting ? boost - boostBurnRate * Time.timeScale * 0.05f * TimeMaster.inverseFixedTimeFactor : boost, 0, maxBoost);
        boostPrev = boosting;

        if (canBoost && ignition && boost > 0 && (vp.accelInput > 0 || vp.localVelocity.z > 1))
        {
            if (((boostReleased && !boosting) || boosting) && vp.boostButton)
            {
                boosting = true;
                boostReleased = false;
            }
            else
            {
                boosting = false;
            }
        }
        else
        {
            boosting = false;
        }

        if (!vp.boostButton)
        {
            boostReleased = true;
        }

        if (boostLoopSnd && boostSnd)
        {
            if (boosting && !boostLoopSnd.isPlaying)
            {
                boostLoopSnd.Play();
            }
            else if (!boosting && boostLoopSnd.isPlaying)
            {
                boostLoopSnd.Stop();
            }

            if (boosting && !boostPrev)
            {
                boostSnd.clip = boostStart;
                boostSnd.Play();
            }
            else if (!boosting && boostPrev)
            {
                boostSnd.clip = boostEnd;
                boostSnd.Play();
            }
        }

        // Calculate proper input
        actualAccel = Mathf.Lerp(vp.brakeIsReverse && vp.reversing && vp.accelInput <= 0 ? vp.brakeInput : vp.accelInput, Mathf.Max(vp.accelInput, vp.burnout), vp.burnout);
        float accelGet = canReverse ? actualAccel : Mathf.Clamp01(actualAccel);
        actualInput = inputCurve.Evaluate(Mathf.Abs(accelGet)) * Mathf.Sign(accelGet);
        targetDrive.curve = torqueCurve;

        if (ignition)
        {
            float boostEval = boostPowerCurve.Evaluate(Mathf.Abs(vp.localVelocity.z));
            // Set RPM
            targetDrive.rpm = Mathf.Lerp(targetDrive.rpm, actualInput * maxRPM * 1000 * (boosting ? 1 + boostEval : 1), (1 - inertia) * Time.timeScale);
            // Set torque
            if (targetDrive.feedbackRPM > targetDrive.rpm)
            {
                targetDrive.torque = 0;
            }
            else
            {
                targetDrive.torque = torqueCurve.Evaluate(targetDrive.feedbackRPM * 0.001f - (boosting ? boostEval : 0)) * Mathf.Lerp(targetDrive.torque, power * Mathf.Abs(System.Math.Sign(actualInput)), (1 - inertia) * Time.timeScale) * (boosting ? 1 + boostEval : 1);
            }

            // Send RPM and torque through drivetrain
            if (outputDrives.Length > 0)
            {
                float torqueFactor = Mathf.Pow(1f / outputDrives.Length, driveDividePower);
                float tempRPM = 0;

                foreach (DriveForce curOutput in outputDrives)
                {
                    tempRPM += curOutput.feedbackRPM;
                    curOutput.SetDrive(targetDrive, torqueFactor);
                }

                targetDrive.feedbackRPM = tempRPM / outputDrives.Length;
            }

            if (transmission)
            {
                shifting = transmission.shiftTime > 0;
            }
            else
            {
                shifting = false;
            }
        }
        else
        {
            // If turned off, set RPM and torque to 0 and distribute it through drivetrain
            targetDrive.rpm = 0;
            targetDrive.torque = 0;
            targetDrive.feedbackRPM = 0;
            shifting = false;

            if (outputDrives.Length > 0)
            {
                foreach (DriveForce curOutput in outputDrives)
                {
                    curOutput.SetDrive(targetDrive);
                }
            }
        }
    }

    private void Update()
    {
        // Set audio pitch
        if (snd && ignition)
        {
            airPitch = vp.groundedWheels > 0 || actualAccel != 0 ? 1 : Mathf.Lerp(airPitch, 0, 0.5f * Time.deltaTime);
            pitchFactor = (actualAccel != 0 || vp.groundedWheels == 0 || !pitchDecreaseWithoutThrottle ? 1 : 0.5f) * (shifting ?
                (pitchIncreaseBetweenShift ?
                    Mathf.Sin((transmission.shiftTime / transmission.shiftDelay) * Mathf.PI) :
                    Mathf.Min(transmission.shiftDelay, Mathf.Pow(transmission.shiftTime, 2)) / transmission.shiftDelay) :
                1) * airPitch;
            targetPitch = Mathf.Abs((targetDrive.feedbackRPM * 0.001f) / maxRPM) * pitchFactor;
        }

        // Set engine sound properties
        if (!ignition)
        {
            targetPitch = 0;
        }

        if (snd)
        {
            if (ignition)
            {
                snd.enabled = true;
                snd.pitch = Mathf.Lerp(snd.pitch, Mathf.Lerp(minPitch, maxPitch, targetPitch), 20 * Time.deltaTime) + Mathf.Sin(Time.time * 200) * 0.1f;
                snd.volume = Mathf.Lerp(snd.volume, 0.3f + targetPitch * 0.7f, 20 * Time.deltaTime);
            }
            else
            {
                snd.enabled = false;
            }
        }

        // Play boost particles
        if (boostParticles.Length > 0)
        {
            foreach (ParticleSystem curBoost in boostParticles)
            {
                if (boosting && curBoost.isStopped)
                {
                    curBoost.Play();
                }
                else if (!boosting && curBoost.isPlaying)
                {
                    curBoost.Stop();
                }
            }
        }
    }

    [Header("Performance")]

    [Tooltip("X-axis = RPM in thousands, y-axis = torque.  The rightmost key represents the maximum RPM")]
    public AnimationCurve torqueCurve = AnimationCurve.EaseInOut(0, 0, 8, 1);

    [Range(0, 0.99f)]
    [Tooltip("How quickly the engine adjusts its RPMs")]
    public float inertia;

    [Tooltip("Can the engine turn backwards?")]
    public bool canReverse;
    DriveForce targetDrive;
    [System.NonSerialized]
    public float maxRPM;

    public DriveForce[] outputDrives;

    [Tooltip("Exponent for torque output on each wheel")]
    public float driveDividePower = 3;
    float actualAccel;

    [Header("Transmission")]

    public NewTransmission transmission;
    [System.NonSerialized]
    public bool shifting;

    [Tooltip("Increase sound pitch between shifts")]
    public bool pitchIncreaseBetweenShift;
    [Tooltip("Decrease sound pitch when the throttle is released")]
    public bool pitchDecreaseWithoutThrottle = true;

    // Calculates the max RPM and propagates its effects
    public void GetMaxRPM()
    {
        maxRPM = torqueCurve.keys[torqueCurve.length - 1].time;

        if (outputDrives.Length > 0)
        {
            foreach (DriveForce curOutput in outputDrives)
            {
                curOutput.curve = targetDrive.curve;

                if (curOutput.GetComponent<RVP.Transmission>())
                {
                    curOutput.GetComponent<RVP.Transmission>().ResetMaxRPM();
                }
            }
        }
    }
}

