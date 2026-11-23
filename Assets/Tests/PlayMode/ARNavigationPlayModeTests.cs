using System.Collections;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace ARNavigation.Tests
{
    public class ARNavigationPlayModeTests
    {
        private static System.Type ControllerType => System.Reflection.Assembly.Load("Assembly-CSharp").GetType("ARNavigationController", true);

        [UnityTest]
        public IEnumerator MobileInterfaceLoadsAndRoomSelectionControlsNavigationState()
        {
            SceneManager.LoadScene("SampleScene");
            yield return null;
            yield return null;

            Component controller = (Component)Object.FindAnyObjectByType(ControllerType);
            Assert.That(controller, Is.Not.Null);
            Assert.That((int)ControllerType.GetProperty("RoomCount").GetValue(controller), Is.EqualTo(7));
            Assert.That(GameObject.Find("AR Navigation Mobile Interface"), Is.Not.Null);

            ControllerType.GetMethod("SelectStartRoom").Invoke(controller, new object[] { 0 });
            ControllerType.GetMethod("SelectDestination").Invoke(controller, new object[] { 1 });
            Assert.That((string)ControllerType.GetProperty("SelectedStartName").GetValue(controller), Is.EqualTo("ELT 3"));
            Assert.That((string)ControllerType.GetProperty("SelectedDestinationName").GetValue(controller), Is.EqualTo("ELT 4"));

            ControllerType.GetMethod("StartNavigation").Invoke(controller, null);
            Assert.That((bool)ControllerType.GetProperty("IsNavigating").GetValue(controller), Is.False,
                "Navigation should wait for map calibration.");
            ControllerType.GetMethod("CancelNavigation").Invoke(controller, null);
            Assert.That((string)ControllerType.GetProperty("SelectedDestinationName").GetValue(controller), Is.EqualTo("ELT 4"));
        }

        [UnityTest]
        public IEnumerator BothPickerMenusShowSevenOrderedWaypointButtonsWithWorkingSelectionsAndCompactLayout()
        {
            string[] expected = { "ELT 3", "ELT 4", "ELT 5", "ELT 6", "ELT 7", "ECR 17", "ECR 18" };
            SceneManager.LoadScene("SampleScene");
            yield return null;
            yield return null;

            Component controller = (Component)Object.FindAnyObjectByType(ControllerType);
            GameObject uiObject = GameObject.Find("AR Navigation Mobile Interface");
            Assert.That(uiObject, Is.Not.Null);
            var safeArea = (RectTransform)uiObject.transform.Find("Safe Area Content");
            Assert.That(safeArea, Is.Not.Null);
            // Run layout assertions at a known portrait reference size, independent of editor Game view aspect.
            safeArea.anchorMin = safeArea.anchorMax = new Vector2(0.5f, 0.5f);
            safeArea.sizeDelta = new Vector2(1080f, 1920f);
            safeArea.anchoredPosition = Vector2.zero;
            var picker = Resources.FindObjectsOfTypeAll<RectTransform>()
                .FirstOrDefault(rect => rect.name == "Location Picker")?.gameObject;
            var pickerSafe = (RectTransform)picker.transform.Find("Picker Safe Area");
            pickerSafe.anchorMin = pickerSafe.anchorMax = new Vector2(0.5f, 0.5f);
            pickerSafe.sizeDelta = new Vector2(1080f, 1920f);
            pickerSafe.anchoredPosition = Vector2.zero;

            var startButton = GameObject.Find("Choose starting location Button").GetComponent<Button>();
            startButton.onClick.Invoke();
            yield return null;
            Assert.That(picker.activeSelf, Is.True);
            var card = (RectTransform)picker.transform.Find("Picker Safe Area/Picker Card");
            var list = (RectTransform)card.Find("Room List");
            var viewport = (RectTransform)list.Find("Viewport");
            var content = (RectTransform)viewport.Find("Content");
            var scroll = list.GetComponent<ScrollRect>();
            Assert.That(scroll.vertical, Is.True);
            Assert.That(scroll.viewport, Is.SameAs(viewport));
            Assert.That(scroll.content, Is.SameAs(content));
            Assert.That(card.rect.height, Is.LessThan(900f), "Picker should size to its rows and header, not nearly fill portrait height.");
            AssertPickerRows(content, viewport, expected, requireVisible: true);

            // Invoke each real generated button and verify it selects the mapped authored start transform.
            var startRows = content.GetComponentsInChildren<Button>(true);
            for (int i = 0; i < expected.Length; i++)
            {
                startButton.onClick.Invoke();
                yield return null;
                content = (RectTransform)picker.transform.Find("Picker Safe Area/Picker Card/Room List/Viewport/Content");
                var rows = content.GetComponentsInChildren<Button>(true);
                Assert.That(rows.Length, Is.EqualTo(7));
                rows[i].onClick.Invoke();
                Assert.That((string)ControllerType.GetProperty("SelectedStartName").GetValue(controller), Is.EqualTo(expected[i]));
            }
            Assert.That(startRows.Length, Is.EqualTo(7));

            // Restore a valid start after checking every start callback.
            startButton.onClick.Invoke();
            yield return null;
            content = (RectTransform)picker.transform.Find("Picker Safe Area/Picker Card/Room List/Viewport/Content");
            content.GetComponentsInChildren<Button>(true)[0].onClick.Invoke();
            Assert.That((string)ControllerType.GetProperty("SelectedStartName").GetValue(controller), Is.EqualTo("ELT 3"));

            var destinationButton = GameObject.Find("Choose destination Button").GetComponent<Button>();
            destinationButton.onClick.Invoke();
            yield return null;
            content = (RectTransform)picker.transform.Find("Picker Safe Area/Picker Card/Room List/Viewport/Content");
            AssertPickerRows(content, (RectTransform)content.parent, expected, requireVisible: true);
            var destinationRows = content.GetComponentsInChildren<Button>(true);
            for (int i = 0; i < expected.Length; i++)
            {
                destinationButton.onClick.Invoke();
                yield return null;
                content = (RectTransform)picker.transform.Find("Picker Safe Area/Picker Card/Room List/Viewport/Content");
                var rows = content.GetComponentsInChildren<Button>(true);
                rows[i].onClick.Invoke();
                string selected = (string)ControllerType.GetProperty("SelectedDestinationName").GetValue(controller);
                Assert.That(selected, Is.EqualTo(i == 0 ? string.Empty : expected[i]),
                    $"Destination choice {expected[i]} must select its actual room or be rejected when it matches the start.");
            }

            // Re-selecting the current start invalidates it; changing the start clears the old destination.
            startButton.onClick.Invoke();
            yield return null;
            content = (RectTransform)picker.transform.Find("Picker Safe Area/Picker Card/Room List/Viewport/Content");
            content.GetComponentsInChildren<Button>(true)[4].onClick.Invoke();
            Assert.That((string)ControllerType.GetProperty("SelectedStartName").GetValue(controller), Is.EqualTo("ELT 7"));
            Assert.That((string)ControllerType.GetProperty("SelectedDestinationName").GetValue(controller), Is.Empty);

            destinationButton.onClick.Invoke();
            yield return null;
            content = (RectTransform)picker.transform.Find("Picker Safe Area/Picker Card/Room List/Viewport/Content");
            content.GetComponentsInChildren<Button>(true)[5].onClick.Invoke();
            Assert.That((string)ControllerType.GetProperty("SelectedDestinationName").GetValue(controller), Is.EqualTo("ECR 17"));

            destinationButton.onClick.Invoke();
            yield return null;
            var closeButton = picker.transform.Find("Picker Safe Area/Picker Card/Picker Header/Close Button").GetComponent<Button>();
            closeButton.onClick.Invoke();
            Assert.That(picker.activeSelf, Is.False, "Close button must dismiss the modal.");

            Assert.That((bool)ControllerType.GetProperty("CanStartNavigation").GetValue(controller), Is.False,
                "Selection without calibration and active tracking must not enable Start Navigation.");
            ControllerType.GetField("isCalibrated", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(controller, true);
            yield return null;
            Assert.That((bool)ControllerType.GetProperty("CanStartNavigation").GetValue(controller), Is.False,
                "Even a selected, calibrated route must not start while AR tracking is unavailable.");
            Assert.That(GameObject.Find("Start Navigation Button").GetComponent<Button>().interactable, Is.False);
            Assert.That(GameObject.Find("Calibrate Map Button").GetComponent<Button>().interactable, Is.False);
        }

        private static void AssertPickerRows(RectTransform content, RectTransform viewport, string[] expected, bool requireVisible)
        {
            var rows = content.GetComponentsInChildren<Button>(true);
            Assert.That(rows.Length, Is.EqualTo(7), "The live picker hierarchy must contain exactly seven room buttons.");
            Assert.That(content.childCount, Is.EqualTo(7));
            var contentBounds = RectTransformUtility.CalculateRelativeRectTransformBounds(content.parent, content);
            Assert.That(contentBounds.size.y, Is.GreaterThanOrEqualTo(content.rect.height - 1f));
            var viewportBounds = viewport.rect;
            for (int i = 0; i < rows.Length; i++)
            {
                Assert.That(rows[i].gameObject.name, Is.EqualTo(expected[i] + " Button"), $"Unexpected row at index {i}.");
                var rowRect = (RectTransform)rows[i].transform;
                var localBounds = RectTransformUtility.CalculateRelativeRectTransformBounds(viewport, rowRect);
                Assert.That(localBounds.size.y, Is.GreaterThan(0f), $"{expected[i]} must have a visible touch target height.");
                if (requireVisible)
                {
                    Assert.That(localBounds.min.y, Is.GreaterThanOrEqualTo(viewportBounds.yMin - 1f), $"{expected[i]} must be inside the portrait viewport.");
                    Assert.That(localBounds.max.y, Is.LessThanOrEqualTo(viewportBounds.yMax + 1f), $"{expected[i]} must be inside the portrait viewport.");
                }
                for (int j = i + 1; j < rows.Length; j++)
                {
                    var otherBounds = RectTransformUtility.CalculateRelativeRectTransformBounds(viewport, rows[j].transform);
                    Assert.That(Mathf.Abs(localBounds.center.y - otherBounds.center.y), Is.GreaterThan(1f),
                        $"{expected[i]} and {expected[j]} must occupy distinct visible positions.");
                }
            }
        }

        [UnityTest]
        public IEnumerator RouteArrowsRenderAndArrivalFeedbackSurvivesCancellation()
        {
            SceneManager.LoadScene("SampleScene");
            yield return null;
            yield return null;

            Component controller = (Component)Object.FindAnyObjectByType(ControllerType);
            Transform cameraTransform = ((Camera)ControllerType.GetField("arCamera").GetValue(controller)).transform;
            foreach (Behaviour behaviour in cameraTransform.GetComponents<Behaviour>())
                if (behaviour.GetType().Name.Contains("TrackedPoseDriver")) behaviour.enabled = false;

            Transform destinations = GameObject.Find("[Destinations]").transform;
            Transform start = destinations.GetChild(0);
            Transform destination = destinations.GetChild(1);
            ControllerType.GetMethod("SelectStartRoom").Invoke(controller, new object[] { 0 });
            ControllerType.GetMethod("SelectDestination").Invoke(controller, new object[] { 1 });
            ControllerType.GetField("isCalibrated", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(controller, true);
            cameraTransform.position = start.position + Vector3.up * (float)ControllerType.GetField("cameraHeightAboveFloor").GetValue(controller);

            // Test-only explicit pose simulation: exercise route visualization without claiming an XR session is tracking.
            ControllerType.GetField("isNavigating", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(controller, true);
            ControllerType.GetMethod("UpdateRoute", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(controller, null);
            float distance = (float)ControllerType.GetProperty("RemainingDistance").GetValue(controller);
            Assert.That(distance, Is.GreaterThan(0f));
            var line = controller.GetComponent<LineRenderer>();
            Assert.That(line.positionCount, Is.GreaterThanOrEqualTo(2));
            GameObject arrows = GameObject.Find("AR Navigation Arrows");
            Assert.That(arrows, Is.Not.Null);
            Assert.That(arrows.activeSelf, Is.True);
            Assert.That(arrows.GetComponent<MeshFilter>().sharedMesh.vertexCount, Is.GreaterThan(0));

            Vector3 originalRouteStart = line.GetPosition(0);
            Vector3 walkDirection = (line.GetPosition(1) - line.GetPosition(0)).normalized;
            cameraTransform.position = start.position + walkDirection * 0.25f +
                                       Vector3.up * (float)ControllerType.GetField("cameraHeightAboveFloor").GetValue(controller);
            ControllerType.GetMethod("UpdateRoute", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(controller, null);
            Assert.That((float)ControllerType.GetProperty("RemainingDistance").GetValue(controller), Is.LessThan(distance),
                "Remaining distance should advance without rebuilding the displayed route.");
            Assert.That(Vector3.Distance(line.GetPosition(0), originalRouteStart), Is.LessThan(0.01f));
            cameraTransform.position = start.position + walkDirection * 0.95f +
                                       Vector3.up * (float)ControllerType.GetField("cameraHeightAboveFloor").GetValue(controller);
            ControllerType.GetMethod("UpdateRoute", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(controller, null);
            Assert.That(Vector3.Distance(line.GetPosition(0), originalRouteStart), Is.GreaterThan(0.6f),
                "The route should refresh after meaningful tracked movement.");

            cameraTransform.position = destination.position + Vector3.up * (float)ControllerType.GetField("cameraHeightAboveFloor").GetValue(controller);
            ControllerType.GetField("arrivalDwellSeconds").SetValue(controller, 0f);
            ControllerType.GetMethod("UpdateRoute", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(controller, null);
            string arrivedMessage = (string)ControllerType.GetProperty("StatusMessage").GetValue(controller);
            Assert.That(arrivedMessage, Does.Contain("arrived at ELT 4"));
            Assert.That((bool)ControllerType.GetProperty("IsNavigating").GetValue(controller), Is.False);
            ControllerType.GetMethod("CancelNavigation").Invoke(controller, null);
            Assert.That((string)ControllerType.GetProperty("StatusMessage").GetValue(controller), Is.EqualTo(arrivedMessage));
        }
    }
}
