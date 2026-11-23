using System.Collections.Generic;
using TMPro;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using UnityEngine.XR.ARFoundation;

/// <summary>Aligns the authored floor map (and its baked NavMesh) to AR tracking space and guides the user.</summary>
[RequireComponent(typeof(LineRenderer))]
public class ARNavigationController : MonoBehaviour
{
    public static readonly string[] SelectableRoomOrder =
    {
        "ELT 3", "ELT 4", "ELT 5", "ELT 6", "ELT 7", "ECR 17", "ECR 18"
    };

    [Header("AR and map")]
    public Transform xrOrigin;
    public Camera arCamera;
    public Transform mapRoot;
    public Transform destinationsParent;
    [Tooltip("Known corridor direction in map-local coordinates.")]
    public Vector3 corridorForwardMap = Vector3.forward;
    [Tooltip("Approximate camera height above the floor. Confirm on site.")]
    [Min(0.5f)] public float cameraHeightAboveFloor = 1.5f;

    [Header("Legacy scene UI references")]
    public TMP_Dropdown currentRoomDropdown;
    public TMP_Dropdown destinationDropdown;
    public TextMeshProUGUI statusText;

    [Header("Navigation")]
    [Min(0.1f)] public float pathUpdateInterval = 0.4f;
    [Min(0.2f)] public float arrivalThreshold = 1.1f;
    [Min(0f)] public float arrivalDwellSeconds = 1.0f;
    [Min(0f)] public float lineElevation = 0.055f;
    [Min(0.2f)] public float arrowSpacing = 0.95f;
    [Min(0.2f)] public float rerouteDistance = 0.75f;
    [Min(0.1f)] public float maxStartSnapDistance = 0.65f;
    [Min(0.1f)] public float maxDestinationSnapDistance = 0.75f;
    public bool diagnostics;

    private readonly List<Transform> roomNodes = new List<Transform>();
    private readonly List<Vector3> meshVertices = new List<Vector3>(640);
    private readonly List<int> meshTriangles = new List<int>(960);
    private readonly List<Vector3> lastRouteCorners = new List<Vector3>(64);
    private LineRenderer lineRenderer;
    private NavMeshPath navMeshPath;
    private NavMeshSurface mapNavMeshSurface;
    private Mesh arrowMesh;
    private Material arrowMaterial;
    private GameObject arrowObject;
    private Transform currentStartRoom;
    private Transform currentTargetRoom;
    private bool isNavigating;
    private bool isCalibrated;
    private bool arrivalShown;
    private float arrivalDwellTimer;
    private float refreshTimer;
    private float remainingDistance;
    private string statusMessage = "Initializing AR…";
    private string guidanceMessage = "Choose your current location to begin.";
    private string lastTrackingMessage;
    private bool routePaused;
    private Vector3 lastRouteStart;

    public int RoomCount => roomNodes.Count;
    public string GetRoomName(int index) => index >= 0 && index < roomNodes.Count ? roomNodes[index].name : string.Empty;
    public string SelectedStartName => currentStartRoom != null ? currentStartRoom.name : string.Empty;
    public string SelectedDestinationName => currentTargetRoom != null ? currentTargetRoom.name : string.Empty;
    public string StatusMessage => statusMessage;
    public string GuidanceMessage => guidanceMessage;
    public float RemainingDistance => remainingDistance;
    public bool IsNavigating => isNavigating;
    public bool IsCalibrated => isCalibrated;
    public bool RoutePaused => routePaused;
    public bool HasStart => currentStartRoom != null;
    public bool HasDestination => currentTargetRoom != null;
    public bool TrackingReady => ARSession.state == ARSessionState.SessionTracking;
    public bool CanStartNavigation => AreNavigationPrerequisitesMet(HasStart, HasDestination, isCalibrated, TrackingReady, isNavigating);
    public Transform GetRoomTransform(int index) => index >= 0 && index < roomNodes.Count ? roomNodes[index] : null;

