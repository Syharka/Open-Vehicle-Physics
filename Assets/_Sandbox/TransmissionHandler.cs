using RVP;
using System;
using UnityEngine;

/* TO DO */
/* 
 * Feedback RPM on suspension can be applied to Wheels
 * Transmission has 2 unique drivetrains to be refactored, both using RPM, Torque & Curve. ID1 matching engine, ID2 is unique
*/

public class TransmissionHandler
{
    public Drivetrain targetDrive;
    public Drivetrain newDrive { get; private set; }

    #region Settings
    public TransmissionSettings transmissionSettings;
    public TransmissionExtraValues extra { get; private set; }
    public TransmissionGearValues gear { get; private set; }
    public TransmissionClutchValues clutch { get; private set; }
    #endregion

    public bool automatic { get; private set; } = true;

    public float maxRPM { get; private set; } = -1;
    public int currentGear { get; private set; }
    private int firstGear;
    public float curGearRatio { get; private set; }
    public float shiftTime { get; private set; }

    private Gear upperGear; // Next gear above current
    private Gear lowerGear; // Next gear below current
    private float upshiftDifference; // RPM difference between current gear and upper gear
    private float downshiftDifference; // RPM difference between current gear and lower gear

    public void Init(TransmissionSettings _transmissionSettings)
    {
        extra = _transmissionSettings.extra;
        gear = _transmissionSettings.gear;
        clutch = _transmissionSettings.clutch;

        newDrive = new Drivetrain();

        currentGear = Mathf.Clamp(gear.startGear, 0, gear.gears.Length - 1);

        GetFirstGear();
    }

    public void SetOutputDrives(VehicleController _vc, float _ratio)
    {
        int enabledDrives = 0;

        // Check for which outputs are enabled
        foreach (NewSuspension curOutput in _vc.suspensions)
        {
            if (curOutput.targetDrive.active)
            {
                enabledDrives++;
            }
        }

        float torqueFactor = Mathf.Pow(1f / enabledDrives, extra.driveDividePower);
        float tempRPM = 0;

        foreach (NewSuspension curOutput in _vc.suspensions)
        {
            if (curOutput.targetDrive.active)
            {
                tempRPM += extra.skidSteerDrive ? Mathf.Abs(curOutput.targetDrive.feedbackRPM) : curOutput.targetDrive.feedbackRPM;
                curOutput.targetDrive.SetDrive(newDrive, torqueFactor);
            }
        }

        targetDrive.feedbackRPM = (tempRPM / enabledDrives) * _ratio;

    }

    public void ResetMaxRPM()
    {
        maxRPM = -1; // Setting this to -1 triggers derived classes to recalculate things
    }

    public void UpdateTransmission(VehicleController _vc)
    {
        shiftTime = Mathf.Max(0, shiftTime - Time.timeScale * TimeMaster.inverseFixedTimeFactor);
        curGearRatio = gear.gears[currentGear].ratio;

        // Calculate upperGear and lowerGear
        float actualFeedbackRPM = targetDrive.feedbackRPM / Mathf.Abs(curGearRatio);
        int upGearOffset = 1;
        int downGearOffset = 1;

        while ((gear.skipNeutral || automatic) && gear.gears[Mathf.Clamp(currentGear + upGearOffset, 0, gear.gears.Length - 1)].ratio == 0 && currentGear + upGearOffset != 0 && currentGear + upGearOffset < gear.gears.Length - 1)
        {
            upGearOffset++;
        }

        while ((gear.skipNeutral || automatic) && gear.gears[Mathf.Clamp(currentGear - downGearOffset, 0, gear.gears.Length - 1)].ratio == 0 && currentGear - downGearOffset != 0 && currentGear - downGearOffset > 0)
        {
            downGearOffset++;
        }

        upperGear = gear.gears[Mathf.Min(gear.gears.Length - 1, currentGear + upGearOffset)];
        lowerGear = gear.gears[Mathf.Max(0, currentGear - downGearOffset)];

        // Perform RPM calculations
        if (maxRPM == -1)
        {
            maxRPM = targetDrive.torqueCurve.keys[targetDrive.torqueCurve.length - 1].time;

            if (extra.autoCalculateRpmRanges)
            {
                CalculateRpmRanges(_vc.engineHandler);
            }
        }

        // Set RPMs and torque of output
        newDrive.torqueCurve = targetDrive.torqueCurve;

        if (curGearRatio == 0 || shiftTime > 0)
        {
            newDrive.rpm = 0;
            newDrive.torque = 0;
        }
        else
        {
            newDrive.rpm = (automatic && extra.skidSteerDrive ? Mathf.Abs(targetDrive.rpm) * Mathf.Sign(_vc.accelInput - (_vc.extras.brakeIsReverse ? _vc.brakeInput * (1 - _vc.burnout) : 0)) : targetDrive.rpm) / curGearRatio;
            newDrive.torque = Mathf.Abs(curGearRatio) * targetDrive.torque;
        }

        // Perform automatic shifting
        upshiftDifference = gear.gears[currentGear].maxRPM - upperGear.minRPM;
        downshiftDifference = lowerGear.maxRPM - gear.gears[currentGear].minRPM;

        if (automatic && shiftTime == 0 && _vc.groundedWheels > 0)
        {
            if (!extra.skidSteerDrive && _vc.burnout == 0)
            {
                if (Mathf.Abs(_vc.localVelocity.z) > 1 || _vc.accelInput > 0 || (_vc.brakeInput > 0 && _vc.extras.brakeIsReverse))
                {
                    if (currentGear < gear.gears.Length - 1
                        && (upperGear.minRPM + upshiftDifference * (curGearRatio < 0 ? Mathf.Min(1, clutch.shiftThreshold) : clutch.shiftThreshold) - actualFeedbackRPM <= 0 || (curGearRatio <= 0 && upperGear.ratio > 0 && (!_vc.reversing || (_vc.accelInput > 0 && _vc.localVelocity.z > curGearRatio * 10))))
                        && !(_vc.brakeInput > 0 && _vc.extras.brakeIsReverse && upperGear.ratio >= 0)
                        && !(_vc.localVelocity.z < 0 && _vc.accelInput == 0))
                    {
                        Shift(1);
                    }
                    else if (currentGear > 0
                        && (actualFeedbackRPM - (lowerGear.maxRPM - downshiftDifference * clutch.shiftThreshold) <= 0 || (curGearRatio >= 0 && lowerGear.ratio < 0 && (_vc.reversing || ((_vc.accelInput < 0 || (_vc.brakeInput > 0 && _vc.extras.brakeIsReverse)) && _vc.localVelocity.z < curGearRatio * 10))))
                        && !(_vc.accelInput > 0 && lowerGear.ratio <= 0)
                        && (lowerGear.ratio > 0 || _vc.localVelocity.z < 1))
                    {
                        Shift(-1);
                    }
                }
            }
            else if (currentGear != firstGear)
            {
                // Shift into first gear if skid steering or burning out
                ShiftToGear(firstGear);
            }
        }

        SetOutputDrives(_vc, curGearRatio);
    }

