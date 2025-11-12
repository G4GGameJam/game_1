/* This script displays on-screen dialogue when the player looks at an object and presses the E key.
 * Adds a limited number of plays and keeps "Press E to Close" working even when interaction is exhausted.
 */

using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

[RequireComponent(typeof(Collider))] //Ensures a Collider exists so this object can be detected by raycasts.
public class PlayDialogue : MonoBehaviour //Defines a component that shows dialogue when interacted with.
{
    [Header("Interaction")] //Groups interaction settings in the Inspector.
    public KeyCode interactKey = KeyCode.E; //Which key the player presses to open/close the dialogue.
    public float interactDistance = 3f; //Maximum distance at which the player can interact.

    [Header("Dialogue")] //Groups dialogue content settings.
    [TextArea(2, 6)] //Displays a multi-line text box in the Inspector.
    public string dialogueText = "Hello! This is a simple dialogue."; //The message to display in the UI.
    public TMP_FontAsset dialogueFont; //Optional TMP font to use for the dialogue text and footer.

    [Header("Use Limit")] //Groups usage limit settings.
    [Tooltip("How many times the dialogue can be opened before this object stops interacting.")] //Explains maxPlays behavior.
    public int maxPlays = 1; //Total number of times the player can open the dialogue.
    [Tooltip("If true, this component disables itself after uses are exhausted.")] //Explains disable-on-exhaust behavior.
    public bool disableComponentWhenExhausted = true; //When true, disables this component after uses run out.

    [Header("Audio")] //Groups audio settings.
    [Tooltip("Interact Audio Clip played on the Player's AudioSource when the dialogue opens.")] //Explains the clip usage.
    public AudioClip interactAudioClip; //Audio clip played via the Player's AudioSource when opening the dialogue.

    Transform _cam; //Cached reference to the player's camera transform.
    HighlightObject _highlight; //Optional outline/highlight component to show interactivity.
    bool _inSight; //Tracks whether this object is currently under the player's aim.

    int _playsLeft; //How many openings remain before the object is exhausted.
    bool _exhausted; //True when no more openings remain.

    // audio cache
    AudioSource _playerAudio; //Cached Player AudioSource for playing interact sounds.
    bool _lookedForPlayerAudio; //Ensures we only attempt to find the Player AudioSource once.
    bool _warnedNoPlayerAudio; //Prevents repeated warnings if no Player AudioSource is found.

    const float kAimRadius = 0.15f; //Sphere cast radius to make aiming more forgiving than a thin ray.

    void Awake() //Called when the component is initialized.
    {
        _playsLeft = Mathf.Max(0, maxPlays); //Clamp initial plays to non-negative and store.
        _exhausted = (_playsLeft == 0); //Mark exhausted if starting with zero allowed plays.
    }

    void Start() //Called on the first frame; caches references and applies initial state.
    {
        _cam = Camera.main ? Camera.main.transform : null; //Cache the main camera transform if available.
        _highlight = GetComponentInChildren<HighlightObject>(includeInactive: true); //Find optional highlight component.

        if (_exhausted) //If already exhausted at start…
        {
            if (_highlight) { _highlight.SetHighlight(false); _highlight.enabled = false; } //Turn off and disable highlight.
            if (disableComponentWhenExhausted) enabled = false; //Optionally disable this component entirely.
        }
        TryCachePlayerAudio(); //Attempt to cache the Player AudioSource.
    }

    void TryCachePlayerAudio() //Finds and caches the Player's AudioSource if possible.
    {
        if (_lookedForPlayerAudio) return; //Skip if we've already attempted to find it.
        _lookedForPlayerAudio = true; //Mark that we tried to find the audio source.

        var player = GameObject.FindGameObjectWithTag("Player"); //Locate the GameObject tagged "Player".
        if (player) _playerAudio = player.GetComponent<AudioSource>(); //Cache its AudioSource if it exists.
    }

    void PlayInteractAudio() //Plays the interaction clip on the Player's AudioSource if set up.
    {
        if (!interactAudioClip) return; //Do nothing if no clip is assigned.
        if (!_playerAudio) TryCachePlayerAudio(); //Try to find the audio source if not yet cached.

        if (_playerAudio) //If we have a valid audio source…
        {
            _playerAudio.PlayOneShot(interactAudioClip); //Play the clip once without interrupting other sounds.
        }
        else if (!_warnedNoPlayerAudio) //If still missing and we haven't warned yet…
        {
            _warnedNoPlayerAudio = true; //Mark that we've warned to avoid spamming.
            Debug.LogWarning("[PlayDialogue] Interact Audio Clip is set, but no 'Player' AudioSource was found (tag 'Player')."); //Helpful setup warning.
        }
    }

