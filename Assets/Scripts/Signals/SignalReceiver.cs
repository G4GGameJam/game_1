/* This script receives signals and runs configured actions (trigger emitters, destroy, spawn, dialogue, animation, audio, load scene, call function).
 * It can optionally add a BoxCollider trigger that fires when tagged SignalEmitter objects enter, with dedupe + fire-limit and editor-safe maintenance.
 */

// SignalReceiver.cs
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using TMPro;

#if UNITY_EDITOR
using UnityEditor;
#endif

[DisallowMultipleComponent] //Prevents adding multiple SignalReceiver components to the same GameObject.
public class SignalReceiver : SignalTarget //Defines a signal target that can execute a list of actions when signaled.
{
    [Header("Trigger via Collider (editor-controlled)")] //Groups collider trigger settings in the Inspector.
    [SerializeField, HideInInspector] private bool useColliderTrigger = false; //When true, auto-manages a trigger BoxCollider for entry-based firing.

    [Tooltip("Only objects with this tag will trigger the receiver.")] //Explains which tag is required to trigger.
    public string triggerTag = "EmitterObject"; //Tag required on entering objects to count as valid triggers.

    [Tooltip("Size offset to apply around the renderer bounds when creating the trigger collider.")] //Explains padding for the auto BoxCollider.
    public Vector3 triggerPadding = new Vector3(0.02f, 0.02f, 0.02f); //Extra size added to the computed bounds for the trigger collider.

    public int triggerLimit = 1; //How many times the collider trigger may fire (0 = unlimited).
    [SerializeField, HideInInspector] private bool disableTriggerWhenExhausted = true; //If true, disables the collider when the limit is reached.

    // Only this collider is created/removed by this script.
    [SerializeField, HideInInspector] private BoxCollider _autoTriggerCollider; //Reference to the auto-created BoxCollider trigger.

#if UNITY_EDITOR
    // Prevent multiple delayCall enqueues while Unity spam-calls OnValidate.
    [NonSerialized] private bool _pendingEditorSync = false; //Stops stacking multiple deferred editor updates.
#endif

    // Track unique "roots" currently inside so each entry fires once.
    // We use the entering collider's attachedRigidbody root (if any), otherwise the collider's transform root.
    private readonly HashSet<int> _insideRoots = new HashSet<int>(); //Tracks instance IDs of roots inside the trigger to dedupe multi-collider objects.

    // Count how many times we've actually fired due to collider entries.
    private int _colliderFiresSoFar = 0; //How many entry-based fires have occurred (for enforcing triggerLimit).

    [Serializable] //Allows nested class to be serialized and shown in the Inspector.
    public class ActionEntry //Defines one action the receiver can perform when signaled.
    {
        public ActionType action = ActionType.None; //Which action type to run for this entry.

        // ---- Trigger Emitter ----
        public SignalEmitter emitterToTrigger; //Emitter to trigger directly.

        // ---- Destroy Object(s) ----
        public GameObject[] destroyObjects; //Objects to destroy.
        public float destroyDelay = 0f; //Optional delay before destroying.

        // ---- Spawn Object (mirrors SpawnObject.cs) ----
        public enum SpawnActionMode { ActivateExisting, SpawnPrefab } //Two spawn behaviors: activate or instantiate.
        public SpawnActionMode spawnMode = SpawnActionMode.ActivateExisting; //Selected spawn behavior.

        // Activate Existing
        public GameObject objectToActivate; //Existing object to SetActive(true).
        public bool activateOnlyOnce = true; //If true, activation can only happen once.

        // Spawn Prefab
        public GameObject prefabToSpawn; //Prefab to instantiate.
        public Transform spawnAt; //Spawn point transform.
        public int maxSpawns = 1; //Maximum spawns allowed (0 = unlimited).

        // runtime counters (not serialized)
        [NonSerialized] public int _spawnedSoFar; //Runtime counter for number of spawns.
        [NonSerialized] public bool _activatedOnce; //Runtime flag if activation already occurred.