    public static bool AreNavigationPrerequisitesMet(bool hasStart, bool hasDestination, bool calibrated, bool trackingReady, bool navigating)
        => hasStart && hasDestination && calibrated && trackingReady && !navigating;
    public string TrackingMessage => GetTrackingMessage();

    private void Awake()
    {
        lineRenderer = GetComponent<LineRenderer>();
        lineRenderer.useWorldSpace = true;
        lineRenderer.enabled = false;
        navMeshPath = new NavMeshPath();

        arrowMesh = new Mesh { name = "AR Navigation Direction Arrows" };
        arrowMesh.MarkDynamic();
        arrowObject = new GameObject("AR Navigation Arrows", typeof(MeshFilter), typeof(MeshRenderer));
        arrowObject.GetComponent<MeshFilter>().sharedMesh = arrowMesh;
        arrowObject.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        var renderer = arrowObject.GetComponent<MeshRenderer>();
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
        arrowMaterial = new Material(shader) { name = "AR Navigation Arrow Material" };
        Color accent = new Color32(18, 174, 153, 230);
        if (arrowMaterial.HasProperty("_BaseColor")) arrowMaterial.SetColor("_BaseColor", accent);
        if (arrowMaterial.HasProperty("_Color")) arrowMaterial.SetColor("_Color", accent);
        renderer.sharedMaterial = arrowMaterial;
        arrowObject.SetActive(false);
    }

    private void Start()
    {
        ResolveDependencies();
        PopulateRoomList();
        if (GetComponent<ARNavigationMobileUI>() == null) gameObject.AddComponent<ARNavigationMobileUI>();
        if (arCamera == null || mapRoot == null || destinationsParent == null)
            SetStatus("Setup problem: camera, map, or room list reference is missing.", "Check scene references.");
        else
            SetStatus("AR is starting. Select your start room, stand there, and face the known corridor.", "Select a start location.");
    }

    private void Update()
    {
        if (!isNavigating) return;
        if (!TrackingReady)
        {
            if (!routePaused) { ClearRoute(); routePaused = true; }
            string tracking = TrackingMessage;
            if (lastTrackingMessage != tracking)
            {
                lastTrackingMessage = tracking;
                SetStatus(tracking, "Route is paused until AR tracking recovers.");
            }
            return;
        }
        lastTrackingMessage = null;
        if (routePaused) { routePaused = false; refreshTimer = pathUpdateInterval; }
        refreshTimer += Time.unscaledDeltaTime;
        if (refreshTimer >= Mathf.Max(0.15f, pathUpdateInterval))
        {
            refreshTimer = 0f;
            UpdateRoute();
        }
    }

    private void OnDestroy()
    {
        if (arrowMesh != null) Destroy(arrowMesh);
        if (arrowMaterial != null) Destroy(arrowMaterial);
        if (arrowObject != null) Destroy(arrowObject);
    }

    private void ResolveDependencies()
    {
        if (arCamera == null) arCamera = Camera.main;
        if (xrOrigin == null && GameObject.Find("XR Origin") is GameObject origin) xrOrigin = origin.transform;
        if (mapRoot == null && GameObject.Find("[Map_Root]") is GameObject map) mapRoot = map.transform;
        if (destinationsParent == null && GameObject.Find("[Destinations]") is GameObject destinations) destinationsParent = destinations.transform;
        if (mapRoot != null) mapNavMeshSurface = mapRoot.GetComponent<NavMeshSurface>();
        if (statusText == null && GameObject.Find("DebugStatusText") is GameObject text) statusText = text.GetComponent<TextMeshProUGUI>();
        if (currentRoomDropdown == null && GameObject.Find("CurrentRoomDropdown") is GameObject startDropdown) currentRoomDropdown = startDropdown.GetComponent<TMP_Dropdown>();
        if (destinationDropdown == null && GameObject.Find("DestinationDropdown") is GameObject destinationDrop) destinationDropdown = destinationDrop.GetComponent<TMP_Dropdown>();
    }

