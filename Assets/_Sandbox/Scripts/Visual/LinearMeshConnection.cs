using UnityEngine;

[ExecuteAlways]
[DisallowMultipleComponent]
public class LinearMeshConnection : MonoBehaviour
{
    [Header("Connections")]
    [Tooltip("Position to point from")]
    public Transform originConnection;
    public Vector3 originOffset;
    public Vector3 originRotation;
    private Vector3 originPointConnection => originConnection.TransformPoint(originOffset);

    [Space]
    [Tooltip("Position to point at")]
    public Transform targetConnection;
    public Vector3 targetOffset;
    public Vector3 targetRotation;
    private Vector3 targetPointConnection => targetConnection.TransformPoint(targetOffset);

    [Header("Options")]
    [Tooltip("Rotate to point at target?")]
    public bool rotate = true;
    public bool clampRotations = false;
    public Vector3 rotationAxisFactor = Vector3.one;
    private Vector3 initialRotation;

    [Tooltip("Scale along local z-axis to reach target?")]
    public bool stretch;
    private float initialDist;
    private Vector3 initialScale;

    private void Start()
    {
        if (!targetConnection || !originConnection) return;

        SetInitailValues();
    }

    private void SetInitailValues()
    {
        if (!Application.isPlaying) return;

        initialDist = Mathf.Max(Vector3.Distance(originPointConnection, targetPointConnection), 0.01f);
        initialRotation = transform.localEulerAngles;
        initialScale = transform.localScale;
    }

    public void SetNewConnections(Transform _origin, Vector3 _originOffset, Transform _target, Vector3 _targetOffset)
    {
        originConnection = _origin;
        originOffset = _originOffset;
        targetConnection = _target;
        targetOffset = _targetOffset;
    }

    private void Update()
    {
        if (!targetConnection || !originConnection) return;

        MoveToOrigin();
        RotateToTarget();
        StretchToTarget();
    }

    private void MoveToOrigin() => transform.position = originPointConnection;

    private void RotateToTarget()
    {
        if (!rotate) return;

        transform.rotation = Quaternion.LookRotation((targetPointConnection - originPointConnection).normalized, transform.parent.forward);

        if (!clampRotations) return;
        Vector3 lockLocalVectors = transform.localEulerAngles;
        lockLocalVectors.x = Mathf.Clamp(lockLocalVectors.x, initialRotation.x - rotationAxisFactor.x, initialRotation.x + rotationAxisFactor.x);
        lockLocalVectors.y = Mathf.Clamp(lockLocalVectors.y, initialRotation.y - rotationAxisFactor.y, initialRotation.y + rotationAxisFactor.y);
        lockLocalVectors.z = Mathf.Clamp(lockLocalVectors.z, initialRotation.z - rotationAxisFactor.z, initialRotation.z + rotationAxisFactor.z);
        transform.localEulerAngles = lockLocalVectors;
    }

    private void StretchToTarget()
    {
        if (!stretch || !Application.isPlaying) return;

        Vector3 stretchScale = transform.localScale;
        stretchScale.z = initialScale.z * (Vector3.Distance(originPointConnection, targetPointConnection) / initialDist);
        transform.localScale = stretchScale;        
    }

    void OnDrawGizmosSelected()
    {
        if (!targetConnection || !originConnection) return;

        Gizmos.color = Color.green;

        Gizmos.DrawLine(originPointConnection, targetPointConnection);
        Gizmos.DrawWireSphere(originPointConnection, 0.01f);
        Gizmos.DrawWireSphere(targetPointConnection, 0.01f);
    }
}
