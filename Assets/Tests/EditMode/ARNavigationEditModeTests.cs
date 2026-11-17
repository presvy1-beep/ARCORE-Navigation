using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

namespace ARNavigation.Tests
{
    public class ARNavigationEditModeTests
    {
        private static Type ControllerType => Assembly.Load("Assembly-CSharp").GetType("ARNavigationController", true);

        private static void RestoreScenes(UnityEditor.SceneManagement.SceneSetup[] setup)
        {
            if (setup != null && setup.Length > 0) EditorSceneManager.RestoreSceneManagerSetup(setup);
            else EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }

        [Test]
        public void CalibrationYawAlignsKnownCorridorWithCameraHeading()
        {
            float right = (float)ControllerType.GetMethod("CalculateAlignmentYaw").Invoke(null,
                new object[] { Vector3.forward, Vector3.right });
            float left = (float)ControllerType.GetMethod("CalculateAlignmentYaw").Invoke(null,
                new object[] { Vector3.forward, Vector3.left });
            Assert.That(right, Is.EqualTo(90f).Within(0.01f));
            Assert.That(left, Is.EqualTo(-90f).Within(0.01f));
        }

        [Test]
        public void RemainingDistanceUsesEveryPathSegment()
        {
            Vector3[] corners = { Vector3.zero, new Vector3(0, 0, 3), new Vector3(4, 0, 3) };
            float distance = (float)ControllerType.GetMethod("CalculatePathDistance").Invoke(null, new object[] { corners });
            Assert.That(distance, Is.EqualTo(7f).Within(0.001f));
        }

        [TestCase(5f, "Turn right in 5.0 m")]
        [TestCase(1f, "Turn right now")]
        public void TurnGuidanceReportsDirectionAndDistance(float firstLeg, string expected)
        {
            Vector3[] corners = { Vector3.zero, new Vector3(0, 0, firstLeg), new Vector3(firstLeg, 0, firstLeg) };
            string actual = (string)ControllerType.GetMethod("GetTurnInstruction").Invoke(null,
                new object[] { corners, "Room B" });
            Assert.That(actual, Is.EqualTo(expected));
        }

        [Test]
        public void ArrivalUsesHorizontalDistanceAndConfiguredThreshold()
        {
            MethodInfo method = ControllerType.GetMethod("IsWithinArrivalThreshold");
            Assert.That((bool)method.Invoke(null, new object[] { new Vector3(0.9f, 0, 0), Vector3.zero, 1f }), Is.True);
            Assert.That((bool)method.Invoke(null, new object[] { new Vector3(1.1f, 0, 0), Vector3.zero, 1f }), Is.False);
        }

        [Test]
        public void SceneHasSerializedNavigationAndARRootReferences()
        {
            var setup = EditorSceneManager.GetSceneManagerSetup();
            try
            {
                EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity", OpenSceneMode.Single);
                Component controller = (Component)UnityEngine.Object.FindAnyObjectByType(ControllerType);
                Assert.That(controller, Is.Not.Null);
                Assert.That(ControllerType.GetField("arCamera").GetValue(controller), Is.Not.Null);
                Assert.That(ControllerType.GetField("xrOrigin").GetValue(controller), Is.Not.Null);
                Assert.That(ControllerType.GetField("mapRoot").GetValue(controller), Is.Not.Null);
                Assert.That(ControllerType.GetField("destinationsParent").GetValue(controller), Is.Not.Null);
                Assert.That(ControllerType.GetField("currentRoomDropdown").GetValue(controller), Is.Not.Null);
                Assert.That(ControllerType.GetField("destinationDropdown").GetValue(controller), Is.Not.Null);
                Assert.That(ControllerType.GetField("statusText").GetValue(controller), Is.Not.Null);
                Assert.That(GameObject.Find("AR Session"), Is.Not.Null);
                Assert.That(GameObject.FindWithTag("MainCamera").GetComponent<Camera>(), Is.Not.Null);
                Assert.That(FindComponent("UnityEngine.XR.ARFoundation.ARSession, Unity.XR.ARFoundation"), Is.Not.Null);
                Assert.That(FindComponent("UnityEngine.XR.ARFoundation.ARCameraManager, Unity.XR.ARFoundation"), Is.Not.Null);
                Assert.That(FindComponent("UnityEngine.XR.ARFoundation.ARCameraBackground, Unity.XR.ARFoundation"), Is.Not.Null);
            }
            finally
            {
                RestoreScenes(setup);
            }
        }

