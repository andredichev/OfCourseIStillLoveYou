using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using HullcamVDS;
using OfCourseIStillLoveYou.Client;
using UnityEngine;

namespace OfCourseIStillLoveYou
{
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public class Core : MonoBehaviour
    {
        public static Dictionary<int, TrackingCamera> TrackedCameras = new Dictionary<int, TrackingCamera>();

        private static bool _lastDebugModeState = false;

        private float _cameraFpsLimit = Settings.FpsLimit > 0 ? Settings.FpsLimit : 24;
        public float _lastUpdateTime = 0f;

        void Start()
        {
            StartCoroutine(InitPQS()); // fixes atmosphere issues after loading game in-orbit
        }

        private void Awake()
        {
            GrpcClient.ConnectToServer(Settings.EndPoint, Settings.Port);
        }

        public static void Log(string message)
        {
            Debug.Log($"[OfCourseIStillLoveYou]: {message}");
        }

        public static List<MuMechModuleHullCamera> GetAllTrackingCameras()
        {
            List<MuMechModuleHullCamera> result = new List<MuMechModuleHullCamera>();

            if (!FlightGlobals.ready) return result;

            foreach (var vessel in FlightGlobals.VesselsLoaded)
            {
                result.AddRange(vessel.FindPartModulesImplementing<MuMechModuleHullCamera>());
            }

            return result;
        }

        void Update()
        {
            if (Time.time > _lastUpdateTime + (1f / _cameraFpsLimit))
            {
                RenderCameras();
            }
        }

        void LateUpdate()
        {
            UpdateTelemetry();

            if (Time.time > _lastUpdateTime + (1f / _cameraFpsLimit))
            {
                Refresh();
                _lastUpdateTime = Time.time;
            }
        }

        private void UpdateTelemetry()
        {
            foreach (var trackedCamerasValue in TrackedCameras.Values.Where(trackedCamerasValue => trackedCamerasValue.Enabled))
            {
                trackedCamerasValue.CalculateSpeedAltitude();
                trackedCamerasValue.UpdateTargetText();
            }
        }

        private static void SyncDebugMode()
        {
            bool currentDebugMode = DeferredWrapper.IsDebugModeEnabled();

            if (currentDebugMode != _lastDebugModeState)
            {
                Log($"Debug mode changed to {currentDebugMode}, syncing all cameras");

                foreach (var trackedCamera in TrackedCameras.Values)
                {
                    if (trackedCamera.Enabled)
                    {
                        trackedCamera.SyncDebugMode(currentDebugMode);
                    }
                }

                _lastDebugModeState = currentDebugMode;
            }
        }

        private void Refresh()
        {
            foreach (var trackedCamerasValue in TrackedCameras.Values.Where(trackedCamerasValue => trackedCamerasValue.Enabled))
            {
                trackedCamerasValue.LateUpdateCameras();
                trackedCamerasValue.SendCameraImage();
            }
        }

        private void RenderCameras()
        {
            foreach (var trackedCamerasValue in TrackedCameras.Values.Where(trackedCamerasValue => trackedCamerasValue.Enabled))
            {
                trackedCamerasValue.UpdateCameras();
            }
        }

        private IEnumerator InitPQS()
        {
            while (!FlightGlobals.ready)
                yield return null;

            CelestialBody body = FlightGlobals.currentMainBody;
            if (body == null)
                yield break;

            PQS pqs = body.pqsController;
            if (pqs == null)
                yield break;

            pqs.SetTarget(body.transform);
            pqs.ActivateSphere();
        }
    }
}
