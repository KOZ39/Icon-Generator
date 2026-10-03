using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace KOZ39.IconGenerator
{
    internal static class IconCapture
    {
        internal sealed class CaptureSignature
        {
            private readonly HashSet<Renderer> _renderers;
            private readonly HashSet<GameObject> _excludedObjects = new();
            private readonly HashSet<GameObject> _enabledInactiveObjects = new();
            private readonly HashSet<(Renderer Renderer, string Shape, float Threshold)> _parts;
            private readonly HashSet<Component> _forcedMenuItems = new();
            private readonly IconCameraPose? _pose;

            internal CaptureSignature(CaptureClone clone, IconCameraPose? pose = null)
            {
                _pose = pose;
                var selection = clone.Selection;
                _renderers = clone.OriginalRenderers.ToHashSet();
                _parts = clone
                    .MeshParts.Values.Where(part => _renderers.Contains(part.Renderer))
                    .SelectMany(part =>
                        part.Shapes.Select(shape =>
                            ((Renderer)part.Renderer, shape.Name, shape.Threshold)
                        )
                    )
                    .ToHashSet();

                if (clone.HasEffects)
                {
                    _forcedMenuItems.UnionWith(clone.ForcedMenuItems);
                    _excludedObjects.UnionWith(selection.Excluded);
                    _enabledInactiveObjects.UnionWith(
                        selection.Enabled.Where(item =>
                            !item.activeSelf
                            && !selection.Sources.Contains(item)
                            && !selection.IsExcluded(item.transform)
                        )
                    );
                }
            }

            internal bool Matches(CaptureSignature other) =>
                _pose.HasValue == other._pose.HasValue
                && (!_pose.HasValue || _pose.Value.Matches(other._pose.Value))
                && _renderers.SetEquals(other._renderers)
                && _parts.SetEquals(other._parts)
                && _forcedMenuItems.SetEquals(other._forcedMenuItems)
                && _excludedObjects.SetEquals(other._excludedObjects)
                && _enabledInactiveObjects.SetEquals(other._enabledInactiveObjects);
        }

        internal sealed class CaptureSet
        {
            private readonly List<(CaptureSignature Signature, string Path)> _savedCaptures = new();

            internal bool Contains(CaptureSignature signature) =>
                _savedCaptures.Any(item => signature.Matches(item.Signature));

            internal string FindPath(CaptureSignature signature) =>
                _savedCaptures.FirstOrDefault(item => signature.Matches(item.Signature)).Path;

            internal void Add(CaptureSignature signature, string path = null) =>
                _savedCaptures.Add((signature, path));
        }

        internal static Texture2D Render(
            CaptureClone clone,
            IconGeneratorSettings settings,
            int resolution,
            Action<CaptureValidationException> reportWarning = null,
            bool rejectEmpty = false,
            IconCameraPose? pose = null,
            bool validateEnvironment = true
        )
        {
            if (validateEnvironment)
            {
                CaptureEnvironment.Validate();
            }

            var previous = RenderTexture.active;
            var asynchronousShaders = ShaderUtil.allowAsyncCompilation;
            RenderTexture target = null;
            RenderTexture processed = null;
            Material displayMaterial = null;
            Texture2D result = null;
            GameObject cameraObject = null;

            try
            {
                ShaderUtil.allowAsyncCompilation = false;
                ReportWarnings(clone, reportWarning);

                cameraObject = new GameObject("IconGenerator_Camera")
                {
                    hideFlags = HideFlags.HideAndDontSave,
                };
                cameraObject.transform.SetParent(clone.Root.transform, false);
                var camera = cameraObject.AddComponent<Camera>();

                ConfigureCamera(camera, clone.GetFramingCorners(settings, pose), settings, pose);
                target = CreateTarget(resolution);
                camera.targetTexture = target;

                if (rejectEmpty && !settings.transparentBackground && !settings.outline)
                {
                    var background = camera.backgroundColor;
                    camera.backgroundColor = Color.clear;
                    camera.Render();
                    RequireVisiblePixels(target);
                    camera.backgroundColor = background;
                }

                camera.Render();

                if (rejectEmpty && settings.outline)
                {
                    RequireVisiblePixels(target);
                }

                if (settings.outline)
                {
                    processed = RenderTexture.GetTemporary(
                        resolution,
                        resolution,
                        0,
                        RenderTextureFormat.ARGB32,
                        RenderTextureReadWrite.sRGB
                    );
                    displayMaterial = IconImageEffects.CreateMaterial();
                    IconImageEffects.Apply(target, processed, displayMaterial, settings);
                }

                RenderTexture.active = processed != null ? processed : target;
                result = new Texture2D(resolution, resolution, TextureFormat.RGBA32, false, false)
                {
                    hideFlags = HideFlags.HideAndDontSave,
                };
                result.ReadPixels(new Rect(0, 0, resolution, resolution), 0, 0);

                if (settings.transparentBackground && !settings.outline)
                {
                    if (rejectEmpty && !CaptureVisibleArea.FindVisiblePixelBounds(result).HasValue)
                    {
                        throw new CaptureValidationException("EmptyCapture");
                    }

                    UnpremultiplyAlpha(result);
                }

                result.Apply(false, false);
                camera.targetTexture = null;
                var output = result;
                result = null;

                return output;
            }
            finally
            {
                RenderTexture.active = previous;
                ShaderUtil.allowAsyncCompilation = asynchronousShaders;

                if (cameraObject != null)
                {
                    Object.DestroyImmediate(cameraObject);
                }

                ReleaseAndDestroy(target);

                if (result != null)
                {
                    Object.DestroyImmediate(result);
                }

                if (processed != null)
                {
                    RenderTexture.ReleaseTemporary(processed);
                }

                if (displayMaterial != null)
                {
                    Object.DestroyImmediate(displayMaterial);
                }
            }
        }

        internal static void ReportWarnings(
            CaptureClone clone,
            Action<CaptureValidationException> reportWarning
        )
        {
            if (reportWarning == null)
            {
                return;
            }

            if (clone.Unsupported.Count > 0)
            {
                reportWarning(
                    new CaptureValidationException(
                        "UnsupportedRenderers",
                        string.Join(", ", clone.Unsupported)
                    )
                );
            }

            foreach (
                var warning in clone
                    .Warnings.GroupBy(item => item.Identity)
                    .Select(group => group.First())
            )
            {
                reportWarning(warning);
            }

            if (CaptureEnvironment.HasLightsOutsideCaptureMask())
            {
                reportWarning(new CaptureValidationException("LightCullingMaskMismatch"));
            }
        }

        internal static void ReleaseAndDestroy(RenderTexture texture)
        {
            if (texture != null)
            {
                texture.Release();
                Object.DestroyImmediate(texture);
            }
        }

        private static void RequireVisiblePixels(RenderTexture target)
        {
            if (!CaptureVisibleArea.FindVisiblePixelBounds(target).HasValue)
            {
                throw new CaptureValidationException("EmptyCapture");
            }
        }

        internal static RenderTexture CreateTarget(int resolution)
        {
            var target = new RenderTexture(
                resolution,
                resolution,
                24,
                RenderTextureFormat.ARGB32,
                RenderTextureReadWrite.sRGB
            )
            {
                hideFlags = HideFlags.HideAndDontSave,
                antiAliasing = 4,
            };

            target.antiAliasing = Mathf.Max(
                1,
                SystemInfo.GetRenderTextureSupportedMSAASampleCount(target.descriptor)
            );
            target.Create();

            return target;
        }

        internal static void ConfigureCamera(
            Camera camera,
            IReadOnlyList<Vector3> corners,
            IconGeneratorSettings settings,
            IconCameraPose? pose = null
        )
        {
            var background = settings.backgroundColor;
            background.a = 1;

            ConfigureCamera(
                camera,
                CalculateFrame(corners, settings, pose),
                settings.transparentBackground || settings.outline ? Color.clear : background
            );
        }

        internal static void ConfigureCamera(Camera camera, CaptureFrame frame, Color background)
        {
            camera.enabled = false;
            camera.orthographic = true;
            camera.allowHDR = false;
            camera.allowMSAA = true;
            camera.useOcclusionCulling = false;
            camera.cullingMask = 1 << CaptureClone.Layer;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = background;

            camera.transform.SetPositionAndRotation(frame.Position, frame.Rotation);
            camera.orthographicSize = frame.OrthographicSize;
            camera.nearClipPlane = frame.NearClipPlane;
            camera.farClipPlane = frame.FarClipPlane;
            camera.aspect = 1;
        }

        internal static CaptureFrame CalculateFrame(
            IReadOnlyList<Vector3> corners,
            IconGeneratorSettings settings,
            IconCameraPose? pose = null
        )
        {
            var camera = pose ?? settings.SharedCamera;

            return CaptureFraming.Calculate(
                corners,
                camera.rotation,
                camera.padding,
                camera.zoom,
                camera.keepWholeObject,
                offset: camera.offset,
                inset: settings.outline ? (settings.outlineWidth + 1f) / settings.resolution : 0
            );
        }

        private static void UnpremultiplyAlpha(Texture2D texture)
        {
            var pixels = texture.GetRawTextureData<Color32>();
            var linear = QualitySettings.activeColorSpace == ColorSpace.Linear;

            for (var i = 0; i < pixels.Length; i++)
            {
                var pixel = pixels[i];

                if (pixel.a == 0)
                {
                    pixels[i] = new Color32(0, 0, 0, 0);
                    continue;
                }

                if (pixel.a == 255)
                {
                    continue;
                }

                Color value = pixel;
                var alpha = value.a;

                if (linear)
                {
                    value = value.linear;
                }

                value.r /= alpha;
                value.g /= alpha;
                value.b /= alpha;

                if (linear)
                {
                    value = value.gamma;
                }

                value.a = alpha;
                pixels[i] = value;
            }
        }
    }
}
