/*
 * Manages Title, Pause, In-game Controls, End-game UI, and Emotion_Room chaining.
 * - Auto-detects "Emotion_Room", "Emotion_Room_2", ... in Build Settings (ordered).
 * - End Game UI "Next Room" button (tag: NextRoom) is shown only if a next room exists.
 * - End Game panel (tag: GameOverPNL) height is set to 600 when next room exists, else 520.
 */

using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.EventSystems;

using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Text.RegularExpressions;

public class GameManager : MonoBehaviour //Main game flow controller for title/pause/game-over and room chaining.
{
    // ---------- Scene naming ----------
    private const string TitleSceneName = "Title_Screen"; //Exact name of the title scene to load/return to.
    private const string RoomBaseName = "Emotion_Room"; //Base name for rooms when none are found in build list.
    private static readonly Regex kRoomRegex = new Regex(@"^Emotion_Room(?:_(\d+))?$"); //Regex to match Emotion_Room and optional numeric suffix.

    // ---------- UI tags ----------
    private const string Tag_TitleScreen = "TitleScreen"; //Tag for the Title screen root.
    private const string Tag_PauseScreen = "PauseScreen"; //Tag for the Pause screen root.
    private const string Tag_TitleControls = "TitleControls"; //Tag for the Title Controls panel.
    private const string Tag_GameControls = "GameControls"; //Tag for the in-game Controls panel.
    private const string Tag_EndGame = "GameOver"; //Tag for the End Game UI root.
    private const string Tag_NextRoom = "NextRoom";    // "Next Room" button (or its container) tag.
    private const string Tag_EndPanel = "GameOverPNL"; // End Game root panel tag (to be resized).

    // ---------- Cached UI roots ----------
    private GameObject _titleScreen; //Cached Title screen GameObject.
    private GameObject _pauseScreen; //Cached Pause screen GameObject.
    private GameObject _titleControls; //Cached Title Controls panel.
    private GameObject _gameControls; //Cached in-game Controls panel.
    private GameObject _endGameScreen; //Cached End Game UI root.
    private GameObject _nextRoomButton;   //Cached "Next Room" button or container.
    private GameObject _endPanel;         //Cached End Game panel for height adjustments.

    // ---------- State flags ----------
    private bool _inTitle; //True if the active scene is the title.
    private bool _isPaused; //True if the pause menu is currently shown.
    private bool _gameOver; //True if the game-over state is currently active.

    // =============================== Unity Lifecycle ===============================
    private void Awake() //Runs when the object is initialized before Start.
    {
        SceneManager.activeSceneChanged += OnActiveSceneChanged; //Subscribe to scene change callback to rewire UI/state.
        RefreshSceneState(); //Initial wiring and state setup for the current scene.
    }

    private void OnDestroy() //Runs when the object is being destroyed.
    {
        SceneManager.activeSceneChanged -= OnActiveSceneChanged; //Unsubscribe to avoid leaks/null-callbacks.
    }

    private void Update() //Called once per frame to handle input.
    {
        if (_gameOver) return; //Ignore input when the game-over screen is up.

        if (!_inTitle && Input.GetKeyDown(KeyCode.Escape)) //If not on title, Esc toggles pause/menus.
        {
            if (_isPaused && _gameControls && _gameControls.activeSelf) //If currently viewing in-game controls panel…
            {
                ShowPauseFromControls(); //Swap back to the pause menu.
                return; //Stop further processing this frame.
            }

            if (_isPaused) ResumeGame(); //If paused, resume gameplay.
            else PauseGame(); //If not paused, enter pause.
        }
    }

    // =============================== Scene / UI Wiring ===============================
    private void OnActiveSceneChanged(Scene _, Scene __) //Callback when the active scene changes.
    {
        RefreshSceneState(); //Re-detect UI roots and reset state for the new scene.
    }

