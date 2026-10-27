using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Start / pause menu: choose character, path and difficulty. Opens at launch; M or Tab opens it any time.
/// Drawn with IMGUI so it needs no prefab, font asset or EventSystem. The game is paused while it is open.
/// </summary>
public class TyreMenu : MonoBehaviour
{
    public TyrePushBootstrap game;

    bool open;
    bool firstOpen = true;
    int character, path, difficulty;

    Texture2D dim, panelTex;
    GUIStyle title, header, body, button, bigButton, hint;

    void Start() { Open(); }

    void OnDestroy()
    {
        if (open) Time.timeScale = 1f;
    }

    void Update()
    {
        Keyboard kb = Keyboard.current;
        if (kb == null) return;

        if (kb.mKey.wasPressedThisFrame || kb.tabKey.wasPressedThisFrame)
        {
            if (open && !firstOpen) Close();
            else if (!open) Open();
        }
        else if (open && kb.enterKey.wasPressedThisFrame)
        {
            StartGame();
        }
    }

    void Open()
    {
        open = true;
        character = TyreGameSettings.Character;
        path = TyreGameSettings.Path;
        difficulty = (int)TyreGameSettings.Difficulty;

        Time.timeScale = 0f;
        if (game != null)
        {
            game.MenuOpen = true;
            if (game.Player != null)
            {
                if (game.Player.input != null) game.Player.input.enabled = false;
                if (game.Player.cam != null) { game.Player.cam.inputBlocked = true; game.Player.cam.UnlockCursor(); }
            }
        }
    }

    void Close()
    {
        open = false;
        firstOpen = false;
        Time.timeScale = 1f;
        if (game != null)
        {
            game.MenuOpen = false;
            if (game.Player != null)
            {
                if (game.Player.input != null) game.Player.input.enabled = true;
                if (game.Player.cam != null) { game.Player.cam.inputBlocked = false; game.Player.cam.LockCursor(); }
            }
        }
    }

    void StartGame()
    {
        TyreGameSettings.Character = character;
        TyreGameSettings.Path = path;
        TyreGameSettings.Difficulty = (TyreDifficulty)difficulty;
        if (game != null) game.ApplySelection(true);
        Close();
    }

    // ------------------------------------------------------------------
    void EnsureStyles()
    {
        if (title != null) return;

        dim = new Texture2D(1, 1); dim.SetPixel(0, 0, new Color(0f, 0f, 0f, 0.78f)); dim.Apply();
        panelTex = new Texture2D(1, 1); panelTex.SetPixel(0, 0, new Color(0.09f, 0.09f, 0.11f, 0.97f)); panelTex.Apply();

        Color orange = new Color(0.98f, 0.58f, 0.12f, 1f);

        title = new GUIStyle(GUI.skin.label) { fontSize = 64, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
        title.normal.textColor = orange;

        header = new GUIStyle(GUI.skin.label) { fontSize = 30, fontStyle = FontStyle.Bold };
        header.normal.textColor = orange;

        body = new GUIStyle(GUI.skin.label) { fontSize = 26, wordWrap = true };
        body.normal.textColor = new Color(0.92f, 0.92f, 0.92f, 1f);

        hint = new GUIStyle(body) { fontSize = 22, alignment = TextAnchor.MiddleCenter };
        hint.normal.textColor = new Color(1f, 1f, 1f, 0.6f);

        button = new GUIStyle(GUI.skin.button) { fontSize = 28, fontStyle = FontStyle.Bold };
        button.normal.textColor = Color.white;
        button.onNormal.textColor = orange;
        button.hover.textColor = Color.white;
        button.onHover.textColor = orange;

        bigButton = new GUIStyle(GUI.skin.button) { fontSize = 40, fontStyle = FontStyle.Bold };
        bigButton.normal.textColor = Color.white;
        bigButton.hover.textColor = orange;
    }

    int Section(string head, float x, ref float y, float w, int selected, string[] names, int columns, string description)
    {
        GUI.Label(new Rect(x, y, w, 40f), head, header);
        y += 44f;

        int rows = Mathf.CeilToInt(names.Length / (float)columns);
        float h = rows * 66f;
        int result = GUI.SelectionGrid(new Rect(x, y, w, h), selected, names, columns, button);
        y += h + 8f;

        if (!string.IsNullOrEmpty(description))
        {
            GUI.Label(new Rect(x, y, w, 70f), description, body);
            y += 62f;
        }
        y += 14f;
        return result;
    }

    void OnGUI()
    {
        if (!open)
        {
            EnsureStyles();
            GUI.Label(new Rect(Screen.width - 260f, 10f, 250f, 30f), "M  Menu", new GUIStyle(hint) { alignment = TextAnchor.MiddleRight });
            return;
        }

        EnsureStyles();

        float s = Screen.height / 1080f;
        GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1f));
        float W = Screen.width / s;

        GUI.DrawTexture(new Rect(0f, 0f, W, 1080f), dim);
        Rect panel = new Rect((W - 1300f) * 0.5f, 50f, 1300f, 980f);
        GUI.DrawTexture(panel, panelTex);

        float x = panel.x + 60f;
        float w = panel.width - 120f;
        float y = panel.y + 20f;

        GUI.Label(new Rect(x, y, w, 80f), "TYRE PUSH", title);
        y += 96f;

        string[] chars = game != null ? game.CharacterNames : new[] { "X Bot" };
        character = Mathf.Clamp(Section("CHARACTER", x, ref y, w, character, chars, Mathf.Min(chars.Length, 5), null), 0, chars.Length - 1);

        path = Section("PATH", x, ref y, w, path, TyreGameSettings.PathNames, 4, TyreGameSettings.PathInfo[path]);

        DifficultyProfile dp = TyreGameSettings.Profile((TyreDifficulty)difficulty);
        difficulty = Section("DIFFICULTY", x, ref y, w, difficulty, TyreGameSettings.DifficultyNames, 3, dp.description);

        y += 6f;
        if (firstOpen)
        {
            if (GUI.Button(new Rect(x, y, w, 90f), "START", bigButton)) StartGame();
        }
        else
        {
            float half = (w - 20f) * 0.5f;
            if (GUI.Button(new Rect(x, y, half, 90f), "RESUME", bigButton)) Close();
            if (GUI.Button(new Rect(x + half + 20f, y, half, 90f), "START / RESTART", bigButton)) StartGame();
        }
        y += 100f;

        GUI.Label(new Rect(x, y, w, 30f), "Enter = start    M / Tab = open or close this menu", hint);
    }
}
