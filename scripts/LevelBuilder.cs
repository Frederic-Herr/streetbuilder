using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Linq;
using Sirenix.OdinInspector;

public class LevelBuilder : MonoBehaviour
{
    public static LevelBuilder Instance { get; private set; }

    [SerializeField] private bool buildLevelOnStart = true;
    [SerializeField] private CarBehaviour car;
    [SerializeField] private GameObject starPrefab;
    [SerializeField] private Transform levelParent;
    [SerializeField] private GameObject goalPrefab;
    [SerializeField] private GameObject destroyFXPrefab;
    [SerializeField] private int fixedPathCount = 2;
    [SerializeField] private int maxRowCount = 6;
    [SerializeField] private int maxColumnCount = 4;
    [SerializeField] private int borderTilesPerSide = 1;
    [SerializeField] private int tileWidth = 30;
    [SerializeField] private int tileHeight = 30;
    [SerializeField] private float emptyTileYPos = 0f;
    [SerializeField, Range(0f, 1f)] private float buffChancePerRow = 0.1f;
    [SerializeField] private Transform[] startWayPoints;
    [SerializeField, BoxGroup("Level length")] private int minLevelLength = 10;
    [SerializeField, BoxGroup("Level length")] private int maxLevelLength = 50;
    [SerializeField, BoxGroup("Level length")] private float levelLengthIncreasePerLevel = 0.1f;

    private int currentRowIndex;
    private Dictionary<Vector2, Tile> tiles = new Dictionary<Vector2, Tile>();
    [SerializeField, ReadOnly, BoxGroup("Level length")] private int levelLength = 100;
    bool setCar;
    private Tile lastTile;
    private float carZDistance;
    private TileSelection tileSelection;
    private List<GameObject> emptyTiles = new List<GameObject>();
    private List<GameObject> builtTiles = new List<GameObject>();
    [SerializeField, ReadOnly] private GameObject[] powerUpPrefabs;
    private int lastBuiltRowIndex;
    private PlayerController playerController;
    private GameManager gameManager;
    private Coroutine buildCoroutine;
    private List<Vector2> starsPosition = new List<Vector2>();
    private Vector2 currentStreetPos;
    private Vector2 previousStreetPos;
    private List<Vector2> path = new List<Vector2>();
    private bool stopBuilding = false;

    private void Awake()
    {
        if (Instance != null)
        {
            Destroy(this);
            return;
        }
        Instance = this;
    }

    private void OnDestroy()
    {
        if (playerController) playerController.OnTileSelected -= PlayerController_OnTileSelected;
    }

    void Start()
    {
        gameManager = GameManager.Instance;
        levelLength = gameManager.PlayingTutorial ? 3 : gameManager.scaling.levelLength;
        playerController = PlayerController.Instance;
        playerController.OnTileSelected += PlayerController_OnTileSelected;
        tileSelection = TileSelection.Instance;
        emptyTiles = tileSelection.SelectedTheme.environmentTiles.ToList().FindAll(x => x.GetComponent<Tile>().CurrentTileType == Tile.TileType.Empty);
        builtTiles = tileSelection.SelectedTheme.environmentTiles.ToList().FindAll(x => x.GetComponent<Tile>().CurrentTileType == Tile.TileType.Built);
        powerUpPrefabs = Resources.LoadAll<GameObject>("PowerUps");

        SetSeed();

        if (buildLevelOnStart)
        {
            BuildBase();
            buildCoroutine = StartCoroutine(BuildLevel());
        }
        else
        {
            GetLevel();
        }
    }

    public void SetLevelLength(int length)
    {
        levelLength = length;
    }

    /// <summary>
    /// Sets the seed for the Random class.
    /// If the game is not endless, the seed is set to the hash code of the string "Level_X",
    /// where X is the current level number.
    /// If the game is endless, the seed is set to a random number.
    /// </summary>
    private void SetSeed()
    {
        if (!gameManager.PlayingEndlessMode)
        {
            Random.InitState($"Level_{gameManager.PlayingLevel}".GetHashCode());
        }
        else
        {
            System.Random random = new System.Random();
            Random.InitState(random.Next(int.MinValue, int.MaxValue));
        }
    }