    void Update() //Runs every frame to handle aiming, highlighting, and interaction.
    {
        if (_cam == null) return; //If no camera is available, skip processing.

        // If exhausted, keep highlight off and do nothing else.
        if (_exhausted) //If no uses remain…
        {
            if (_highlight) _highlight.SetHighlight(false); //Ensure highlight is off.
            return; //Exit early to avoid interaction.
        }

        Ray ray = new Ray(_cam.position, _cam.forward); //Create a ray from the camera forward to represent player aim.
        _inSight = false; //Reset sight flag each frame.

        // Improved raycast: sphere within range, accept children.
        bool isHit = false; //Tracks whether the aim hits this object or its children.
        var hits = Physics.SphereCastAll(ray, kAimRadius, interactDistance, ~0, QueryTriggerInteraction.Collide); //Sphere cast to find forgiving hits along the aim path.
        for (int i = 0; i < hits.Length; i++) //Iterate over all hits returned by the cast.
        {
            var h = hits[i]; //Current hit info.
            if (!h.collider) continue; //Skip if no collider (safety).
            var t = h.collider.transform; //Transform of the hit collider.
            if (t == transform || t.IsChildOf(transform)) { isHit = true; break; } //Accept if the hit belongs to this object or its children.
        }

        if (isHit) //If we are currently aimed at…
        {
            _inSight = true; //Record sight state.
            if (_highlight) _highlight.SetHighlight(true); //Enable highlight if available.

            if (Input.GetKeyDown(interactKey)) //If the interact key was pressed this frame…
            {
                // Play audio, then show dialogue and consume a play; close key handled by UI.
                PlayInteractAudio(); //Play the configured audio clip if possible.
                SimpleTMPDialogueUI.Show(dialogueText, dialogueFont, interactKey); //Open the dialogue singleton UI.
                _playsLeft = Mathf.Max(0, _playsLeft - 1); //Consume one remaining use, clamped at zero.

                GetComponent<SignalEmitter>()?.OnInteracted(); //Notify a SignalEmitter (if present) that interaction occurred.

                if (_playsLeft == 0) Exhaust(); //If no plays remain, mark as exhausted and optionally disable.
                return; //Exit to avoid running the "not in sight" branch below.
            }
        }

        if (!_inSight && _highlight) _highlight.SetHighlight(false); //Turn off highlight when not aimed at.

        // NOTE: close-on-key is in SimpleTMPDialogueUI so it still works if this script is disabled. //Explains why closing still works after disable.
    }

    void Exhaust() //Handles the transition to an exhausted state (no further interactions).
    {
        _exhausted = true; //Mark as exhausted.
        if (_highlight) { _highlight.SetHighlight(false); _highlight.enabled = false; } //Turn off and disable highlight visuals.
        if (disableComponentWhenExhausted) enabled = false; //Optionally disable this component to stop Update().
    }

    public void ShowExternal(string message, TMP_FontAsset font = null) //Public helper to open the dialogue programmatically.
    {
        var type = typeof(PlayDialogue); //Get the PlayDialogue type for reflection.
        var nested = type.GetNestedType("SimpleTMPDialogueUI", System.Reflection.BindingFlags.NonPublic); //Find the nested UI class.
        var show = nested.GetMethod("Show", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static, //Get the Show method via reflection.
                                    null, new System.Type[] { typeof(string), typeof(TMP_FontAsset), typeof(KeyCode) }, null); //Specify the exact signature to find.
        show?.Invoke(null, new object[] { message, font, interactKey }); //Invoke the static Show method if found.
    }

    // --------------------------------------------------------------------
    // SimpleTMPDialogueUI  (singleton UI that now also listens for close key)
    // --------------------------------------------------------------------
    private static class SimpleTMPDialogueUI //A private static singleton UI builder/manager for dialogue.
    {
        static GameObject _root; //Root GameObject for the UI hierarchy.
        static Canvas _canvas; //Canvas used to render the UI in screen space.
        static Image _dimmer; //Fullscreen dimmer behind the panel.
        static GameObject _panel; //Container panel for the text and footer.
        static TextMeshProUGUI _text; //TMP text component for the dialogue body.
        static TextMeshProUGUI _footerLabel; //TMP text component for the footer ("Press E to Close").

        static bool _openedThisFrame; //Prevents immediate close on the same frame as open.
        static KeyCode _closeKey = KeyCode.E; //Key used to close the dialogue, set by Show().