    // Shift gears by the number entered
    public void Shift(int dir)
    {
        shiftTime = clutch.shiftDelay;
        currentGear += dir;

        while ((gear.skipNeutral || automatic) && gear.gears[Mathf.Clamp(currentGear, 0, gear.gears.Length - 1)].ratio == 0 && currentGear != 0 && currentGear != gear.gears.Length - 1)
        {
            currentGear += dir;
        }

        currentGear = Mathf.Clamp(currentGear, 0, gear.gears.Length - 1);

    }

    // Shift straight to the gear specified
    public void ShiftToGear(int _gear)
    {
        shiftTime = clutch.shiftDelay;
        currentGear = Mathf.Clamp(_gear, 0, gear.gears.Length - 1);
    }

    // Caculate ideal RPM ranges for each gear (works most of the time)
    public void CalculateRpmRanges(EngineHandler _engineHandler)
    {
        bool cantCalc = false;

        if (_engineHandler != null)
        {
            maxRPM = _engineHandler.performance.torqueCurve.keys[_engineHandler.performance.torqueCurve.length - 1].time;
        }
        else
        {            
            cantCalc = true;
        }

        if (!cantCalc)
        {
            float prevGearRatio;
            float nextGearRatio;
            float actualMaxRPM = maxRPM * 1000;

            for (int i = 0; i < gear.gears.Length; i++)
            {
                prevGearRatio = gear.gears[Mathf.Max(i - 1, 0)].ratio;
                nextGearRatio = gear.gears[Mathf.Min(i + 1, gear.gears.Length - 1)].ratio;

                if (gear.gears[i].ratio < 0)
                {
                    gear.gears[i].minRPM = actualMaxRPM / gear.gears[i].ratio;

                    if (nextGearRatio == 0)
                    {
                        gear.gears[i].maxRPM = 0;
                    }
                    else
                    {
                        gear.gears[i].maxRPM = actualMaxRPM / nextGearRatio + (actualMaxRPM / nextGearRatio - gear.gears[i].minRPM) * 0.5f;
                    }
                }
                else if (gear.gears[i].ratio > 0)
                {
                    gear.gears[i].maxRPM = actualMaxRPM / gear.gears[i].ratio;

                    if (prevGearRatio == 0)
                    {
                        gear.gears[i].minRPM = 0;
                    }
                    else
                    {
                        gear.gears[i].minRPM = actualMaxRPM / prevGearRatio - (gear.gears[i].maxRPM - actualMaxRPM / prevGearRatio) * 0.5f;
                    }
                }
                else
                {
                    gear.gears[i].minRPM = 0;
                    gear.gears[i].maxRPM = 0;
                }

                gear.gears[i].minRPM *= 0.55f;
                gear.gears[i].maxRPM *= 0.55f;
            }
        }
    }

    // Returns the first gear (first gear above neutral)
    public void GetFirstGear()
    {
        for (int i = 0; i < gear.gears.Length; i++)
        {
            if (gear.gears[i].ratio == 0)
            {
                firstGear = i + 1;
                break;
            }
        }
    }
}

[Serializable]
public class Drivetrain
{
    public float rpm;
    public float torque;
    public AnimationCurve torqueCurve;
    public float feedbackRPM; // RPM sent back through the drivetrain
    public bool active = true;

    public void SetDrive(Drivetrain from)
    {
        rpm = from.rpm;
        torque = from.torque;
        torqueCurve = from.torqueCurve;
    }

    // Same as previous, but with torqueFactor multiplier for torque
    public void SetDrive(Drivetrain from, float torqueFactor)
    {
        rpm = from.rpm;
        torque = from.torque * torqueFactor;
        torqueCurve = from.torqueCurve;
    }
}