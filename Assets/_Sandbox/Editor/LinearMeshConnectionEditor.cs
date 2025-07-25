using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(LinearMeshConnection))]
[CanEditMultipleObjects]
public class LinearMeshConnectionEditor : Editor
{
    static bool showHandles = true;

    public override void OnInspectorGUI()
    {
        showHandles = EditorGUILayout.Toggle("Show Handles", showHandles);
        SceneView.RepaintAll();

        DrawDefaultInspector();
    }

    public void OnSceneGUI()
    {
        LinearMeshConnection targetScript = (LinearMeshConnection)target;
        Undo.RecordObject(targetScript, "LinearMeshConnection Change");

        if (showHandles && targetScript.gameObject.activeInHierarchy)
        {
            if (targetScript.targetConnection && Tools.current == Tool.Move)
            {
                targetScript.targetOffset = targetScript.targetConnection.InverseTransformPoint(Handles.PositionHandle(targetScript.targetConnection.TransformPoint(targetScript.targetOffset), Tools.pivotRotation == PivotRotation.Local ? targetScript.targetConnection.rotation : Quaternion.identity));
            }
        }

        if (GUI.changed)
        {
            EditorUtility.SetDirty(targetScript);
        }
    }
}
