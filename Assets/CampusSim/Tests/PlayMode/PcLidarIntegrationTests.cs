using System.Collections;
using InhaExpress.Client.Presentation;
using InhaExpress.Client.Simulation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using System.Reflection;
using UnityEngine.Rendering.Universal;

namespace InhaExpress.Client.Tests
{
    public sealed class PcLidarIntegrationTests
    {
        private static bool HasDetectedClass(LidarSensorRig sensor, LidarEntityClass entityClass)
        {
            var observation = sensor.LatestObservation;
            for (int i = 0; i < observation.DetectionCount; i++)
                if (observation.GetDetection(i).EntityClass == entityClass) return true;
            return false;
        }

        [Test]
        public void FixedMapClassificationKeepsReturnsAndDoesNotOverrideActors()
        {
            var root = new GameObject("static-label-owner");
            var obstacle = GameObject.CreatePrimitive(PrimitiveType.Cube);
            try
            {
                obstacle.transform.position = new Vector3(0, .5f, 4);
                obstacle.AddComponent<LidarStaticMapSurface>();
                var source = PcLidarIntegration.Attach(root.transform, "static-label",
                    new Bounds(new Vector3(0, .5f, 0), new Vector3(1, 1, 2)));
                Physics.SyncTransforms();
                var frame = source.Capture(1);
                var integration = root.GetComponentInChildren<PcLidarIntegration>();
                Assert.That(System.Array.Exists(frame.ranges_mm, r => r > 0), Is.True, "Raw returns are retained for safety.");
                Assert.That(HasDetectedClass(integration.Sensor, LidarEntityClass.StaticObstacle), Is.True);
                Assert.That(integration.EffectActive, Is.False,
                    "Static map returns are retained for safety but do not alone announce an encounter.");
                source.SetSafetyFeedback(true);
                Assert.That(integration.EffectActive, Is.True, "A server-confirmed static hazard still activates feedback.");
                source.SetSafetyFeedback(false);
                obstacle.AddComponent<Rigidbody>().isKinematic = true;
                Physics.SyncTransforms();
                frame = source.Capture(2);
                Assert.That(HasDetectedClass(integration.Sensor, LidarEntityClass.Unknown), Is.True,
                    "A Rigidbody actor cannot inherit fixed-map semantics.");
                obstacle.AddComponent<LidarTarget>().SetObservedClass(LidarEntityClass.Pedestrian);
                frame = source.Capture(3);
                Assert.That(HasDetectedClass(integration.Sensor, LidarEntityClass.Pedestrian), Is.True);
            }
            finally { Object.DestroyImmediate(root); Object.DestroyImmediate(obstacle); }
        }