    /// <summary>Re-detects scene, caches tagged UI, resets states, and sets initial visibility.</summary>
    private void RefreshSceneState() //Rescans the scene for UI GameObjects and sets default visibility.
    {
        var active = SceneManager.GetActiveScene().name; //Get the active scene name.
        _inTitle = (active == TitleSceneName); //Determine whether we’re on the title scene.
        _gameOver = false; //Clear any game-over state on scene change.
        _isPaused = false; //Clear pause state on scene change.
        SetTimeScale(1f); //Ensure the game is unpaused in terms of timescale.

        // Cache all tagged UI roots (found even if inactive)
        _titleScreen = FindInActiveSceneByTag(Tag_TitleScreen); //Find Title screen.
        _pauseScreen = FindInActiveSceneByTag(Tag_PauseScreen); //Find Pause screen.
        _titleControls = FindInActiveSceneByTag(Tag_TitleControls); //Find Title Controls.
        _gameControls = FindInActiveSceneByTag(Tag_GameControls); //Find In-game Controls.
        _endGameScreen = FindInActiveSceneByTag(Tag_EndGame); //Find End Game UI.
        _nextRoomButton = FindInActiveSceneByTag(Tag_NextRoom); //Find Next Room button.
        _endPanel = FindInActiveSceneByTag(Tag_EndPanel); //Find End Game panel (RectTransform).

        if (_inTitle) //If we are on the title scene…
        {
            ShowCursor(true); //Show cursor and unlock it.
            SetActiveSafe(_titleScreen, true); //Show Title screen.
            SetActiveSafe(_titleControls, false); //Hide Title Controls.
            SetActiveSafe(_pauseScreen, false); //Hide Pause screen (not relevant on title).
            SetActiveSafe(_gameControls, false); //Hide in-game Controls.
            SetActiveSafe(_endGameScreen, false); //Hide End Game UI.
            SetActiveSafe(_nextRoomButton, false); //Hide Next Room button by default on title.
            // Panel height doesn't matter on title, but reset to base for consistency
            SetEndPanelHeight(520f); //Set panel height to base value for consistency.
        }
        else //In a room scene…
        {
            ShowCursor(false); //Hide and lock cursor for gameplay.
            SetActiveSafe(_pauseScreen, false); //Ensure Pause is hidden.
            SetActiveSafe(_gameControls, false); //Ensure in-game Controls are hidden.
            SetActiveSafe(_titleScreen, false); //Hide Title screen.
            SetActiveSafe(_titleControls, false); //Hide Title Controls.
            SetActiveSafe(_endGameScreen, false); //Hide End Game UI until triggered.
            SetActiveSafe(_nextRoomButton, false); //Hide Next Room until we know one exists.
            // Default room state uses base height; will adjust on Game Over
            SetEndPanelHeight(520f); //Start with base panel height; may change to 600 at game over.
        }

        ClearUISelection(); //Clear any selected UI element to avoid stray focus.
    }

    /// <summary>Finds a GameObject by tag in the active scene, even if disabled. Null if tag missing/not found.</summary>
    private GameObject FindInActiveSceneByTag(string tagName) //Searches entire scene hierarchy for a GameObject with a specific tag.
    {
        if (!TagExists(tagName)) return null; //If the tag doesn’t exist in the project, abort.
        var scene = SceneManager.GetActiveScene(); //Get current scene reference.
        if (!scene.IsValid()) return null; //If scene is invalid, return null.

        var roots = scene.GetRootGameObjects(); //Fetch all root GameObjects.
        for (int i = 0; i < roots.Length; i++) //Iterate roots to perform a recursive search.
        {
            var t = FindInChildrenByTag(roots[i].transform, tagName); //Search this root’s hierarchy.
            if (t) return t.gameObject; //Return the first match found.
        }
        return null; //No match found across all roots.
    }

    private static Transform FindInChildrenByTag(Transform parent, string tagName) //Recursively searches children for a tag.
    {
        if (parent.CompareTag(tagName)) return parent; //If the parent itself has the tag, return it.
        for (int i = 0; i < parent.childCount; i++) //Iterate through children.
        {
            var hit = FindInChildrenByTag(parent.GetChild(i), tagName); //Recurse into child.
            if (hit) return hit; //Return on first match.
        }
        return null; //No child with that tag found in this branch.
    }

