using System;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace OfCourseIStillLoveYou
{
    public static class ScattererWrapper
    {
        private static bool? _isScattererAvailable;
        private static Assembly _scattererAssembly;

        private static Type _scatteringCommandBufferType;
        private static Type _oceanCommandBufferType;
        private static Type _skySphereCommandBufferType;

        private static MethodInfo _scatteringEnableMethod;
        private static MethodInfo _oceanEnableMethod;

        private static int _lastLogFrame = -999;
        private static bool _lastRenderingState = false;

        public static bool IsScattererAvailable
        {
            get
            {
                if (_isScattererAvailable.HasValue)
                    return _isScattererAvailable.Value;

                try
                {
                    var allAssemblies = AppDomain.CurrentDomain.GetAssemblies();

                    _scattererAssembly = allAssemblies.FirstOrDefault(a =>
                        !a.IsDynamic &&
                        a.GetName().Name.Equals("scatterer", StringComparison.OrdinalIgnoreCase));

                    if (_scattererAssembly == null)
                    {
                        Debug.Log("[OfCourseIStillLoveYou]: Scatterer not found");
                        _isScattererAvailable = false;
                        return false;
                    }

                    _scatteringCommandBufferType = _scattererAssembly.GetType("Scatterer.ScatteringCommandBuffer");
                    _oceanCommandBufferType = _scattererAssembly.GetType("Scatterer.OceanCommandBuffer");
                    _skySphereCommandBufferType = _scattererAssembly.GetType("Scatterer.SkySphereLocalCommandBuffer");

                    if (_scatteringCommandBufferType == null || _oceanCommandBufferType == null)
                    {
                        Debug.LogWarning("[OfCourseIStillLoveYou]: Scatterer component types not found");
                        _isScattererAvailable = false;
                        return false;
                    }

                    _scatteringEnableMethod = _scatteringCommandBufferType.GetMethod("EnableForThisFrame",
                        BindingFlags.Public | BindingFlags.Instance);
                    _oceanEnableMethod = _oceanCommandBufferType.GetMethod("EnableForThisFrame",
                        BindingFlags.Public | BindingFlags.Instance);

                    Debug.Log($"[OfCourseIStillLoveYou]: Scatterer types found - " +
                              $"ScatteringEnable: {_scatteringEnableMethod != null}, " +
                              $"OceanEnable: {_oceanEnableMethod != null}");

                    _isScattererAvailable = true;
                    Debug.Log("[OfCourseIStillLoveYou]: Scatterer integration enabled");
                    return true;
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[OfCourseIStillLoveYou]: Error checking Scatterer availability: {ex.Message}");
                    _isScattererAvailable = false;
                    return false;
                }
            }
        }

        public static void ApplyScattererToCamera(Camera targetCamera)
        {
            if (!IsScattererAvailable)
            {
                return;
            }

            if (targetCamera == null)
            {
                Debug.LogWarning("[OfCourseIStillLoveYou]: Cannot apply Scatterer to null camera");
                return;
            }

            try
            {
                Debug.Log($"[OfCourseIStillLoveYou]: Scatterer will auto-register to {targetCamera.name} when objects are visible");

                UnityEngine.Object.FindObjectOfType<MonoBehaviour>()?.StartCoroutine(CheckScattererComponentsDelayed(targetCamera));
            }
            catch (Exception ex)
            {
                Debug.LogError($"[OfCourseIStillLoveYou]: Failed to apply Scatterer: {ex.Message}");
            }
        }

        private static System.Collections.IEnumerator CheckScattererComponentsDelayed(Camera camera)
        {
            yield return new UnityEngine.WaitForSeconds(0.5f);

            if (camera == null) yield break;

            var scattering = _scatteringCommandBufferType != null ? camera.GetComponent(_scatteringCommandBufferType) : null;
            var ocean = _oceanCommandBufferType != null ? camera.GetComponent(_oceanCommandBufferType) : null;
            var skySphere = _skySphereCommandBufferType != null ? camera.GetComponent(_skySphereCommandBufferType) : null;

            Debug.Log($"[OfCourseIStillLoveYou]: Scatterer auto-registration check for {camera.name}:");
            Debug.Log($"  - ScatteringCommandBuffer: {scattering != null}");
            Debug.Log($"  - OceanCommandBuffer: {ocean != null}");
            Debug.Log($"  - SkySphereLocalCommandBuffer: {skySphere != null}");

            if (scattering == null && ocean == null && skySphere == null)
            {
                Debug.LogWarning($"[OfCourseIStillLoveYou]: Scatterer did NOT auto-register components to {camera.name}. " +
                                "This may be normal if no Scatterer effects are active on the current body.");
            }
        }

        public static void ForceEnableScattererComponents(Camera camera)
        {
            if (!IsScattererAvailable || camera == null)
                return;

            try
            {
                bool scatteringEnabled = false;
                bool oceanEnabled = false;

                if (_scatteringCommandBufferType != null && _scatteringEnableMethod != null)
                {
                    var component = camera.GetComponent(_scatteringCommandBufferType);
                    if (component != null)
                    {
                        try
                        {
                            _scatteringEnableMethod.Invoke(component, null);
                            scatteringEnabled = true;
                        }
                        catch (Exception ex)
                        {
                            Debug.LogWarning($"[OfCourseIStillLoveYou]: Failed to enable ScatteringCommandBuffer: {ex.Message}");
                        }
                    }
                }

                if (_oceanCommandBufferType != null && _oceanEnableMethod != null)
                {
                    var component = camera.GetComponent(_oceanCommandBufferType);
                    if (component != null)
                    {
                        try
                        {
                            _oceanEnableMethod.Invoke(component, null);
                            oceanEnabled = true;
                        }
                        catch (Exception ex)
                        {
                            Debug.LogWarning($"[OfCourseIStillLoveYou]: Failed to enable OceanCommandBuffer: {ex.Message}");
                        }
                    }
                }

                bool isRendering = scatteringEnabled || oceanEnabled;
                if ((isRendering != _lastRenderingState) || (isRendering && Time.frameCount - _lastLogFrame > 300))
                {
                    if (isRendering)
                    {
                        Debug.Log($"[OCISLY-Scatterer] Rendering on {camera.name} - Scattering:{scatteringEnabled} Ocean:{oceanEnabled}");
                    }
                    else if (_lastRenderingState)
                    {
                        Debug.Log($"[OCISLY-Scatterer] Stopped rendering on {camera.name}");
                    }
                    _lastRenderingState = isRendering;
                    _lastLogFrame = Time.frameCount;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[OfCourseIStillLoveYou]: Error forcing Scatterer components: {ex.Message}");
            }
        }

        public static void RemoveScattererFromCamera(Camera camera)
        {
            if (!IsScattererAvailable || camera == null)
                return;

            try
            {
                if (_scatteringCommandBufferType != null)
                {
                    var component = camera.GetComponent(_scatteringCommandBufferType);
                    if (component != null)
                    {
                        UnityEngine.Object.Destroy(component);
                        Debug.Log($"[OfCourseIStillLoveYou]: Removed ScatteringCommandBuffer from {camera.name}");
                    }
                }

                if (_oceanCommandBufferType != null)
                {
                    var component = camera.GetComponent(_oceanCommandBufferType);
                    if (component != null)
                    {
                        UnityEngine.Object.Destroy(component);
                        Debug.Log($"[OfCourseIStillLoveYou]: Removed OceanCommandBuffer from {camera.name}");
                    }
                }

                if (_skySphereCommandBufferType != null)
                {
                    var component = camera.GetComponent(_skySphereCommandBufferType);
                    if (component != null)
                    {
                        UnityEngine.Object.Destroy(component);
                        Debug.Log($"[OfCourseIStillLoveYou]: Removed SkySphereLocalCommandBuffer from {camera.name}");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[OfCourseIStillLoveYou]: Error removing Scatterer from camera: {ex.Message}");
            }
        }

        public static string GetDiagnosticInfo(Camera camera)
        {
            if (!IsScattererAvailable || camera == null)
                return "Scatterer not available";

            var info = $"Scatterer Integration for {camera.name}:\n";

            try
            {
                var scattering = _scatteringCommandBufferType != null ? camera.GetComponent(_scatteringCommandBufferType) : null;
                var ocean = _oceanCommandBufferType != null ? camera.GetComponent(_oceanCommandBufferType) : null;
                var skySphere = _skySphereCommandBufferType != null ? camera.GetComponent(_skySphereCommandBufferType) : null;

                info += $"- ScatteringCommandBuffer component: {scattering != null}\n";
                info += $"- OceanCommandBuffer component: {ocean != null}\n";
                info += $"- SkySphereLocalCommandBuffer component: {skySphere != null}\n";

                if (_scatteringEnableMethod != null)
                {
                    info += $"- EnableForThisFrame method available: Yes\n";
                }
                else
                {
                    info += $"- EnableForThisFrame method available: No (reflection failed)\n";
                }
            }
            catch (Exception ex)
            {
                info += $"- Error getting diagnostic info: {ex.Message}\n";
            }

            return info;
        }

        public static void EnsureOceanRenderingSetup(Camera camera)
        {
            if (!IsScattererAvailable || camera == null)
                return;

            try
            {
                var oceanCommandBuffer = _oceanCommandBufferType != null ? camera.GetComponent(_oceanCommandBufferType) : null;

                if (oceanCommandBuffer == null && _oceanCommandBufferType != null)
                {
                    Debug.LogWarning($"[OCISLY-Scatterer] No OceanCommandBuffer on {camera.name} - ocean may not render correctly");
                }

                var screenCopyType = _scattererAssembly?.GetType("Scatterer.ScreenCopyCommandBuffer");
                if (screenCopyType != null)
                {
                    var screenCopy = camera.GetComponent(screenCopyType);
                    if (screenCopy == null)
                    {
                        Debug.Log($"[OCISLY-Scatterer] No ScreenCopyCommandBuffer on {camera.name} - transparency may not work");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[OCISLY-Scatterer] Error checking ocean setup: {ex.Message}");
            }
        }
    }
}
