using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace KOZ39.IconGenerator
{
    internal static class CaptureVisibleArea
    {
        private const int Resolution = 512;

        internal static IReadOnlyList<Vector3> Measure(CaptureClone clone, Vector3 angles)
        {
            var active = RenderTexture.active;
            var asynchronousShaders = ShaderUtil.allowAsyncCompilation;
            var renderingDisabled = clone
                .Renderers.Select(renderer => renderer.forceRenderingOff)
                .ToArray();
            GameObject cameraObject = null;
            RenderTexture target = null;

            try
            {
                ShaderUtil.allowAsyncCompilation = false;
                cameraObject = new GameObject("IconGenerator_VisibleAreaCamera")
                {
                    hideFlags = HideFlags.HideAndDontSave,
                };
                cameraObject.transform.SetParent(clone.Root.transform, false);
                var camera = cameraObject.AddComponent<Camera>();
                var frame = CaptureFraming.Calculate(clone.WorldCorners, angles, 0.1f, 1, false);
                IconCapture.ConfigureCamera(camera, frame, Color.clear);
                target = IconCapture.CreateTarget(Resolution);
                camera.targetTexture = target;

                foreach (var renderer in clone.Renderers)
                {
                    renderer.forceRenderingOff = false;
                }

                camera.Render();
                var visiblePixelBounds = FindVisiblePixelBounds(target);

                if (!visiblePixelBounds.HasValue)
                {
                    return clone.WorldCorners;
                }

                var bounds = visiblePixelBounds.Value;
                var minimum = new Vector2(
                    Mathf.Max(0, bounds.xMin - 1),
                    Mathf.Max(0, bounds.yMin - 1)
                );
                var maximum = new Vector2(
                    Mathf.Min(Resolution, bounds.xMax + 1),
                    Mathf.Min(Resolution, bounds.yMax + 1)
                );
                var inverse = Quaternion.Inverse(frame.Rotation);
                var cameraSpaceDepths = clone
                    .WorldCorners.Select(point => (inverse * point).z)
                    .ToArray();
                var minimumDepth = cameraSpaceDepths.Min();
                var maximumDepth = cameraSpaceDepths.Max();
                var center = inverse * frame.Position;
                var centerOffset =
                    ((minimum + maximum) / (2 * Resolution) - Vector2.one * 0.5f)
                    * (2 * frame.OrthographicSize);

                center.x += centerOffset.x;
                center.y += centerOffset.y;
                center.z = (minimumDepth + maximumDepth) * 0.5f;

                var size = (maximum - minimum) * (2 * frame.OrthographicSize / Resolution);
                var corners = new List<Vector3>(8);

                CaptureClone.AddCorners(
                    new Bounds(center, new Vector3(size.x, size.y, maximumDepth - minimumDepth)),
                    Matrix4x4.Rotate(frame.Rotation),
                    corners
                );

                return corners;
            }
            finally
            {
                for (var index = 0; index < clone.Renderers.Count; index++)
                {
                    if (clone.Renderers[index] != null)
                    {
                        clone.Renderers[index].forceRenderingOff = renderingDisabled[index];
                    }
                }

                if (cameraObject != null)
                {
                    Object.DestroyImmediate(cameraObject);
                }

                RenderTexture.active = active;

                IconCapture.ReleaseAndDestroy(target);

                ShaderUtil.allowAsyncCompilation = asynchronousShaders;
            }
        }

        internal static RectInt? FindVisiblePixelBounds(RenderTexture source)
        {
            var active = RenderTexture.active;
            var image = new Texture2D(
                source.width,
                source.height,
                TextureFormat.RGBA32,
                false,
                false
            )
            {
                hideFlags = HideFlags.HideAndDontSave,
            };

            try
            {
                RenderTexture.active = source;
                image.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0);

                return FindVisiblePixelBounds(image);
            }
            finally
            {
                RenderTexture.active = active;
                Object.DestroyImmediate(image);
            }
        }

        internal static RectInt? FindVisiblePixelBounds(Texture2D image)
        {
            var pixels = image.GetRawTextureData<Color32>();
            var width = image.width;
            var height = image.height;
            var minimum = new Vector2Int(width, height);
            var maximum = new Vector2Int(-1, -1);

            for (var y = 0; y < height; y++)
            {
                var row = y * width;
                var first = 0;

                while (first < width && pixels[row + first].a == 0)
                {
                    first++;
                }

                if (first == width)
                {
                    continue;
                }

                var last = width - 1;

                while (pixels[row + last].a == 0)
                {
                    last--;
                }

                minimum.x = Mathf.Min(minimum.x, first);
                minimum.y = Mathf.Min(minimum.y, y);
                maximum.x = Mathf.Max(maximum.x, last);
                maximum.y = y;
            }

            return maximum.x < 0 ? null : new RectInt(minimum, maximum - minimum + Vector2Int.one);
        }
    }
}
