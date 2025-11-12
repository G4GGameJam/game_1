/* SpawnObject.cs
 * Press E while looking at this object to perform the current Action Mode:
 *   - ActivateExisting (default): SetActive(true) on a target object
 *   - SpawnPrefab: Instantiate a prefab at a spawn point
 *
 * Includes improved raycasting (sphere cast) + optional HighlightObject support.
 * Now also supports an optional AudioClip (played on the Player's AudioSource).
 */

using UnityEngine;

[RequireComponent(typeof(Collider))] //Ensures a Collider exists so this object can be raycast and interacted with.
public class SpawnObject : MonoBehaviour //Defines a component that activates objects or spawns prefabs on interaction.
{
    public enum ActionMode { ActivateExisting, SpawnPrefab } //Two modes: turn on an existing object or spawn a prefab.

    public ActionMode mode = ActionMode.ActivateExisting; //Default interaction mode is ActivateExisting.

    public KeyCode interactKey = KeyCode.E; //Key the player presses to trigger the action.
    public float interactDistance = 3f; //How far from the camera the player can be to interact.

    public GameObject objectToActivate; //Reference to an existing object that will be set active.
    public bool activateOnlyOnce = true; //If true, activation happens only one time.

    public GameObject prefabToSpawn; //Prefab that will be instantiated when in SpawnPrefab mode.
    public Transform spawnAt; //Transform indicating where and how the prefab is spawned.
    public int maxSpawns = 1; //Maximum number of times the prefab may be spawned (0 = infinite).

    // --- Audio (optional) ---
    [Tooltip("Optional. Plays this clip on the Player's AudioSource when E triggers the action.")] //Inspector hint describing interactSfx.
    public AudioClip interactSfx; //Sound effect played via the Player's AudioSource when the action fires.

    // Internals
    private Transform _cam; //Cached reference to the player camera Transform.
    private HighlightObject _highlight; //Optional highlighter to show when interaction is possible.
    private bool _inSight; //True if the player is currently aiming at this object.
    private int _spawnedSoFar; //Counts how many prefabs have been spawned.
    private bool _activatedOnce; //Tracks if ActivateExisting has already been performed (when one-shot).

    // Audio cache
    private AudioSource _playerAudio; //Cached Player AudioSource used to play interactSfx.
    private bool _lookedForPlayerAudio; //Prevents repeated lookups for the Player AudioSource.
    private bool _warnedNoPlayerAudio; //Prevents spamming the console if no Player AudioSource is found.

    // Improved aim radius for lenient targeting
    const float kAimRadius = 0.15f; //SphereCast radius to make aiming more forgiving than a thin ray.

    void Start() //Called on the first frame; caches references.
    {
        _cam = Camera.main ? Camera.main.transform : null; //Cache main camera Transform if available.
        _highlight = GetComponentInChildren<HighlightObject>(includeInactive: true); //Find optional child highlighter.
        TryCachePlayerAudio(); //Try to cache the Player's AudioSource for SFX.
    }

    void TryCachePlayerAudio() //Finds and caches the Player AudioSource once.
    {
        if (_lookedForPlayerAudio) return; //Skip if we already attempted lookup.
        _lookedForPlayerAudio = true; //Mark that the lookup has been attempted.

        var player = GameObject.FindGameObjectWithTag("Player"); //Find the GameObject tagged "Player".
        if (player) _playerAudio = player.GetComponent<AudioSource>(); //Cache its AudioSource if present.
    }

    void MaybePlaySfx() //Plays interactSfx on the Player's AudioSource if configured.
    {
        if (!interactSfx) return; //If no clip is assigned, do nothing.
        if (!_playerAudio) TryCachePlayerAudio(); //If no cached source, try to find it now.

        if (_playerAudio) //If we have a valid source…
        {
            _playerAudio.PlayOneShot(interactSfx); //Play the clip once without interrupting other sounds.
        }
        else if (!_warnedNoPlayerAudio) //If still missing and not yet warned…
        {
            _warnedNoPlayerAudio = true; //Prevent duplicate warnings.
            Debug.LogWarning("[SpawnObject] Interact SFX set, but no 'Player' AudioSource found."); //Helpful setup warning.
        }
    }