    private void PopulateRoomList()
    {
        roomNodes.Clear();
        if (destinationsParent == null) return;
        // Keep room choices stable and exclude internal waypoints such as Bridge Corner.
        // Each choice resolves to the actual authored child Transform under [Destinations].
        foreach (string roomName in SelectableRoomOrder)
        {
            Transform room = destinationsParent.Find(roomName);
            if (room == null)
            {
                Debug.LogError($"Selectable room waypoint '{roomName}' is missing under {destinationsParent.name}.", this);
                continue;
            }
            roomNodes.Add(room);
        }
        var names = new List<string> { "Choose a location" };
        foreach (Transform room in roomNodes) names.Add(room.name);
        if (currentRoomDropdown != null)
        {
            currentRoomDropdown.ClearOptions();
            currentRoomDropdown.AddOptions(new List<string>(names));
            currentRoomDropdown.onValueChanged.RemoveListener(OnCurrentRoomSelected);
            currentRoomDropdown.onValueChanged.AddListener(OnCurrentRoomSelected);
        }
        if (destinationDropdown != null)
        {
            destinationDropdown.ClearOptions();
            destinationDropdown.AddOptions(new List<string>(names));
            destinationDropdown.onValueChanged.RemoveListener(OnDestinationSelected);
            destinationDropdown.onValueChanged.AddListener(OnDestinationSelected);
        }
    }

    public void SelectStartRoom(int roomIndex) => OnCurrentRoomSelected(roomIndex + 1);
    public void SelectDestination(int roomIndex) => OnDestinationSelected(roomIndex + 1);

    public void OnCurrentRoomSelected(int dropdownIndex)
    {
        StopNavigationInternal();
        currentStartRoom = dropdownIndex > 0 && dropdownIndex <= roomNodes.Count ? roomNodes[dropdownIndex - 1] : null;
        currentTargetRoom = null;
        isCalibrated = false;
        arrivalShown = false;
        if (currentRoomDropdown != null) currentRoomDropdown.SetValueWithoutNotify(dropdownIndex);
        if (destinationDropdown != null) destinationDropdown.SetValueWithoutNotify(0);
        SetStatus(currentStartRoom == null ? "Select a start location." : $"Start: {currentStartRoom.name}. Stand at this location and face along the marked corridor before calibration.",
            "Calibrate while standing at your selected start.");
    }

    public void OnDestinationSelected(int dropdownIndex)
    {
        if (isNavigating) StopNavigationInternal();
        currentTargetRoom = dropdownIndex > 0 && dropdownIndex <= roomNodes.Count ? roomNodes[dropdownIndex - 1] : null;
        if (destinationDropdown != null) destinationDropdown.SetValueWithoutNotify(dropdownIndex);
        if (currentTargetRoom == null)
        {
            if (isNavigating) CancelNavigation();
            SetStatus("Choose a destination.", HasStart ? "Ready when you are." : "Select your start location.");
            return;
        }
        if (currentStartRoom == currentTargetRoom)
        {
            currentTargetRoom = null;
            if (destinationDropdown != null) destinationDropdown.SetValueWithoutNotify(0);
            SetStatus("Choose a destination different from your start.", "Your current location is the start.");
            return;
        }
        SetStatus($"Destination: {currentTargetRoom.name}.", isCalibrated ? "Tap Start Navigation when ready." : "Calibrate the map before starting.");
    }

    /// <summary>Calibrate the floor-map root using a selected room, the tracking camera, and known corridor heading.</summary>
    public void CalibrateAlignment()
    {
        if (currentStartRoom == null || mapRoot == null || arCamera == null)
        {
            SetStatus("Choose a start location first.", "Calibration needs a selected room and an AR camera.");
            return;
        }
        if (!TrackingReady)
        {
            SetStatus(TrackingMessage, "Wait for AR tracking, then try calibration again.");
            return;
        }

        // Recalibration changes the aligned NavMesh pose. Stop any existing route first.
        StopNavigationInternal();

        Vector3 cameraForward = Vector3.ProjectOnPlane(arCamera.transform.forward, Vector3.up);
        Vector3 mapForward = Vector3.ProjectOnPlane(mapRoot.TransformDirection(corridorForwardMap), Vector3.up);
        if (cameraForward.sqrMagnitude < 0.001f || mapForward.sqrMagnitude < 0.001f)
        {
            SetStatus("Calibration reference direction is invalid.", "Check the configured corridor direction.");
            return;
        }

        float yaw = CalculateAlignmentYaw(mapForward, cameraForward);
        mapRoot.rotation = Quaternion.AngleAxis(yaw, Vector3.up) * mapRoot.rotation;
        Vector3 cameraFloor = arCamera.transform.position - Vector3.up * cameraHeightAboveFloor;
        mapRoot.position += cameraFloor - currentStartRoom.position;
        RefreshNavMeshAtMapTransform();
        isCalibrated = true;
        arrivalShown = false;
        SetStatus($"Map calibrated at {currentStartRoom.name}.", "Choose a destination, then tap Start Navigation.");
    }

