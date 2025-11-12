/* This script allows objects to be picked up and dropped by the player using the E key. */

using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

[DefaultExecutionOrder(1000)] //Ensures this script runs after most others so references are initialized.
[RequireComponent(typeof(Collider))] //Forces a Collider to exist so the object can be targeted by raycasts.
public class PickupObject : MonoBehaviour //Defines a component that lets the player pick up and drop this object.
{
    [Header("Interaction")] //Groups interaction settings in the Inspector.
    public KeyCode interactKey = KeyCode.E; //Which key the player presses to pick up or drop the object.
    public float interactDistance = 3f; //How close the player must be to interact with this object.
    private HighlightObject highlight; //Optional outline/highlight script to show when the object is targetable.

    [Header("Audio")] //Groups audio settings in the Inspector.
    [Tooltip("Interact Audio Clip played on the Player's AudioSource when picking up or dropping.")] //Explains the audio clip field in the Inspector.
    public AudioClip interactAudioClip; //Sound to play (via the Player's AudioSource) on pickup and on drop.

    private Transform holdPoint; //Where the object will be held (a Transform tagged "HoldPoint").
    Transform _playerCam; //Cached reference to the player camera's Transform for aim checks.
    bool _inSight; //Tracks if the player is currently aiming at this object.
    bool _isHeld; //Tracks if this object is currently being held.

    Quaternion _heldRotationWorld; //Stores the rotation the object had when picked up (kept while held).
    RigidbodyConstraints _rbConstraintsBefore; //Saves Rigidbody constraints so they can be restored on drop.

    private static PickupObject s_currentlyHeld; //Global reference so only one PickupObject can be held at a time.

    const float kAimRadius = 0.15f; //Spherecast radius to make aiming more forgiving than a thin ray.

    AudioSource _playerAudio; //Cached Player AudioSource to play the interact sound.
    bool _lookedForPlayerAudio; //Ensures we only attempt to find the Player AudioSource once.
    bool _warnedNoPlayerAudio; //Prevents spamming the console if the Player AudioSource is missing.

    void Awake() //Runs when the object is initialized in the scene.
    {
        if (Camera.main) _playerCam = Camera.main.transform; //Caches the main camera Transform if available.

        if (!holdPoint) //If no holdPoint was assigned in the Inspector…
        {
            var hp = GameObject.FindGameObjectWithTag("HoldPoint"); //Looks for a Transform tagged "HoldPoint".
            if (hp) holdPoint = hp.transform; //Caches the found hold point if present.
        }

        var rbs = GetComponentsInChildren<Rigidbody>(includeInactive: true); //Finds all child Rigidbodies, including inactive ones.
        var prevKinematic = new bool[rbs.Length]; //Stores their original isKinematic states.

        for (int i = 0; i < rbs.Length; i++) //Loops through each child Rigidbody.
        {
            prevKinematic[i] = rbs[i].isKinematic; //Saves whether it was kinematic.
            rbs[i].isKinematic = true; //Temporarily makes it kinematic to safely adjust mesh colliders.
        }

        FixMeshColliders(); //Forces any MeshColliders to be convex for stable physics and queries.

        for (int i = 0; i < rbs.Length; i++) //Loops through each child Rigidbody again.
        {
            rbs[i].isKinematic = prevKinematic[i]; //Restores its original kinematic state.
        }
    }

    void Start() //Runs on the first frame after Awake.
    {
        if (!highlight) highlight = GetComponentInChildren<HighlightObject>(includeInactive: true); //Finds a HighlightObject in children if not assigned.
        TryCachePlayerAudio(); //Attempts to locate and cache the Player's AudioSource.
    }

    void TryCachePlayerAudio() //Finds and caches the Player's AudioSource once.
    {
        if (_lookedForPlayerAudio) return; //Skips if we've already tried locating it.
        _lookedForPlayerAudio = true; //Marks that we've attempted the lookup.

        var player = GameObject.FindGameObjectWithTag("Player"); //Searches for the GameObject tagged "Player".
        if (player) _playerAudio = player.GetComponent<AudioSource>(); //Caches its AudioSource if found.
    }

    void PlayInteractAudio() //Plays the interact sound on the Player's AudioSource if available.
    {
        if (!interactAudioClip) return; //Does nothing if no clip is assigned.
        if (!_playerAudio) TryCachePlayerAudio(); //If no cached source yet, try to find it now.

        if (_playerAudio) //If a valid Player AudioSource exists…
        {
            _playerAudio.PlayOneShot(interactAudioClip); //Plays the clip once without interrupting other sounds.
        }
        else if (!_warnedNoPlayerAudio) //If still missing and we haven't warned yet…
        {
            _warnedNoPlayerAudio = true; //Ensure we warn only once.
            Debug.LogWarning("[PickupObject] Interact Audio Clip is set, but no 'Player' AudioSource was found (tag 'Player')."); //Helpful setup warning.
        }
    }