    private static bool TagExists(string tagName) //Returns true if the tag is defined in the project.
    {
        try { _ = GameObject.FindGameObjectsWithTag(tagName); return true; } //If call succeeds, tag exists.
        catch { return false; } //If it throws, the tag does not exist.
    }

    // =============================== Title Screen API ===============================
    public void StartGame() //Called by UI button to start the game from title.
    {
        SetTimeScale(1f); //Ensure gameplay time scale is normal.
        _isPaused = false; //Clear any pause state.
        _gameOver = false; //Clear any game-over state.
        ShowCursor(false); //Hide and lock cursor for gameplay.
        ClearUISelection(); //Clear UI selection so gameplay doesn’t start with focused UI.

        string first = GetFirstEmotionRoom(); //Find the first Emotion_Room configured in Build Settings.
        SceneManager.LoadScene(first); //Load the first room scene.
    }

    public void ShowTitleControls() //Opens the Title Controls panel.
    {
        SetActiveSafe(_titleScreen, false); //Hide the main Title screen.
        SetActiveSafe(_titleControls, true); //Show the Title Controls.
        ShowCursor(true); //Ensure cursor is visible for UI interaction.
        ClearUISelection(); //Clear any previous selection.
    }

    public void CloseTitleControls() //Closes Title Controls and returns to main Title screen.
    {
        SetActiveSafe(_titleControls, false); //Hide the Title Controls.
        SetActiveSafe(_titleScreen, true); //Show the Title screen again.
        ShowCursor(true); //Keep cursor visible on title.
        ClearUISelection(); //Clear any selected UI.
    }

    public void QuitGame() //Quits the application (Editor: stops play mode).
    {
        ClearUISelection(); //Clear selection to avoid lingering focus.
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false; //In Editor, stop play mode.
#else
        Application.Quit(); //In build, quit the application.
#endif
    }

    // =============================== Pause Menu (Rooms) ===============================
    private void PauseGame() //Enters the pause state and shows the pause menu.
    {
        if (_inTitle || _gameOver) return; //Do nothing if on title or game-over.

        _isPaused = true; //Mark paused.
        SetTimeScale(0f); //Freeze gameplay time.
        ShowCursor(true); //Show cursor for UI.

        SetActiveSafe(_pauseScreen, true); //Show pause menu.
        SetActiveSafe(_gameControls, false); //Hide in-game controls subpanel.
        ClearUISelection(); //Clear selected UI element.
    }

    public void ResumeGame() //Leaves the pause state and hides pause UI.
    {
        if (_gameOver) return; //Do nothing if game-over UI is up.

        _isPaused = false; //Clear pause flag.
        SetTimeScale(1f); //Resume gameplay time.
        ShowCursor(false); //Hide cursor for gameplay.

        SetActiveSafe(_pauseScreen, false); //Hide pause menu.
        SetActiveSafe(_gameControls, false); //Hide in-game controls if it was open.
        ClearUISelection(); //Clear selected UI.
    }

    public void ShowInGameControls() //Opens the in-game controls panel from pause.
    {
        if (!_isPaused || _gameOver) return; //Only allowed while paused and not game-over.

        SetActiveSafe(_pauseScreen, false); //Hide pause menu.
        SetActiveSafe(_gameControls, true); //Show controls panel.
        ShowCursor(true); //Ensure cursor is visible.
        ClearUISelection(); //Clear selection.
    }

    public void CloseInGameControls() //Closes the in-game controls and returns to the pause menu.
    {
        if (_gameOver) return; //Not allowed during game-over.

        SetActiveSafe(_gameControls, false); //Hide controls panel.
        SetActiveSafe(_pauseScreen, true); //Show pause menu again.
        ShowCursor(true); //Keep cursor visible.
        SetTimeScale(0f); //Remain paused.
        _isPaused = true; //Keep pause flag.
        ClearUISelection(); //Clear selection.
    }