    public int GetLevelLength()
    {
        return levelLength;
    }

    /// <summary>
    /// Initializes and builds the editor level by selecting a theme, gathering empty and built tiles,
    /// and then constructing the base layout of the level. Starts the level building coroutine.
    /// </summary>
    [Button]
    public void BuildLevelEditor()
    {
        tileSelection = GameObject.FindObjectOfType<TileSelection>();
        tileSelection.SetTheme(PlayerPrefs.GetString("SelectedTheme", "Gras"));

        emptyTiles = tileSelection.SelectedTheme.environmentTiles.ToList().FindAll(x => x.GetComponent<Tile>().CurrentTileType == Tile.TileType.Empty);
        builtTiles = tileSelection.SelectedTheme.environmentTiles.ToList().FindAll(x => x.GetComponent<Tile>().CurrentTileType == Tile.TileType.Built);

        BuildBase();
        StartCoroutine(BuildLevel());
    }

    /// <summary>
    /// Clears the current level by destroying all tiles in the tiles dictionary and clearing the dictionary.
    /// Resets the currentRowIndex to 0.
    /// </summary>
    [Button]
    public void ClearLevel()
    {
        currentRowIndex = 0;

        foreach (KeyValuePair<Vector2, Tile> entry in tiles)
        {
#if UNITY_EDITOR
            DestroyImmediate(entry.Value.gameObject);
#else
            Destroy(entry.Value.gameObject);
#endif
        }

        tiles.Clear();
    }

    /// <summary>
    /// Gets all tiles in the level and assigns them a GridPosition by their order in the list.
    /// Tiles are named "Tile_X-Y", where X and Y are the coordinates of the tile in the grid.
    /// If the tile is a street tile, it is set as the last tile and the car is set up on that tile.
    /// </summary>
    private void GetLevel()
    {
        List<Tile> allTiles = levelParent.GetComponentsInChildren<Tile>().ToList();
        allTiles.RemoveAll(x => x.CurrentTileType == Tile.TileType.Border);

        if (allTiles == null || allTiles.Count <= 0) return;

        for (int i = 0; i < allTiles.Count; i++)
        {
            if (allTiles[i].CurrentTileType == Tile.TileType.Street || allTiles[i].CurrentTileType == Tile.TileType.Built || allTiles[i].CurrentTileType == Tile.TileType.Empty)
            {
                Vector2 gridPos = new Vector2(i % 5, (int)(i / 5));
                tiles.Add(gridPos, allTiles[i]);
                allTiles[i].gameObject.name = $"Tile_{gridPos.x}-{gridPos.y}";
                allTiles[i].GridPosition = gridPos;

                if (allTiles[i].CurrentTileType == Tile.TileType.Street)
                {
                    lastTile = allTiles[i];
                    car.CurrentTile = allTiles[i];
                    car.SetUpCar();
                }
            }
        }
    }

    private void PlayerController_OnTileSelected(Tile tile)
    {
        HighlightTiles(tile != null);
    }

    public GameObject GetRandomEmptyTile()
    {
        return emptyTiles[Random.Range(0, emptyTiles.Count)];
    }

    /// <summary>
    /// Builds the level row by row. If the game is in endless mode, a new row is created
    /// whenever the car has moved a distance of tileHeight on the z-axis. If the game is
    /// not in endless mode, a new row is created whenever the car has moved past the
    /// current row. The level is built in a coroutine.
    /// </summary>
    private IEnumerator BuildLevel()
    {
        float previousZPos = car.transform.position.z;

        while (true)
        {
            carZDistance += car.transform.position.z - previousZPos;
            previousZPos = car.transform.position.z;

            if (gameManager.PlayingEndlessMode)
            {
                if (carZDistance >= tileHeight)
                {
                    CreateNewRow();
                    carZDistance = 0f;
                }
            }
            else
            {
                if (currentRowIndex <= levelLength)
                {
                    CreateNewRow();
                    carZDistance = 0f;
                }
            }
            yield return null;
        }
    }