    private void RefreshNavMeshAtMapTransform()
    {
        if (mapNavMeshSurface == null) mapNavMeshSurface = mapRoot != null ? mapRoot.GetComponent<NavMeshSurface>() : null;
        if (mapNavMeshSurface == null) return;
        // Explicitly re-register baked data at the transformed root pose so the first AR path uses aligned coordinates.
        mapNavMeshSurface.RemoveData();
        mapNavMeshSurface.AddData();
    }

    public float GetCalibrationYaw(Vector3 cameraForward) => CalculateAlignmentYaw(
        Vector3.ProjectOnPlane(mapRoot.TransformDirection(corridorForwardMap), Vector3.up), cameraForward);

    public static float CalculateAlignmentYaw(Vector3 mapForward, Vector3 cameraForward)
    {
        mapForward = Vector3.ProjectOnPlane(mapForward, Vector3.up).normalized;
        cameraForward = Vector3.ProjectOnPlane(cameraForward, Vector3.up).normalized;
        return Vector3.SignedAngle(mapForward, cameraForward, Vector3.up);
    }

    public static float CalculatePathDistance(IReadOnlyList<Vector3> corners)
    {
        float distance = 0f;
        if (corners == null) return distance;
        for (int i = 1; i < corners.Count; i++) distance += Vector3.Distance(corners[i - 1], corners[i]);
        return distance;
    }

    public static bool IsWithinArrivalThreshold(Vector3 cameraFloor, Vector3 destination, float threshold)
    {
        float dx = cameraFloor.x - destination.x;
        float dz = cameraFloor.z - destination.z;
        return dx * dx + dz * dz <= threshold * threshold;
    }

    public static string GetTurnInstruction(IReadOnlyList<Vector3> corners, string destinationName)
    {
        if (corners == null || corners.Count < 2) return "Finding route…";
        float remaining = CalculatePathDistance(corners);
        float distanceToCorner = Vector3.Distance(corners[0], corners[1]);
        Vector3 incoming = Vector3.ProjectOnPlane(corners[1] - corners[0], Vector3.up).normalized;
        for (int i = 2; i < corners.Count; i++)
        {
            Vector3 outgoing = Vector3.ProjectOnPlane(corners[i] - corners[i - 1], Vector3.up).normalized;
            float turn = Vector3.SignedAngle(incoming, outgoing, Vector3.up);
            if (Mathf.Abs(turn) >= 32f)
            {
                string direction = turn > 0f ? "right" : "left";
                return distanceToCorner <= 1.15f ? $"Turn {direction} now" : $"Turn {direction} in {distanceToCorner:0.0} m";
            }
            distanceToCorner += Vector3.Distance(corners[i - 1], corners[i]);
            incoming = outgoing;
        }
        if (remaining <= 2.2f) return $"Approaching {destinationName}";
        return remaining <= 4.5f ? $"Continue to {destinationName}" : "Continue straight";
    }

