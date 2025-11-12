#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(SignalEmitter))]
public class SignalEmitterEditor : Editor
{
    SerializedProperty targetsProp;
    SerializedProperty idleColorProp;

    void OnEnable()
    {
        targetsProp = serializedObject.FindProperty("targets");
        idleColorProp = serializedObject.FindProperty("idleLineColor");
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        var emitter = (SignalEmitter)target;

        EditorGUILayout.HelpBox(
            "This Emitter is used to send signals to other targets.", MessageType.Info
        );

        // --- Targets list ---
        EditorGUILayout.PropertyField(targetsProp, new GUIContent("Targets"), true);

        EditorGUILayout.Space(10);

        // --- Line toggle button ---
        if (emitter.GetShowLines())
        {
            if (GUILayout.Button("Turn OFF Lines", GUILayout.Height(24)))
            {
                Undo.RecordObject(emitter, "Disable Signal Lines");
                emitter.SetShowLines(false);
                EditorUtility.SetDirty(emitter);
            }

            // Only show color picker when lines are ON
            EditorGUILayout.Space(6);
            EditorGUILayout.PropertyField(idleColorProp, new GUIContent("Line Color"));
        }
        else
        {
            if (GUILayout.Button("Turn ON Lines", GUILayout.Height(24)))
            {
                Undo.RecordObject(emitter, "Enable Signal Lines");
                emitter.SetShowLines(true);
                EditorUtility.SetDirty(emitter);
            }
        }

        serializedObject.ApplyModifiedProperties();
    }
}
#endif