    private void ShowPauseFromControls() //Helper that switches from controls panel back to pause menu.
    {
        if (_gameOver) return; //Do nothing if game-over.

        SetActiveSafe(_gameControls, false); //Hide controls panel.
        SetActiveSafe(_pauseScreen, true); //Show pause menu.
        ShowCursor(true); //Ensure cursor visible.
        SetTimeScale(0f); //Stay paused.
        _isPaused = true; //Keep pause flag.
        ClearUISelection(); //Clear selection.
    }

    public void ReturnToTitle() //Loads the Title scene and resets states.
    {
        SetTimeScale(1f); //Ensure normal time.
        _isPaused = false; //Clear pause.
        _gameOver = false; //Clear game-over.
        ShowCursor(true); //Show cursor on title.
        ClearUISelection(); //Clear selected UI.
        SceneManager.LoadScene(TitleSceneName); //Load the title scene.
    }

    // =============================== End-Game UI (Rooms) ===============================
    /// <summary>
    /// Shows End Game UI, toggles Next Room button, and adjusts panel height (600 if next exists, else 520).
    /// </summary>
    public void TriggerGameOver() //Call this to bring up the end-game screen and handle next-room logic.
    {
        if (_inTitle) return; //Ignore if somehow called on title.

        _gameOver = true; //Enter game-over state.
        _isPaused = false; //Not paused—explicit game-over state.
        SetTimeScale(0f); //Freeze gameplay.
        ShowCursor(true); //Show cursor for UI.

        SetActiveSafe(_pauseScreen, false); //Hide pause.
        SetActiveSafe(_gameControls, false); //Hide controls.
        SetActiveSafe(_endGameScreen, true); //Show end-game UI.

        // Decide if a next room exists
        bool hasNext = HasNextRoom(); //Check if there is another room after this one.

        // Toggle NextRoom button visibility
        SetActiveSafe(_nextRoomButton, hasNext); //Show if next exists; hide otherwise.

        // Adjust End Game panel height
        SetEndPanelHeight(hasNext ? 600f : 520f); //Set panel height: 600 with next, 520 without.

        ClearUISelection(); //Clear selected UI element.
    }

    /// <summary>Reloads the current room.</summary>
    public void RestartRoom() //Restarts the current scene (room).
    {
        _gameOver = false; //Clear game-over.
        _isPaused = false; //Clear pause.
        SetTimeScale(1f); //Resume time.
        ShowCursor(false); //Hide cursor for gameplay.
        ClearUISelection(); //Clear UI selection.

        string current = SceneManager.GetActiveScene().name; //Get active scene name.
        SceneManager.LoadScene(current); //Reload the same scene.
    }

    /// <summary>
    /// Advances to the next Emotion_Room if one exists; otherwise returns to title.
    /// Wire this to the "Next Room" button.
    /// </summary>
    public void NextRoomOrTitle() //Loads the next room or returns to title if none.
    {
        var ordered = GetOrderedEmotionRoomsFromBuild(); //Get the ordered list of Emotion_Room scenes.
        var current = SceneManager.GetActiveScene().name; //Current room name.

        int idx = ordered.FindIndex(n => n == current); //Find index of current room.
        if (idx >= 0 && idx + 1 < ordered.Count) //If a next room exists…
        {
            _gameOver = false; //Clear game-over.
            _isPaused = false; //Clear pause.
            SetTimeScale(1f); //Resume time.
            ShowCursor(false); //Hide cursor for gameplay.
            ClearUISelection(); //Clear selection.
            SceneManager.LoadScene(ordered[idx + 1]); //Load the next room.
        }
        else
        {
            ReturnToTitle(); //No next room—go back to title.
        }
    }

