#if UNITY_EDITOR
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

[CustomEditor(typeof(SignalCounter))]
public class SignalCounterEditor : Editor
{
    SerializedProperty emittersProp;
    SerializedProperty targetProp;
    SerializedProperty idleColorProp;
    ReorderableList emittersList;

    void OnEnable()
    {
        emittersProp = serializedObject.FindProperty("emitters");
        targetProp = serializedObject.FindProperty("target");
        idleColorProp = serializedObject.FindProperty("idleLineColor");

        emittersList = new ReorderableList(serializedObject, emittersProp, true, true, true, true);
        emittersList.drawHeaderCallback = rect =>
        {
            EditorGUI.LabelField(rect, "Emitters to Watch (SignalEmitter)");
        };

        emittersList.elementHeight = EditorGUIUtility.singleLineHeight + 4;
        emittersList.drawElementCallback = (rect, index, active, focused) =>
        {
            rect.y += 2;
            rect.height = EditorGUIUtility.singleLineHeight;

            var element = emittersProp.GetArrayElementAtIndex(index);
            var obj = element.objectReferenceValue;

            EditorGUI.BeginChangeCheck();
            obj = EditorGUI.ObjectField(rect, $"Emitter {index + 1}", obj, typeof(SignalEmitter), true);
            if (EditorGUI.EndChangeCheck())
            {
                if (obj == null || obj is SignalEmitter)
                    element.objectReferenceValue = obj;
            }
        };
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        var counter = (SignalCounter)target;

        // --- Help Box ---
        EditorGUILayout.HelpBox(
            "This counter listens for multiple Signals. Once every listed Singal has fired at least once, " +
            "it sends a Signal to the set target.",
            MessageType.Info
        );

        // --- Emitters list ---
        EditorGUILayout.Space(4);
        emittersList.DoLayoutList();

        // --- Target field ---
        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("Target to Notify", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(targetProp);

        // --- Toggle lines & optional color ---
        EditorGUILayout.Space(10);
        if (counter.GetShowLines())
        {
            if (GUILayout.Button("Turn OFF Lines", GUILayout.Height(24)))
            {
                Undo.RecordObject(counter, "Disable Signal Lines");
                counter.SetShowLines(false);
                EditorUtility.SetDirty(counter);
            }

            // Show color field only when ON
            EditorGUILayout.Space(6);
            EditorGUILayout.PropertyField(idleColorProp, new GUIContent("Line Color"));
        }
        else
        {
            if (GUILayout.Button("Turn ON Lines", GUILayout.Height(24)))
            {
                Undo.RecordObject(counter, "Enable Signal Lines");
                counter.SetShowLines(true);
                EditorUtility.SetDirty(counter);
            }
        }

        serializedObject.ApplyModifiedProperties();
    }
}
#endif
