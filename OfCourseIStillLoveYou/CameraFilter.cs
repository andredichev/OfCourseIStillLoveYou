using HullcamVDS;
using System;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;

namespace OfCourseIStillLoveYou
{
    public class CameraFilterNightVision : HullcamVDS.CameraFilterNightVision
    {
        public override bool Activate() { return true; }

        public override void Deactivate() { }

        public override void LateUpdate() { }
    }

    public class MovieTimeFilterWrapper : MonoBehaviour
    {

        public enum eFilterType { Flight, Map, Centre, TrackingStation };

        protected static eFilterType currentMode;

        private string moduleName = "";
        private HullcamVDS.CameraFilter cameraFilter = null;
        private HullcamVDS.CameraFilter.eCameraMode cameraMode;
        private eFilterType filterType;

        private float brightness = 1f;
        private float contrast = 2f;

        private float brightnessFactorFlightMode = 0.5f;
        private float contrastFactorFlightMode = 0.75f;

        private float brightnessFactorMapMode = 0.25f;
        private float contrastFactorMapMode = 0.75f;

        private bool title = true;
        private string titleFile = "dockingdisplay.png";
        private Texture2D titleTexture = null;

        private Material _shader = null;

        private Canvas _uiCanvas;
        private Text _dockingOverlayText;

        private bool HasTargetData = false;
        private string targetName;
        private double targetDistance = double.NaN;
        private double targetRelVelocity = double.NaN;
        private double targetVelX;
        private double targetVelY;
        private double targetVelZ;

        public MovieTimeFilterWrapper() { }

        private Material GetFilterShader(HullcamVDS.CameraFilter filter)
        {
            var shaderField = filter.GetType().GetField("mtShader", BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.FlattenHierarchy);
            if (shaderField != null)
            {
                // Debug.Log("[OCISLY-CameraFilter] Get access to shader value");
                return (Material)shaderField.GetValue(filter);
            }
            else
            {
                // Debug.Log("[OCISLY-CameraFilter] Cannot get access to shader value");
                return null;
            }
        }

        private HullcamVDS.CameraFilter CreateFilter(HullcamVDS.CameraFilter.eCameraMode mode)
        {
            HullcamVDS.CameraFilter newFilter = null;

            if (mode == CameraFilter.eCameraMode.NightVision)
            {
                newFilter = new CameraFilterNightVision();
            }
            else
            {
                newFilter = HullcamVDS.CameraFilter.CreateFilter(mode);
                _shader = GetFilterShader(newFilter);
            }

            return newFilter;
        }

        public void Initialize(string module, eFilterType filtType, bool initializeCamera = true)
        {
            moduleName = module;
            filterType = filtType;


            if (initializeCamera)
            {
                cameraFilter = CreateFilter(cameraMode);
                cameraFilter.Activate();
            }
            currentMode = (filterType == eFilterType.Map ? eFilterType.Flight : filterType);

            if (titleFile != "")
                titleTexture = HullcamVDS.CameraFilter.LoadTextureFile(titleFile);
        }

        public void SetMode(HullcamVDS.CameraFilter.eCameraMode mode)
        {
            if (mode != cameraMode)
            {
                HullcamVDS.CameraFilter newFilter = CreateFilter(mode);
                if (newFilter != null && newFilter.Activate())
                {
                    if (cameraFilter != null)
                    {
                        cameraFilter.Save(moduleName);
                        cameraFilter.Deactivate();
                    }
                    cameraFilter = newFilter;
                    cameraFilter.Load(moduleName);
                    cameraMode = mode;
                }
            }
        }
        public void ToggleTitleMode()
        {
            title = !title;
        }

        public void RefreshTitleTexture()
        {
            if (titleTexture != null)
                MonoBehaviour.Destroy(titleTexture);
            titleTexture = null;
            if (titleFile != "")
                titleTexture = HullcamVDS.CameraFilter.LoadTextureFile(titleFile);
        }

        public HullcamVDS.CameraFilter GetFilter()
        {
            return cameraFilter;
        }

        public void SetFilter(HullcamVDS.CameraFilter filter)
        {
            cameraFilter = filter;
        }

