using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing.Printing;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;
using HullcamVDS;
using OfCourseIStillLoveYou.Client;

namespace OfCourseIStillLoveYou
{
    public class TrackingCamera
    {
        private const float ButtonHeight = 18;
        private const float Gap = 2;
        private const float Line = ButtonHeight + Gap;
        private const float ButtonWidth = 3 * ButtonHeight + 4 * Gap;
        private const float MaxCameraSize = 360;
        private const string Altitude = "ALTITUDE: ", Km = " KM", Speed = "SPEED: ", Kmh = " KM/H";

        private static readonly float controlsStartY = 22;
        private static readonly Font TelemetryFont = Font.CreateDynamicFontFromOSFont("Bahnschrift Semibold", 16);

        private static readonly GUIStyle ButtonStyle = new GUIStyle(HighLogic.Skin.button)
        { fontSize = 10, wordWrap = true };


        private static readonly GUIStyle TelemetryGuiStyle = new GUIStyle()
        { alignment = TextAnchor.MiddleCenter, normal = new GUIStyleState() { textColor = Color.white }, fontStyle = FontStyle.Bold, font = TelemetryFont };

        HullcamVDS.CameraFilter.eCameraMode _cameraMode;
        MovieTimeFilterWrapper _filter;

        public static Texture2D ResizeTexture =
            GameDatabase.Instance.GetTexture("OfCourseIStillLoveYou/Textures/" + "resizeSquare", false);

        private readonly MuMechModuleHullCamera _hullcamera;


        private float _initialCamImageWidthSize = 360;
        private float _initialCamImageHeightSize = 360;
        private float _adjCamImageWidthSize = 360;
        private float _adjCamImageHeightSize = 360;

        private readonly List<Camera> _cameras = new List<Camera>();
        private float _windowHeight;

        private Rect _windowRect;
        private float _windowWidth;
        public RenderTexture TargetCamRenderTexture;
        private readonly Texture2D _texture2D = new Texture2D(Settings.Width, Settings.Height, TextureFormat.ARGB32, false);

        private byte[] _jpgTexture;

        private bool _lastDebugModeState = false;
        private bool _diagnosticRun = false;

        public PerCameraSunflareManager sunflareManager; GameObject sunflareManagerGO;

        public void SyncDebugMode(bool debugModeEnabled)
        {
            foreach (var camera in _cameras)
            {
                if (camera != null)
                {
                    if (!debugModeEnabled)
                    {
                        DeferredWrapper.ForceRemoveDebugMode(camera);
                    }
                    else
                    {
                        DeferredWrapper.ToggleCameraDebugMode(camera, debugModeEnabled);
                    }
                }
            }

            _lastDebugModeState = debugModeEnabled;
        }

        public void UpdateCameras()
        {
            if (_filter != null)
                _filter.Update();

            if (sunflareManager != null)
                sunflareManager.UpdateFlares();

            for (int i = _cameras.Count - 1; i >= 0; --i)
            {
                if (_cameras[i] != null)
                {
                    _cameras[i].fieldOfView = _hullcamera.cameraFoV;
                    _cameras[i].Render();
                }
            }
        }


        public void LateUpdateCameras()
        {
            // disabled due to performance issues
            //ScattererWrapper.ForceEnableScattererComponents(_cameras[0]);
            //ScattererOceanHelper.UpdateOceanForCamera(_cameras[0]);

            ParallaxWrapper.RenderParallaxToCustomCameras(new Camera[] { _cameras[0] });
            FireflyWrapper.UpdateFireflyForCamera(_cameras[0], _hullcamera.vessel);
        }

        public void SendCameraImage()
        {
            if (!StreamingEnabled) return;

            Graphics.CopyTexture(TargetCamRenderTexture, _texture2D);

            AsyncGPUReadback.Request(_texture2D, 0,
                request =>
                {
                    Task.Run(() => _texture2D.LoadRawTextureData(request.GetData<byte>()))
                        .ContinueWith(previous => _jpgTexture = _texture2D.EncodeToJPG())
                        .ContinueWith(previous =>
                            GrpcClient.SendCameraTextureAsync(new CameraData
                            {
                                CameraId = Id.ToString(),
                                CameraName = Name,
                                Speed = SpeedString,
                                Altitude = AltitudeString,
                                Texture = _jpgTexture
                            }));
                }
            );
        }