        // ---- Play Dialogue ----
        public PlayDialogue dialogueTarget; //PlayDialogue component to display text with.
        [TextArea(2, 6)] public string dialogueText; //Dialogue text to show.
        public TMP_FontAsset dialogueFont; //Optional TMP font for the dialogue.

        // ---- Play Animation ----
        public Animator animator; //Animator to trigger.
        public string animatorTrigger = "Trigger"; //Animator trigger parameter name.

        // ---- Play Audio ----
        public AudioClip clip; //Audio clip to play (on Player AudioSource).
        public bool stopBeforePlay = false; //If true, stop the source before playing.

        // ---- Load Scene ----
        public string sceneName; //Scene to load.

        // ---- Call Function ----
        public Component functionTarget; //Component on which to call a function.
        public string functionName = "OnSignal"; //Function name to SendMessage.
    }

    public enum ActionType //Enumerates all available action types.
    {
        None, //No action.
        TriggerEmitter, //Call OnInteracted on another emitter.
        DestroyObject, //Destroy specified GameObjects.
        SpawnObject, //Activate existing or spawn a prefab.
        PlayDialogue, //Show a dialogue panel.
        PlayAnimation, //Trigger an Animator parameter.
        PlayAudio, //Play an audio clip on the Player AudioSource.
        LoadScene, //Load a scene by name.
        CallFunction //SendMessage a function on a component.
    }

    [Tooltip("Run these actions (top to bottom) when this receiver gets a signal.")] //Explains action execution order.
    public ActionEntry[] actions; //List of actions to execute upon receiving a signal.

    static AudioSource _cachedPlayerAudio; //Shared cached Player AudioSource for audio actions.

    void Reset() //Called when the component is added or reset to defaults.
    {
        if (string.IsNullOrEmpty(triggerTag)) triggerTag = "EmitterObject"; //Default tag if unset.
        triggerLimit = 1; // sensible default: fire once //Set default limit to one entry fire.
    }

    void OnEnable() //Called when the component or GameObject becomes enabled.
    {
        if (Application.isPlaying) //Only manage runtime colliders while in play mode.
            MaintainAutoTriggerCollider(runtimeCreateDestroy: true); //Create/refresh or remove auto trigger at runtime as needed.
    }

    void OnDestroy() //Called when the component or GameObject is being destroyed.
    {
        RemoveAutoTriggerCollider(runtimeImmediate: Application.isPlaying); //Clean up auto trigger collider appropriately.
    }

    // ---------- CRITICAL: never create/destroy components synchronously here ----------
    void OnValidate() //Editor callback when something changes in the Inspector.
    {
#if UNITY_EDITOR
        if (Application.isPlaying) return; //Skip in play mode; runtime path handles it.
        if (_pendingEditorSync) return; //Avoid enqueuing multiple times.

        _pendingEditorSync = true; //Mark that a deferred sync is pending.
        EditorApplication.delayCall += () => //Defer until it's safe to modify components.
        {
            _pendingEditorSync = false; //Clear pending flag.
            if (this == null) return; //If component was deleted, bail out.

            if (useColliderTrigger) //If collider trigger is enabled…
            {
                if (_autoTriggerCollider == null) //If we don't have one yet…
                {
                    // Safe add via Undo outside validation
                    _autoTriggerCollider = Undo.AddComponent<BoxCollider>(gameObject); //Add BoxCollider with Undo support.
                }

                if (_autoTriggerCollider != null) //If collider exists…
                {
                    ConfigureAutoTriggerCollider(_autoTriggerCollider); //Ensure trigger + hide flags.
                    UpdateAutoTriggerGeometry(_autoTriggerCollider); //Fit collider to renderer bounds + padding.
                }
            }
            else //If collider trigger is disabled…
            {
                if (_autoTriggerCollider != null) //If one exists…
                {
                    // Safe remove via Undo outside validation
                    Undo.DestroyObjectImmediate(_autoTriggerCollider); //Remove it with Undo support.
                    _autoTriggerCollider = null; //Clear reference.
                }
            }

            EditorUtility.SetDirty(this); //Mark this object dirty so changes persist.
        };
#endif
    }