    /// <summary>
    /// Builds the goal at the end of the level. The goal is a game object
    /// that is instantiated at the end of the level. The goal is positioned
    /// at the end of the level and rotated to face the player.
    /// </summary>
    private void BuildGoal()
    {
        GameObject goalObj = Instantiate(goalPrefab, levelParent);
        Vector3 goalPos = new Vector3(0f, 0f, (currentRowIndex - 1) * tileWidth + 35f);
        goalObj.transform.position = goalPos;
        goalObj.transform.eulerAngles = new Vector3(0f, 90f, 0f);

        LevelProgress.Instance.SetEndPoint(new Vector3(0f, 0f, (currentRowIndex - 1) * tileWidth));
    }

    public void SetTile(Vector2 gridPosition, Tile tile)
    {
        tiles[gridPosition] = tile;
    }

    /// <summary>
    /// Builds the base of the level. If the game is not in endless mode,
    /// the fixed path is created and the stars are set. The tiles dictionary
    /// is cleared and the power up prefabs are loaded. The current row index
    /// is set to 0 and the base rows are built.
    /// </summary>
    [Button]
    public void BuildBase()
    {
        if (!gameManager.PlayingEndlessMode)
        {
            for (int i = 0; i < fixedPathCount; i++)
            {
                CreatePath();
            }

            SetStars();
        }

        powerUpPrefabs = Resources.LoadAll<GameObject>("PowerUps");
        currentRowIndex = 0;

        foreach (KeyValuePair<Vector2, Tile> entry in tiles)
        {
#if UNITY_EDITOR
            DestroyImmediate(entry.Value.gameObject);
#else
            Destroy(entry.Value.gameObject);
#endif
        }

        tiles.Clear();

        for (int i = 0; i < maxRowCount; i++)
        {
            CreateNewRow();
        }
    }

    /// <summary>
    /// Creates a path for the level. The path is created by adding
    /// points to the path until the end of the level is reached.
    /// The points are added by calling the AddPointToPath function
    /// which randomly selects a direction and moves the current
    /// position in that direction.
    /// </summary>
    private void CreatePath()
    {
        currentStreetPos = new Vector2(Random.Range(0, maxColumnCount), 0f);

        while (currentStreetPos.y <= levelLength)
        {
            AddPointToPath();
        }
    }

    /// <summary>
    /// Sets the positions of the three stars in the level.
    /// The stars are placed at 20%, 50% and 80% of the level length.
    /// The star is placed on the first grid position in the path
    /// that is at the given height.
    /// </summary>
    private void SetStars()
    {
        bool starOne = false;
        bool starTwo = false;
        bool starThree = false;

        for (int i = 0; i < path.Count; i++)
        {
            if (!starOne && path[i].y == (int)(levelLength * 0.2f))
            {
                for (int j = 0; j < maxColumnCount; j++)
                {
                    if (path.Contains(new Vector2(j, path[i].y)))
                    {
                        starsPosition.Add(path[i]);
                        starOne = true;
                        break;
                    }
                }
            }
            else if (!starTwo && path[i].y == (int)(levelLength * 0.5f))
            {
                for (int j = 0; j < maxColumnCount; j++)
                {
                    if (path.Contains(new Vector2(j, path[i].y)))
                    {
                        starsPosition.Add(path[i]);
                        starTwo = true;
                        break;
                    }
                }
            }
            else if (!starThree && path[i].y == (int)(levelLength * 0.8f))
            {
                for (int j = 0; j < maxColumnCount; j++)
                {
                    if (path.Contains(new Vector2(j, path[i].y)))
                    {
                        starsPosition.Add(path[i]);
                        starThree = true;
                        break;
                    }
                }
            }
        }
    }