        public TrackingCamera(int id, MuMechModuleHullCamera hullcamera)
        {
            Id = id;
            _hullcamera = hullcamera;

            TargetCamRenderTexture = new RenderTexture(Settings.Width, Settings.Height, 24, RenderTextureFormat.ARGB32)
            {
                antiAliasing = 1
            };

            TargetCamRenderTexture.Create();

            CalculateInitialSize();

            _windowWidth = _adjCamImageWidthSize + 3 * ButtonHeight + 16 + 2 * Gap;
            _windowHeight = _adjCamImageHeightSize + 23;
            _windowRect = new Rect(Screen.width - _windowWidth, Screen.height - _windowHeight, _windowWidth,
                _windowHeight);

            SetCameras();

            ResizeTargetWindow();

            Enabled = true;
        }

        private void CalculateInitialSize()
        {
            // always use square gui window

            // if (Settings.Width > Settings.Height)
            // {
            //     _adjCamImageHeightSize = Settings.Height * MaxCameraSize / Settings.Width;
            //     _initialCamImageHeightSize = _adjCamImageHeightSize;
            //     _adjCamImageWidthSize = 360;
            // }
            // else
            // {
            //     _adjCamImageWidthSize = Settings.Width * MaxCameraSize / Settings.Height;
            //     _initialCamImageWidthSize = _adjCamImageWidthSize;
            //     _adjCamImageHeightSize = 360;
            // }

            _initialCamImageWidthSize = _initialCamImageHeightSize =
                _adjCamImageWidthSize = _adjCamImageHeightSize = MaxCameraSize;

            Debug.Log($"OCISLY:_adjCamImageHeightSize = {_adjCamImageHeightSize} _adjCamImageWidthSize = {_adjCamImageWidthSize}");
        }

        public string Name { get; private set; }

        public Vessel Vessel => _hullcamera?.vessel;

        public int Id { get; }

        public bool Enabled { get; set; }

        public float TargetWindowScaleMax { get; set; } = 3f;

        public float TargetWindowScaleMin { get; set; } = 0.5f;


        public bool ResizingWindow { get; set; }

        public float TargetWindowScale { get; set; } = 1;
        public string AltitudeString { get; private set; }
        public string SpeedString { get; private set; }
        public bool StreamingEnabled { get; private set; }