    // Centralized maintenance (used at runtime or via deferred editor calls)
    void MaintainAutoTriggerCollider(bool runtimeCreateDestroy) //Creates/configures or removes the auto trigger collider.
    {
        if (useColliderTrigger) //If feature is enabled…
        {
            if (_autoTriggerCollider == null) //Create one if missing.
            {
#if UNITY_EDITOR
                if (!Application.isPlaying && !runtimeCreateDestroy) //Editor-time creation with Undo support.
                {
                    _autoTriggerCollider = Undo.AddComponent<BoxCollider>(gameObject); //Add via Undo in editor.
                }
                else
#endif
                {
                    _autoTriggerCollider = gameObject.AddComponent<BoxCollider>(); //Add directly at runtime.
                }
            }

            if (_autoTriggerCollider != null) //If we have a collider…
            {
                ConfigureAutoTriggerCollider(_autoTriggerCollider); //Set it as a hidden trigger.
                UpdateAutoTriggerGeometry(_autoTriggerCollider); //Fit to bounds and padding.
            }
        }
        else //Feature disabled -> remove collider if present.
        {
            RemoveAutoTriggerCollider(runtimeImmediate: runtimeCreateDestroy); //Remove appropriately (Undo in editor, Destroy at runtime).
        }
    }

    void ConfigureAutoTriggerCollider(BoxCollider bc) //Applies trigger settings and hide flags to the BoxCollider.
    {
        bc.isTrigger = true; //Make collider a trigger so it doesn't block physics.
#if UNITY_EDITOR
        bc.hideFlags = HideFlags.DontSaveInBuild | HideFlags.DontSaveInEditor; //Hide and avoid saving the helper collider.
#endif
    }

    void UpdateAutoTriggerGeometry(BoxCollider bc) //Fits the BoxCollider to this object's renderer bounds plus padding.
    {
        var bounds = CalculateHierarchyRendererBounds(transform); //Compute bounds from all child renderers.
        if (bounds.size.sqrMagnitude > 0f) //If we have valid bounds…
        {
            bc.center = transform.InverseTransformPoint(bounds.center); //Convert world center to local space.
            bc.size = bounds.size + triggerPadding; //Use bounds size plus padding.
        }
        else //No renderers—fallback to a small default box.
        {
            bc.center = Vector3.zero; //Center at origin.
            bc.size = Vector3.one * 0.5f; //Default half-meter cube.
        }
    }

    void RemoveAutoTriggerCollider(bool runtimeImmediate) //Removes the auto BoxCollider safely.
    {
        if (_autoTriggerCollider == null) return; //Nothing to remove.

#if UNITY_EDITOR
        if (!Application.isPlaying && !runtimeImmediate) //Editor-time, non-immediate removal with Undo.
        {
            Undo.DestroyObjectImmediate(_autoTriggerCollider); //Remove via Undo so it can be restored.
        }
        else
#endif
        {
            Destroy(_autoTriggerCollider); //Runtime (or immediate) removal.
        }

        _autoTriggerCollider = null; //Clear reference after removal.
    }

    static Bounds CalculateHierarchyRendererBounds(Transform root) //Computes a world-space AABB from all Renderer bounds under root.
    {
        var renderers = root.GetComponentsInChildren<Renderer>(true); //Collect all renderers including inactive.
        bool hasAny = false; //Tracks if we found any renderer at all.
        Bounds b = new Bounds(root.position, Vector3.zero); //Initialize bounds.

        foreach (var r in renderers) //Iterate through renderers.
        {
            if (!hasAny) { b = r.bounds; hasAny = true; } //Seed bounds with the first renderer.
            else b.Encapsulate(r.bounds); //Grow bounds to include subsequent renderers.
        }
        return hasAny ? b : new Bounds(root.position, Vector3.zero); //Return computed bounds or empty at root.
    }

    // --------- Collider-based trigger handling with limits and dedupe ---------