        const float kMaxWidth = 560f; //Maximum text width before wrapping.
        const float kMinWidth = 260f; //Minimum panel width.
        const float kMaxHeight = 420f; //Maximum text height before clipping.
        const float kPaddingX = 24f; //Horizontal padding inside the panel.
        const float kPaddingTop = 24f; //Top padding inside the panel.
        const float kPaddingBot = 56f; //Bottom padding to make room for footer.
        const float kMarginX = 32f; //Right margin from screen edge.
        const float kMarginY = 32f; //Bottom margin from screen edge.

        public static bool IsOpen => _root && _root.activeSelf; //Returns true if the UI is built and currently visible.

        // Accept closeKey
        public static void Show(string message, TMP_FontAsset font, KeyCode closeKey) //Builds/shows the UI and fills it with content.
        {
            EnsureBuilt(); //Build the UI if it doesn't exist.
            _closeKey = closeKey;        // remember which key should close the panel //Stores the key that will close the UI.
            _openedThisFrame = true; //Mark open-on-this-frame to prevent instant close.

            _text.enableWordWrapping = true; //Enable wrapping so long messages wrap to new lines.
            _text.overflowMode = TextOverflowModes.Overflow; //Allow overflow vertically; height is clamped separately.
            _text.text = string.IsNullOrEmpty(message) ? "" : message; //Set the text, defaulting to empty if null.

            if (font) { _text.font = font; _footerLabel.font = font; } //If a font was provided, apply it to both text and footer.

            AutoSizeAndPlacePanel(message); //Compute panel size based on text size and place it.
            _root.SetActive(true); //Show the UI root.
        }

        public static bool CanCloseThisFrame() => !_openedThisFrame; //Returns false only on the frame the UI opened.

        public static void Hide() //Hides the dialogue UI if it exists.
        {
            if (_root) _root.SetActive(false); //Deactivate the root to hide everything.
        }

        static void LateUpdateGuard() { _openedThisFrame = false; } //Resets the "opened this frame" flag at the end of the frame.

        static void EnsureBuilt() //Builds the dialogue UI hierarchy once (singleton).
        {
            if (_root != null) return; //Do nothing if already built.

            _root = new GameObject("PlayDialogueUI"); //Create a root GO for the UI.
            Object.DontDestroyOnLoad(_root); //Keep the UI alive across scene loads.

            _canvas = _root.AddComponent<Canvas>(); //Add a Canvas to render UI.
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay; //Draw on top of the screen.
            _canvas.sortingOrder = 32760; //Very high order to appear above most UI.

            var scaler = _root.AddComponent<CanvasScaler>(); //Add a CanvasScaler to handle different screen sizes.
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; //Scale relative to a reference resolution.
            scaler.referenceResolution = new Vector2(1920, 1080); //Target reference resolution.
            scaler.matchWidthOrHeight = 0.5f; //Blend between width and height scaling.

            _root.AddComponent<GraphicRaycaster>(); //Enable UI raycasting for events (future-proofing).
            EnsureEventSystem(); //Make sure an EventSystem exists so UI can process input.

            var dimGO = new GameObject("Dimmer"); //Create a background dimmer object.
            dimGO.transform.SetParent(_root.transform, false); //Parent under the UI root.
            _dimmer = dimGO.AddComponent<Image>(); //Add an Image to act as the dimmer.
            _dimmer.color = new Color(0f, 0f, 0f, 0.35f); //Set a semi-transparent black color.
            var dimRT = _dimmer.rectTransform; //Cache the RectTransform of the dimmer.
            dimRT.anchorMin = Vector2.zero; dimRT.anchorMax = Vector2.one; //Stretch to full screen.
            dimRT.offsetMin = Vector2.zero; dimRT.offsetMax = Vector2.zero; //No offsets to fully cover.

            _panel = new GameObject("Panel"); //Create the dialogue panel container.
            _panel.transform.SetParent(_root.transform, false); //Parent under the UI root.
            var panelImg = _panel.AddComponent<Image>(); //Add a background Image for the panel.
            panelImg.color = Color.white; //Set panel background color to white.
            var panelRT = (RectTransform)_panel.transform; //Cast to RectTransform for sizing/positioning.
            panelRT.anchorMin = new Vector2(1f, 0f); //Anchor to bottom-right corner.
            panelRT.anchorMax = new Vector2(1f, 0f); //Anchor to bottom-right corner.
            panelRT.pivot = new Vector2(1f, 0f); //Pivot at bottom-right.
            panelRT.anchoredPosition = new Vector2(-kMarginX, kMarginY); //Offset inward by margins.

            var textGO = new GameObject("Text"); //Create a GO for the TMP text.
            textGO.transform.SetParent(_panel.transform, false); //Parent under the panel.
            _text = textGO.AddComponent<TextMeshProUGUI>(); //Add TMP text component for dialogue body.
            _text.text = "Dialogue goes here."; //Default placeholder text.
            _text.fontSize = 30; //Readable body font size.
            _text.color = Color.black; //Text color.
            _text.alignment = TextAlignmentOptions.TopLeft; //Align text to top-left.
            var textRT = _text.rectTransform; //Cache text RectTransform.
            textRT.anchorMin = new Vector2(0f, 0f); //Stretch across panel vertically.
            textRT.anchorMax = new Vector2(1f, 1f); //Stretch across panel horizontally.
            textRT.pivot = new Vector2(0.5f, 0.5f); //Center pivot for consistency.
            textRT.offsetMin = new Vector2(kPaddingX, kPaddingBot); //Left/bottom padding (space for footer).
            textRT.offsetMax = new Vector2(-kPaddingX, -kPaddingTop); //Right/top padding.

            var footerGO = new GameObject("Footer"); //Create a GO for the footer label.
            footerGO.transform.SetParent(_panel.transform, false); //Parent under the panel.
            _footerLabel = footerGO.AddComponent<TextMeshProUGUI>(); //Add TMP text for the footer hint.
            _footerLabel.text = "Press E to Close"; //Default footer text.
            _footerLabel.fontSize = 18; //Smaller footer font size.
            _footerLabel.color = Color.black; //Footer text color.
            _footerLabel.alignment = TextAlignmentOptions.BottomRight; //Place footer text at bottom-right.
            var footerRT = _footerLabel.rectTransform; //Cache footer RectTransform.
            footerRT.anchorMin = new Vector2(0f, 0f); //Anchor to the bottom edge.
            footerRT.anchorMax = new Vector2(1f, 0f); //Stretch horizontally along bottom.
            footerRT.pivot = new Vector2(1f, 0f); //Pivot at bottom-right.
            footerRT.sizeDelta = new Vector2(0f, 32f); //Set footer height.
            footerRT.anchoredPosition = new Vector2(-kPaddingX, kPaddingX); //Position with right margin and small vertical inset.

            _root.SetActive(false); //Start hidden until Show is called.

            // Updater handles both late flag reset AND global close key.
            var updater = _root.AddComponent<SimpleLateUpdater>(); //Add a helper component to process updates.
            updater.onLateUpdate = LateUpdateGuard; //Assign a callback to reset the open-on-this-frame flag.
        }

