/* Plays an animation when the player looks at this object and presses a key.
 * Uses a forgiving SphereCast and scans all hits up to interactDistance.
 * Inspector shows: interactKey, interactDistance, animationTrigger, Interact Audio Clip.
 */

using UnityEngine;

[RequireComponent(typeof(Collider))] //Ensures a Collider exists so this object can be raycast and detected.
[RequireComponent(typeof(Animator))] //Ensures an Animator exists so we can trigger an animation.
public class PlayAnimation : MonoBehaviour //Defines a component that triggers an animation on interaction.
{
    [Header("Interaction Settings")] //Groups interaction fields in the Inspector.
    public KeyCode interactKey = KeyCode.E;           //Key the player presses to trigger the animation.
    [Min(0f)] public float interactDistance = 3f;     //Maximum distance the player can be to interact.

    [Header("Animation Settings")] //Groups animation fields in the Inspector.
    public string animationTrigger = "triggerName";   //Name of the Animator trigger parameter to set.

    [Header("Audio")] //Groups audio fields in the Inspector.
    [Tooltip("Interact Audio Clip played on the Player's AudioSource when the animation is triggered.")] //Explains the audio clip field.
    public AudioClip interactAudioClip; //Clip to play (on the Player's AudioSource) when interaction occurs.

    // --- Internals (not shown in Inspector) ---
    const float kRadius = 0.15f; //Fixed sphere cast radius to make aiming more forgiving.
    Animator _animator; //Cached Animator reference for triggering the animation.
    HighlightObject _highlight; //Optional highlighter to visually indicate interactivity.
    Transform _playerCam; //Cached player camera Transform for ray origin/direction.
    bool _inSight; //Tracks whether this object is currently under the player's aim.

    // audio cache
    AudioSource _playerAudio; //Cached reference to the Player's AudioSource for playing the clip.
    bool _lookedForPlayerAudio; //Prevents repeated lookups for the Player's AudioSource.
    bool _warnedNoPlayerAudio; //Prevents spamming warnings if no Player AudioSource is found.

    void Start() //Called on the first frame; caches components and tries to find the camera/audio.
    {
        _animator = GetComponent<Animator>(); //Gets the Animator attached to this object.
        _highlight = GetComponentInChildren<HighlightObject>(includeInactive: true); //Finds a child HighlightObject, if any.
        _playerCam = Camera.main ? Camera.main.transform : null; //Caches the main camera Transform if it exists.
        TryCachePlayerAudio(); //Attempts to find and cache the Player's AudioSource.
    }

    void TryCachePlayerAudio() //Finds the Player's AudioSource once and caches it.
    {
        if (_lookedForPlayerAudio) return; //Skips if we've already attempted a lookup.
        _lookedForPlayerAudio = true; //Marks that a lookup has been done.

        var player = GameObject.FindGameObjectWithTag("Player"); //Searches for a GameObject tagged "Player".
        if (player) _playerAudio = player.GetComponent<AudioSource>(); //Caches the Player's AudioSource if present.
    }

    void PlayInteractAudio() //Safely plays the interactAudioClip on the Player's AudioSource.
    {
        if (!interactAudioClip) return; //Do nothing if no clip is assigned.
        if (!_playerAudio) TryCachePlayerAudio(); //If audio source isn't cached yet, try to find it now.

        if (_playerAudio) //If we have a valid audio source…
        {
            _playerAudio.PlayOneShot(interactAudioClip); //Play the clip once without interrupting other sounds.
        }
        else if (!_warnedNoPlayerAudio) //If still missing and we haven't warned yet…
        {
            _warnedNoPlayerAudio = true; //Ensure we only warn once.
            Debug.LogWarning("[PlayAnimation] Interact Audio Clip is set, but no 'Player' AudioSource was found (tag 'Player')."); //Logs a helpful setup warning.
        }
    }

    void Update() //Runs every frame; handles aim detection, highlighting, and interaction.
    {
        if (!_playerCam) //If we haven't cached a camera yet…
        {
            var cam = Camera.main; //Attempt to find the main camera.
            if (!cam) return; //If still no camera, we cannot interact—exit.
            _playerCam = cam.transform; //Cache the camera Transform.
        }

        _inSight = false; //Reset sight flag each frame.

        Ray ray = new Ray(_playerCam.position, _playerCam.forward); //Builds a ray from the camera forward to represent the player's aim.

        // SphereCastAll so distance matters even if something is in front; we’ll scan all hits
        var hits = Physics.SphereCastAll( //Casts a sphere along the aim ray to find forgiving hits along the path.
            ray, //The ray to cast from the camera forward.
            kRadius, //Radius of the sphere for lenient targeting.
            interactDistance, //Maximum distance to check for hits.
            ~0, //Layer mask: ~0 means collide with everything.
            QueryTriggerInteraction.Collide //Include trigger colliders in the results.
        );

        // Find nearest hit that belongs to this object (or a child)
        float bestDist = float.PositiveInfinity; //Tracks the closest valid hit distance.
        foreach (var h in hits) //Iterates over all sphere cast hits.
        {
            if (!h.collider) continue; //Skips if the hit lacks a collider (safety).
            if (h.distance >= bestDist) continue; //Skips if this hit is farther than the best so far.

            var t = h.collider.transform; //Gets the Transform of the hit collider.
            if (t == transform || t.IsChildOf(transform)) //Checks if the hit belongs to this object or one of its children.
            {
                bestDist = h.distance; //Updates the nearest hit distance.
                _inSight = true; //Marks this object as currently targeted.
            }
        }

        // Highlight while in sight
        if (_highlight) _highlight.SetHighlight(_inSight); //Turns the highlight on/off based on targeting.

        // Trigger on key press
        if (_inSight && Input.GetKeyDown(interactKey)) //If targeted and the interaction key was pressed this frame…
        {
            if (_animator && !string.IsNullOrEmpty(animationTrigger)) //If we have an Animator and a valid trigger name…
                _animator.SetTrigger(animationTrigger); //Fires the trigger to play the configured animation.

            PlayInteractAudio(); //Plays the interaction sound if available.
            GetComponent<SignalEmitter>()?.OnInteracted(); //Notifies a SignalEmitter (if present) that interaction occurred.
        }
    }
}