    void Update() //Runs every frame to handle aiming, pickup, and drop input.
    {
        if (!_playerCam) return; //If there's no camera, interaction is impossible—exit early.

        if (!_isHeld) //If the object is currently not held…
        {
            if (s_currentlyHeld != null && s_currentlyHeld != this) //If another object is already held…
            {
                if (highlight) highlight.SetHighlight(false); //Make sure this one is not highlighted.
                return; //Do not allow interacting while something else is held.
            }

            Ray ray = new Ray(_playerCam.position, _playerCam.forward); //Ray from camera forward representing the player's aim.
            _inSight = false; //Reset the "in sight" flag each frame.

            float bestDist = float.PositiveInfinity; //Tracks the closest hit distance on this object.
            var hits = Physics.SphereCastAll(ray, kAimRadius, interactDistance, ~0, QueryTriggerInteraction.Collide); //Spherecast to find lenient hits within range.
            for (int i = 0; i < hits.Length; i++) //Iterate through all spherecast hits.
            {
                var h = hits[i]; //Current hit info.
                if (!h.collider) continue; //Skip if no collider was hit (safety).
                var t = h.collider.transform; //Transform of the hit collider.
                bool hitThis = (t == transform) || t.IsChildOf(transform); //True if the hit belongs to this object or a child.
                if (!hitThis) continue; //Ignore hits on other objects.
                if (h.distance < bestDist) bestDist = h.distance; //Keep the nearest valid hit distance.
            }

            if (bestDist < float.PositiveInfinity) //If at least one valid hit was found…
            {
                _inSight = true; //Mark that we are currently aimed at.
                if (highlight) highlight.SetHighlight(true); //Turn on highlight/outline if available.

                if (Input.GetKeyDown(interactKey)) //If the player pressed the interact key this frame…
                {
                    if (s_currentlyHeld == null || s_currentlyHeld == this) //Only proceed if nothing else is held (or it's already us).
                        Pickup(); //Start holding this object.
                }
            }

            if (!_inSight && highlight) highlight.SetHighlight(false); //If not aimed at, ensure highlight is off.
        }
        else //If the object is currently being held…
        {
            if (holdPoint) //If we have a valid hold point…
            {
                transform.position = holdPoint.position; //Move the object to the hold point's position.
                transform.rotation = _heldRotationWorld; //Maintain the rotation captured at pickup.
            }

            if (Input.GetKeyDown(interactKey)) //If the player presses the interact key while holding…
            {
                Drop(); //Release the object.
            }
        }
    }

    void Pickup() //Begins holding the object at the hold point and adjusts physics.
    {
        if (s_currentlyHeld != null && s_currentlyHeld != this) return; //If something else is held, abort.
        s_currentlyHeld = this; //Claim the global "held" slot.

        _isHeld = true; //Mark as currently held.
        _heldRotationWorld = transform.rotation; //Capture current rotation to preserve while held.

        var rb = GetComponent<Rigidbody>(); //Try to get the object's Rigidbody for physics tweaks.
        if (rb) //If there is a Rigidbody…
        {
            _rbConstraintsBefore = rb.constraints; //Save its current constraints.
            rb.constraints = RigidbodyConstraints.FreezeRotation; //Freeze rotation to avoid wobbling in hand.
            rb.isKinematic = true; //Make kinematic so we can move it via transform.
        }

        if (holdPoint) transform.SetParent(holdPoint, worldPositionStays: true); //Parent to the hold point so it follows player.
        if (highlight) highlight.SetHighlight(false); //Turn off highlight now that it's held.

        PlayInteractAudio(); //Play pickup sound if configured.
    }

    void Drop() //Stops holding the object and restores physics state.
    {
        if (s_currentlyHeld == this) s_currentlyHeld = null; //Release the global "held" slot if we own it.
        _isHeld = false; //Mark as no longer held.

        transform.SetParent(null, worldPositionStays: true); //Unparent so it returns to the scene root (keeping world pose).

        var rb = GetComponent<Rigidbody>(); //Try to get the Rigidbody to restore physics settings.
        if (rb) //If present…
        {
            transform.rotation = _heldRotationWorld; //Re-apply the preserved rotation for a clean release.
            rb.isKinematic = false; //Return to dynamic physics.
            rb.angularVelocity = Vector3.zero; //Clear any spin when released.
            rb.velocity = Vector3.zero; //Clear linear velocity to avoid sudden throws.
            rb.constraints = _rbConstraintsBefore; //Restore original constraints.
        }

        PlayInteractAudio(); //Play drop sound if configured.
    }

    void FixMeshColliders() //Forces any MeshColliders under this object to convex for stable physics.
    {
        var meshCols = GetComponentsInChildren<MeshCollider>(includeInactive: true); //Finds all MeshColliders in children.
        for (int i = 0; i < meshCols.Length; i++) //Loops through each MeshCollider found.
        {
            var mc = meshCols[i]; //Reference to the current MeshCollider.
            if (!mc) continue; //Skip null entries (safety).
            mc.convex = true; //Mark as convex so it works with non-kinematic rigidbodies and queries.
        }
    }
}