    void OnTriggerEnter(Collider other) //Called by Unity when another collider enters our trigger.
    {
        if (!useColliderTrigger) return; //Ignore if feature is off.
        if (!other || !other.gameObject.activeInHierarchy) return; //Ignore null or inactive colliders.
        if (triggerLimit > 0 && _colliderFiresSoFar >= triggerLimit) return; //Stop if we've reached the fire limit.
        if (string.IsNullOrEmpty(triggerTag)) return; //Require a tag to compare.
        if (!other.CompareTag(triggerTag)) return; //Only accept colliders with the required tag.

        // Require a SignalEmitter on the incoming object (or its parents)
        var emitter = other.GetComponentInParent<SignalEmitter>(); //Look for a SignalEmitter on the object or its parents.
        if (emitter == null) return; //Abort if no emitter is present.

        // Deduplicate multiple child colliders of the same object/rigidbody.
        var root = other.attachedRigidbody ? other.attachedRigidbody.transform : other.transform.root; //Use rigidbody root if available, else transform root.
        int rootId = root.GetInstanceID(); //Get a stable ID for dedupe.
        if (!_insideRoots.Add(rootId)) return; //If already inside, ignore this extra collider enter.

        // Fire only if we still have budget
        if (triggerLimit == 0 || _colliderFiresSoFar < triggerLimit) //Check fire allowance.
        {
            _colliderFiresSoFar++; //Count this fire.
            OnSignal(emitter); //Run configured actions as a signal from this emitter.

            // If we've exhausted the limit, optionally disable the trigger so it can't fire again.
            if (triggerLimit > 0 && _colliderFiresSoFar >= triggerLimit && disableTriggerWhenExhausted) //If we've hit the cap and need to shut off…
            {
                if (_autoTriggerCollider) _autoTriggerCollider.enabled = false; //Disable the trigger collider to prevent future fires.
            }
        }
    }

    void OnTriggerExit(Collider other) //Called when a collider exits our trigger volume.
    {
        if (!other) return; //Safety check.
        var root = other.attachedRigidbody ? other.attachedRigidbody.transform : other.transform.root; //Find the root used for dedupe.
        _insideRoots.Remove(root.GetInstanceID()); //Remove the root from "inside" set so re-entry can fire again.
    }

    // Note: external (non-collider) signals are not limited by triggerLimit.
    public override void OnSignal(SignalEmitter from) //Executes all configured actions when this receiver gets a signal.
    {
        if (actions == null || actions.Length == 0) return; //Nothing to do if no actions are configured.

        foreach (var a in actions) //Iterate through each action entry.
        {
            if (a == null || a.action == ActionType.None) continue; //Skip empty/disabled entries.

            switch (a.action) //Execute based on the action type.
            {
                case ActionType.TriggerEmitter:
                    if (a.emitterToTrigger) a.emitterToTrigger.OnInteracted(); //Trigger another emitter.
                    break;

                case ActionType.DestroyObject:
                    if (a.destroyObjects != null) //If we have targets…
                    {
                        foreach (var go in a.destroyObjects) //Destroy each target.
                        {
                            if (!go) continue; //Skip nulls.
                            if (a.destroyDelay <= 0f) Destroy(go); //Destroy immediately.
                            else Destroy(go, a.destroyDelay); //Destroy after a delay.
                        }
                    }
                    break;

                case ActionType.SpawnObject:
                    HandleSpawnObject(a); //Run spawn/activate logic matching SpawnObject.cs.
                    break;

                case ActionType.PlayDialogue:
                    if (a.dialogueTarget) //If a dialogue target exists…
                        a.dialogueTarget.ShowExternal(a.dialogueText, a.dialogueFont); //Display text with optional font.
                    break;

                case ActionType.PlayAnimation:
                    if (a.animator && !string.IsNullOrEmpty(a.animatorTrigger)) //If Animator and trigger name are valid…
                        a.animator.SetTrigger(a.animatorTrigger); //Fire the animation trigger.
                    break;

                case ActionType.PlayAudio:
                    if (a.clip) //If a clip is assigned…
                    {
                        var src = GetPlayerAudioSource(); //Use the Player's AudioSource.
                        if (src)
                        {
                            if (a.stopBeforePlay) src.Stop(); //Optionally stop any currently playing audio.
                            src.PlayOneShot(a.clip); //Play the clip once.
                        }
                        else
                        {
                            Debug.LogWarning("[SignalReceiver] No Player AudioSource found. Tag your player 'Player' and add an AudioSource.", this); //Setup warning.
                        }
                    }
                    break;

                case ActionType.LoadScene:
                    if (!string.IsNullOrEmpty(a.sceneName)) //If a scene name is provided…
                        SceneManager.LoadScene(a.sceneName); //Load the scene.
                    break;

                case ActionType.CallFunction:
                    if (a.functionTarget && !string.IsNullOrEmpty(a.functionName)) //If we have a target and function name…
                    {
                        a.functionTarget.gameObject.SendMessage( //Send a message to call the function.
                            a.functionName, //Method name to call.
                            SendMessageOptions.DontRequireReceiver //Ignore if method doesn't exist.
                        );
                    }
                    break;
            }
        }
    }