    /// <summary>
    /// Adds a point to the path of the level. The point is selected
    /// by randomly choosing a direction and moving the current position
    /// in that direction. The function also ensures that the point is
    /// not already in the path and that the point is within the bounds
    /// of the level.
    /// </summary>
    private void AddPointToPath()
    {
        int rndDirection = Random.Range(0, 2);
        if (rndDirection == 0)
        {
            if (currentStreetPos.x == 0)
            {
                if (path.Any(x => x.x == 1 && x.y == currentStreetPos.y))
                {
                    currentStreetPos.y++;
                }
                else
                {
                    currentStreetPos.x++;
                }
            }
            else if (currentStreetPos.x == maxColumnCount - 1)
            {
                if (path.Any(x => x.x == maxColumnCount - 2 && x.y == currentStreetPos.y))
                {
                    currentStreetPos.y++;
                }
                else
                {
                    currentStreetPos.x--;
                }
            }
            else
            {
                if (path.Contains(new Vector2(currentStreetPos.x - 1, currentStreetPos.y)))
                {
                    currentStreetPos.x++;
                }
                else if (path.Contains(new Vector2(currentStreetPos.x + 1, currentStreetPos.y)))
                {
                    currentStreetPos.x--;
                }
                else
                {
                    bool left = Random.Range(0, 2) == 0;
                    currentStreetPos.x += left ? -1 : 1;
                }
            }
        }
        else
        {
            currentStreetPos.y++;
        }

        currentStreetPos.x = Mathf.Clamp(currentStreetPos.x, 0, maxColumnCount);
        previousStreetPos = currentStreetPos;

        path.Add(currentStreetPos);
    }

