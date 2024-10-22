using NaughtyAttributes;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.Events;

public class TileSelection : MonoBehaviour
{
    public static TileSelection Instance { get; private set; }

    [System.Serializable]
    public class TileTheme
    {
        public string themeName;
        public GameObject[] streetTiles;
        public GameObject[] environmentTiles;
        public GameObject[] borderTiles;
        public GameObject[] starTiles;

        public TileTheme(string tileThemeName)
        {
            themeName = tileThemeName;
        }
    }

    [SerializeField] private GameObject[] fakeRerollTiles;
    [SerializeField] private bool useSeed;
    [SerializeField] private bool autoReroll = true;
    [SerializeField, ShowIf(nameof(autoReroll))] private float rerollTime = 3f;
    [SerializeField] private GameObject autoRerollObject;
    [SerializeField] private GameObject rerollButtonObject;
    [SerializeField] private bool createSelectionOnStart = true;
    [SerializeField] private bool createSelectionOnUse = true;
    [SerializeField] private int maxSelectionTiles = 4;
    [SerializeField] private int maxEqualTiles = 2;
    [SerializeField] private float tileGrowSpeed = 2f;
    [SerializeField] private float rerollCooldown = 10f;
    [SerializeField] private Transform tileParent;
    [SerializeField] private Vector3 offset;
    [SerializeField] private LayerMask backgroundMask;
    [SerializeField] private SpriteRenderer backgroundRenderer;
    [SerializeField] private UnityEvent<float> OnRerollCooldownProgressChanged;
    public UnityEvent<Tile> OnTilePlaced;

    [SerializeField, ReadOnly] private List<TileTheme> tileThemes = new List<TileTheme>();
    private float currentRerollCooldown;

    private TileTheme selectedTheme;
    public TileTheme SelectedTheme
    {
        get => selectedTheme;
        set
        {
            if (selectedTheme == value) return;

            selectedTheme = value;

            if (selectedTheme != null)
            {
                PlayerPrefs.SetString("SelectedTheme", selectedTheme.themeName);
            }
        }
    }

    private Dictionary<int, GameObject> selectionSlots = new Dictionary<int, GameObject>();
    private PlayerController playerController;
    private float backgroundWidth;
    private float tileDistance;
    private bool selectionEnabled = true;
    private System.Random rand;

    /// <summary>
    /// Ensures only one instance of TileSelection is created.
    /// Sets the theme based on the selected theme name.
    /// </summary>
    private void Awake()
    {
        if (Instance != null)
        {
            Destroy(this);
            return;
        }
        Instance = this;

        rand = new System.Random();
        SetTheme(PlayerPrefs.GetString("SelectedTheme", "Gras"));

        autoRerollObject.SetActive(autoReroll);
    }

    /// <summary>
    /// Sets up the tile selection by setting the maximum equal tiles in selection based on the game mode,
    /// setting the selection position, and creating the selection slots.
    /// If auto reroll is enabled, sets the current reroll cooldown to 0 and sets the reroll cooldown to the reroll time.
    /// If create selection on start is enabled, adds a tile to the selection for each selection slot.
    /// </summary>
    void Start()
    {
        maxEqualTiles = GameManager.Instance.scaling.maxEqualTilesInSelection;
        SetSelectionPosition();
        currentRerollCooldown = rerollCooldown;
        playerController = PlayerController.Instance;

        if (autoReroll)
        {
            currentRerollCooldown = 0f;
            rerollCooldown = rerollTime;
        }

        for (int i = 0; i < maxSelectionTiles; i++)
        {
            selectionSlots.Add(i, null);
            if (createSelectionOnStart) AddTileToSelection();
        }
    }

    private void Update()
    {
        HandleRerollCooldown();
    }

    /// <summary>
    /// Handles the reroll cooldown. If the cooldown is not finished, increments the current reroll cooldown
    /// and calls OnRerollCooldownProgressChanged with the progress. If the cooldown is finished and auto reroll
    /// is enabled, calls RerollSelection.
    /// </summary>
    private void HandleRerollCooldown()
    {
        if (currentRerollCooldown < rerollCooldown)
        {
            currentRerollCooldown += Time.deltaTime;
            OnRerollCooldownProgressChanged?.Invoke(currentRerollCooldown / rerollCooldown);
        }
        else
        {
            if (autoReroll)
            {
                RerollSelection();
            }
        }

    }

    private IEnumerator HandleAutoReroll()
    {
        while (true)
        {
            yield return new WaitForSeconds(rerollTime);

            RerollSelection(true);
        }
    }

    public Vector3 GetSlotPos(int slot)
    {
        if (selectionSlots[slot])
        {
            return selectionSlots[slot].transform.position;
        }
        else
        {
            return Vector3.zero;
        }
    }

    public void EnableSlot(int slotIndex)
    {
        selectionSlots[slotIndex]?.GetComponent<Tile>().EnableCollider(true);
    }

