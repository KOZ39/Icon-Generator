using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace KOZ39.IconGenerator
{
    internal static class CaptureEnvironment
    {
        internal static void Validate()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                throw new CaptureValidationException("EditModeOnly");
            }

            CaptureRootMarker[] markers = null;
            var conflicts = Resources
                .FindObjectsOfTypeAll<Transform>()
                .Where(item =>
                    item != null
                    && item.gameObject.scene.IsValid()
                    && !EditorUtility.IsPersistent(item)
                    && item.gameObject.layer == CaptureClone.Layer
                    && !CaptureRootMarker.Owns(
                        item,
                        markers ??= Resources.FindObjectsOfTypeAll<CaptureRootMarker>()
                    )
                )
                .Select(item => SelectionReference.HierarchyPath(item))
                .Take(12)
                .ToArray();

            if (conflicts.Length > 0)
            {
                throw new CaptureValidationException(
                    "CaptureLayerConflict",
                    string.Join("\n", conflicts)
                );
            }

            if (GraphicsSettings.currentRenderPipeline != null)
            {
                throw new CaptureValidationException("UnsupportedRenderPipeline");
            }
        }

        internal static bool HasLightsOutsideCaptureMask() =>
            Resources
                .FindObjectsOfTypeAll<Light>()
                .Any(light =>
                    light != null
                    && light.gameObject.scene.IsValid()
                    && light.isActiveAndEnabled
                    && (light.cullingMask & (1 << CaptureClone.Layer)) == 0
                );
    }
}