        private void SetCameras()
        {
            // === NEAR CAMERA (Main rendering camera) ===
            var cam1Obj = new GameObject("OCISLY_NearCamera");
            var partNearCamera = cam1Obj.AddComponent<Camera>();
            var mainCamera = Camera.allCameras.FirstOrDefault (_cam => _cam.name == "Camera 00");

            partNearCamera.CopyFrom(mainCamera);
            partNearCamera.transform.parent = _hullcamera.cameraTransformName.Length <= 0
                ? _hullcamera.part.transform
                : _hullcamera.part.FindModelTransform(_hullcamera.cameraTransformName);
            partNearCamera.transform.position = _hullcamera.transform.position;
            partNearCamera.transform.rotation = _hullcamera.transform.rotation;
            partNearCamera.transform.localPosition = _hullcamera.cameraPosition;
            partNearCamera.transform.localRotation = Quaternion.LookRotation(_hullcamera.cameraForward, _hullcamera.cameraUp);
            partNearCamera.nearClipPlane = 0.07f;
            partNearCamera.fieldOfView = _hullcamera.cameraFoV;
            partNearCamera.targetTexture = TargetCamRenderTexture;
            partNearCamera.allowHDR = true;
            partNearCamera.allowMSAA = true;
            partNearCamera.enabled = false;
            partNearCamera.forceIntoRenderTexture = true;
            _cameras.Add(partNearCamera);
            cam1Obj.AddComponent<CanvasHack>();

            // Apply rendering enhancements
            TufxWrapper.AddPostProcessing(partNearCamera);
            DeferredWrapper.EnableDeferredRendering(partNearCamera);
            DeferredWrapper.SyncDebugMode(partNearCamera);
            ParallaxWrapper.ApplyParallaxToCamera(partNearCamera, mainCamera);

            // === SCALED SPACE CAMERA (Distant objects, planets) ===
            var cam2Obj = new GameObject("OCISLY_ScaledCamera");
            var partScaledCamera = cam2Obj.AddComponent<Camera>();
            var mainSkyCam = Camera.allCameras.FirstOrDefault (_cam => _cam.name == "Camera ScaledSpace");

            partScaledCamera.CopyFrom(mainSkyCam);

            partScaledCamera.transform.parent = mainSkyCam.transform.parent;
            partScaledCamera.transform.position = _hullcamera.transform.position;
            partScaledCamera.transform.rotation = _hullcamera.transform.rotation;
            partScaledCamera.transform.localPosition = _hullcamera.cameraPosition;
            partScaledCamera.transform.localRotation = Quaternion.LookRotation(_hullcamera.cameraForward, _hullcamera.cameraUp);
            partScaledCamera.transform.localScale = Vector3.one;
            partScaledCamera.nearClipPlane = 0.07f;
            partScaledCamera.fieldOfView = _hullcamera.cameraFoV;
            partScaledCamera.targetTexture = TargetCamRenderTexture;
            partScaledCamera.allowHDR = true;
            partScaledCamera.allowMSAA = true;
            partScaledCamera.enabled = false;
            partScaledCamera.forceIntoRenderTexture = true;
            _cameras.Add(partScaledCamera);

            // Sync rotation with near camera
            var camRotator = cam2Obj.AddComponent<TgpCamRotator>();
            camRotator.NearCamera = partNearCamera;
            cam2Obj.AddComponent<CanvasHack>();

            // === GALAXY CAMERA (Skybox, stars) ===
            var galaxyCamObj = new GameObject("OCISLY_GalaxyCamera");
            var galaxyCam = galaxyCamObj.AddComponent<Camera>();
            var mainGalaxyCam = Camera.allCameras.FirstOrDefault (_cam => _cam.name == "GalaxyCamera");

            galaxyCam.CopyFrom(mainGalaxyCam);

            galaxyCam.transform.parent = mainGalaxyCam.transform.parent;
            galaxyCam.transform.position = _hullcamera.transform.position;
            galaxyCam.transform.rotation = _hullcamera.transform.rotation;
            galaxyCam.transform.localPosition = _hullcamera.cameraPosition;
            galaxyCam.transform.localRotation = Quaternion.LookRotation(_hullcamera.cameraForward, _hullcamera.cameraUp);
            galaxyCam.transform.localScale = Vector3.one;
            galaxyCam.nearClipPlane = 0.07f;
            galaxyCam.fieldOfView = _hullcamera.cameraFoV;
            galaxyCam.targetTexture = TargetCamRenderTexture;
            galaxyCam.allowHDR = true;
            galaxyCam.allowMSAA = true;
            galaxyCam.enabled = false;
            galaxyCam.forceIntoRenderTexture = true;
            _cameras.Add(galaxyCam);

            var camRotatorGalaxy = galaxyCamObj.AddComponent<TgpCamRotator>();
            camRotatorGalaxy.NearCamera = partNearCamera;
            galaxyCamObj.AddComponent<CanvasHack>();

            // === VISUAL EFFECTS (Apply to NearCamera) ===

            // Scatterer (atmosphere, ocean)
            //ScattererWrapper.ApplyScattererToCamera(partNearCamera); // disabled due to performance issues

            // Initialize Scatterer ocean rendering
            //if (ScattererWrapper.IsScattererAvailable)
            //    ScattererOceanHelper.FindOceanNode(_hullcamera.vessel.mainBody.name); // disabled due to performance issues

            // Scatterer SunFlare
            try
            {
                sunflareManagerGO = new GameObject("HullCamera Scatterer sunflare manager" + _hullcamera.GetInstanceID());
                sunflareManager = sunflareManagerGO.AddComponent<PerCameraSunflareManager>();
                sunflareManager.Init(partScaledCamera, partNearCamera);
            }
            catch (Exception e)
            {
                Debug.Log("[OCISLY] Cannot create sunflare manager");
            }

            // EVE (clouds, water effects)
            EVEWrapper.ApplyEVEToCamera(partNearCamera, mainCamera);

            _cameraMode = (HullcamVDS.CameraFilter.eCameraMode)_hullcamera.cameraMode;
            ApplyCameraFilter(partNearCamera);

            // === SET CAMERA NAMES (MUST BE LAST - CopyFrom overwrites names) ===
            _cameras[0].name = "jrNear";
            _cameras[1].name = "jrScaled";
            _cameras[2].name = "jrGalaxy";
        }

