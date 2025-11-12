/* This script allows objects to be highlighted when looked at and collected with the E key.
 * Includes improved raycasting and plays an AudioClip on the Player's AudioSource when collected.
 */

using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Collider))] //Makes sure a Collider exists so this object can be detected by raycasts.
public class Collectable : MonoBehaviour //Defines a component that can be attached to a GameObject and collected.
{
    [Header("Interaction")] //Creates a labeled section in the Inspector for interaction settings.
    public KeyCode interactKey = KeyCode.E; //Which key must be pressed to collect this object.
    public float interactDistance = 3f; //How far the player can be to interact with the object.
    private HighlightObject highlight; //Reference to a HighlightObject to toggle outline/visual highlight.

    [Header("Audio")] //Creates a labeled section in the Inspector for audio settings.
    [Tooltip("Audio clip that plays when the object is collected.")] //Adds a hover tooltip describing the field below.
    public AudioClip interactAudioClip; //Sound to play through the Player's AudioSource when collected.

    Transform _playerCam; //Cached reference to the player's camera transform for aiming.
    bool _inSight; //Tracks whether the object is currently targeted by the player's look ray.

    // internal references
    const float kAimRadius = 0.15f; //Radius for sphere casting to make aiming more forgiving.
    AudioSource _playerAudio; //Cached reference to the Player's AudioSource used to play SFX.
    bool _lookedForPlayerAudio; //Ensures we only try to find the Player's AudioSource once.
    bool _warnedNoPlayerAudio; //Prevents repeating the warning if no Player AudioSource is found.

    void Start() //Runs once when the object becomes active in the scene.
    {
        if (!highlight) highlight = GetComponentInChildren<HighlightObject>(includeInactive: true); //Finds a HighlightObject in children if not assigned.
        _playerCam = Camera.main ? Camera.main.transform : null; //Caches the main camera's transform if a main camera exists.
        TryCachePlayerAudio(); //Attempts to locate and cache the Player's AudioSource.
    }

    void TryCachePlayerAudio() //Finds the Player's AudioSource (once) and caches it for playback.
    {
        if (_lookedForPlayerAudio) return; //Exits if we've already attempted to locate the audio source.
        _lookedForPlayerAudio = true; //Marks that we've attempted to find the Player's AudioSource.

        var player = GameObject.FindGameObjectWithTag("Player"); //Looks for a GameObject tagged "Player".
        if (player) _playerAudio = player.GetComponent<AudioSource>(); //If found, tries to get its AudioSource component.
    }

    void PlayInteractAudio() //Safely plays the interaction audio clip on the Player's AudioSource.
    {
        if (!interactAudioClip) return; //Does nothing if no audio clip is assigned.
        if (!_playerAudio) TryCachePlayerAudio(); //If no audio source is cached yet, try to find it now.

        if (_playerAudio) //Checks if a valid audio source is available.
        {
            _playerAudio.PlayOneShot(interactAudioClip); //Plays the clip once without interrupting other sounds.
        }
        else if (!_warnedNoPlayerAudio) //If no audio source and we haven't warned yet…
        {
            _warnedNoPlayerAudio = true; //Mark that we've shown the warning.
            Debug.LogWarning("[Collectable] Interact Audio Clip is set, but no 'Player' AudioSource was found."); //Logs a helpful warning.
        }
    }

    void Update() //Runs every frame to handle aiming, highlighting, and interaction input.
    {
        if (!_playerCam) return; //If no camera is available, skip processing.

        Ray ray = new Ray(_playerCam.position, _playerCam.forward); //Builds a ray from the camera forward to represent the player's look direction.
        _inSight = false; //Resets sight status each frame before checks.

        // Improved sphere cast for lenient targeting
        bool isHit = false; //Tracks whether this object (or a child) is under the aim ray.
        var hits = Physics.SphereCastAll(ray, kAimRadius, interactDistance, ~0, QueryTriggerInteraction.Collide); //Casts a sphere along the ray to detect nearby colliders within distance.
        for (int i = 0; i < hits.Length; i++) //Iterates over all sphere cast hits.
        {
            var h = hits[i]; //Gets the current hit info.
            if (!h.collider) continue; //Skips if this hit has no collider (safety guard).
            var t = h.collider.transform; //Gets the Transform from the hit collider.
            if (t == transform || t.IsChildOf(transform)) //Checks if the hit belongs to this object or one of its children.
            {
                isHit = true; //Marks that our object is under the aim cone.
                break; //Stops searching after the first valid hit.
            }
        }

        if (isHit) //If the object is being looked at within range…
        {
            _inSight = true; //Record that we are in sight for this frame.
            if (highlight) highlight.SetHighlight(true); //Turn on outline/visual highlight if available.

            if (Input.GetKeyDown(interactKey)) //If the player pressed the interact key this frame…
            {
                PlayInteractAudio(); //Play the collection sound (if configured and available).
                if (highlight) highlight.SetHighlight(false); //Disable the highlight since the object will be removed.
                Destroy(gameObject); //Remove the collectable from the scene to simulate pickup.
                GetComponent<SignalEmitter>()?.OnInteracted(); //If a SignalEmitter exists, notify it that interaction occurred.
                return; //Exit to avoid running the "not in sight" logic below this frame.
            }
        }

        if (!_inSight && highlight) highlight.SetHighlight(false); //If not looking at it, ensure highlight is turned off.
    }
}