    /// <summary>True if there is a room after the current one in build order.</summary>
    private bool HasNextRoom() //Checks build list to see if a next room exists.
    {
        var ordered = GetOrderedEmotionRoomsFromBuild(); //Get ordered list.
        var current = SceneManager.GetActiveScene().name; //Current room.

        int idx = ordered.FindIndex(n => n == current); //Index of current room.
        return (idx >= 0 && idx + 1 < ordered.Count); //True if there is a next index in range.
    }

    // =============================== Helpers: Rooms Listing ===============================
    private static int ExtractRoomOrder(string sceneName) //Returns numeric order for Emotion_Room_N; Emotion_Room (no N) => 1.
    {
        var m = kRoomRegex.Match(sceneName); //Match against the regex.
        if (!m.Success) return int.MaxValue; //If not an Emotion_Room, put at end.
        if (!m.Groups[1].Success) return 1; //No suffix means first (1).
        if (int.TryParse(m.Groups[1].Value, out int n)) return n; //Parse numeric suffix.
        return int.MaxValue; //If parse fails, push to the end.
    }

    private static List<string> GetOrderedEmotionRoomsFromBuild() //Collects and orders Emotion_Room scenes from Build Settings.
    {
        var list = new List<string>(); //Accumulator for matching scene names.
        int count = SceneManager.sceneCountInBuildSettings; //How many scenes in Build Settings.

        for (int i = 0; i < count; i++) //Iterate all build scenes.
        {
            string path = SceneUtility.GetScenePathByBuildIndex(i); //Get scene path.
            if (string.IsNullOrEmpty(path)) continue; //Skip invalid entries.

            string name = Path.GetFileNameWithoutExtension(path); //Extract scene name.
            if (kRoomRegex.IsMatch(name)) //If it matches Emotion_Room naming…
                list.Add(name); //Add to the list.
        }

        return list
            .Distinct() //Ensure unique names (avoid duplicates).
            .OrderBy(n => ExtractRoomOrder(n)) //Order by parsed numeric suffix (Emotion_Room => 1).
            .ToList(); //Return as a list.
    }

    private static string GetFirstEmotionRoom() //Returns the first Emotion_Room or the base name as a fallback.
    {
        var rooms = GetOrderedEmotionRoomsFromBuild(); //Get ordered rooms.
        return rooms.Count > 0 ? rooms[0] : RoomBaseName; //First room if any; else fallback base.
    }

    // =============================== Helpers: General ===============================
    private void SetTimeScale(float v) //Safely sets Time.timeScale if it actually changed.
    {
        if (!Mathf.Approximately(Time.timeScale, v)) //Only update when different to avoid noise.
            Time.timeScale = v; //Apply new timescale.
    }

    private void SetActiveSafe(GameObject go, bool active) //Enables/disables a GameObject only if it needs changing.
    {
        if (!go) return; //Null-safe check.
        if (go.activeSelf != active) go.SetActive(active); //Apply only if state differs.
    }

    private void ShowCursor(bool show) //Shows/hides the cursor and locks/unlocks it accordingly.
    {
        Cursor.visible = show; //Set cursor visibility.
        Cursor.lockState = show ? CursorLockMode.None : CursorLockMode.Locked; //Unlock for UI, lock for gameplay.
    }

    private void ClearUISelection() //Clears current EventSystem selection to avoid accidental button focus.
    {
        if (EventSystem.current)
            EventSystem.current.SetSelectedGameObject(null); //Clear selected object if an EventSystem exists.
    }

    /// <summary>
    /// Safely sets the height (sizeDelta.y) of the End Game panel RectTransform.
    /// Keeps the current width intact. No-op if panel or RectTransform is missing.
    /// </summary>
    private void SetEndPanelHeight(float height) //Resizes the end panel height while preserving width.
    {
        if (!_endPanel) return; //No panel to resize.
        var rt = _endPanel.GetComponent<RectTransform>(); //Get RectTransform from the panel.
        if (!rt) return; //No RectTransform found.

        var size = rt.sizeDelta; //Copy current sizeDelta (width/height).
        size.y = height; //Update height only.
        rt.sizeDelta = size; //Apply new size.
    }
}
