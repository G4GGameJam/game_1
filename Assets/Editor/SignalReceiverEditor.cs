// SignalReceiverEditor.cs
#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(SignalReceiver))]
[CanEditMultipleObjects]
public class SignalReceiverEditor : Editor
{
    SerializedProperty actionsProp;

    // Collider trigger props
    SerializedProperty triggerTagProp;
    SerializedProperty triggerPaddingProp;
    SerializedProperty useColliderTriggerProp; // hidden backing field
    SerializedProperty triggerLimitProp;
    // NOTE: no serialized property for disableTriggerWhenExhausted (hidden on purpose)

    void OnEnable()
    {
        // Actions
        actionsProp = serializedObject.FindProperty("actions");

        // Collider trigger group
        triggerTagProp = serializedObject.FindProperty("triggerTag");
        triggerPaddingProp = serializedObject.FindProperty("triggerPadding");
        useColliderTriggerProp = serializedObject.FindProperty("useColliderTrigger"); // hidden
        triggerLimitProp = serializedObject.FindProperty("triggerLimit");
    }

    public override void OnInspectorGUI()
    {
        EditorGUILayout.HelpBox(
            "Select an Action you want to happen once a Signal is Received. (You may select multiple).",
            MessageType.Info);

        serializedObject.Update();

        // ---------- Actions ----------
        if (actionsProp == null)
        {
            DrawDefaultInspector();
            serializedObject.ApplyModifiedProperties();
            return;
        }

        EditorGUILayout.PropertyField(actionsProp, new GUIContent("Actions"), false);

        if (actionsProp.isExpanded)
        {
            EditorGUI.indentLevel++;
            for (int i = 0; i < actionsProp.arraySize; i++)
            {
                var elem = actionsProp.GetArrayElementAtIndex(i);
                DrawActionEntry(elem, i);
            }

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("+ Add Action")) actionsProp.arraySize++;
            using (new EditorGUI.DisabledScope(actionsProp.arraySize == 0))
            {
                if (GUILayout.Button("Remove Last"))
                    actionsProp.arraySize = Mathf.Max(0, actionsProp.arraySize - 1);
            }
            EditorGUILayout.EndHorizontal();
            EditorGUI.indentLevel--;
        }

        EditorGUILayout.Space(8);

        // ---------- Collider Trigger ----------
        EditorGUILayout.HelpBox(
            "When enabled, actions do not perform until a Signal Emitter object (with the correct Tag and a SignalEmitter component) " +
            "enters this object's BoxCollider.",
            MessageType.Info);

        bool currentlyOn = useColliderTriggerProp != null && useColliderTriggerProp.boolValue;

        using (new EditorGUI.DisabledScope(!currentlyOn))
        {
            EditorGUILayout.PropertyField(triggerTagProp, new GUIContent("Trigger Tag"));
            EditorGUILayout.PropertyField(triggerPaddingProp, new GUIContent("Trigger Padding"));

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Trigger Limits", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(triggerLimitProp, new GUIContent("Trigger Limit (0 = Unlimited)"));

            // Optional runtime hint only
            if (EditorApplication.isPlaying && targets.Length == 1)
            {
                EditorGUILayout.Space(2);
                EditorGUILayout.HelpBox(
                    "Runtime: If the collider stops firing, the limit may have been reached (the collider may have been auto-disabled).",
                    MessageType.None);
            }
        }

        EditorGUILayout.Space(6);
        string label = currentlyOn ? "Turn Off Collider Trigger" : "Turn On Collider Trigger";
        if (GUILayout.Button(label, GUILayout.Height(26f)))
        {
            foreach (var t in targets)
            {
                var r = t as SignalReceiver;
                if (r == null) continue;
                r.EditorApplyColliderTrigger(!currentlyOn);
            }
            // refresh serialized view
            serializedObject.Update();
        }