    public void HighlightSlot(int index)
    {
        selectionSlots[index]?.GetComponent<Tile>().ShowBuildTilePlaceHighlight(true);
    }

    public void StopHighlightSlot(int index)
    {
        selectionSlots[index]?.GetComponent<Tile>().ShowBuildTilePlaceHighlight(false);
    }

    public void DisableSlot(int slotIndex)
    {
        selectionSlots[slotIndex]?.GetComponent<Tile>().EnableCollider(false);
    }

    public void EnableSelection()
    {
        selectionEnabled = true;

        foreach (var item in selectionSlots)
        {
            item.Value?.GetComponent<Tile>().EnableCollider(true);
        }
    }

    public void DisableSelection()
    {
        selectionEnabled = false;

        foreach (var item in selectionSlots)
        {
            item.Value?.GetComponent<Tile>().EnableCollider(false);
        }
    }

    public float GetGrowSpeed()
    {
        return tileGrowSpeed;
    }

    public void CallTilePlaced(Tile tile)
    {
        OnTilePlaced?.Invoke(tile);
    }

    /// <summary>
    /// Adds a tile to the tile selection at the first available slot.
    /// If a selectionTile is provided, it will be instantiated and added to the selection.
    /// Otherwise, a random tile from the current theme will be chosen.
    /// </summary>
    /// <param name="selectionTile">Optional tile to add to the selection. If null, a random tile from the current theme will be used.</param>
    public void AddTileToSelection(GameObject selectionTile = null)
    {
        int targetSlot = 0;

        for (int i = 0; i < selectionSlots.Count; i++)
        {
            if (!selectionSlots[i])
            {
                targetSlot = i;
            }
        }

        GameObject tileObj = Instantiate(selectionTile ? selectionTile : GetRandomTile(), Vector3.zero, Quaternion.identity, tileParent);
        tileObj.transform.localPosition = new Vector3(tileDistance * targetSlot + offset.x, 1f, 0f);
        Tile tile = tileObj.GetComponent<Tile>();
        tile.selectionIndex = targetSlot;
        tile.CurrentTileType = Tile.TileType.Selection;
        tile.GrowTile();
        selectionSlots[targetSlot] = tileObj;
    }

    public void ActivateCreatSelectionOnUse(bool activate)
    {
        createSelectionOnUse = activate;
    }

    /// <summary>
    /// Removes a tile from the tile selection at the given index.
    /// If destroy is true, the tile will be destroyed.
    /// If the tile is the currently selected tile, it will be set to null.
    /// If createSelectionOnUse is true, a new tile will be added to the selection to fill the gap.
    /// </summary>
    /// <param name="index">Index of the tile to remove.</param>
    /// <param name="destroy">If true, the tile will be destroyed.</param>
    public void RemoveTileFromSelection(int index, bool destroy = false)
    {
        if (playerController.CurrentSelectedTile == selectionSlots[index])
        {
            playerController.CurrentSelectedTile = null;
        }

        if (destroy)
        {
            Destroy(selectionSlots[index]);
        }
        selectionSlots[index] = null;
        if (createSelectionOnUse) AddTileToSelection();
    }

    /// <summary>
    /// Rerolls the current tile selection. If ignoreCooldown is false, it will check if the current reroll cooldown is less than the reroll cooldown, and if so, it will not reroll the selection.
    /// It will then remove all tiles from the selection and add new ones to fill the gap. If createSelectionOnUse is true, it will add new tiles to the selection. If ignoreCooldown is false, it will reset the current reroll cooldown to 0.
    /// </summary>
    /// <param name="ignoreCooldown">If true, the current reroll cooldown will be ignored and the selection will be rerolled anyway.</param>
    public void RerollSelection(bool ignoreCooldown = false)
    {
        if (!ignoreCooldown && currentRerollCooldown < rerollCooldown) return;

        for (int i = 0; i < selectionSlots.Count; i++)
        {
            RemoveTileFromSelection(i, true);
        }

        if (!createSelectionOnUse)
        {
            for (int i = 0; i < selectionSlots.Count; i++)
            {
                AddTileToSelection();
            }
        }

        if (!ignoreCooldown) currentRerollCooldown = 0f;
    }

