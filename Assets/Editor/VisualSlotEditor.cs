#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(VisualSlot))]
public class VisualSlotEditor : Editor
{
    public override void OnInspectorGUI()
    {
        EditorGUILayout.HelpBox(
        "Set your 3D Visual Object below, make sure to Auto Fit the Trigger afterwards.", MessageType.Info);

        // Draw only serialized fields (anchor is hidden by attribute)
        DrawDefaultInspector();
    }
}
#endif