    void Update() //Runs every frame to handle aiming, highlighting, and input.
    {
        if (_cam == null) return; //If no camera is available, skip.

        var canAct = CanAct(mode); //Check whether the action is currently allowed for the selected mode.

        // Improved ray test: sphere cast and check if hit this object or its children
        Ray ray = new Ray(_cam.position, _cam.forward); //Ray from camera forward representing the player's aim.
        _inSight = false; //Reset sight flag.
        bool isHit = false; //Tracks whether this object (or a child) was hit.

        var hits = Physics.SphereCastAll(ray, kAimRadius, interactDistance, ~0, QueryTriggerInteraction.Collide); //Sphere cast for forgiving targeting.
        for (int i = 0; i < hits.Length; i++) //Iterate through all hits.
        {
            var h = hits[i]; //Current hit info.
            if (!h.collider) continue; //Skip if no collider (safety).
            var t = h.collider.transform; //Transform of the hit collider.
            if (t == transform || t.IsChildOf(transform)) //If the hit belongs to this object or its children…
            {
                isHit = true; //Mark as hit.
                break; //No need to check further hits.
            }
        }

        if (isHit) //If the player is aiming at this object…
        {
            _inSight = true; //Mark in-sight.
            if (_highlight) _highlight.SetHighlight(canAct); //Show highlight only if action can be performed.

            if (canAct && Input.GetKeyDown(interactKey)) //If action is allowed and interact key pressed this frame…
            {
                Perform(mode); //Perform the selected action.
                MaybePlaySfx(); //Play optional SFX after successful action.
                GetComponent<SignalEmitter>()?.OnInteracted(); //Notify SignalEmitter (if present) about interaction.
            }
        }

        if (!_inSight && _highlight) //If not aiming at this object and a highlighter exists…
            _highlight.SetHighlight(false); //Ensure highlight is off.
    }

    // --- Public API ---

    public void Perform(ActionMode which) //Performs the requested action mode.
    {
        if (which == ActionMode.ActivateExisting) ActivateNow(); //If mode is ActivateExisting, turn on the target object.
        else SpawnNow(); //Otherwise, spawn a prefab at the spawn point.
    }

    public void ActivateNow() //Immediately activates the referenced object, respecting one-shot rules.
    {
        if (!objectToActivate) return; //Do nothing if no object is assigned.
        if (activateOnlyOnce && _activatedOnce) return; //Abort if it's a one-shot and already used.

        objectToActivate.SetActive(true); //Enable the target object.
        _activatedOnce = true; //Record that activation has occurred.

        if (_highlight && !CanAct(ActionMode.ActivateExisting)) //If no longer actionable in this mode…
            _highlight.SetHighlight(false); //Turn off the highlight.
    }

    public void SpawnNow() //Immediately spawns a prefab at the spawn point, respecting maxSpawns.
    {
        if (!prefabToSpawn || !spawnAt) return; //Do nothing if prefab or spawn point is missing.
        if (!CanAct(ActionMode.SpawnPrefab)) return; //Abort if max spawns have been reached.

        Instantiate(prefabToSpawn, spawnAt.position, spawnAt.rotation); //Create the prefab at the spawn location/rotation.
        _spawnedSoFar++; //Increment the spawn counter.

        if (_highlight && !CanAct(ActionMode.SpawnPrefab)) //If spawning is no longer allowed…
            _highlight.SetHighlight(false); //Turn off the highlight.
    }

    public bool CanAct(ActionMode which) //Returns true if the selected action can currently be performed.
    {
        if (which == ActionMode.ActivateExisting) //For activation mode…
        {
            if (!objectToActivate) return false; //Must have a target object assigned.
            if (activateOnlyOnce && _activatedOnce) return false; //Disallow if one-shot already used.
            // Only allow when currently OFF (pressing E turns it on)
            return !objectToActivate.activeSelf; //Permit only if the object is currently inactive.
        }
        else // SpawnPrefab //For spawn mode…
        {
            if (!prefabToSpawn || !spawnAt) return false; //Must have a prefab and spawn point.
            return (maxSpawns == 0) || (_spawnedSoFar < maxSpawns); //Allow if unlimited (0) or if under the limit.
        }
    }

    public void ResetSpawns() => _spawnedSoFar = 0; //Utility to reset the spawn counter.
    public void ResetActivation() => _activatedOnce = false; //Utility to allow activation again in one-shot setups.
}
