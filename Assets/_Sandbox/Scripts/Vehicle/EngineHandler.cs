using RVP;
using UnityEngine;

public class EngineHandler
{
    private Drivetrain targetDrive;

    #region Settings
    public EngineTempValues temp { get; private set; }
    public EngineBoostValues boost { get; private set; }
    public EnginePerformanceValues performance { get; private set; }
    #endregion

    public bool ignition { get; private set; } = true;
    public float power { get; private set; } = 1;

    protected float actualInput; // Input after applying the input curve
    public bool boosting { get; private set; }

    private bool boostReleased;
    public float maxRPM { get; private set; }

    private float actualAccel;
    public bool shifting { get; private set; }

    public void Init(EngineSettings _motorSettings)
    {
        temp = _motorSettings.temp;
        boost = _motorSettings.boost;
        performance = _motorSettings.performance;

        targetDrive = new Drivetrain();
        targetDrive.torqueCurve = performance.torqueCurve;
    }

    public void UpdateMotor(VehicleController _vc)
    {
        // Boost logic
        boost.boostPower = Mathf.Clamp(boosting ? boost.boostPower - boost.boostBurnRate * Time.timeScale * 0.05f * TimeMaster.inverseFixedTimeFactor : boost.boostPower, 0, boost.maxBoost);

        if (boost.canBoost && ignition && boost.boostPower > 0 && (_vc.accelInput > 0 || _vc.localVelocity.z > 1))
        {
            if (((boostReleased && !boosting) || boosting) && _vc.boostButton)
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

        if (!_vc.boostButton)
        {
            boostReleased = true;
        }

        // Calculate proper input
        actualAccel = Mathf.Lerp(_vc.extras.brakeIsReverse && _vc.reversing && _vc.accelInput <= 0 ? _vc.brakeInput : _vc.accelInput, Mathf.Max(_vc.accelInput, _vc.burnout), _vc.burnout);
        float accelGet = performance.canReverse ? actualAccel : Mathf.Clamp01(actualAccel);
        actualInput = temp.inputCurve.Evaluate(Mathf.Abs(accelGet)) * Mathf.Sign(accelGet);
        //targetDrive.torqueCurve = performance.torqueCurve;

        if (ignition)
        {
            float boostEval = boost.boostPowerCurve.Evaluate(Mathf.Abs(_vc.localVelocity.z));
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
            tempRPM += _vc.transmissionHandler.targetDrive.feedbackRPM;
            _vc.transmissionHandler.targetDrive.SetDrive(targetDrive, torqueFactor);

            targetDrive.feedbackRPM = tempRPM / 1;

            if (_vc.transmissionHandler != null)
            {
                shifting = _vc.transmissionHandler.shiftTime > 0;
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
            _vc.transmissionHandler.targetDrive.SetDrive(targetDrive);
        }
    }

    // Calculates the max RPM and propagates its effects
    public void GetMaxRPM(VehicleController _vc)
    {
        maxRPM = performance.torqueCurve.keys[performance.torqueCurve.length - 1].time;

        _vc.transmissionHandler.targetDrive = new Drivetrain();
        _vc.transmissionHandler.targetDrive.torqueCurve = targetDrive.torqueCurve;
        _vc.transmissionHandler.ResetMaxRPM();
    }
}

