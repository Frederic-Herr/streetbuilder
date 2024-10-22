using NaughtyAttributes;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;
using System;

public class PlayerController : MonoBehaviour
{
    public static PlayerController Instance { get; private set; }

    public enum CameraType
    {
        Orthographic,
        Perspective
    }

    [SerializeField] private bool enableColliderOnPlace = true;
    [SerializeField] private CarBehaviour car;
    [SerializeField] private AudioPlayer audioPlayer;
    [SerializeField] private GameObject buildFXPrefab;
    [SerializeField] private GameObject buildPreviewPrefab;
    [SerializeField] private TextMeshProUGUI timerText;
    [SerializeField] private GameObject timerTextObj;
    [SerializeField] private float cameraFollowSpeed = 5f;
    [SerializeField] private float dragHeight = 10f;
    [SerializeField] private float zDistanceToCar;
    [SerializeField] private Camera orthographicCamera;
    [SerializeField] private Camera persprectiveCamera;
    [SerializeField] private Image deleteImage;
    [SerializeField] private float deletionTime = 1f;
    [SerializeField] private CameraType cameraType;
    [SerializeField] private LayerMask tileLayerMask;
    [SerializeField] private GameObject powerUpSlotPrefab;
    [SerializeField] private RectTransform powerUpSlotParent;
    [SerializeField] private PowerUp[] powerUps;

    private PowerUp currentPowerUp;
    public PowerUp CurrentPowerUp
    {
        get => currentPowerUp;
        set
        {
            currentPowerUp = value;

            if (powerUpObj)
            {
                Destroy(powerUpObj);
            }

            if (currentPowerUp)
            {
                currentPowerUp.OnPowerUpUsed += CurrentPowerUp_OnPowerUpUsed;
                currentPowerUp.OnCollect(car.CurrentTile.transform.position);
            }

            if (currentPowerUp && currentPowerUp.powerUpSlotPrefab)
            {
                powerUpObj = currentPowerUp.powerUpObj;
                powerUpObj.GetComponent<RectTransform>().anchoredPosition = Vector2.zero;
            }
            else if (currentPowerUp)
            {
                powerUpObj = Instantiate(powerUpSlotPrefab, powerUpSlotParent);
                powerUpObj.GetComponent<RectTransform>().anchoredPosition = Vector2.zero;
            }
        }
    }

    private GameObject powerUpObj;

    private Tile currentHoveredTile;
    public Tile CurrentHoveredTile
    {
        get => currentHoveredTile;
        set
        {
            if (value == currentHoveredTile) return;

            currentHoveredTile = value;
        }
    }

    private Tile currentSelectedTile;
    public Tile CurrentSelectedTile
    {
        get => currentSelectedTile;
        set
        {
            if (value == currentSelectedTile) return;

            if (currentSelectedTile)
            {
                if (enableColliderOnPlace) currentSelectedTile.EnableCollider(true);
                if (previewObject) Destroy(previewObject);
            }

            currentSelectedTile = value;

            OnTileSelected?.Invoke(currentSelectedTile);

            if (currentSelectedTile)
            {
                startSelectedTilePosition = CurrentSelectedTile.transform.localPosition;
                previewObject = Instantiate(buildPreviewPrefab, currentSelectedTile.gameObject.transform);
                previewObject.transform.localPosition = new Vector3(0, 6.5f, 0f);

                if (cam.orthographic)
                {
                    previousMousePos = cam.ScreenToWorldPoint(Input.mousePosition);
                }
                else
                {
                    previousMousePos = cam.ScreenToWorldPoint(new Vector3(-Input.mousePosition.x, -Input.mousePosition.y, cam.transform.position.z));
                }
                currentSelectedTile.EnableCollider(false);
            }
        }
    }

    private Vector3 previousMousePos;
    private Vector3 startSelectedTilePosition;
    private LevelBuilder levelBuilder;
    private Camera cam;
    private Coroutine deleteCoroutine;
    private GameManager gameManager;
    private float currentTimer;
    private bool runTimer;
    private bool timeUnscaled;
    private float timerTime;
    private bool mouseWasDown;
    private GameObject previewObject;

    private bool inDestroyerMode;
    public bool InDestroyerMode
    {
        get => inDestroyerMode;
        set
        {
            inDestroyerMode = value;
        }
    }

    public delegate void OnTileDestroyedDelegate();
    public event OnTileDestroyedDelegate OnTileDestroyed;