    /// <summary>
    /// Creates a new row of tiles in the level. This is done by iterating over the column count and creating a new tile for each column.
    /// For each tile, it checks if the tile is a border tile and if the tile is a street tile. If the tile is a street tile, it sets the car
    /// to the current tile and sets up the car. If the tile is a border tile, it creates a new border tile.
    /// It also checks if the current row index is greater than or equal to the level length and if so, it builds the goal and stops building.
    /// It also creates a new build coroutine and starts it.
    /// </summary>
    private void CreateNewRow()
    {
        if (stopBuilding) return;

        if (gameManager.PlayingEndlessMode)
        {
            AddPointToPath();
        }

        for (int i = 0; i < borderTilesPerSide; i++)
        {
            Vector3 positionLeft = new Vector3((i + 1) * -tileWidth + levelParent.position.x, -0.01f, currentRowIndex * tileHeight + levelParent.position.z);
            GameObject borderObjectLeft = Instantiate(tileSelection.SelectedTheme.borderTiles[Random.Range(0, tileSelection.SelectedTheme.borderTiles.Length)], positionLeft, Quaternion.identity, levelParent);
            borderObjectLeft.name = "Border Object Left " + currentRowIndex;
            borderObjectLeft.GetComponent<Tile>().CurrentTileType = Tile.TileType.Border;

            Vector3 positionRight = new Vector3((maxColumnCount + i) * tileWidth + levelParent.position.x, -0.01f, currentRowIndex * tileHeight + levelParent.position.z);
            GameObject borderObjectRight = Instantiate(tileSelection.SelectedTheme.borderTiles[Random.Range(0, tileSelection.SelectedTheme.borderTiles.Length)], positionRight, Quaternion.identity, levelParent);
            borderObjectRight.name = "Border Object Right " + currentRowIndex;
            borderObjectRight.GetComponent<Tile>().CurrentTileType = Tile.TileType.Border;
        }

        int rndStartIndex = 0;
        int rndBuildAmount = 0;
        List<int> buildIndeces = new List<int>();
        List<int> availableIndeces = new List<int>();
        List<Vector2> availableBuffPlaces = new List<Vector2>();
        availableBuffPlaces.AddRange(path);
        starsPosition.ForEach(x => availableBuffPlaces.Remove(x));
        float rndBuffChance = Random.Range(0f, 1f);
        bool createBuff = false;
        int rndBuffPlaceIndex = 0;

        for (int i = 0; i < maxColumnCount; i++)
        {
            if (!path.Any(x => x.x == i && x.y == currentRowIndex))
            {
                availableIndeces.Add(i);
            }
        }

        if (currentRowIndex == 0)
        {
            rndStartIndex = 2;
        }

        if (currentRowIndex > 3)
        {
            for (int i = 0; i < maxColumnCount; i++)
            {
                float buildChance = Random.Range(0f, 1f);
                if (gameManager.scaling.obstacleChance >= buildChance) rndBuildAmount++;

            }
            rndBuildAmount = Mathf.Clamp(rndBuildAmount, 0, availableIndeces.Count);

            if (rndBuildAmount > 0)
            {
                lastBuiltRowIndex = currentRowIndex;
            }

            for (int i = 0; i < rndBuildAmount; i++)
            {
                int rndIndex = Random.Range(0, availableIndeces.Count);
                buildIndeces.Add(availableIndeces[rndIndex]);
                availableIndeces.Remove(availableIndeces[rndIndex]);
            }
        }

        if (currentRowIndex > 1 && rndBuffChance <= buffChancePerRow)
        {
            Vector2 firstPlace = availableBuffPlaces.FirstOrDefault(x => x.y == currentRowIndex);

            if (firstPlace != Vector2.zero)
            {
                rndBuffPlaceIndex = (int)firstPlace.x;
                createBuff = true;
            }
        }

        if (!gameManager.PlayingEndlessMode && currentRowIndex >= levelLength)
        {
            BuildGoal();
            createBuff = false;
            buildIndeces.Clear();
            stopBuilding = true;
        }

        for (int i = 0; i < maxColumnCount; i++)
        {
            GameObject objectToCreate = null;
            if (buildIndeces.Contains(i))
            {
                buildIndeces.Remove(i);
                objectToCreate = builtTiles[Random.Range(0, builtTiles.Count)];
            }
            else
            {
                objectToCreate = emptyTiles[Random.Range(0, emptyTiles.Count)];
            }

            if (i == rndStartIndex && currentRowIndex == 0)
            {
                setCar = true;
                List<GameObject> availableStartTiles = new List<GameObject>();
                availableStartTiles.AddRange(tileSelection.SelectedTheme.streetTiles.
                    Where(x => x.GetComponent<Tile>().GetDirections().HasFlag(Tile.Direction.Bottom) &&
                    x.GetComponent<Tile>().GetDirections().HasFlag(Tile.Direction.Top)));

                objectToCreate = availableStartTiles[Random.Range(0, availableStartTiles.Count)];
            }

            Tile tile = CreateTileAtPosition(objectToCreate, new Vector2(i, currentRowIndex));

            if (starsPosition.Contains(new Vector2(i, currentRowIndex)))
            {
                GameObject starObj = Instantiate(starPrefab);
                tile.RelatedPowerUp = starObj.GetComponent<PowerUpObject>();
            }

            if (createBuff && i == rndBuffPlaceIndex)
            {
                GameObject buffObj = Instantiate(powerUpPrefabs[Random.Range(0, powerUpPrefabs.Length)]);
                tile.RelatedPowerUp = buffObj.GetComponent<PowerUpObject>();
            }

            if (!tile)
            {
                Debug.LogError(tile.gameObject.name + " is missing Tile Script");
                continue;
            }

            if (setCar)
            {
                currentStreetPos = tile.GridPosition;
                previousStreetPos = currentStreetPos;
                tile.CurrentTileType = Tile.TileType.Street;
                lastTile = tile;
                setCar = false;
                car.CurrentTile = tile;
                car.SetUpCar();
            }
        }

        currentRowIndex++;

        if (stopBuilding && buildCoroutine != null) StopCoroutine(buildCoroutine);
    }

    private void HighlightTiles(bool highlight)
    {
        if (tiles == null || tiles.Count <= 0) return;

        foreach (KeyValuePair<Vector2, Tile> tile in tiles)
        {
            if (tile.Value.CurrentTileType == Tile.TileType.Empty || tile.Value.CurrentTileType == Tile.TileType.Street)
            {
                tile.Value.HighlightTile(highlight);
            }
        }
    }

