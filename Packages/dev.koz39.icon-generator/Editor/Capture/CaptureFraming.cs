using System.Collections.Generic;
using UnityEngine;

namespace KOZ39.IconGenerator
{
    internal readonly struct CaptureFrame
    {
        internal readonly Vector3 Position;
        internal readonly Quaternion Rotation;
        internal readonly Vector2 Offset;
        internal readonly float OrthographicSize;
        internal readonly float NearClipPlane;
        internal readonly float FarClipPlane;

        internal CaptureFrame(
            Vector3 position,
            Quaternion rotation,
            float orthographicSize,
            float nearClipPlane,
            float farClipPlane,
            Vector2 offset = default
        )
        {
            Position = position;
            Rotation = rotation;
            OrthographicSize = orthographicSize;
            NearClipPlane = nearClipPlane;
            FarClipPlane = farClipPlane;
            Offset = offset;
        }
    }

    internal static class CaptureFraming
    {
        internal static CaptureFrame Calculate(
            IReadOnlyList<Vector3> worldCorners,
            Vector3 angles,
            float padding,
            float zoom,
            bool keepWholeObject,
            float aspect = 1,
            Vector2 offset = default,
            float inset = 0
        )
        {
            if (worldCorners.Count == 0)
            {
                throw new CaptureValidationException("NoRenderers");
            }

            var rotation = Quaternion.Euler(IconGeneratorSettings.NormalizeRotation(angles));
            var inverse = Quaternion.Inverse(rotation);
            var minimum = new Vector3(
                float.PositiveInfinity,
                float.PositiveInfinity,
                float.PositiveInfinity
            );
            var maximum = new Vector3(
                float.NegativeInfinity,
                float.NegativeInfinity,
                float.NegativeInfinity
            );

            foreach (var corner in worldCorners)
            {
                var point = inverse * corner;

                if (!IsFinite(point))
                {
                    throw new CaptureValidationException("InvalidBounds");
                }

                minimum = Vector3.Min(minimum, point);
                maximum = Vector3.Max(maximum, point);
            }

            var center = (minimum + maximum) * 0.5f;
            var extent = (maximum - minimum) * 0.5f;
            var baseSize = Mathf.Max(
                0.0001f,
                Mathf.Max(extent.y, extent.x / Mathf.Max(aspect, 0.0001f))
            );
            var effectiveZoom = IconGeneratorSettings.NormalizeZoom(zoom, padding, keepWholeObject);
            var size = baseSize * (1 + padding) / effectiveZoom;

            if (keepWholeObject)
            {
                inset = Mathf.Clamp(inset, 0, 0.45f);
                size = Mathf.Max(size, baseSize / (1 - 2 * inset));
                var limitX = Mathf.Max(0, (size * aspect - extent.x) / (2 * size * aspect) - inset);
                var limitY = Mathf.Max(0, (size - extent.y) / (2 * size) - inset);

                offset.x = Mathf.Clamp(offset.x, -limitX, limitX);
                offset.y = Mathf.Clamp(offset.y, -limitY, limitY);
            }

            center.x += offset.x * 2 * size * aspect;
            center.y += offset.y * 2 * size;
            var margin = Mathf.Max(0.01f, (maximum - minimum).magnitude * 0.05f);
            var position = rotation * new Vector3(center.x, center.y, minimum.z - margin * 2);

            return new CaptureFrame(
                position,
                rotation,
                size,
                margin,
                Mathf.Max(margin * 4, maximum.z - minimum.z + margin * 3),
                offset
            );
        }

        private static bool IsFinite(Vector3 value) =>
            float.IsFinite(value.x) && float.IsFinite(value.y) && float.IsFinite(value.z);
    }
}