    public delegate void OnTimerFinishDelegate();
    public event OnTimerFinishDelegate OnTimerFinish;

    public delegate void TileSelected(Tile tile);
    public event TileSelected OnTileSelected;

    private bool mouseOverUI;

    private void OnValidate()
    {
        SetCamera();
    }

    private void CurrentPowerUp_OnPowerUpUsed()
    {
        CurrentPowerUp.OnPowerUpUsed -= CurrentPowerUp_OnPowerUpUsed;
    }

    private void Awake()
    {
        if (Instance != null)
        {
            Destroy(this);
            return;
        }
        Instance = this;
    }

    void Start()
    {
        gameManager = GameManager.Instance;
        levelBuilder = LevelBuilder.Instance;
        CurrentPowerUp = null;
        SetCamera();
    }

    void Update()
    {
        HandleTimer();

        UpdateCameraPosition();

        HandleInput();
    }

    /// <summary>
    /// Sets the camera type based on the CameraType enum.
    /// Activates the orthographic camera if CameraType is Orthographic and deactivates the perspective camera.
    /// Activates the perspective camera if CameraType is Perspective and deactivates the orthographic camera.
    /// </summary>
    private void SetCamera()
    {
        if (cameraType == CameraType.Orthographic)
        {
            orthographicCamera.gameObject.SetActive(true);
            persprectiveCamera.gameObject.SetActive(false);
            cam = orthographicCamera;
        }
        else
        {
            orthographicCamera.gameObject.SetActive(false);
            persprectiveCamera.gameObject.SetActive(true);
            cam = persprectiveCamera;
        }
    }

    public void OnMouseOverSelection()
    {
        if (gameManager.CurrentBuildMode == GameManager.BuildMode.Click) return;
        CurrentHoveredTile = null;
        mouseOverUI = true;
    }

    public void OnMouseLeftSelection()
    {
        if (gameManager.CurrentBuildMode == GameManager.BuildMode.Click) return;
        mouseOverUI = false;
    }

