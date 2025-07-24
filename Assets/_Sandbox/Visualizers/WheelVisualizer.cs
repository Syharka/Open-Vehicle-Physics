using RVP;
using UnityEngine;

[ExecuteAlways]
public class WheelVisualizer : MonoBehaviour
{
    private VehicleController controller => transform.GetTopmostParentComponent<VehicleController>();
    private NewSuspension suspension => GetComponentInParent<NewSuspension>();
    private NewWheel wheel => GetComponentInChildren<NewWheel>();
    void OnDrawGizmosSelected()
    {
        if (!controller && !suspension && !wheel) return;

        if (wheel.transform.GetChild(0))
        {
            SteeringControlValues activeSteerSettings = controller.steeringSettings.control;
            WheelSizeValues activeSettings = wheel.wheelSettings.size;

            Transform wheelPoint = wheel.transform.GetChild(0);

            float camberSin = -Mathf.Sin(suspension.camberAngle * Mathf.Deg2Rad);
            float steerSin = Mathf.Sin(Mathf.Lerp(-activeSteerSettings.steerRange, activeSteerSettings.steerRange, (wheel.steerAngle + 1) * 0.5f) * Mathf.Deg2Rad);
            float minSteerSin = Mathf.Sin(-activeSteerSettings.steerRange * wheel.steerFactor * Mathf.Deg2Rad);
            float maxSteerSin = Mathf.Sin(activeSteerSettings.steerRange * wheel.steerFactor * Mathf.Deg2Rad);

            #region WheelSizeVisualizer
            float tireActualRadius = activeSettings.tireRadius;

            Gizmos.color = Color.white;
            GizmosExtra.DrawWireCylinder(wheelPoint.position, wheelPoint.forward, activeSettings.tireRadius, activeSettings.tireWidth * 2);

            Gizmos.color = new Color(1, 1, 1, 1);
            GizmosExtra.DrawWireCylinder(wheelPoint.position, wheelPoint.forward, activeSettings.tireRadius, activeSettings.tireWidth * 2);
            #endregion

            #region SteeringVisualizer
            if (wheel.steerFactor != 0)
            {
                Gizmos.color = Color.magenta;

                Gizmos.DrawWireSphere(wheelPoint.position, 0.05f);

                Gizmos.DrawLine(wheelPoint.position, wheelPoint.position + transform.TransformDirection(minSteerSin,
                    camberSin * (1 - Mathf.Abs(minSteerSin)),
                    Mathf.Cos(-activeSteerSettings.steerRange * Mathf.Deg2Rad) * (1 - Mathf.Abs(camberSin))
                    ).normalized);

                Gizmos.DrawLine(wheelPoint.position, wheelPoint.position + transform.TransformDirection(maxSteerSin,
                    camberSin * (1 - Mathf.Abs(maxSteerSin)),
                    Mathf.Cos(activeSteerSettings.steerRange * Mathf.Deg2Rad) * (1 - Mathf.Abs(camberSin))
                    ).normalized);

                Gizmos.DrawLine(wheelPoint.position + transform.TransformDirection(minSteerSin,
                    camberSin * (1 - Mathf.Abs(minSteerSin)),
                    Mathf.Cos(-activeSteerSettings.steerRange * Mathf.Deg2Rad) * (1 - Mathf.Abs(camberSin))
                    ).normalized * 0.9f,
                wheelPoint.position + transform.TransformDirection(maxSteerSin,
                    camberSin * (1 - Mathf.Abs(maxSteerSin)),
                    Mathf.Cos(activeSteerSettings.steerRange * Mathf.Deg2Rad) * (1 - Mathf.Abs(camberSin))
                    ).normalized * 0.9f);

                Gizmos.DrawLine(wheelPoint.position, wheelPoint.position + transform.TransformDirection(steerSin,
                    camberSin * (1 - Mathf.Abs(steerSin)),
                    Mathf.Cos(-activeSteerSettings.steerRange * Mathf.Deg2Rad) * (1 - Mathf.Abs(camberSin))
                    ).normalized);
            }
            #endregion
        }
    }
}