        static void AutoSizeAndPlacePanel(string message) //Calculates panel size from message length and clamps it.
        {
            Vector2 preferred = _text.GetPreferredValues(message, kMaxWidth, 0f); //Measure preferred size for the given width.
            float textW = Mathf.Clamp(preferred.x, kMinWidth, kMaxWidth); //Clamp text width to min/max.
            float textH = Mathf.Min(preferred.y, kMaxHeight); //Limit text height to max allowed.
            float panelW = textW + (kPaddingX * 2f); //Panel width is text width plus horizontal padding.
            float panelH = textH + kPaddingTop + kPaddingBot; //Panel height adds top/bottom padding.

            var panelRT = (RectTransform)_panel.transform; //Get the panel RectTransform.
            panelRT.sizeDelta = new Vector2(panelW, panelH); //Apply computed size.
            LayoutRebuilder.ForceRebuildLayoutImmediate(panelRT); //Force layout update to reflect changes immediately.
        }

        static void EnsureEventSystem() //Creates an EventSystem if the scene doesn't already have one.
        {
            if (EventSystem.current != null) return; //Do nothing if one already exists.
            var es = new GameObject("EventSystem"); //Create a new EventSystem GO.
            Object.DontDestroyOnLoad(es); //Keep it across scene loads.
            es.AddComponent<EventSystem>(); //Add the EventSystem component.
            es.AddComponent<StandaloneInputModule>(); //Add input module for standalone input.
        }

        private class SimpleLateUpdater : MonoBehaviour //Tiny helper MonoBehaviour to process Update and LateUpdate.
        {
            public System.Action onLateUpdate; //Callback invoked during LateUpdate.

            void Update() //Called every frame before LateUpdate.
            {
                // Global close listener: works even if the PlayDialogue component is disabled.
                if (IsOpen && Input.GetKeyDown(_closeKey) && CanCloseThisFrame()) //If open and the close key is pressed (not the same frame we opened)…
                {
                    Hide(); //Hide the dialogue UI.
                }

                // Also allow ESC as a universal close fallback.
                if (IsOpen && Input.GetKeyDown(KeyCode.Escape) && CanCloseThisFrame()) //Allow Esc to close as well.
                {
                    Hide(); //Hide the dialogue UI.
                }
            }

            void LateUpdate() { onLateUpdate?.Invoke(); } //Invoke the assigned late-update callback.
        }
    }
}