    /// <summary>
    /// Handles user input for placing and removing tiles.
    /// If the game is in Click build mode, it will place a tile at the mouse position
    /// when the left mouse button is clicked. If the game is in DragDrop build mode,
    /// it will drag the selected tile to the mouse position while the left mouse button
    /// is held down and place it when the button is released. If the game is in Destroyer
    /// mode, it will destroy the tile at the mouse position when the left mouse button
    /// is clicked.
    /// </summary>
    private void HandleInput()
    {
        RaycastHit hit;
        Ray ray = new Ray();
        ray = cam.ScreenPointToRay(Input.mousePosition);

        #region OnMouseDown
        bool mouseDown = false;

        if (Application.isEditor)
        {
            if (Input.GetMouseButtonDown(0))
            {
                mouseDown = true;
            }
        }
        else
        if (Input.touchCount > 0)
        {
            if (!mouseWasDown)
            {
                mouseDown = true;
                mouseWasDown = true;
            }
        }
       
        bool isOverUI = EventSystem.current.currentSelectedGameObject != null;

        if (gameManager.CurrentBuildMode == GameManager.BuildMode.DragDrop)
        {
            isOverUI = false;
        }

        if (mouseDown && isOverUI && gameManager.CurrentBuildMode == GameManager.BuildMode.Click)
        {
            if (CurrentSelectedTile)
            {
                SetCurrentTilePosition(startSelectedTilePosition);
                CurrentSelectedTile = null;
                return;
            }
        }

        if (mouseDown && !isOverUI)
        {
            if (Physics.Raycast(ray, out hit, 1000f, tileLayerMask))
            {
                Tile tile = hit.collider.GetComponent<Tile>();

                if (!tile)
                {
                    return;
                }

                if (InDestroyerMode)
                {
                    if (tile != car.CurrentTile && tile.CurrentTileType == Tile.TileType.Built)
                    {
                        Vector2 position = tile.GridPosition;
                        PowerUpObject powerUpObject = tile.RelatedPowerUp;
                        if (powerUpObject)
                        {
                            powerUpObject.transform.SetParent(null);
                        }

                        levelBuilder.DestroyTile(position);
                        Tile newtile = levelBuilder.CreateTileAtPosition(levelBuilder.GetRandomEmptyTile(), position);

                        if (powerUpObject)
                        {
                            newtile.RelatedPowerUp = powerUpObject;
                        }

                        InDestroyerMode = false;
                        OnTileDestroyed?.Invoke();
                    }
                }
                else
                {
                    if (tile.CurrentTileType == Tile.TileType.Selection)
                    {
                        CurrentSelectedTile = tile;
                    }
                    else
                    {
                        if (gameManager.CurrentBuildMode == GameManager.BuildMode.Click)
                        {
                            CurrentHoveredTile = tile;
                            if (CurrentSelectedTile && CurrentHoveredTile)
                            {
                                PlaceSelectedTile();
                            }
                            else if (CurrentSelectedTile)
                            {
                                SetCurrentTilePosition(startSelectedTilePosition);
                                CurrentSelectedTile = null;
                            }
                        }
                    }
                }
            }
        }
        #endregion

        #region OnMouseUp

        bool mouseUp = false;

        if (Application.isEditor)
        {
            mouseUp = Input.GetMouseButtonUp(0);
        }
        else
        {
            if (mouseWasDown)
            {
                mouseUp = Input.touchCount == 0;
            }
        }

        if (mouseUp)
        {
            mouseWasDown = false;

            if (gameManager.CurrentBuildMode == GameManager.BuildMode.DragDrop)
            {
                if (CurrentSelectedTile && !CurrentHoveredTile)
                {
                    CurrentSelectedTile.transform.localPosition = startSelectedTilePosition;
                }
                else if (CurrentSelectedTile && CurrentHoveredTile)
                {
                    PlaceSelectedTile();
                }

                CurrentSelectedTile = null;
            }
        }

        #endregion

        if (CurrentSelectedTile)
        {
            Vector3 mousePos = Vector3.zero;
            if (cam.orthographic)
            {
                if (Input.GetMouseButton(0))
                {
                    mousePos = cam.ScreenToWorldPoint(Input.mousePosition);
                }
                else if (Input.touchCount > 0)
                {
                    mousePos = cam.ScreenToWorldPoint(Input.touches[0].position);
                }
            }
            else
            {
                if (Physics.Raycast(ray, out hit, 1000))
                {
                    mousePos = hit.point;
                }
                else
                {
                    mousePos = Vector3.zero;
                }
            }

            if (!CurrentHoveredTile)
            {
                SetCurrentTilePosition(new Vector3(mousePos.x, dragHeight, mousePos.z));
            }

            if (cam.orthographic)
            {
                if (Input.GetMouseButton(0))
                {
                    previousMousePos = cam.ScreenToWorldPoint(Input.mousePosition);
                }
                else if (Input.touchCount > 0)
                {
                    previousMousePos = cam.ScreenToWorldPoint(Input.touches[0].position);
                }
            }
            else
            {
                if (Physics.Raycast(ray, out hit, 1000))
                {
                    previousMousePos = hit.point;
                }
                else
                {
                    mousePos = Vector3.zero;
                }
            }

            if (Physics.Raycast(ray, out hit, 1000, tileLayerMask) && !mouseOverUI)
            {
                Tile tile = hit.collider.GetComponent<Tile>();

                if (!tile)
                {
                    CurrentHoveredTile = null;
                    SetCurrentTilePosition(new Vector3(mousePos.x, hit.point.y + 1f, mousePos.z));
                    return;
                }

                if (car.CurrentTile != tile && (tile.CurrentTileType == Tile.TileType.Empty || tile.CurrentTileType == Tile.TileType.Street))
                {
                    CurrentHoveredTile = tile;
                    SetCurrentTilePosition(new Vector3(CurrentHoveredTile.transform.position.x, -0.01f, CurrentHoveredTile.transform.position.z));
                }
                else
                {
                    if (CurrentHoveredTile)
                    {
                        SetCurrentTilePosition(new Vector3(mousePos.x, dragHeight, mousePos.z));
                    }
                    else
                    {
                        SetCurrentTilePosition(new Vector3(mousePos.x, hit.point.y + dragHeight, mousePos.z));
                    }
                    CurrentHoveredTile = null;
                }
            }
            else
            {
                if (CurrentHoveredTile)
                {
                    SetCurrentTilePosition(new Vector3(mousePos.x, dragHeight, mousePos.z));
                }
                CurrentHoveredTile = null;
            }
        }
    }