    /// <summary>
    /// Randomly selects a street tile from the available tiles in the theme. If no street tiles are available, returns null.
    /// Checks if the selected tile already exists in the selection slots and limits the number of equal tiles to a specified maximum.
    /// </summary>
    private GameObject GetRandomTile()
    {
        if (SelectedTheme.streetTiles.Length <= 0)
        {
            Debug.LogError($"Theme: {SelectedTheme.themeName} has no Tiles");
            return null;
        }

        List<GameObject> availableTiles = new List<GameObject>();

        for (int i = 0; i < SelectedTheme.streetTiles.Length; i++)
        {
            bool alreadyExists = false;
            int equalCount = 0;

            for (int j = 0; j < selectionSlots.Count; j++)
            {
                if (!selectionSlots[j]) continue;

                if (selectionSlots[j].name.Contains(SelectedTheme.streetTiles[i].name))
                {
                    equalCount++;

                    if (equalCount == maxEqualTiles)
                    {
                        alreadyExists = true;
                    }
                }
            }

            if (!alreadyExists) availableTiles.Add(SelectedTheme.streetTiles[i]);
        }

        int rndIndex = 0;
        if (useSeed)
        {
            rndIndex = Random.Range(0, availableTiles.Count);
        }
        else
        {
            rndIndex = rand.Next(0, availableTiles.Count);
        }
        return availableTiles[rndIndex];
    }

    /// <summary>
    /// Sets the current tile theme to the theme with the given name.
    /// If no theme with the given name is found, a debug error will be logged.
    /// </summary>
    /// <param name="themeName">Name of the theme to select.</param>
    public void SetTheme(string themeName)
    {
        for (int i = 0; i < tileThemes.Count; i++)
        {
            if (tileThemes[i].themeName == themeName)
            {
                SelectedTheme = tileThemes[i];
                return;
            }
        }

        Debug.LogError($"No Theme with the Name: {themeName} found");
    }

    [Button]
    public void SetSelectionPosition()
    {
        transform.localPosition = new Vector3(transform.localPosition.x, transform.localPosition.y, (Camera.main.orthographicSize - 24f) * -1f);
        SetSelectionBackground();
    }

    /// <summary>
    /// Sets the size of the background renderer of the tile selection to match the viewport size
    /// and positions the tile parent to be centered on the screen. The background width is stored
    /// in the backgroundWidth variable and the tile distance between each tile is calculated and
    /// stored in the tileDistance variable. The BoxCollider of the background renderer is also updated
    /// to match the new size of the background renderer.
    /// </summary>
    private void SetSelectionBackground()
    {
        Ray rayLeft = Camera.main.ViewportPointToRay(new Vector3(0f, 0f, 0f));
        Ray rayRight = Camera.main.ViewportPointToRay(new Vector3(1f, 0f, 0f));

        backgroundRenderer.transform.localScale = new Vector3(1f, backgroundRenderer.transform.localScale.y, backgroundRenderer.transform.localScale.z);
        backgroundRenderer.size = new Vector2(Vector3.Distance(rayLeft.origin, rayRight.origin), backgroundRenderer.size.y);

        BoxCollider boxCollider = backgroundRenderer.GetComponent<BoxCollider>();
        boxCollider.size = new Vector3(backgroundRenderer.size.x, backgroundRenderer.size.y, boxCollider.size.z);

        backgroundWidth = backgroundRenderer.size.x;
        tileDistance = backgroundWidth / maxSelectionTiles;
        tileParent.localPosition = new Vector3(tileParent.InverseTransformPoint(rayLeft.origin).x * 0.5f, 0f, 0f);
    }

    /// <summary>
    /// Loads all themes from the Resources/Themes folder and stores them in the tileThemes list.
    /// The themes are loaded by getting the directories in the Resources/Themes folder.
    /// For each directory, the folder name is used as the theme name and the contents of the
    /// StreetTiles, EnvironmentTiles, BorderTiles and StarTiles folders are loaded as GameObjects
    /// and stored in the corresponding fields of the TileTheme class.
    /// </summary>
    [Sirenix.OdinInspector.Button]
    public void LoadThemes()
    {
        tileThemes.Clear();

        string themesBasePath = Application.dataPath + "/Resources/Themes";

        string[] themesPath = Directory.GetDirectories(themesBasePath);

        for (int i = 0; i < themesPath.Length; i++)
        {
            themesPath[i] = themesPath[i].Replace("\\", "/");
            themesPath[i] = themesPath[i].Replace(Application.dataPath + "/Resources/", "");

            TileTheme tileTheme = new TileTheme(themesPath[i].Replace("Themes/", ""));
            string streetTilesPath = themesPath[i] + "/StreetTiles";
            string environmentTilesPath = themesPath[i] + "/EnvironmentTiles";
            string borderTilesPath = themesPath[i] + "/BorderTiles";
            string starTilesPath = themesPath[i] + "/StarTiles";
            tileTheme.streetTiles = Resources.LoadAll(streetTilesPath, typeof(GameObject)).Cast<GameObject>().ToArray();
            tileTheme.environmentTiles = Resources.LoadAll(environmentTilesPath, typeof(GameObject)).Cast<GameObject>().ToArray();
            tileTheme.borderTiles = Resources.LoadAll(borderTilesPath, typeof(GameObject)).Cast<GameObject>().ToArray();
            tileTheme.starTiles = Resources.LoadAll(starTilesPath, typeof(GameObject)).Cast<GameObject>().ToArray();

            tileThemes.Add(tileTheme);
        }
    }
}