        private void ApplyCameraFilter(Camera camera)
        {
            _filter = camera.gameObject.AddComponent<MovieTimeFilterWrapper>();
            if (_filter != null)
            {
                _filter.Initialize(camera.name + "Filter", MovieTimeFilterWrapper.eFilterType.Flight);
                _filter.SetMode(_cameraMode);

                if (_cameraMode == CameraFilter.eCameraMode.DockingCam)
                    _filter.AttachDockingOverlayToCamera(camera, _adjCamImageWidthSize, _adjCamImageHeightSize, TargetWindowScale);
            }
        }

        public void CreateGui()
        {
            if (!Enabled) return;

            if (_hullcamera == null || _hullcamera.vessel == null)
            {
                Disable();
                return;
            }

            Name = _hullcamera.vessel.GetDisplayName() + "." + _hullcamera.cameraName;

            _windowRect = GUI.Window(Id, _windowRect, WindowTargetCam, Name);
        }

        public void CheckIfResizing()
        {
            if (!Enabled) return;

            if (Event.current.type == EventType.MouseUp)
                if (ResizingWindow)
                    ResizingWindow = false;
        }

        private void WindowTargetCam(int windowId)
        {
            if (!Enabled) return;

            _adjCamImageWidthSize = _initialCamImageWidthSize * TargetWindowScale;
            _adjCamImageHeightSize = _initialCamImageHeightSize * TargetWindowScale;

            if (GUI.Button(new Rect(_windowWidth - 18, 2, 20, 16), " ", GUI.skin.button))
            {
                Disable();
                return;
            }
            GUI.DragWindow(new Rect(0, 0, _windowHeight - 18, 30));

            var imageRect = DrawTexture();

            // Right side control buttons
            DrawSideControlButtons(imageRect);

            DrawTelemetry(imageRect);

            //resizing
            var resizeRect =
                new Rect(_windowWidth - 18, _windowHeight - 18, 16, 16);


            GUI.DrawTexture(resizeRect, ResizeTexture, ScaleMode.StretchToFill, true);

            if (Event.current.type == EventType.MouseDown && Event.current.clickCount == 2 && imageRect.Contains(Event.current.mousePosition))
            {
                MinimalUi = !MinimalUi;
                ResizeTargetWindow();
            }

            if (Event.current.type == EventType.MouseDown && resizeRect.Contains(Event.current.mousePosition))
                ResizingWindow = true;

            if (Event.current.type == EventType.Repaint && ResizingWindow)
                if (Math.Abs(Mouse.delta.x) > 1 || Math.Abs(Mouse.delta.y) > 0.1f)
                {
                    var diff = Mouse.delta.x + Mouse.delta.y;
                    UpdateTargetScale(diff);
                    ResizeTargetWindow();
                }

            //ResetZoomKeys();
            RepositionWindow(ref _windowRect);
        }

        private Rect DrawTexture()
        {
            var imageRect = new Rect(2, 20, _adjCamImageWidthSize, _adjCamImageHeightSize);

            GUI.DrawTexture(imageRect, TargetCamRenderTexture, ScaleMode.ScaleAndCrop , false);
            return imageRect;
        }

        private void DrawTelemetry(Rect imageRect)
        {
            if (MinimalUi) return;

            var dataStyle = new GUIStyle(TelemetryGuiStyle)
            {
                fontSize = (int) Mathf.Clamp(16 * TargetWindowScale, 9, 16),
            };

            var targetRangeRect = new Rect(imageRect.x,
                _adjCamImageHeightSize * 0.94f - (int)Mathf.Clamp(18 * TargetWindowScale, 9, 18), _adjCamImageWidthSize,
                (int)Mathf.Clamp(18 * TargetWindowScale, 10, 18));


            GUI.Label(targetRangeRect, String.Concat(AltitudeString, Environment.NewLine, SpeedString), dataStyle);
        }

        public bool MinimalUi { get; set; } = true;