    public void StartNavigation()
    {
        if (currentStartRoom == null || currentTargetRoom == null || mapRoot == null || arCamera == null || !isCalibrated)
        {
            SetStatus("Select start and destination, then calibrate the map.", "Navigation is not ready yet.");
            return;
        }
        if (currentStartRoom == currentTargetRoom)
        {
            SetStatus("Choose a destination different from your start.", "Navigation has not started.");
            return;
        }
        if (!TrackingReady)
        {
            SetStatus(TrackingMessage, "Wait for AR tracking to recover before starting navigation.");
            return;
        }
        StopNavigationInternal();
        isNavigating = true;
        arrivalShown = false;
        arrivalDwellTimer = 0f;
        refreshTimer = 0f;
        SetStatus("Starting navigation…", "Finding a complete route.");
        lastTrackingMessage = null;
        if (TrackingReady) UpdateRoute();
    }

    public void CancelNavigation()
    {
        if (!isNavigating && arrivalShown) return;
        StopNavigationInternal();
        if (!arrivalShown) SetStatus("Navigation paused.", "Press Start Navigation to resume or choose another destination.");
    }

    public void Recalibrate() => CalibrateAlignment();
    public void ApplyYawAlignment() => CalibrateAlignment();
    public void ResetYaw() => CalibrateAlignment();
    public void TriggerYawReset() => CalibrateAlignment();
    public void DebugTriggerYawReset() => CalibrateAlignment();
    public void TriggerManualMode() => CalibrateAlignment();
    public void TriggerAutoMode() => SetStatus("Select your start location and calibrate the map.", "Ready to plan a route.");
    public void AssignAnchor() => CalibrateAlignment();

    private void UpdateRoute()
    {
        if (arCamera == null || currentTargetRoom == null) return;
        Vector3 cameraFloor = arCamera.transform.position - Vector3.up * cameraHeightAboveFloor;
        Vector3 targetPosition = currentTargetRoom.position;
        if (!TrySnap(cameraFloor, maxStartSnapDistance, out NavMeshHit startHit))
        {
            ClearRoute(); routePaused = true;
            SetStatus("Position is outside the mapped corridor.", "Return to the corridor or recalibrate at your selected start.");
            LogDiagnostics(cameraFloor, Vector3.zero, float.PositiveInfinity, "start snap failed", false);
            return;
        }
        routePaused = false;
        bool closeEnough = IsWithinArrivalThreshold(cameraFloor, targetPosition, arrivalThreshold) &&
                           TrySnap(targetPosition, maxDestinationSnapDistance, out NavMeshHit arrivalHit) &&
                           !NavMesh.Raycast(startHit.position, arrivalHit.position, out _, NavMesh.AllAreas);
        if (closeEnough)
        {
            arrivalDwellTimer += Mathf.Max(0f, Time.unscaledDeltaTime);
            if (arrivalDwellTimer >= arrivalDwellSeconds)
            {
                arrivalShown = true;
                StopNavigationInternal();
                SetStatus($"You’ve arrived at {currentTargetRoom.name}.", "Destination reached.");
                return;
            }
        }
        else arrivalDwellTimer = 0f;

        if (!TrySnap(targetPosition, maxDestinationSnapDistance, out NavMeshHit endHit))
        {
            ClearRoute();
            SetStatus($"No walkable floor found near {currentTargetRoom.name}.", "Recalibrate at your selected start location.");
            return;
        }
        if (lastRouteCorners.Count >= 2 && Vector3.Distance(startHit.position, lastRouteStart) < rerouteDistance)
        {
            SetStatus($"Routing to {currentTargetRoom.name}.", GetTurnInstruction(lastRouteCorners, currentTargetRoom.name));
            LogDiagnostics(cameraFloor, startHit.position, Vector3.Distance(cameraFloor, startHit.position), "stable route", false);
            return;
        }
        if (!NavMesh.CalculatePath(startHit.position, endHit.position, NavMesh.AllAreas, navMeshPath) ||
            navMeshPath.status != NavMeshPathStatus.PathComplete || navMeshPath.corners.Length < 2)
        {
            ClearRoute();
            SetStatus($"No complete route to {currentTargetRoom.name}.", "Try a different destination.");
            return;
        }

        Vector3[] corners = navMeshPath.corners;
        if (!AreSegmentsWalkable(corners))
        {
            ClearRoute();
            SetStatus("Route leaves the walkable corridor.", "Recalibrate at your selected start location.");
            return;
        }
        remainingDistance = CalculatePathDistance(corners);
        bool routeChanged = lastRouteCorners.Count == 0 ||
                            Vector3.Distance(startHit.position, lastRouteStart) >= rerouteDistance;
        if (routeChanged && lastRouteCorners.Count > 0 && !HasRouteChanged(corners, rerouteDistance)) routeChanged = false;
        if (routeChanged)
        {
            lastRouteStart = startHit.position;
            lastRouteCorners.Clear();
            lastRouteCorners.AddRange(corners);
            RenderRoute(corners);
        }
        guidanceMessage = GetTurnInstruction(corners, currentTargetRoom.name);
        SetStatus($"Routing to {currentTargetRoom.name}.", guidanceMessage);
        LogDiagnostics(cameraFloor, startHit.position, Vector3.Distance(cameraFloor, startHit.position), navMeshPath.status.ToString(), routeChanged);
    }

