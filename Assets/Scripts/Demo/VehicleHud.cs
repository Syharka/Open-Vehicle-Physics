using UnityEngine;
using System.Collections;
using UnityEngine.UI;

namespace RVP
{
    [DisallowMultipleComponent]
    [AddComponentMenu("RVP/Demo Scripts/Vehicle HUD", 1)]

    // Class for the HUD in the demo
    public class VehicleHud : MonoBehaviour
    {
        public GameObject targetVehicle;
        public Text speedText;
        public Text gearText;
        public Slider rpmMeter;
        public Slider boostMeter;
        public Text propertySetterText;
        public Text stuntText;
        public Text scoreText;
        VehicleController vp;
        EngineHandler engine;
        NewTransmission trans;
        StuntDetect stunter;
        public bool stuntMode;
        float stuntEndTime = -1;
        PropertyToggleSetter propertySetter;

        private void Start() {
            Initialize(targetVehicle);
        }

        public void Initialize(GameObject newVehicle) {
            if (!newVehicle) { return; }
            targetVehicle = newVehicle;
            vp = targetVehicle.GetComponent<VehicleController>();

            trans = targetVehicle.GetComponentInChildren<NewTransmission>();

            if (stuntMode) {
                stunter = targetVehicle.GetComponent<StuntDetect>();
            }

            engine = vp.engineHandler;
            propertySetter = targetVehicle.GetComponent<PropertyToggleSetter>();

            stuntText.gameObject.SetActive(stuntMode);
            scoreText.gameObject.SetActive(stuntMode);
        }

        void Update() {
            if (vp) {
                speedText.text = (vp.rb.linearVelocity.magnitude * 2.23694f).ToString("0") + " MPH";

                if (trans) {
                        gearText.text = "Gear: " + (trans.currentGear == 0 ? "R" : (trans.currentGear == 1 ? "N" : (trans.currentGear - 1).ToString()));
                }

                if (engine != null) {

                    if (engine.boost.maxBoost > 0) {
                        boostMeter.value = engine.boost.boostPower / engine.boost.maxBoost;
                    }
                }

                if (stuntMode && stunter) {
                    stuntEndTime = string.IsNullOrEmpty(stunter.stuntString) ? Mathf.Max(0, stuntEndTime - Time.deltaTime) : 2;

                    if (stuntEndTime == 0) {
                        stuntText.text = "";
                    }
                    else if (!string.IsNullOrEmpty(stunter.stuntString)) {
                        stuntText.text = stunter.stuntString;
                    }

                    scoreText.text = "Score: " + stunter.score.ToString("n0");
                }

                if (propertySetter) {
                    propertySetterText.text = propertySetter.currentPreset == 0 ? "Normal Steering" : (propertySetter.currentPreset == 1 ? "Skid Steering" : "Crab Steering");
                }
            }
        }
    }
}