        public HullcamVDS.CameraFilter.eCameraMode GetMode()
        {
            return cameraMode;
        }

        public void Update()
        {
            UpdateDockingOverlayText();
        }

        public void LateUpdate()
        {
            if (cameraFilter != null)
                cameraFilter.LateUpdate();
        }

        private void UpdateFilterBrightnessViaShader()
        {
            if (_shader != null)
            {
                float currentBrightness = _shader.GetFloat("_Brightness");
                // Debug.Log($"[OCISLY-CameraFilter] Current brightness value {currentBrightness}");

                float newBrightness = currentBrightness;

                if (MapView.MapIsEnabled)
                {
                    newBrightness = Mathf.Clamp(currentBrightness * brightnessFactorMapMode, 0f, 2f);
                }
                else
                {
                    newBrightness = Mathf.Clamp(currentBrightness * brightnessFactorFlightMode, 0f, 2f);
                }

                // Debug.Log($"[OCISLY-CameraFilter] New brightness value {newBrightness}");
                _shader.SetFloat("_Brightness", newBrightness);
            }
            else
            {
                // Debug.Log("[OCISLY-CameraFilter] Cannot get access to shader value");
            }
        }

        private void UpdateFilterContrastViaShader()
        {
            if (_shader != null)
            {
                float currentContrast = _shader.GetFloat("_Contrast");
                // Debug.Log($"[OCISLY-CameraFilter] Current contrast value {currentContrast}");

                float newContrast = currentContrast;

                if (MapView.MapIsEnabled)
                {
                    newContrast = Mathf.Clamp(currentContrast * contrastFactorMapMode, 0f, 4f);
                }
                else
                {
                    newContrast = Mathf.Clamp(currentContrast * contrastFactorFlightMode, 0f, 4f);
                }

                // Debug.Log($"[OCISLY-CameraFilter] New contrast value {newContrast}");
                _shader.SetFloat("_Contrast", newContrast);
            }
            else
            {
                // Debug.Log("[OCISLY-CameraFilter] Cannot get access to shader value");
            }
        }

        private void OnRenderImage(RenderTexture source, RenderTexture target)
        {
            if (cameraFilter != null)
            {
                cameraFilter.RenderTitlePage(title, titleTexture);
                cameraFilter.RenderImageWithFilter(source, target);

                if (_shader != null)
                {
                    UpdateFilterBrightnessViaShader();
                    UpdateFilterContrastViaShader();

                    Graphics.Blit(source, target, _shader);
                }
            }
            else
            {
                Graphics.Blit(source, target);
            }
        }

        public static eFilterType LoadedScene()
        {
            if (currentMode == eFilterType.Flight && !MapView.MapIsEnabled)
                return eFilterType.Flight;
            else if (currentMode == eFilterType.Flight && MapView.MapIsEnabled)
                return eFilterType.Map;
            return currentMode;
        }

        public void AttachDockingOverlayToCamera(Camera camera, float displayWidth, float displayHeight, float targetWindowScale)
        {
            const int customUiLayer = 31;
            camera.cullingMask |= 1 << customUiLayer;

            GameObject overlayGo = new GameObject("OCISLY_Overlay");
            overlayGo.layer = customUiLayer;
            overlayGo.transform.SetParent(camera.transform, false);

            _uiCanvas = overlayGo.AddComponent<Canvas>();
            _uiCanvas.renderMode = RenderMode.ScreenSpaceCamera;
            _uiCanvas.worldCamera = camera;
            _uiCanvas.planeDistance = 0.1f;

            var scaler = overlayGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;

            var tgtGO = new GameObject("DockingOverlay");
            tgtGO.layer = customUiLayer;
            tgtGO.transform.SetParent(overlayGo.transform, false);

            var outline = tgtGO.AddComponent<Outline>();
            outline.effectColor = Color.black;
            outline.effectDistance = new Vector2(2, -2);

            _dockingOverlayText = tgtGO.AddComponent<Text>();
            _dockingOverlayText.font = Font.CreateDynamicFontFromOSFont("Courier New", 16);
            _dockingOverlayText.fontSize = 2 * (int)Mathf.Clamp(16 * targetWindowScale, 9, 16);
            _dockingOverlayText.alignment = TextAnchor.UpperLeft;
            _dockingOverlayText.color = Color.white;
            _dockingOverlayText.raycastTarget = false;

            var rtTgt = _dockingOverlayText.rectTransform;
            rtTgt.anchorMin = new Vector2(0f, 1f);
            rtTgt.anchorMax = new Vector2(0f, 1f);
            rtTgt.pivot = new Vector2(0f, 1f);

            float texW = Settings.Width;
            float texH = Settings.Height;
            float scale = Mathf.Max(displayWidth / texW, displayHeight / texH);
            float visibleW = displayWidth / scale;
            float visibleH = displayHeight / scale;
            float offsetX = (texW - visibleW) / 2f;
            float offsetY = (texH - visibleH) / 2f;

            float padding = 5f;
            rtTgt.anchoredPosition = new Vector2(offsetX + padding, -offsetY - padding);
            rtTgt.sizeDelta = new Vector2(visibleW - 2f * padding, visibleH - 2f * padding);
        }