        [Test]
        public void SelectableRoomsAreSevenOrderedAuthoredWaypointsAndExcludeBridgeCorner()
        {
            string[] expected = { "ELT 3", "ELT 4", "ELT 5", "ELT 6", "ELT 7", "ECR 17", "ECR 18" };
            CollectionAssert.AreEqual(expected, (string[])ControllerType.GetField("SelectableRoomOrder").GetValue(null));
            var setup = EditorSceneManager.GetSceneManagerSetup();
            try
            {
                EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity", OpenSceneMode.Single);
                Component controller = (Component)UnityEngine.Object.FindAnyObjectByType(ControllerType);
                Transform destinations = GameObject.Find("[Destinations]").transform;
                ControllerType.GetMethod("ResolveDependencies", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(controller, null);
                ControllerType.GetMethod("PopulateRoomList", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(controller, null);
                Assert.That((int)ControllerType.GetProperty("RoomCount").GetValue(controller), Is.EqualTo(7));
                for (int i = 0; i < expected.Length; i++)
                {
                    Transform mapped = (Transform)ControllerType.GetMethod("GetRoomTransform").Invoke(controller, new object[] { i });
                    Assert.That(mapped, Is.SameAs(destinations.Find(expected[i])), $"Room index {i} must map to the authored {expected[i]} waypoint.");
                }
                Assert.That(destinations.Find("Bridge Corner"), Is.Not.Null, "The internal waypoint remains authored for NavMesh use.");
                Assert.That(ControllerType.GetField("currentRoomDropdown").GetValue(controller), Is.Not.Null);
                Component dropdown = (Component)ControllerType.GetField("currentRoomDropdown").GetValue(controller);
                var options = (IEnumerable)dropdown.GetType().GetProperty("options").GetValue(dropdown);
                var optionNames = new List<string>();
                foreach (object option in options)
                    optionNames.Add((string)option.GetType().GetProperty("text").GetValue(option));
                CollectionAssert.AreEqual(new[] { "Choose a location" }.Concat(expected), optionNames);
            }
            finally { RestoreScenes(setup); }
        }

        [TestCase(false, true, true, true, false)]
        [TestCase(true, false, true, true, false)]
        [TestCase(true, true, false, true, false)]
        [TestCase(true, true, true, false, false)]
        [TestCase(true, true, true, true, true)]
        [TestCase(true, true, true, true, false)]
        public void NavigationPrerequisitesIncludeTrackingAndIdleState(bool hasStart, bool hasDestination, bool calibrated,
            bool tracking, bool navigating)
        {
            bool canStart = (bool)ControllerType.GetMethod("AreNavigationPrerequisitesMet").Invoke(null,
                new object[] { hasStart, hasDestination, calibrated, tracking, navigating });
            Assert.That(canStart, Is.EqualTo(hasStart && hasDestination && calibrated && tracking && !navigating));
        }

        [Test]
        public void AndroidPortraitIdentityAndBuildSceneAreConfigured()
        {
            Assert.That(EditorBuildSettings.scenes.Any(scene => scene.enabled && scene.path == "Assets/Scenes/SampleScene.unity"), Is.True);
            Assert.That(PlayerSettings.defaultInterfaceOrientation, Is.EqualTo(UIOrientation.Portrait));
            Assert.That(PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.Android), Is.EqualTo("com.mahindrauniversity.arnav2ndfloor"));
            Assert.That(AssetDatabase.LoadAssetAtPath<ScriptableObject>("Assets/XR/Loaders/ARCoreLoader.asset"), Is.Not.Null);
            string loaderGuid = AssetDatabase.AssetPathToGUID("Assets/XR/Loaders/ARCoreLoader.asset");
            string settings = File.ReadAllText("Assets/XR/XRGeneralSettingsPerBuildTarget.asset");
            Assert.That(settings, Does.Contain(loaderGuid));
            string arCoreSettings = File.ReadAllText("Assets/XR/Settings/ARCoreSettings.asset");
            Assert.That(arCoreSettings, Does.Contain("m_Requirement: 0"), "ARCore must be required by the Android app manifest.");
        }

        [Test]
        public void BakedNavMeshRemainsNavigableAfterMapRootTranslationAndRotation()
        {
            var setup = EditorSceneManager.GetSceneManagerSetup();
            try
            {
                EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity", OpenSceneMode.Single);
                Transform mapRoot = GameObject.Find("[Map_Root]").transform;
                Transform destinations = GameObject.Find("[Destinations]").transform;
                Type surfaceType = Type.GetType("Unity.AI.Navigation.NavMeshSurface, Unity.AI.Navigation", true);
                Component surface = mapRoot.GetComponent(surfaceType);
                Assert.That(surface, Is.Not.Null);
                Vector3 originalPosition = mapRoot.position;
                Quaternion originalRotation = mapRoot.rotation;
                try
                {
                    surfaceType.GetMethod("RemoveData").Invoke(surface, null);
                    mapRoot.SetPositionAndRotation(originalPosition + new Vector3(14, 2, -9),
                        Quaternion.Euler(0, 37, 0) * originalRotation);
                    surfaceType.GetMethod("AddData").Invoke(surface, null);
                    var rooms = new List<Transform>();
                    foreach (Transform room in destinations) rooms.Add(room);
                    Assert.That(rooms.Count, Is.EqualTo(8));
                    for (int i = 0; i < rooms.Count; i++)
                    {
                        Assert.That(NavMesh.SamplePosition(rooms[i].position, out var from, 2f, NavMesh.AllAreas), Is.True,
                            rooms[i].name + " should remain on the transformed NavMesh");
                        for (int j = i + 1; j < rooms.Count; j++)
                        {
                            Assert.That(NavMesh.SamplePosition(rooms[j].position, out var to, 2f, NavMesh.AllAreas), Is.True);
                            var path = new NavMeshPath();
                            Assert.That(NavMesh.CalculatePath(from.position, to.position, NavMesh.AllAreas, path), Is.True);
                            Assert.That(path.status, Is.EqualTo(NavMeshPathStatus.PathComplete),
                                $"{rooms[i].name} -> {rooms[j].name} should be complete after map alignment");
                        }
                    }
                }
                finally
                {
                    surfaceType.GetMethod("RemoveData").Invoke(surface, null);
                    mapRoot.SetPositionAndRotation(originalPosition, originalRotation);
                    surfaceType.GetMethod("AddData").Invoke(surface, null);
                }
            }
            finally { RestoreScenes(setup); }
        }

        [Test]
        public void CancellationStopsRouteAndClearsVisualsButKeepsDestinationForResume()
        {
            var setup = EditorSceneManager.GetSceneManagerSetup();
            try
            {
                EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity", OpenSceneMode.Single);
                Component controller = (Component)UnityEngine.Object.FindAnyObjectByType(ControllerType);
                ControllerType.GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(controller, null);
                ControllerType.GetMethod("PopulateRoomList", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(controller, null);
                ControllerType.GetMethod("OnCurrentRoomSelected").Invoke(controller, new object[] { 1 });
                ControllerType.GetMethod("OnDestinationSelected").Invoke(controller, new object[] { 2 });
                ControllerType.GetField("isCalibrated", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(controller, true);
                // Editor tests have no real AR tracking subsystem; set the active state only to test cancellation cleanup.
                ControllerType.GetField("isNavigating", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(controller, true);
                Assert.That((bool)ControllerType.GetProperty("IsNavigating").GetValue(controller), Is.True);
                string destination = (string)ControllerType.GetProperty("SelectedDestinationName").GetValue(controller);
                Assert.That(destination, Is.Not.Empty);
                ControllerType.GetMethod("CancelNavigation").Invoke(controller, null);
                Assert.That((bool)ControllerType.GetProperty("IsNavigating").GetValue(controller), Is.False);
                Assert.That((string)ControllerType.GetProperty("SelectedDestinationName").GetValue(controller), Is.EqualTo(destination));
                var line = (LineRenderer)ControllerType.GetField("lineRenderer", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(controller);
                Assert.That(line, Is.Not.Null);
                Assert.That(line.enabled, Is.False);
                Assert.That(line.positionCount, Is.EqualTo(0));
            }
            finally { RestoreScenes(setup); }
        }

        private static Component FindComponent(string qualifiedTypeName)
        {
            Type type = Type.GetType(qualifiedTypeName, true);
            foreach (GameObject gameObject in Resources.FindObjectsOfTypeAll<GameObject>())
            {
                if (!gameObject.scene.IsValid()) continue;
                Component component = gameObject.GetComponent(type);
                if (component != null) return component;
            }
            return null;
        }
    }
}