    /// <summary>
    /// Places the selected street section on the hovered tile in the level.
    /// Only works if the hovered tile is not a built or border tile.
    /// </summary>
    private void PlaceSelectedTile()
    {
        if (CurrentHoveredTile.CurrentTileType == Tile.TileType.Built || CurrentHoveredTile.CurrentTileType == Tile.TileType.Border) return;

        Tile tileToUse = CurrentSelectedTile;
        CurrentSelectedTile.transform.SetParent(CurrentHoveredTile.transform.parent);
        Vector3 pos = new Vector3(CurrentHoveredTile.transform.position.x, -0.01f, CurrentHoveredTile.transform.position.z);
        CurrentSelectedTile.transform.position = pos;
        CurrentSelectedTile.CurrentTileType = Tile.TileType.Street;
        CurrentSelectedTile.GridPosition = CurrentHoveredTile.GridPosition;
        TileSelection.Instance.RemoveTileFromSelection(CurrentSelectedTile.selectionIndex);
        CurrentSelectedTile.RelatedPowerUp = CurrentHoveredTile.RelatedPowerUp;
        Destroy(CurrentHoveredTile.gameObject);
        CurrentHoveredTile = null;
        levelBuilder.SetTile(CurrentSelectedTile.GridPosition, CurrentSelectedTile);
        audioPlayer.PlaySound();
        GameObject fxObject = Instantiate(buildFXPrefab, pos + new Vector3(0, 6.5f, 0f), Quaternion.identity);
        Destroy(fxObject, 2f);
        CurrentSelectedTile.ShowBuildTilePlaceHighlight(false);
        CurrentSelectedTile = null;
        TileSelection.Instance.CallTilePlaced(tileToUse);
    }

    private void SetCurrentTilePosition(Vector3 pos)
    {
        if (CurrentSelectedTile && gameManager.CurrentBuildMode == GameManager.BuildMode.DragDrop)
        {
            CurrentSelectedTile.transform.position = pos;
        }
    }

    public Transform GetPowerUpParent()
    {
        return powerUpSlotParent;
    }

    public void GetRandomPowerUp()
    {
        CurrentPowerUp = powerUps[UnityEngine.Random.Range(0, powerUps.Length)];
    }

    public void StartTimer(float time, bool unscaledTime)
    {
        timerTextObj.SetActive(true);
        timerText.enabled = true;
        timerTime = time;
        currentTimer = 0f;
        timeUnscaled = unscaledTime;
        runTimer = true;
    }

    private void HandleTimer()
    {
        if (runTimer)
        {
            currentTimer += timeUnscaled ? Time.unscaledDeltaTime : Time.deltaTime;
            timerText.SetText((timerTime - currentTimer).ToString("0"));

            if (currentTimer >= timerTime)
            {
                runTimer = false;
                OnTimerFinish?.Invoke();
                timerText.enabled = false;
                timerTextObj.SetActive(false);
            }
        }
    }

    /// <summary>
    /// Updates the camera's position to smoothly follow the car.
    /// This is done by using Vector3.Lerp to smoothly move the camera's position
    /// to a position that is a certain distance behind the car.
    /// </summary>
    private void UpdateCameraPosition()
    {
        if (!gameManager.gameStarted) return;

        transform.position = Vector3.Lerp(transform.position, new Vector3(transform.position.x, transform.position.y, car.transform.position.z + zDistanceToCar), cameraFollowSpeed * Time.deltaTime);
    }

    /// <summary>
    /// Sets the camera's position to be behind the car by a certain distance at the start of the game.
    /// </summary>
    public void SetCameraPositon()
    {
        transform.position = new Vector3(transform.position.x, transform.position.y, car.transform.position.z + zDistanceToCar);
    }

    private void StopDelete()
    {
        if (deleteCoroutine != null)
        {
            StopCoroutine(deleteCoroutine);
            deleteCoroutine = null;
            deleteImage.fillAmount = 0f;
        }
    }

    private void StartDeleteCoroutine()
    {
        StopDelete();
        deleteCoroutine = StartCoroutine(HandleDelete());
    }

    /// <summary>
    /// Handles deleting the currently selected tile when the delete timer is finished.
    /// The delete timer is a coroutine that fills up the delete image over the deletion time.
    /// When the timer is finished, the currently selected tile is deleted and removed from the tile selection.
    /// </summary>
    private IEnumerator HandleDelete()
    {
        float currentTime = 0f;

        while (currentTime < deletionTime)
        {
            currentTime += Time.deltaTime;
            deleteImage.fillAmount = currentTime / deletionTime;
            yield return null;
        }
        deleteImage.fillAmount = 0f;

        TileSelection.Instance.RemoveTileFromSelection(CurrentSelectedTile.selectionIndex);
        Destroy(CurrentSelectedTile.gameObject);
    }
}