        private void UpdateDockingOverlayText()
        {
            HasTargetData = (FlightGlobals.ActiveVessel.targetObject is Vessel || FlightGlobals.ActiveVessel.targetObject is ModuleDockingNode);
            if (_dockingOverlayText != null)
            {
                if (HasTargetData)
                {
                    targetName = FlightGlobals.fetch.VesselTarget.GetName();
                    targetVelX = Math.Round(Vector3d.Dot(FlightGlobals.ship_tgtVelocity, FlightGlobals.ActiveVessel.ReferenceTransform.right), 3);
                    targetVelY = Math.Round(Vector3d.Dot(FlightGlobals.ship_tgtVelocity, FlightGlobals.ActiveVessel.ReferenceTransform.forward), 3);
                    targetVelZ = Math.Round(Vector3d.Dot(FlightGlobals.ship_tgtVelocity, FlightGlobals.ActiveVessel.ReferenceTransform.up), 3);

                    Vessel targetVessel;
                    if (FlightGlobals.ActiveVessel.targetObject is Vessel)
                        targetVessel = (Vessel)FlightGlobals.ActiveVessel.targetObject;
                    else
                        targetVessel = ((ModuleDockingNode)FlightGlobals.ActiveVessel.targetObject).vessel;
                    Orbit activeOrbit = FlightGlobals.ActiveVessel.orbit;
                    Orbit targetOrbit = targetVessel.orbit;

                    Vector3d activeVesselPos = FlightGlobals.ActiveVessel.orbit.getRelativePositionAtUT(Planetarium.GetUniversalTime()) + FlightGlobals.ActiveVessel.orbit.referenceBody.position;
                    Vector3d targetVesselPos = targetVessel.orbit.getRelativePositionAtUT(Planetarium.GetUniversalTime()) + targetVessel.orbit.referenceBody.position;

                    targetDistance = (activeVesselPos - targetVesselPos).magnitude;

                    _dockingOverlayText.text =
                        $"Target: {targetName}" + "\n" +
                        $"DST:    {Math.Round(targetDistance, 2)} m" + "\n" +
                        $"TCA:    " + "\n" +
                        $"" + "\n" +
                        $"Relative Speed" + "\n" +
                        $"X: {(targetVelX > 0 ? " " : "-")}{Math.Abs(targetVelX)}" + "\n" +
                        $"Y: {(targetVelY > 0 ? " " : "-")}{Math.Abs(targetVelY)}" + "\n" +
                        $"Z: {(targetVelZ > 0 ? " " : "-")}{Math.Abs(targetVelZ)}" + "\n";
                }
                else
                {
                    _dockingOverlayText.text =
                        $"Target: None";
                }
            }
        }

        public void Destroy()
        {
            if (_uiCanvas != null)
            {
                UnityEngine.Object.Destroy(_uiCanvas.gameObject);
                _uiCanvas = null;
                _dockingOverlayText = null;
            }
        }
    }
}