        private void DrawSideControlButtons(Rect imageRect)
        {
            if (MinimalUi) return;

            var startX = imageRect.width + 3 * Gap;
            var streamingRect = new Rect(startX, controlsStartY, ButtonWidth, ButtonHeight + Line);

            if (!StreamingEnabled)
            {
                if (GUI.Button(streamingRect, "Enable streaming", ButtonStyle)) StreamingEnabled = true;
            }
            else
            {
                if (GUI.Button(streamingRect, "Disable streaming", ButtonStyle)) StreamingEnabled = false;
            }
        }

        public void CalculateSpeedAltitude()
        {
            var altitudeInKm = (float) Math.Round(_hullcamera.vessel.altitude / 1000f, 1);
            var speed = (int) Math.Round(_hullcamera.vessel.speed * 3.6f, 0);

            AltitudeString = string.Concat(Altitude, altitudeInKm.ToString("0.0"), Km);
            SpeedString = string.Concat(Speed, speed, Kmh);
        }

        private void UpdateTargetScale(float diff)
        {
            var scaleDiff = diff / (_windowRect.width + _windowRect.height) * 100 * .01f;
            TargetWindowScale += Mathf.Abs(scaleDiff) > .01f ? scaleDiff : scaleDiff > 0 ? .01f : -.01f;

            TargetWindowScale += Mathf.Abs(scaleDiff) > .01f ? scaleDiff : scaleDiff > 0 ? .01f : -.01f;
            TargetWindowScale = Mathf.Clamp(TargetWindowScale,
                TargetWindowScaleMin,
                TargetWindowScaleMax);
        }

        private void ResizeTargetWindow()
        {
            if (MinimalUi)
            {
                _windowWidth = _initialCamImageWidthSize * TargetWindowScale + 2 * Gap;
            }
            else
            {
                _windowWidth = _initialCamImageWidthSize * TargetWindowScale + 3 * ButtonHeight + 16 + 2 * Gap;
            }
            _windowHeight = _initialCamImageHeightSize * TargetWindowScale + 23;
            // _windowRect = new Rect(_windowRect.x, _windowRect.y, _windowWidth, _windowHeight);
            _windowRect.width = _windowWidth;
            _windowRect.height = _windowHeight;
        }

        internal static void RepositionWindow(ref Rect windowPosition)
        {
            // This method uses Gui point system.
            if (windowPosition.x < 0) windowPosition.x = 0;
            if (windowPosition.y < 0) windowPosition.y = 0;

            if (windowPosition.xMax > Screen.width)
                windowPosition.x = Screen.width - windowPosition.width;
            if (windowPosition.yMax > Screen.height)
                windowPosition.y = Screen.height - windowPosition.height;
        }

        public void Disable()
        {
            Enabled = false;
            StreamingEnabled = false;

            _jpgTexture = null;

            if (TargetCamRenderTexture != null)
            {
                TargetCamRenderTexture.Release();
                UnityEngine.Object.Destroy(TargetCamRenderTexture);
                TargetCamRenderTexture = null;
            }

            if (_texture2D != null)
            {
                UnityEngine.Object.Destroy(_texture2D);
            }

            if (sunflareManager != null)
            {
                UnityEngine.Object.Destroy(sunflareManager);
                sunflareManager = null;
            }

            if (sunflareManagerGO != null)
            {
                UnityEngine.Object.Destroy(sunflareManagerGO);
                sunflareManagerGO = null;
            }

            for (int i = _cameras.Count - 1; i >= 0; --i)
            {
                if (_cameras[i] != null)
                {
                    // Disable Deferred Rendering
                    DeferredWrapper.ForceRemoveDebugMode(_cameras[i]);
                    DeferredWrapper.DisableDeferredRendering(_cameras[i]);

                    // Disable Parallax and EVE wrappers
                    ParallaxWrapper.RemoveParallaxFromCamera(_cameras[i]);
                    EVEWrapper.RemoveEVEFromCamera(_cameras[i]);

                    // Disable Scatterer wrapper
                    //ScattererWrapper.RemoveScattererFromCamera(_cameras[i]);

                    // Disable Firefly wrapper and cleanup tracking
                    FireflyWrapper.RemoveFireflyFromCamera(_cameras[i]);
                    FireflyWrapper.CleanupCamera(_cameras[i]);

                    _cameras[i].enabled = false;
                    UnityEngine.Object.Destroy(_cameras[i].gameObject);
                }
            }

            _cameras.Clear();
        }
    }
}
