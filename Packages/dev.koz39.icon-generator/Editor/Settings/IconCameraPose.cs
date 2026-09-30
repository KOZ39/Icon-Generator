using System;
using UnityEngine;

namespace KOZ39.IconGenerator
{
    [Serializable]
    internal struct IconCameraPose
    {
        [SerializeField]
        internal Vector3 rotation;

        [SerializeField]
        internal Vector2 offset;

        [SerializeField]
        internal float zoom;

        [SerializeField]
        internal float padding;

        [SerializeField]
        internal bool keepWholeObject;

        [SerializeField]
        internal bool useVisibleArea;

        internal bool ShouldUseVisibleArea => keepWholeObject && useVisibleArea;

        internal float SliderZoomMaximum =>
            keepWholeObject ? 1 + padding : IconGeneratorSettings.SliderMaxZoom;

        internal IconCameraPose(
            Vector3 rotation,
            Vector2 offset,
            float zoom,
            bool keepWholeObject = false,
            bool useVisibleArea = false,
            float padding = IconGeneratorSettings.DefaultPadding
        )
        {
            this.rotation = rotation;
            this.offset = offset;
            this.zoom = zoom;
            this.padding = padding;
            this.keepWholeObject = keepWholeObject;
            this.useVisibleArea = useVisibleArea;
        }

        internal bool Matches(IconCameraPose other) =>
            Quaternion.Angle(Quaternion.Euler(rotation), Quaternion.Euler(other.rotation)) < 0.01f
            && offset == other.offset
            && Mathf.Approximately(zoom, other.zoom)
            && Mathf.Approximately(padding, other.padding)
            && keepWholeObject == other.keepWholeObject
            && ShouldUseVisibleArea == other.ShouldUseVisibleArea;
    }

    [Serializable]
    internal sealed class ItemCameraSettings
    {
        [SerializeField]
        internal string objectId;

        [SerializeField]
        internal bool enabled;

        [SerializeField]
        internal IconCameraPose pose;

        [NonSerialized]
        private SelectionReference _reference;

        internal SelectionReference Reference =>
            _reference != null && _reference.Id == objectId
                ? _reference
                : _reference = SelectionReference.Resolve(objectId);

        internal void Refresh()
        {
            Reference.Refresh();
            objectId = _reference.Id;
        }
    }
}
