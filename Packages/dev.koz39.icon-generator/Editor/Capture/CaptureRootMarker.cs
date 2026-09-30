using UnityEngine;

namespace KOZ39.IconGenerator
{
    internal sealed class CaptureRootMarker : ScriptableObject
    {
        internal const string MarkerName = "KOZ39.IconGenerator.CaptureOwner";

        [SerializeField]
        internal GameObject root;

        internal static CaptureRootMarker Create(GameObject root)
        {
            var marker = CreateInstance<CaptureRootMarker>();

            marker.name = MarkerName;
            marker.hideFlags = HideFlags.HideAndDontSave;
            marker.root = root;

            return marker;
        }

        internal static bool Owns(Transform transform, CaptureRootMarker[] markers)
        {
            foreach (var marker in markers)
            {
                if (
                    marker.name == MarkerName
                    && marker.root != null
                    && marker.root.name == CaptureClone.RootName
                    && transform.IsChildOf(marker.root.transform)
                )
                {
                    return true;
                }
            }

            return false;
        }
    }
}
