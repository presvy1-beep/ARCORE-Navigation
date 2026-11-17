using System;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;

public static class AndroidBuildCommand
{
    public static void AuditScene()
    {
        var scene = EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity", OpenSceneMode.Single);
        var controller = UnityEngine.Object.FindAnyObjectByType<ARNavigationController>();
        if (controller == null) throw new InvalidOperationException("ARNavigationController missing in SampleScene.");
        Debug.Log($"Audit scene: {scene.path}; mapRoot={controller.mapRoot}; " +
                  $"camera={controller.arCamera}; XR Origin={controller.xrOrigin}; destinations={controller.destinationsParent}");
        if (controller.destinationsParent == null) throw new InvalidOperationException("Destinations parent is missing.");
        var triangulation = NavMesh.CalculateTriangulation();
        if (triangulation.vertices.Length == 0) Debug.LogError("NavMesh triangulation is empty.");
        else
        {
            var min = triangulation.vertices[0];
            var max = min;
            foreach (var vertex in triangulation.vertices) { min = Vector3.Min(min, vertex); max = Vector3.Max(max, vertex); }
            Debug.Log($"NavMesh vertices={triangulation.vertices.Length}, bounds min={min}, max={max}");
        }
        var rooms = new System.Collections.Generic.List<Transform>();
        foreach (Transform room in controller.destinationsParent)
        {
            rooms.Add(room);
            bool sampled = NavMesh.SamplePosition(room.position, out var hit, 2f, NavMesh.AllAreas);
            Debug.Log($"Destination {room.name} at {room.position}; NavMesh within 2m={sampled}" + (sampled ? $" hit={hit.position}" : ""));
        }
        int completeRoutes = 0;
        int unreachableRoutes = 0;
        var path = new NavMeshPath();
        for (int start = 0; start < rooms.Count; start++)
        for (int end = start + 1; end < rooms.Count; end++)
        {
            bool valid = NavMesh.SamplePosition(rooms[start].position, out var from, 2f, NavMesh.AllAreas) &&
                         NavMesh.SamplePosition(rooms[end].position, out var to, 2f, NavMesh.AllAreas) &&
                         NavMesh.CalculatePath(from.position, to.position, NavMesh.AllAreas, path) &&
                         path.status == NavMeshPathStatus.PathComplete;
            if (valid) completeRoutes++; else unreachableRoutes++;
            Debug.Log($"Route {rooms[start].name} -> {rooms[end].name}: {(valid ? "complete" : "unreachable/invalid")}");
        }
        Debug.Log($"Waypoint pairs: {completeRoutes} complete, {unreachableRoutes} invalid or unreachable.");
    }

    public static void Build()
    {
        System.IO.Directory.CreateDirectory("Builds/Android");
        var options = new BuildPlayerOptions
        {
            scenes = new[] { "Assets/Scenes/SampleScene.unity" },
            locationPathName = "Builds/Android/AR-Nav-2ndFloor.apk",
            target = BuildTarget.Android,
            options = BuildOptions.None
        };

        BuildReport report = BuildPipeline.BuildPlayer(options);
        if (report.summary.result != BuildResult.Succeeded)
            throw new InvalidOperationException("Android build failed: " + report.summary.result);
    }
}