    public void DestroyTile(Vector2 gridPosition)
    {
        GameObject bombObj = Instantiate(destroyFXPrefab, tiles[gridPosition].gameObject.transform.position + new Vector3(0f, 10f, 0f), Quaternion.identity);
        Destroy(bombObj, 5f);

        Destroy(tiles[gridPosition].gameObject);
        tiles[gridPosition] = null;
    }

    /// <summary>
    /// Creates a new tile at the given grid position.
    /// </summary>
    /// <param name="tilePrefab">Prefab to instantiate as the tile.</param>
    /// <param name="gridPosition">Grid position to place the tile at.</param>
    /// <returns>The newly created tile.</returns>
    public Tile CreateTileAtPosition(GameObject tilePrefab, Vector2 gridPosition)
    {
        Vector3 position = new Vector3(gridPosition.x * tileWidth + levelParent.position.x, -0.01f, gridPosition.y * tileHeight + levelParent.position.z);
        GameObject emptyTileObj = Instantiate(tilePrefab, position, Quaternion.identity, levelParent);
        emptyTileObj.name = $"Tile_{gridPosition.x}_{gridPosition.y}";
        Tile tile = emptyTileObj.GetComponent<Tile>();

        if (tile.CurrentTileType != Tile.TileType.Selection && tile.hasRandomRotation)
        {
            int rndRotationIndex = Random.Range(0, 4);
            emptyTileObj.transform.eulerAngles = new Vector3(0f, rndRotationIndex * 90f, 0f);
        }

        if (tile.CurrentTileType == Tile.TileType.Empty)
        {
            emptyTileObj.transform.position = new Vector3(emptyTileObj.transform.position.x, emptyTileYPos, emptyTileObj.transform.position.z);
        }

        tile.GridPosition = gridPosition;

        if (tiles.ContainsKey(tile.GridPosition))
        {
            tiles[tile.GridPosition] = tile;
        }
        else
        {
            tiles.Add(tile.GridPosition, tile);
        }

        return emptyTileObj.GetComponent<Tile>();
    }

    /// <summary>
    /// Determines if the next tile in a given direction is buildable.
    /// Checks if the tile in the specified direction is either empty or a street tile.
    /// </summary>
    public bool NextTileIsBuildable(Tile currentTile)
    {
        Vector2 currentPos = currentTile.GridPosition;
        Tile.Direction originDirection = car.GetOriginDirection();

        if (currentTile.GetDirections().HasFlag(Tile.Direction.Top) && originDirection != Tile.Direction.Top)
        {
            Vector2 topDirection = new Vector2(0f, 1f);
            if (tiles.ContainsKey(currentPos + topDirection))
            {
                return tiles[currentPos + topDirection].CurrentTileType == Tile.TileType.Empty
                    || tiles[currentPos + topDirection].CurrentTileType == Tile.TileType.Street;
            }
        }

        if (currentTile.GetDirections().HasFlag(Tile.Direction.Right) && originDirection != Tile.Direction.Right)
        {
            Vector2 rightDirection = new Vector2(1f, 0f);
            if (tiles.ContainsKey(currentPos + rightDirection))
            {
                return tiles[currentPos + rightDirection].CurrentTileType == Tile.TileType.Empty
                     || tiles[currentPos + rightDirection].CurrentTileType == Tile.TileType.Street;
            }
        }

        if (currentTile.GetDirections().HasFlag(Tile.Direction.Left) && originDirection != Tile.Direction.Left)
        {
            Vector2 leftDirection = new Vector2(-1f, 0f);
            if (tiles.ContainsKey(currentPos + leftDirection))
            {
                return tiles[currentPos + leftDirection].CurrentTileType == Tile.TileType.Empty
                     || tiles[currentPos + leftDirection].CurrentTileType == Tile.TileType.Street;
            }
        }

        if (currentTile.GetDirections().HasFlag(Tile.Direction.Bottom) && originDirection != Tile.Direction.Bottom)
        {
            Vector2 bottomDirection = new Vector2(0f, -1f);
            if (tiles.ContainsKey(currentPos + bottomDirection))
            {
                return tiles[currentPos + bottomDirection].CurrentTileType == Tile.TileType.Empty
                     || tiles[currentPos + bottomDirection].CurrentTileType == Tile.TileType.Street;
            }
        }

        return false;
    }