        serializedObject.ApplyModifiedProperties();
    }

    void DrawActionEntry(SerializedProperty elem, int index)
    {
        if (elem == null) return;
        var actionProp = elem.FindPropertyRelative("action");

        EditorGUILayout.BeginVertical("box");
        EditorGUILayout.LabelField($"Action #{index + 1}", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(actionProp, new GUIContent("Action Item"));

        var action = (SignalReceiver.ActionType)actionProp.enumValueIndex;

        if (action == SignalReceiver.ActionType.None)
        {
            EditorGUILayout.HelpBox("Choose an Action Item to continue.", MessageType.Info);
            EditorGUILayout.EndVertical();
            EditorGUILayout.Space();
            return;
        }

        switch (action)
        {
            case SignalReceiver.ActionType.TriggerEmitter:
                Draw(elem, "emitterToTrigger", "Emitter To Trigger");
                break;

            case SignalReceiver.ActionType.DestroyObject:
                Draw(elem, "destroyObjects", "Objects To Destroy");
                Draw(elem, "destroyDelay", "Destroy Delay (sec)");
                break;

            case SignalReceiver.ActionType.SpawnObject:
                DrawSpawnObject(elem);
                break;

            case SignalReceiver.ActionType.PlayDialogue:
                Draw(elem, "dialogueTarget", "PlayDialogue Component");
                Draw(elem, "dialogueText", "Dialogue Text");
                Draw(elem, "dialogueFont", "Dialogue Font (optional)");
                break;

            case SignalReceiver.ActionType.PlayAnimation:
                Draw(elem, "animator", "Animator");
                Draw(elem, "animatorTrigger", "Trigger Name");
                break;

            case SignalReceiver.ActionType.PlayAudio:
                Draw(elem, "clip", "Audio Clip (plays on Player)");
                Draw(elem, "stopBeforePlay", "Stop Before Play");
                break;

            case SignalReceiver.ActionType.LoadScene:
                Draw(elem, "sceneName", "Scene Name");
                break;

            case SignalReceiver.ActionType.CallFunction:
                Draw(elem, "functionTarget", "Component With Function");
                Draw(elem, "functionName", "Function Name");
                break;
        }

        EditorGUILayout.EndVertical();
        EditorGUILayout.Space();
    }

    void DrawSpawnObject(SerializedProperty elem)
    {
        var modeProp = elem.FindPropertyRelative("spawnMode");
        var mode = (SignalReceiver.ActionEntry.SpawnActionMode)modeProp.enumValueIndex;

        string label = "Current Action Mode: " + (mode == SignalReceiver.ActionEntry.SpawnActionMode.ActivateExisting
            ? "Activate Existing"
            : "Spawn Prefab");

        if (GUILayout.Button(label, GUILayout.Height(28)))
        {
            Undo.RecordObject(target, "Toggle Spawn Action Mode");
            modeProp.enumValueIndex = (mode == SignalReceiver.ActionEntry.SpawnActionMode.ActivateExisting)
                ? (int)SignalReceiver.ActionEntry.SpawnActionMode.SpawnPrefab
                : (int)SignalReceiver.ActionEntry.SpawnActionMode.ActivateExisting;
        }

        EditorGUILayout.Space(4);

        EditorGUILayout.HelpBox(
            "- Activate Existing turns ON a hidden object already in the scene.\n" +
            "- Spawn Prefab instantiates a prefab at a spawn point.",
            MessageType.None);

        EditorGUILayout.Space(4);

        if ((SignalReceiver.ActionEntry.SpawnActionMode)modeProp.enumValueIndex
            == SignalReceiver.ActionEntry.SpawnActionMode.ActivateExisting)
        {
            EditorGUILayout.LabelField("Activate (default)", EditorStyles.boldLabel);
            Draw(elem, "objectToActivate", "Object To Activate");
            Draw(elem, "activateOnlyOnce", "Activate Only Once");
        }
        else
        {
            EditorGUILayout.LabelField("Spawn", EditorStyles.boldLabel);
            Draw(elem, "prefabToSpawn", "Prefab To Spawn");
            Draw(elem, "spawnAt", "Spawn Point");
            Draw(elem, "maxSpawns", "Max Spawns (0 = unlimited)");
        }
    }

    static void Draw(SerializedProperty root, string relative, string label = null)
    {
        var p = root.FindPropertyRelative(relative);
        if (p != null)
            EditorGUILayout.PropertyField(p, new GUIContent(label ?? p.displayName), true);
    }
}
#endif
