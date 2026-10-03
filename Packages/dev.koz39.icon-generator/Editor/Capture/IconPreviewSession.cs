using System;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace KOZ39.IconGenerator
{
    internal sealed class IconPreviewSession : IDisposable
    {
        private CaptureClone _clone;
        private Camera _camera;
        private RenderTexture _target;
        private Material _displayMaterial;
        private readonly IconCapture.CaptureSignature _signature;
        internal RenderTexture Texture { get; private set; }
        internal bool IsValid => _clone?.Root != null && _camera != null && Texture != null;
        internal BlendShapeWeightMode BlendShapeWeightMode { get; }
        internal GameObject CaptureTarget { get; }
        internal bool IncludeInactiveObjects { get; }

        internal IconPreviewSession(
            ResolvedCaptureSelection selection,
            int resolution,
            Action<CaptureValidationException> reportWarning = null,
            BlendShapeWeightMode blendShapeWeightMode = BlendShapeWeightMode.None,
            GameObject captureTarget = null,
            bool includeInactiveObjects = false,
            CaptureEffectsCache effectsCache = null,
            bool validateEnvironment = true
        )
        {
            if (validateEnvironment)
            {
                CaptureEnvironment.Validate();
            }

            BlendShapeWeightMode = blendShapeWeightMode;
            CaptureTarget = captureTarget;
            IncludeInactiveObjects = includeInactiveObjects;

            try
            {
                if (captureTarget != null && !includeInactiveObjects)
                {
                    selection = selection.WithTargetBranchEnabled(
                        captureTarget,
                        includeDescendants: false
                    );
                }

                _clone = CaptureClone.Build(
                    selection,
                    captureTarget,
                    blendShapeWeightMode,
                    IncludeInactiveObjects,
                    effectsCache
                );

                _signature = new IconCapture.CaptureSignature(_clone);
                SetVisible(false);
                IconCapture.ReportWarnings(_clone, reportWarning);

                var cameraObject = new GameObject("IconGenerator_PreviewCamera")
                {
                    hideFlags = HideFlags.HideAndDontSave,
                };
                cameraObject.transform.SetParent(_clone.Root.transform, false);
                _camera = cameraObject.AddComponent<Camera>();
                _camera.enabled = false;
                SetResolution(resolution);
                _displayMaterial = IconImageEffects.CreateMaterial();
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        internal bool HasSameContent(IconPreviewSession other) =>
            IsValid
            && other?.IsValid == true
            && BlendShapeWeightMode == other.BlendShapeWeightMode
            && _signature.Matches(other._signature);

        internal void Render(IconGeneratorSettings settings)
        {
            var active = RenderTexture.active;
            var srgb = GL.sRGBWrite;
            var asynchronousShaders = ShaderUtil.allowAsyncCompilation;

            try
            {
                ShaderUtil.allowAsyncCompilation = false;
                var pose = settings.CameraFor(CaptureTarget);
                IconCapture.ConfigureCamera(
                    _camera,
                    _clone.GetFramingCorners(settings, pose),
                    settings,
                    pose
                );
                _camera.targetTexture = _target;
                SetVisible(true);
                _camera.Render();
                IconImageEffects.Apply(_target, Texture, _displayMaterial, settings);
            }
            finally
            {
                SetVisible(false);
                _camera.targetTexture = null;
                RenderTexture.active = active;
                GL.sRGBWrite = srgb;
                ShaderUtil.allowAsyncCompilation = asynchronousShaders;
            }
        }

        internal void RefreshBounds() => _clone?.RefreshBounds();

        internal Vector2 ConstrainOffset(IconGeneratorSettings settings)
        {
            var pose = settings.CameraFor(CaptureTarget);

            return IconCapture
                .CalculateFrame(_clone.GetFramingCorners(settings, pose), settings, pose)
                .Offset;
        }

        internal void SetResolution(int resolution)
        {
            if (Texture != null && Texture.width == resolution && _target != null)
            {
                return;
            }

            ReleaseTextures();
            _target = IconCapture.CreateTarget(resolution);
            _target.name = "IconGenerator_PreviewTarget";
            Texture = new RenderTexture(
                resolution,
                resolution,
                0,
                RenderTextureFormat.ARGB32,
                RenderTextureReadWrite.sRGB
            )
            {
                name = "IconGenerator_PreviewDisplay",
                hideFlags = HideFlags.HideAndDontSave,
            };
            Texture.Create();
        }

        private void SetVisible(bool visible)
        {
            if (_clone == null)
            {
                return;
            }

            foreach (var renderer in _clone.Renderers)
            {
                if (renderer != null)
                {
                    renderer.forceRenderingOff = !visible;
                }
            }
        }

        public void Dispose()
        {
            _clone?.Dispose();
            _clone = null;
            _camera = null;
            ReleaseTextures();

            if (_displayMaterial != null)
            {
                Object.DestroyImmediate(_displayMaterial);
                _displayMaterial = null;
            }
        }

        private void ReleaseTextures()
        {
            var active = RenderTexture.active;

            if (active != null && (active == _target || active == Texture))
            {
                RenderTexture.active = null;
            }

            IconCapture.ReleaseAndDestroy(_target);
            _target = null;
            IconCapture.ReleaseAndDestroy(Texture);
            Texture = null;
        }
    }
}