    /// <summary>
    /// Checks if the next tile in the car's direction is a valid tile to move to.
    /// The tile is valid if it has a connection in the direction the car is moving from.
    /// </summary>
    /// <param name="currentTile">The current tile the car is on.</param>
    /// <param name="tile">The next tile the car will move to if it is valid.</param>
    /// <returns>True if the next tile is valid, false otherwise.</returns>
    public bool NextTileIsValid(Tile currentTile, out Tile tile)
    {
        Vector2 currentPos = currentTile.GridPosition;
        Tile.Direction originDirection = car.GetOriginDirection();
        tile = null;

        if (currentTile.GetDirections().HasFlag(Tile.Direction.Top) && originDirection != Tile.Direction.Top)
        {
            Vector2 topDirection = new Vector2(0f, 1f);
            if (tiles.ContainsKey(currentPos + topDirection))
            {
                tile = tiles[currentPos + topDirection];
                car.SetOriginDirection(Tile.Direction.Bottom);
                return tiles[currentPos + topDirection].GetDirections().HasFlag(Tile.Direction.Bottom);
            }
        }

        if (currentTile.GetDirections().HasFlag(Tile.Direction.Right) && originDirection != Tile.Direction.Right)
        {
            Vector2 rightDirection = new Vector2(1f, 0f);
            if (tiles.ContainsKey(currentPos + rightDirection))
            {
                tile = tiles[currentPos + rightDirection];
                car.SetOriginDirection(Tile.Direction.Left);
                return tiles[currentPos + rightDirection].GetDirections().HasFlag(Tile.Direction.Left);
            }
        }

        if (currentTile.GetDirections().HasFlag(Tile.Direction.Left) && originDirection != Tile.Direction.Left)
        {
            Vector2 leftDirection = new Vector2(-1f, 0f);
            if (tiles.ContainsKey(currentPos + leftDirection))
            {
                tile = tiles[currentPos + leftDirection];
                car.SetOriginDirection(Tile.Direction.Right);
                return tiles[currentPos + leftDirection].GetDirections().HasFlag(Tile.Direction.Right);
            }
        }

        if (currentTile.GetDirections().HasFlag(Tile.Direction.Bottom) && originDirection != Tile.Direction.Bottom)
        {
            Vector2 bottomDirection = new Vector2(0f, -1f);
            if (tiles.ContainsKey(currentPos + bottomDirection))
            {
                tile = tiles[currentPos + bottomDirection];
                car.SetOriginDirection(Tile.Direction.Top);
                return tiles[currentPos + bottomDirection].GetDirections().HasFlag(Tile.Direction.Top);
            }
        }

        return false;
    }

    /// <summary>
    /// Returns a list of all neighbour tiles of the given tile.
    /// </summary>
    /// <param name="tile">The tile to get the neighbours of.</param>
    /// <returns>A list of all neighbour tiles of the given tile.</returns>
    public List<Tile> GetNeighbours(Tile tile)
    {
        List<Tile> neighbours = new List<Tile>();
        Vector2 currentPos = tile.GridPosition;

        if (tiles.ContainsKey(currentPos + new Vector2(1, 0)))
        {
            neighbours.Add(tiles[currentPos + new Vector2(1, 0)]);
        }
        if (tiles.ContainsKey(currentPos + new Vector2(-1, 0)))
        {
            neighbours.Add(tiles[currentPos + new Vector2(-1, 0)]);
        }
        if (tiles.ContainsKey(currentPos + new Vector2(0, 1)))
        {
            neighbours.Add(tiles[currentPos + new Vector2(0, 1)]);
        }
        if (tiles.ContainsKey(currentPos + new Vector2(0, -1)))
        {
            neighbours.Add(tiles[currentPos + new Vector2(0, -1)]);
        }

        return neighbours;
    }
}