    public static bool IsSnapDistanceValid(Vector3 requested, Vector3 snapped, float maximum)
        => maximum > 0f && Vector3.Distance(requested, snapped) <= maximum;

    private static bool TrySnap(Vector3 requested, float maximum, out NavMeshHit hit)
        => NavMesh.SamplePosition(requested, out hit, maximum, NavMesh.AllAreas) &&
           IsSnapDistanceValid(requested, hit.position, maximum);

    private static bool AreSegmentsWalkable(IReadOnlyList<Vector3> corners)
    {
        for (int i = 1; i < corners.Count; i++)
        {
            Vector3 a = corners[i - 1], b = corners[i];
            if (NavMesh.Raycast(a, b, out _, NavMesh.AllAreas)) return false;
            int samples = Mathf.CeilToInt(Vector3.Distance(a, b) / 0.25f);
            for (int j = 1; j < samples; j++)
                if (!TrySnap(Vector3.Lerp(a, b, (float)j / samples), 0.12f, out _)) return false;
        }
        return true;
    }

    private void LogDiagnostics(Vector3 floor, Vector3 snapped, float snapDistance, string pathStatus, bool rerouted)
    {
        if (!diagnostics) return;
        Debug.Log($"AR navigation: camera={arCamera.transform.position} floor={floor} snap={snapped} distance={snapDistance:0.00} calibrated={isCalibrated} path={pathStatus} corners=[{string.Join(", ", lastRouteCorners)}] rerouted={rerouted} paused={routePaused}", this);
    }

    private bool HasRouteChanged(IReadOnlyList<Vector3> corners, float movementThreshold)
    {
        if (lastRouteCorners.Count != corners.Count) return true;
        if (lastRouteCorners.Count == 0) return true;
        float thresholdSquared = movementThreshold * movementThreshold;
        if ((lastRouteCorners[0] - corners[0]).sqrMagnitude >= thresholdSquared) return true;
        for (int i = 1; i < corners.Count; i++)
            if ((lastRouteCorners[i] - corners[i]).sqrMagnitude >= 0.16f) return true;
        return false;
    }

    private void RenderRoute(IReadOnlyList<Vector3> corners)
    {
        lineRenderer.enabled = true;
        lineRenderer.positionCount = corners.Count;
        for (int i = 0; i < corners.Count; i++)
        {
            Vector3 position = corners[i];
            position.y += lineElevation;
            lineRenderer.SetPosition(i, position);
        }
        BuildArrowMesh(corners);
        arrowObject.SetActive(meshVertices.Count > 0);
    }