    void HandleSpawnObject(ActionEntry a) //Implements ActivateExisting/SpawnPrefab behavior with runtime counters.
    {
        if (a.spawnMode == ActionEntry.SpawnActionMode.ActivateExisting) //Activate Existing path.
        {
            if (!a.objectToActivate) return; //Need a target object.
            if (a.activateOnlyOnce && a._activatedOnce) return; //One-shot enforcement.

            // Only allow when currently OFF
            if (a.objectToActivate.activeSelf) return; //If already active, do nothing.

            a.objectToActivate.SetActive(true); //Activate the object.
            a._activatedOnce = true; //Record that activation has happened.
        }
        else // SpawnPrefab //Instantiate Prefab path.
        {
            if (!a.prefabToSpawn || !a.spawnAt) return; //Need a prefab and a spawn point.

            // 0 = unlimited
            if (a.maxSpawns != 0 && a._spawnedSoFar >= a.maxSpawns) return; //Enforce spawn cap unless unlimited.

            Instantiate(a.prefabToSpawn, a.spawnAt.position, a.spawnAt.rotation); //Spawn the prefab.
            a._spawnedSoFar++; //Increment the counter.
        }
    }

    static AudioSource GetPlayerAudioSource() //Locates and caches the Player's AudioSource.
    {
        if (_cachedPlayerAudio) return _cachedPlayerAudio; //Return cached if available.
        var player = GameObject.FindGameObjectWithTag("Player"); //Find Player by tag.
        if (!player) return null; //If no Player exists, bail.
        _cachedPlayerAudio = player.GetComponent<AudioSource>(); //Cache the AudioSource.
        return _cachedPlayerAudio; //Return the cached source.
    }

#if UNITY_EDITOR
    // Inspector hook for your toggle button
    public void EditorApplyColliderTrigger(bool enable) //Called by the custom editor toggle to enable/disable collider triggering.
    {
        Undo.RecordObject(this, enable ? "Enable Collider Trigger" : "Disable Collider Trigger"); //Record change for Undo.
        useColliderTrigger = enable; //Apply the new state.

        // Reset runtime state when toggling in editor
        _insideRoots.Clear(); //Clear dedupe set.
        _colliderFiresSoFar = 0; //Reset fire count.

        // Defer work safely (outside OnValidate)
        if (!_pendingEditorSync) //Only schedule once.
        {
            _pendingEditorSync = true; //Mark pending work.
            EditorApplication.delayCall += () => //Do the heavy lifting safely later.
            {
                _pendingEditorSync = false; //Clear pending flag.
                if (!this) return; //If object was destroyed, stop.
                MaintainAutoTriggerCollider(runtimeCreateDestroy: false); //Create/remove/configure collider as needed.
                if (_autoTriggerCollider) _autoTriggerCollider.enabled = true; //Ensure collider is enabled after toggle.
                EditorUtility.SetDirty(this); //Mark dirty to save changes.
            };
        }
    }
#endif
}
