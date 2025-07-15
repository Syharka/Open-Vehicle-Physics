using RVP;
using UnityEngine;

public class NewMotor : MonoBehaviour
{
    #region Core Components
    protected VehicleController vp { get; private set; }
    private Drivetrain targetDrive;
    #endregion

    #region Settings
    public MotorSettings motorSettings;
    public MotorTempValues temp { get; private set; }
    public MotorBoostValues boost { get; private set; }
    public MotorPerformanceValues performance { get; private set; }
    #endregion

    public bool ignition { get; private set; } = true;
    public float power { get; private set; } = 1;

    protected float actualInput; // Input after applying the input curve

    protected AudioSource snd;

    public float targetPitch { get; private set; }
    protected float pitchFactor;
    protected float airPitch;
    public bool boosting { get; private set; }

    private bool boostReleased;
    private bool boostPrev;
    public AudioSource boostLoopSnd;
    private AudioSource boostSnd; // AudioSource for boostStart and boostEnd
    public ParticleSystem[] boostParticles;
    public float maxRPM { get; private set; }

    private float actualAccel;
    public bool shifting { get; private set; }

    private void Awake()
    {
        temp = motorSettings.temp;
        boost = motorSettings.boost;
        performance = motorSettings.performance;
    }

    public void Start()
    {
        vp = transform.GetTopmostParentComponent<VehicleController>();

        // Get engine sound
        snd = GetComponent<AudioSource>();
        if (snd)
        {
            snd.pitch = temp.minPitch;
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

        targetDrive = new Drivetrain();
        targetDrive.torqueCurve = performance.torqueCurve;
        // Get maximum possible RPM
        GetMaxRPM();
    }

    public void FixedUpdate()
    {
        // Boost logic
        boost.boostPower = Mathf.Clamp(boosting ? boost.boostPower - boost.boostBurnRate * Time.timeScale * 0.05f * TimeMaster.inverseFixedTimeFactor : boost.boostPower, 0, boost.maxBoost);
        boostPrev = boosting;

        if (boost.canBoost && ignition && boost.boostPower > 0 && (vp.accelInput > 0 || vp.localVelocity.z > 1))
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
                boostSnd.clip = boost.boostStart;
                boostSnd.Play();
            }
            else if (!boosting && boostPrev)
            {
                boostSnd.clip = boost.boostEnd;
                boostSnd.Play();
            }
        }

        // Calculate proper input
        actualAccel = Mathf.Lerp(vp.extras.brakeIsReverse && vp.reversing && vp.accelInput <= 0 ? vp.brakeInput : vp.accelInput, Mathf.Max(vp.accelInput, vp.burnout), vp.burnout);
        float accelGet = performance.canReverse ? actualAccel : Mathf.Clamp01(actualAccel);
        actualInput = temp.inputCurve.Evaluate(Mathf.Abs(accelGet)) * Mathf.Sign(accelGet);
        //targetDrive.torqueCurve = performance.torqueCurve;

        if (ignition)
        {
            float boostEval = boost.boostPowerCurve.Evaluate(Mathf.Abs(vp.localVelocity.z));
            // Set RPM
            targetDrive.rpm = Mathf.Lerp(targetDrive.rpm, actualInput * maxRPM * 1000 * (boosting ? 1 + boostEval : 1), (1 - performance.inertia) * Time.timeScale);
            // Set torque
            if (targetDrive.feedbackRPM > targetDrive.rpm)
            {
                targetDrive.torque = 0;
            }
            else
            {
                targetDrive.torque = performance.torqueCurve.Evaluate(targetDrive.feedbackRPM * 0.001f - (boosting ? boostEval : 0)) * Mathf.Lerp(targetDrive.torque, power * Mathf.Abs(System.Math.Sign(actualInput)), (1 - performance.inertia) * Time.timeScale) * (boosting ? 1 + boostEval : 1);
            }

            // Send RPM and torque through drivetrain
            float torqueFactor = Mathf.Pow(1f, performance.driveDividePower);
            float tempRPM = 0;
            tempRPM += vp.transmission.targetDrive.feedbackRPM;
            vp.transmission.targetDrive.SetDrive(targetDrive, torqueFactor);

            targetDrive.feedbackRPM = tempRPM / 1;

            if (vp.transmission)
            {
                shifting = vp.transmission.shiftTime > 0;
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
            vp.transmission.targetDrive.SetDrive(targetDrive);
        }
    }

    private void Update()
    {
        // Set audio pitch
        if (snd && ignition)
        {
            airPitch = vp.groundedWheels > 0 || actualAccel != 0 ? 1 : Mathf.Lerp(airPitch, 0, 0.5f * Time.deltaTime);
            pitchFactor = (actualAccel != 0 || vp.groundedWheels == 0 || !temp.pitchDecreaseWithoutThrottle ? 1 : 0.5f) * (shifting ?
                (temp.pitchIncreaseBetweenShift ?
                    Mathf.Sin((vp.transmission.shiftTime / vp.transmission.clutch.shiftDelay) * Mathf.PI) :
                    Mathf.Min(vp.transmission.clutch.shiftDelay, Mathf.Pow(vp.transmission.shiftTime, 2)) / vp.transmission.clutch.shiftDelay) :
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
                snd.pitch = Mathf.Lerp(snd.pitch, Mathf.Lerp(temp.minPitch, temp.maxPitch, targetPitch), 20 * Time.deltaTime) + Mathf.Sin(Time.time * 200) * 0.1f;
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

    // Calculates the max RPM and propagates its effects
    public void GetMaxRPM()
    {
        maxRPM = performance.torqueCurve.keys[performance.torqueCurve.length - 1].time;

        vp.transmission.targetDrive = new Drivetrain();
        vp.transmission.targetDrive.torqueCurve = targetDrive.torqueCurve;
        vp.transmission.ResetMaxRPM();
    }
}