    private void BuildArrowMesh(IReadOnlyList<Vector3> corners)
    {
        meshVertices.Clear();
        meshTriangles.Clear();
        float total = CalculatePathDistance(corners);
        if (total < 0.6f) { arrowMesh.Clear(); return; }
        float cursor = 0.65f;
        int segment = 1;
        float segmentStart = 0f;
        float segmentLength = Vector3.Distance(corners[0], corners[1]);
        while (cursor < total - 0.35f && meshVertices.Count < 640)
        {
            while (segment < corners.Count - 1 && cursor > segmentStart + segmentLength)
            {
                segmentStart += segmentLength;
                segment++;
                segmentLength = Vector3.Distance(corners[segment - 1], corners[segment]);
            }
            if (segmentLength > 0.001f)
            {
                float t = Mathf.Clamp01((cursor - segmentStart) / segmentLength);
                Vector3 center = Vector3.Lerp(corners[segment - 1], corners[segment], t);
                Vector3 forward = Vector3.ProjectOnPlane(corners[segment] - corners[segment - 1], Vector3.up).normalized;
                if (forward.sqrMagnitude > 0.001f &&
                    cursor - segmentStart >= 0.34f && segmentStart + segmentLength - cursor >= 0.34f &&
                    ArrowFootprintWalkable(center, forward))
                    AddArrowPolygon(center + Vector3.up * (lineElevation + 0.002f), forward);
            }
            cursor += Mathf.Max(0.45f, arrowSpacing);
        }
        arrowMesh.Clear();
        if (meshVertices.Count > 0)
        {
            arrowMesh.SetVertices(meshVertices);
            arrowMesh.SetTriangles(meshTriangles, 0, true);
            arrowMesh.RecalculateNormals();
            arrowMesh.RecalculateBounds();
        }
    }

    private static bool ArrowFootprintWalkable(Vector3 center, Vector3 forward)
    {
        Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
        Vector3[] points = { center + forward * 0.34f, center - forward * 0.34f + right * 0.075f,
            center - forward * 0.34f - right * 0.075f, center + forward * 0.08f + right * 0.20f,
            center + forward * 0.08f - right * 0.20f };
        foreach (Vector3 point in points)
            if (!TrySnap(point, 0.06f, out _) || NavMesh.Raycast(center, point, out _, NavMesh.AllAreas)) return false;
        return true;
    }

    private void AddArrowPolygon(Vector3 center, Vector3 forward)
    {
        Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
        const float halfShaft = 0.075f;
        const float halfHead = 0.20f;
        const float tip = 0.34f;
        const float headBase = 0.08f;
        const float tail = -0.34f;
        int first = meshVertices.Count;
        meshVertices.Add(center);
        Vector3[] polygon =
        {
            center + forward * tip,
            center + forward * headBase + right * halfHead,
            center + forward * headBase + right * halfShaft,
            center + forward * tail + right * halfShaft,
            center + forward * tail - right * halfShaft,
            center + forward * headBase - right * halfShaft,
            center + forward * headBase - right * halfHead
        };
        meshVertices.AddRange(polygon);
        for (int i = 0; i < polygon.Length; i++)
        {
            meshTriangles.Add(first);
            meshTriangles.Add(first + 1 + i);
            meshTriangles.Add(first + 1 + (i + 1) % polygon.Length);
        }
    }

    private void ClearRoute()
    {
        remainingDistance = 0f;
        lastRouteCorners.Clear();
        lastRouteStart = Vector3.zero;
        if (lineRenderer != null) { lineRenderer.positionCount = 0; lineRenderer.enabled = false; }
        if (arrowMesh != null) arrowMesh.Clear();
        if (arrowObject != null) arrowObject.SetActive(false);
    }

    private void StopNavigationInternal()
    {
        isNavigating = false;
        ClearRoute();
    }

    private static string GetTrackingMessage()
    {
        switch (ARSession.state)
        {
            case ARSessionState.SessionTracking: return "Tracking ready";
            case ARSessionState.None:
            case ARSessionState.CheckingAvailability:
            case ARSessionState.Installing:
            case ARSessionState.NeedsInstall: return "Preparing ARCore…";
            case ARSessionState.Unsupported: return "This device does not support ARCore.";
            case ARSessionState.Ready: return "Move the camera slowly to initialize tracking.";
            case ARSessionState.SessionInitializing: return "Move the camera slowly while ARCore initializes.";
            default: return "Tracking is interrupted. Move slowly and point at a textured surface.";
        }
    }

    private void SetStatus(string status, string guidance)
    {
        statusMessage = status;
        guidanceMessage = guidance;
        if (statusText != null) statusText.text = status + "\n" + guidance;
    }
}