        [UnityTest]
        public IEnumerator PcMonochromeIsScopedToDetectedFollowedVehicleAndRestoresCamera()
        {
            var first = new GameObject("backdrop-V01");
            var second = new GameObject("backdrop-V02");
            var scenery = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var cameraObject = new GameObject("backdrop-camera", typeof(Camera));
            var otherCameraObject = new GameObject("other-camera", typeof(Camera));
            var controlsObject = new GameObject("backdrop-controls");
            var canvasObject = new GameObject("backdrop-ui", typeof(Canvas));
            var camera = cameraObject.GetComponent<Camera>();
            camera.enabled = false;
            otherCameraObject.GetComponent<Camera>().enabled = false;
            camera.clearFlags = CameraClearFlags.Skybox;
            camera.backgroundColor = Color.cyan;
            var data = camera.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = false;
            data.volumeLayerMask = 4;
            try
            {
                first.AddComponent<CameraTestVehicle>().id = "backdrop-V01";
                second.AddComponent<CameraTestVehicle>().id = "backdrop-V02";
                var controls = controlsObject.AddComponent<PcOperatorControls>();
                controls.Initialize(camera, new Bounds(Vector3.zero, Vector3.one * 100), 100);
                var bounds = new Bounds(new Vector3(0, .5f, 0), new Vector3(1, 1, 2));
                PcLidarIntegration.Attach(first.transform, "backdrop-V01", bounds);
                PcLidarIntegration.Attach(second.transform, "backdrop-V02", bounds);
                var a = first.GetComponentInChildren<LidarPointCloudVisualizer>();
                var b = second.GetComponentInChildren<LidarPointCloudVisualizer>();
                a.VisualizationEnabled = true;
                var effect = (LidarPointCloudBackdrop)typeof(LidarPointCloudBackdrop)
                    .GetField("instance", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
                Assert.That(effect.IsMonochromeFor(camera), Is.False, "Map overview keeps its colors.");
                controls.SelectVehicle("backdrop-V02");
                Assert.That(effect.IsMonochromeFor(camera), Is.False, "Another vehicle cannot inherit V01's effect.");
                controls.SelectVehicle("backdrop-V01");
                Assert.That(effect.IsMonochromeFor(camera), Is.True);
                Assert.That(effect.IsMonochromeFor(otherCameraObject.GetComponent<Camera>()), Is.False);
                var flags = BindingFlags.NonPublic | BindingFlags.Instance;
                var begin = typeof(LidarPointCloudBackdrop).GetMethod("BeginCamera", flags);
                var end = typeof(LidarPointCloudBackdrop).GetMethod("EndCamera", flags);
                var context = default(UnityEngine.Rendering.ScriptableRenderContext);
                begin.Invoke(effect, new object[] { context, camera });
                var volume = effect.GetComponent<UnityEngine.Rendering.Volume>();
                Assert.That(volume.enabled, Is.True);
                Assert.That(volume.sharedProfile.TryGet<ColorAdjustments>(out var color), Is.True);
                Assert.That(color.saturation.value, Is.EqualTo(-100));
                Assert.That(color.saturation.overrideState, Is.True);
                Assert.That(data.renderPostProcessing, Is.True);
                Assert.That(scenery.GetComponent<Renderer>().forceRenderingOff, Is.False);
                Assert.That(scenery.GetComponent<Collider>().enabled, Is.True);
                Assert.That(canvasObject.GetComponent<Canvas>().enabled, Is.True);
                Assert.That(camera.clearFlags, Is.EqualTo(CameraClearFlags.Skybox));
                Assert.That(camera.backgroundColor, Is.EqualTo(Color.cyan));
                // A different camera must disable the shared volume before it renders.
                begin.Invoke(effect, new object[] { context, otherCameraObject.GetComponent<Camera>() });
                Assert.That(volume.enabled, Is.False);
                Assert.That(data.renderPostProcessing, Is.False);
                Assert.That(data.volumeLayerMask.value, Is.EqualTo(4));
                // Render a red background through the actual URP camera pipeline.
                // Detection must desaturate it without making the scene black.
                camera.cullingMask = 0;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = Color.red;
                var gray = RenderFeedbackSample(camera, "detected-follow");
                Assert.That(gray.r, Is.GreaterThan(.05f));
                Assert.That(gray.r, Is.EqualTo(gray.g).Within(.025f));
                Assert.That(gray.g, Is.EqualTo(gray.b).Within(.025f));
                controls.SelectVehicle("backdrop-V02");
                var normal = RenderFeedbackSample(camera, "other-vehicle");
                Assert.That(normal.r, Is.GreaterThan(normal.g + .2f));
                controls.SelectVehicle("backdrop-V01");
                b.VisualizationEnabled = true;
                a.VisualizationEnabled = false;
                Assert.That(effect.IsMonochromeFor(camera), Is.False);
                controls.SelectVehicle("backdrop-V02");
                Assert.That(effect.IsMonochromeFor(camera), Is.True);
                begin.Invoke(effect, new object[] { context, camera });
                end.Invoke(effect, new object[] { context, camera });
                Assert.That(volume.enabled, Is.False);
                Assert.That(data.renderPostProcessing, Is.False);
                controls.ReleaseFollow();
                Assert.That(effect.IsMonochromeFor(camera), Is.False);
                controls.SelectVehicle("backdrop-V02");
                begin.Invoke(effect, new object[] { context, camera });
                b.VisualizationEnabled = false;
                Assert.That(volume.enabled, Is.False, "Releasing the final owner restores an in-flight camera.");
                Assert.That(data.volumeLayerMask.value, Is.EqualTo(4));
                yield return null;
            }
            finally
            {
                Object.Destroy(controlsObject);
                Object.Destroy(first); Object.Destroy(second); Object.Destroy(scenery);
                Object.Destroy(cameraObject); Object.Destroy(otherCameraObject); Object.Destroy(canvasObject);
            }
            yield return null;
        }

        private static Color RenderFeedbackSample(Camera camera, string name)
        {
            var target = RenderTexture.GetTemporary(64, 64, 24, RenderTextureFormat.ARGB32);
            var previous = RenderTexture.active;
            var image = new Texture2D(64, 64, TextureFormat.RGB24, false);
            try
            {
                UnityEngine.Rendering.RenderPipeline.SubmitRenderRequest(camera,
                    new UnityEngine.Rendering.RenderPipeline.StandardRequest { destination = target });
                RenderTexture.active = target;
                image.ReadPixels(new Rect(0, 0, 64, 64), 0, 0);
                image.Apply();
                var folder = System.IO.Path.GetFullPath(Application.dataPath +
                    "/../artifacts/validation/2026-10-30-lidar-monochrome");
                System.IO.Directory.CreateDirectory(folder);
                System.IO.File.WriteAllBytes(System.IO.Path.Combine(folder, name + ".png"), image.EncodeToPNG());
                return image.GetPixel(32, 32);
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(target);
                Object.Destroy(image);
            }
        }

        [Test]
        public void ContactPlaneRemovesUphillGroundButKeepsRaisedObstacleAndKerbFace()
        {
            var root = new GameObject("slope-lidar-owner");
            var ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            GameObject obstacle = null;
            try
            {
                root.transform.position = new Vector3(2000, 0, 2000);
                ground.transform.position = root.transform.position + new Vector3(0, -.1f, 0);
                ground.transform.localScale = new Vector3(40, .2f, 40);
                ground.transform.rotation = Quaternion.Euler(-8, 0, 0);
                var source = PcLidarIntegration.Attach(root.transform, "slope-test",
                    new Bounds(new Vector3(0, .5f, 0), new Vector3(1, 1, 2)));
                var sensor = root.GetComponentInChildren<PcLidarIntegration>().Sensor;
                Physics.SyncTransforms();
                source.Capture(1);
                Assert.That(sensor.LatestObservation.DetectionCount, Is.GreaterThan(0),
                    "Reproduce uphill ground above the old world-height cutoff.");
                sensor.SetGroundPlane(ground.transform.TransformPoint(new Vector3(0, .5f, 0)), ground.transform.up);
                sensor.SetFrameContext("slope-test", 2, 2);
                sensor.ScanNow();
                Assert.That(sensor.LatestObservation.DetectionCount, Is.Zero);
                Assert.That(sensor.GroundReturnCount, Is.GreaterThan(0));
                obstacle = GameObject.CreatePrimitive(PrimitiveType.Cube);
                obstacle.transform.position = root.transform.position + new Vector3(0, 1, 4);
                Physics.SyncTransforms();
                sensor.ScanNow();
                Assert.That(sensor.LatestObservation.DetectionCount, Is.GreaterThan(0));
                Assert.That(sensor.NearestObstacleSurface, Does.Contain(obstacle.name));
                // A low vertical face must remain visible, even beside a sloping road.
                obstacle.transform.localScale = new Vector3(2, .2f, .3f);
                obstacle.transform.position = root.transform.position + new Vector3(0, .68f, 4);
                Physics.SyncTransforms();
                sensor.ScanNow();
                Assert.That(sensor.LatestObservation.DetectionCount, Is.GreaterThan(0));
            }
            finally
            {
                if (obstacle != null) Object.DestroyImmediate(obstacle);
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(ground);
            }
        }

        // Channel 3 of the PC layout (-30..+15 deg, 7 channels) is -7.5 deg.
        private const int MinusSevenFiveChannel = 3;
        private const int ForwardColumn = 180, RearColumn = 0;

        private static int Range(PhysicalLidarFrame frame, int channel, int column) =>
            frame.ranges_mm[channel * frame.columns + column];

        [UnityTest]
        public IEnumerator ContinuousScanSendsRawRangesWithoutLabelsAndTracksRemoval()
        {
            var root = new GameObject("lidar-owner");
            var ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ground.transform.position = new Vector3(0,-.1f,0);
            ground.transform.localScale = new Vector3(40,.2f,40);
            var unrelated = GameObject.CreatePrimitive(PrimitiveType.Cube);
            unrelated.transform.position = new Vector3(25,1,25);
            var ui = new GameObject("operator-canvas").AddComponent<Canvas>();
            GameObject obstacle = null;
            try
            {
                var source = PcLidarIntegration.Attach(root.transform,"V01",new Bounds(new Vector3(0,.5f,0),new Vector3(1,1,2)));
                Assert.That(PhysicalLidarSource.Create, Is.Not.Null);
                Physics.SyncTransforms();
                var empty = source.Capture(1);
                Assert.That(empty.valid, Is.True);
                Assert.That(empty.columns, Is.EqualTo(360));
                Assert.That(empty.channel_elevations_deg.Length, Is.EqualTo(7));
                Assert.That(empty.channel_elevations_deg[MinusSevenFiveChannel], Is.EqualTo(-7.5f).Within(.001f));
                Assert.That(empty.ranges_mm.Length, Is.EqualTo(2520));
                Assert.That(empty.azimuth_span_deg, Is.EqualTo(360f));
                Assert.That(empty.sensor_up_m, Is.EqualTo(1.25f).Within(.01f));
                Assert.That(empty.ground_up_m, Is.EqualTo(0f).Within(.01f), "Body-bottom fallback without wheels.");
                // Raw sensor: flat ground is returned, not pre-filtered in Unity.
                float flatGroundMm = 1250f / Mathf.Sin(7.5f * Mathf.Deg2Rad);
                Assert.That(Range(empty, MinusSevenFiveChannel, ForwardColumn), Is.EqualTo(flatGroundMm).Within(40));

                obstacle = GameObject.CreatePrimitive(PrimitiveType.Cube);
                obstacle.transform.position = new Vector3(0,.5f,4);
                obstacle.AddComponent<LidarTarget>().SetObservedClass(LidarEntityClass.Pedestrian);
                Physics.SyncTransforms();
                var detected = source.Capture(2);
                float faceMm = 3500f / Mathf.Cos(7.5f * Mathf.Deg2Rad);
                Assert.That(Range(detected, MinusSevenFiveChannel, ForwardColumn), Is.EqualTo(faceMm).Within(40));
                Assert.That(JsonUtility.ToJson(detected), Does.Not.Contain("pedestrian"), "Labels must not cross the control boundary.");
                var integration = root.GetComponentInChildren<PcLidarIntegration>();
                Assert.That(integration.EffectActive, Is.True);
                source.Capture(3);
                Assert.That(unrelated.GetComponent<Renderer>().enabled, Is.True);
                Assert.That(ui.enabled, Is.True, "Automatic overlay must preserve the operator UI.");
                Assert.That(integration.GetComponentInChildren<ParticleSystem>().particleCount, Is.GreaterThan(0));
                Object.Destroy(obstacle); obstacle=null;
                yield return null;
                Physics.SyncTransforms();
                Assert.That(Range(source.Capture(4), MinusSevenFiveChannel, ForwardColumn), Is.EqualTo(flatGroundMm).Within(40));
                yield return new WaitForSecondsRealtime(.8f);
                source.SetSafetyFeedback(false);
                source.Capture(5);
                Assert.That(integration.EffectActive, Is.False);
            }
            finally
            {
                if(obstacle!=null) Object.Destroy(obstacle);
                Object.Destroy(root); Object.Destroy(ground); Object.Destroy(unrelated); Object.Destroy(ui.gameObject);
            }
            yield return null;
        }

        [Test]
        public void WeatherRangeAndOutageReachThePhysicalFrame()
        {
            var root = new GameObject("weather-owner");
            var obstacle = GameObject.CreatePrimitive(PrimitiveType.Cube);
            try
            {
                obstacle.transform.position = new Vector3(0, 1.3f, 10);
                var source = PcLidarIntegration.Attach(root.transform, "weather-test",
                    new Bounds(new Vector3(0, .5f, 0), new Vector3(1, 1, 2)));
                var rig = root.GetComponentInChildren<PcLidarIntegration>().Sensor;
                Physics.SyncTransforms();
                Assert.That(System.Array.Exists(source.Capture(1).ranges_mm, r => r > 0), Is.True);
                rig.ConfigureWeather(LidarSensorRig.WeatherCondition.Fog, LidarSensorRig.RoadCondition.Wet);
                var fog = source.Capture(2);
                Assert.That(fog.max_range_m, Is.EqualTo(6));
                Assert.That(fog.weather, Is.EqualTo("fog"));
                Assert.That(fog.road_condition, Is.EqualTo("wet"));
                Assert.That(System.Array.TrueForAll(fog.ranges_mm, r => r == 0), Is.True, "The 10 m return is beyond fog range.");
                Assert.That(fog.valid, Is.True);
                rig.ConfigureWeather(LidarSensorRig.WeatherCondition.Snow, LidarSensorRig.RoadCondition.Snow, 1f);
                var outage = source.Capture(3);
                Assert.That(outage.max_range_m, Is.EqualTo(8));
                Assert.That(outage.observation_complete, Is.False);
                Assert.That(outage.valid, Is.False);
                Assert.That(System.Array.TrueForAll(outage.ranges_mm, r => r == 0), Is.True);
                Assert.That(outage.ranges_mm.Length, Is.EqualTo(2520));
                rig.ConfigureWeather(LidarSensorRig.WeatherCondition.Rain, LidarSensorRig.RoadCondition.Wet);
                Assert.That(source.Capture(4).max_range_m, Is.EqualTo(12));
                Assert.That(source.Capture(5).valid, Is.True);
            }
            finally { Object.DestroyImmediate(root); Object.DestroyImmediate(obstacle); }
        }

        [UnityTest]
        public IEnumerator OperatorEnvironmentTogglesApplyToExistingAndLateVehicles()
        {
            var root1 = new GameObject("environment-V01");
            var root2 = new GameObject("environment-V02");
            var root3 = new GameObject("environment-V03");
            var hudObject = new GameObject("environment-HUD");
            var canvasObject = new GameObject("environment-canvas", typeof(RectTransform), typeof(Canvas),
                typeof(CanvasScaler), typeof(GraphicRaycaster));
            try
            {
                Assert.That(PhysicalLidarSource.ApplyEnvironment("clear", "dry"), Is.True);
                var bounds = new Bounds(new Vector3(0, .5f, 0), new Vector3(1, 1, 2));
                var first = PcLidarIntegration.Attach(root1.transform, "V01", bounds);
                var second = PcLidarIntegration.Attach(root2.transform, "V02", bounds);
                var rig = root1.GetComponentInChildren<PcLidarIntegration>().Sensor;
                rig.ConfigureWeather(LidarSensorRig.WeatherCondition.Clear, LidarSensorRig.RoadCondition.Dry, 1f, 99);
                canvasObject.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
                var scaler = canvasObject.GetComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1280, 720);
                var hud = hudObject.AddComponent<WebClientHud>();
                // The LAN footer now reads the map's server port during UI construction.
                var mapObject = new GameObject("environment-map");
                mapObject.transform.SetParent(hudObject.transform);
                mapObject.SetActive(false);
                var map = mapObject.AddComponent<InhaExpress.Client.MapClient.CampusServerMap>();
                map.connectOnPlay = false;
                typeof(WebClientHud).GetField("map", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(hud, map);
                var root = (RectTransform)canvasObject.transform;
                FixtureUiFactory.EnsureEventSystem(root);
                typeof(WebClientHud).GetMethod("BuildOperator", BindingFlags.NonPublic | BindingFlags.Instance)
                    .Invoke(hud, new object[] { root });
                var sidebar = root.Find("Dashboard/Sidebar");
                Assert.That(sidebar.Find("Request Tabs/Environment Tab"), Is.Null);
                Assert.That(sidebar.Find("Environment Card"), Is.Null);
                var card = root.Find("Dashboard/Workspace/Map Header");
                var rain = card.Find("Weather Choices/Weather rain").GetComponent<Toggle>();
                rain.OnPointerClick(new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left });
                Assert.That(first.Capture(1).weather, Is.EqualTo("rain"));
                Assert.That(first.Capture(2).observation_complete, Is.False, "UI must preserve the experiment outage setting.");
                Assert.That(second.Capture(1).road_condition, Is.EqualTo("wet"));
                var late = PcLidarIntegration.Attach(root3.transform, "V03", bounds);
                Assert.That(late.Capture(1).weather, Is.EqualTo("rain"));
                Assert.That(late.Capture(2).road_condition, Is.EqualTo("wet"));
                PhysicalLidarSource.ApplyEnvironment("rain", "ice");
                typeof(WebClientHud).GetMethod("RefreshEnvironmentControls", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(hud, null);
                Assert.That(card.Find("Environment Status"), Is.Null);
                Assert.That(PhysicalLidarSource.ReadEnvironment().road_condition, Is.EqualTo("ice"));
                var fog = card.Find("Weather Choices/Weather fog").GetComponent<Toggle>();
                fog.OnPointerClick(new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left });
                fog.OnPointerClick(new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left });
                Assert.That(fog.isOn, Is.True, "Weather must retain one selection.");
                Assert.That(rain.isOn, Is.False);
                Assert.That(PhysicalLidarSource.ReadEnvironment().road_condition, Is.EqualTo("dry"));
                Assert.That(PhysicalLidarSource.ReadEnvironment().min_range_m, Is.EqualTo(6));
                var snow = card.Find("Weather Choices/Weather snow").GetComponent<Toggle>();
                snow.OnPointerClick(new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left });
                Assert.That(late.Capture(3).road_condition, Is.EqualTo("snow"));
                fog.OnPointerClick(new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left });
                var footer = root.Find("Dashboard/Workspace/Map Footer");
                Assert.That(footer.Find("Attribution"), Is.Null);
                var addressLabel = root.Find("Header/Server Address").GetComponentInChildren<Text>();
                Assert.That(addressLabel.text, Does.StartWith("서버 "));
                Assert.That(addressLabel.text, Does.Not.Contain(" / "), "Header must show a single server address.");
                Assert.That(footer.Find("Dispatch Policy/Dispatch/Policy Select/Dispatch Policy Dropdown"), Is.Not.Null);
                Assert.That(footer.Find("Dispatch Policy/Planner/Planner Select/Route Planner Dropdown"), Is.Not.Null);
                Assert.That(sidebar.Find("Dispatch Policy"), Is.Null);
                var spawn = footer.Find("Pedestrian Spawn").GetComponentInChildren<Button>();
                Assert.That(spawn.gameObject.activeInHierarchy, Is.False);
                Assert.That(spawn.interactable, Is.False);
                spawn.onClick.Invoke();
                Assert.That(footer.Find("Pedestrian Status").GetComponentInChildren<Text>(true).text,
                    Does.Contain("차량을 추적"));
                Canvas.ForceUpdateCanvases();
                var statusRect = (RectTransform)footer.Find("Pedestrian Status");
                var policyRect = (RectTransform)footer.Find("Dispatch Policy");
                var statusCorners = new Vector3[4]; var policyCorners = new Vector3[4];
                statusRect.GetWorldCorners(statusCorners); policyRect.GetWorldCorners(policyCorners);
                Assert.That(statusCorners[1].y, Is.LessThanOrEqualTo(policyCorners[0].y),
                    "Pedestrian feedback occupies a separate row from dispatch/route controls.");
                sidebar.Find("Request Tabs/Operation Log Tab").GetComponent<Toggle>().isOn = true;
                Assert.That(spawn.gameObject.activeInHierarchy, Is.False);
                Assert.That(fog.gameObject.activeInHierarchy, Is.True);
                // Manual Inspector divergence must be visible rather than claiming all vehicles applied.
                rig.SetEnvironment(LidarSensorRig.WeatherCondition.Snow, LidarSensorRig.RoadCondition.Wet);
                typeof(WebClientHud).GetMethod("RefreshEnvironmentControls", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(hud, null);
                Assert.That(PhysicalLidarSource.ReadEnvironment().matching_sensor_count, Is.EqualTo(2));
                PhysicalLidarSource.ApplyEnvironment("fog", "wet");
                typeof(WebClientHud).GetMethod("RefreshEnvironmentControls", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(hud, null);
                Assert.That(PhysicalLidarSource.ApplyEnvironment("storm", "mud"), Is.False);
                var controlsCamera = new GameObject("toolbar-camera");
                controlsCamera.transform.SetParent(hudObject.transform);
                var camera = controlsCamera.AddComponent<Camera>(); camera.enabled = false;
                var vehicle = root1.AddComponent<CameraTestVehicle>(); vehicle.id = "V01";
                var body = root1.AddComponent<Rigidbody>(); body.useGravity = false;
                body.linearVelocity = new Vector3(3, 0, 4);
                var controls = hudObject.AddComponent<PcOperatorControls>();
                controls.Initialize(camera, new Bounds(Vector3.zero, Vector3.one * 100), 100);
                typeof(WebClientHud).GetProperty("OperatorControls").SetValue(hud, controls);
                var readout = typeof(WebClientHud).GetMethod("RefreshVehicleReadout", BindingFlags.NonPublic | BindingFlags.Instance);
                readout.Invoke(hud, null);
                var speedPanel = root.Find("Dashboard/Workspace/Map Viewport/Selected Vehicle Speed");
                Assert.That(speedPanel.GetComponent<Image>(), Is.Null, "Speed has no background panel.");
                Assert.That(speedPanel.Find("Vehicle Status"), Is.Null);
                Assert.That(speedPanel.GetComponentInChildren<Outline>(true), Is.Not.Null);
                Assert.That(speedPanel.GetComponentInChildren<Shadow>(true), Is.Not.Null);
                Assert.That(speedPanel.gameObject.activeSelf, Is.False);
                controls.SelectVehicle("V01"); readout.Invoke(hud, null);
                Assert.That(speedPanel.gameObject.activeSelf, Is.True);
                Assert.That(speedPanel.GetComponentInChildren<Text>().text, Is.EqualTo("18 km/h"));
                typeof(WebClientHud).GetMethod("RefreshEnvironmentControls", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(hud, null);
                Assert.That(spawn.interactable, Is.True);
                Assert.That(spawn.gameObject.activeInHierarchy, Is.True);
                controls.ReleaseFollow();
                typeof(WebClientHud).GetMethod("RefreshEnvironmentControls", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(hud, null);
                Assert.That(controls.SelectedVehicle, Is.Not.Null, "Release keeps the last selection.");
                Assert.That(spawn.gameObject.activeInHierarchy, Is.False);
                Assert.That(spawn.interactable, Is.False);
                int spawnCalls = 0;
                var previousSpawn = PedestrianScenarioActions.SpawnAhead;
                try
                {
                    PedestrianScenarioActions.SpawnAhead = _ => { spawnCalls++; return "unexpected"; };
                    spawn.onClick.Invoke();
                    Assert.That(spawnCalls, Is.Zero, "A stale/programmatic click must also check follow mode.");
                }
                finally { PedestrianScenarioActions.SpawnAhead = previousSpawn; }
                controls.DeselectVehicle(); readout.Invoke(hud, null);
                Assert.That(speedPanel.gameObject.activeSelf, Is.False);
                controls.SelectVehicle("V01"); readout.Invoke(hud, null);
                var refreshQueue = typeof(WebClientHud).GetMethod("RefreshQueue", BindingFlags.NonPublic | BindingFlags.Instance);
                var sample = new InhaExpress.Client.MapClient.CampusServerMap.SharedState {
                    queue = new [] {
                        new InhaExpress.Client.MapClient.CampusServerMap.TransportRequest { id = "R101", status = "QUEUED", pickup_landmark_id = "정문", dropoff_landmark_id = "하이테크" },
                        new InhaExpress.Client.MapClient.CampusServerMap.TransportRequest { id = "R102", status = "QUEUED", pickup_landmark_id = "후문", dropoff_landmark_id = "60주년" }
                    }
                };
                Canvas.ForceUpdateCanvases();
                refreshQueue.Invoke(hud, new object[] {sample, true});
                var details = sidebar.Find("Queue Card/Queue Viewport/Queue Content").GetComponentInChildren<Text>();
                Assert.That(details.text, Does.Contain("정문 → 하이테크"));
                Assert.That(details.text.IndexOf("R101"), Is.LessThan(details.text.IndexOf("R102")));
                refreshQueue.Invoke(hud, new object[] {sample, false});
                Assert.That(details.text, Does.Not.Contain("R101"), "Offline state must not display a stale queue.");
                refreshQueue.Invoke(hud, new object[] {sample, true});
                var refreshLog = typeof(WebClientHud).GetMethod("RefreshOperationLog", BindingFlags.NonPublic | BindingFlags.Instance);
                sample.run_id = "readable-log-test";
                sample.vehicles = new [] { new InhaExpress.Client.MapClient.CampusServerMap.Vehicle {
                    id = "V01", phase = "TO_PICKUP", destination = "정문" } };
                sample.requests = new [] { new InhaExpress.Client.MapClient.CampusServerMap.TransportRequest {
                    id = "R101", status = "ASSIGNED", vehicle_id = "V01", pickup_landmark_id = "정문", dropoff_landmark_id = "하이테크" } };
                refreshLog.Invoke(hud, new object[] {sample});
                var logText = sidebar.Find("Operation Log Card/Log Viewport/Log Content").GetComponentInChildren<Text>();
                Assert.That(logText.text, Does.Contain("픽업 정문 방면 이동"));
                Assert.That(logText.text, Does.Contain("V01 차량 배정"));
                Assert.That(logText.text, Does.Not.Contain("TO_PICKUP"));
                string before = logText.text;
                refreshLog.Invoke(hud, new object[] {sample});
                Assert.That(logText.text, Is.EqualTo(before), "Unchanged snapshots must not repeat log entries.");
                sample.requests[0].status = "COMPLETED";
                refreshLog.Invoke(hud, new object[] {sample});
                Assert.That(logText.text, Does.Contain("운송 완료"));
                refreshLog.Invoke(hud, new object[] { new InhaExpress.Client.MapClient.CampusServerMap.SharedState {run_id="empty-new-run"} });
                Assert.That(logText.text, Is.Empty, "A new session must clear the previous session's log.");
                refreshLog.Invoke(hud, new object[] {sample});
                Canvas.ForceUpdateCanvases();
                foreach (var toggle in card.GetComponentsInChildren<Toggle>())
                {
                    var corners = new Vector3[4]; ((RectTransform)toggle.transform).GetWorldCorners(corners);
                    Assert.That(corners[2].x - corners[0].x, Is.GreaterThan(30));
                    Assert.That(corners[2].y - corners[0].y, Is.GreaterThan(20));
                }
                if (System.Environment.GetEnvironmentVariable("INHA_CAPTURE_ENVIRONMENT_UI") == "1")
                {
                    ScreenCapture.CaptureScreenshot(System.IO.Path.GetFullPath(Application.dataPath + "/../.utmp/environment-ui.png"));
                    yield return null;
                    yield return null;
                }
            }
            finally
            {
                Object.DestroyImmediate(root1); Object.DestroyImmediate(root2); Object.DestroyImmediate(root3);
                Object.DestroyImmediate(hudObject); Object.DestroyImmediate(canvasObject);
                PhysicalLidarSource.ApplyEnvironment("clear", "dry");
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator RearVehicleIsObservedAndOwnCollidersAreExcluded()
        {
            var root=GameObject.CreatePrimitive(PrimitiveType.Cube);
            var other=GameObject.CreatePrimitive(PrimitiveType.Cube);
            try
            {
                root.transform.position=new Vector3(0,.5f,0);
                other.transform.position=new Vector3(0,.5f,-4);
                PcLidarIntegration.Attach(other.transform,"V02",new Bounds(Vector3.zero,Vector3.one));
                var source=PcLidarIntegration.Attach(root.transform,"V01",new Bounds(Vector3.zero,Vector3.one));
                Physics.SyncTransforms();
                var frame=source.Capture(1);
                Assert.That(frame.valid,Is.True);
                float faceMm = 3500f / Mathf.Cos(7.5f * Mathf.Deg2Rad);
                Assert.That(Range(frame, MinusSevenFiveChannel, RearColumn), Is.EqualTo(faceMm).Within(40));
                Assert.That(System.Array.Exists(frame.ranges_mm, r => r > 0 && r < 900), Is.False, "Own body must be masked.");
                Assert.That(JsonUtility.ToJson(frame), Does.Not.Contain("vehicle"));
            }
            finally {Object.Destroy(root);Object.Destroy(other);}
            yield return null;
        }
    }
}
