using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace KOZ39.IconGenerator
{
    internal sealed partial class IconGeneratorWindow
    {
        internal GameObject PreviewTarget => _previewTarget;

        internal bool ShowCombinedPreview =>
            PreviewTarget != null
            && _selectedPreview?.IsValid == true
            && _combinedPreview?.IsValid == true
            && (
                !_selectedPreview.HasSameContent(_combinedPreview)
                || !_settings.CameraFor(PreviewTarget).Matches(_settings.SharedCamera)
            );

        internal bool CombinedPreviewPrimary =>
            PreviewTarget != null
            && _combinedPreview?.IsValid == true
            && (
                _selectedPreview?.IsValid != true
                || (ShowCombinedPreview && _combinedPreviewPrimary)
            );

        internal bool InsetPreviewCollapsed
        {
            get => _insetPreviewCollapsed;
            set
            {
                if (_insetPreviewCollapsed == value)
                {
                    return;
                }

                _insetPreviewCollapsed = value;

                if (!value)
                {
                    _previewRenderPending = true;
                }

                _view?.RefreshPreviewOptions();
            }
        }

        internal bool PreviewAtOutputSize
        {
            get => _previewAtOutputSize;
            set
            {
                if (_previewAtOutputSize == value)
                {
                    return;
                }

                _previewAtOutputSize = value;
                _previewRenderPending = true;
                _view?.RefreshPreviewOptions();
            }
        }

        internal bool ShowGrid
        {
            get => EditorPrefs.GetBool(GridKey, false);
            set => EditorPrefs.SetBool(GridKey, value);
        }

        internal int PreviewResolution
        {
            get =>
                IconGeneratorSettings.NormalizePreviewResolution(
                    EditorPrefs.GetInt(
                        PreviewResolutionKey,
                        IconGeneratorSettings.DefaultPreviewResolution
                    )
                );
            set
            {
                EditorPrefs.SetInt(
                    PreviewResolutionKey,
                    IconGeneratorSettings.NormalizePreviewResolution(value)
                );
                _previewRenderPending = true;
            }
        }

        internal void SwapPreviews()
        {
            if (!ShowCombinedPreview)
            {
                return;
            }

            _combinedPreviewPrimary = !_combinedPreviewPrimary;

            if (InsetPreviewCollapsed)
            {
                _previewRenderPending = true;
            }

            _view?.RefreshPreviewOptions();
        }

        private void DestroyPreview()
        {
            DestroyPreviewSession(
                ref _selectedPreview,
                _view?.SelectedPreview,
                _selectedPreviewWarnings
            );
            DestroyPreviewSession(
                ref _combinedPreview,
                _view?.CombinedPreview,
                _combinedPreviewWarnings
            );
        }

        private static void DestroyPreviewSession(
            ref IconPreviewSession session,
            IconPreview preview,
            List<string> warnings
        )
        {
            if (preview != null)
            {
                preview.Texture = null;
            }

            session?.Dispose();
            session = null;
            warnings.Clear();
        }

        private void RequestPreviewRender()
        {
            _selectedPreview?.RefreshBounds();
            _combinedPreview?.RefreshBounds();
            _previewRenderPending = true;
        }

        private void EnsurePreviewDependencies()
        {
            if (_rebuildAssets != null)
            {
                return;
            }

            _rebuildAssets = new HashSet<Object>();
            _renderAssets = new HashSet<Object>();

            foreach (var source in SourceTargets.Where(target => target != null))
            {
                if (EditorUtility.IsPersistent(source))
                {
                    _rebuildAssets.Add(source);
                }

                foreach (var renderer in source.GetComponentsInChildren<Renderer>(true))
                {
                    _rebuildAssets.Add(
                        renderer is SkinnedMeshRenderer skin ? skin.sharedMesh
                        : renderer.TryGetComponent<MeshFilter>(out var filter) ? filter.sharedMesh
                        : null
                    );
                    _renderAssets.UnionWith(renderer.sharedMaterials);
                }

                _renderAssets.UnionWith(
                    NdmfCaptureBridge.FindReferencedMaterials(source.transform.root)
                );
            }

            _rebuildAssets.RemoveWhere(asset => asset == null);
            _renderAssets.RemoveWhere(asset => asset == null);
            _rebuildAssetPaths = AssetPaths(_rebuildAssets);
            _renderAssetPaths = new HashSet<string>(
                AssetDatabase.GetDependencies(AssetPaths(_renderAssets).ToArray(), true),
                StringComparer.OrdinalIgnoreCase
            );

            static HashSet<string> AssetPaths(IEnumerable<Object> assets) =>
                new(
                    assets
                        .Select(AssetDatabase.GetAssetPath)
                        .Where(path => !string.IsNullOrEmpty(path)),
                    StringComparer.OrdinalIgnoreCase
                );
        }

        internal void RequestPreviewRebuild()
        {
            _previewRebuildPending = _previewValidationPending = true;
            _previewRenderPending = _referenceRefreshPending = true;
        }

        internal void SetPreviewTarget(GameObject target)
        {
            if (target != null && !IsSourceObjectAvailable(target))
            {
                return;
            }

            if (_previewTarget == target)
            {
                return;
            }

            _previewTarget = target;
            _previewRenderPending = true;
            _view?.RefreshPreviewOptions();
        }

        private void RenderPreview()
        {
            _capturing = true;

            try
            {
                if (_previewTarget != null && !IsSourceObjectAvailable(_previewTarget))
                {
                    SetPreviewTarget(null);
                }

                var resolution = PreviewAtOutputSize ? _settings.resolution : PreviewResolution;
                var environmentValidated = false;

                if (_previewValidationPending)
                {
                    CaptureEnvironment.Validate();
                    _previewValidationPending = false;
                    environmentValidated = true;
                }

                if (_previewRebuildPending)
                {
                    DestroyPreview();
                    _previewRebuildPending = false;
                    _sourceHierarchyHash = CalculateSourceHierarchyHash();
                    _sceneLighting = FindSceneLighting();
                    _rebuildAssets = null;
                }

                ResolvedCaptureSelection selection = null;
                CaptureEffectsCache effectsCache = null;
                var selectedError = PreparePreviewSession(
                    ref _selectedPreview,
                    _view.SelectedPreview,
                    PreviewTarget,
                    resolution,
                    _selectedPreviewWarnings,
                    ref selection,
                    ref effectsCache,
                    ref environmentValidated
                );
                Exception combinedError = null;

                if (PreviewTarget != null)
                {
                    combinedError = PreparePreviewSession(
                        ref _combinedPreview,
                        _view.CombinedPreview,
                        null,
                        resolution,
                        _combinedPreviewWarnings,
                        ref selection,
                        ref effectsCache,
                        ref environmentValidated
                    );
                }
                else
                {
                    DestroyPreviewSession(
                        ref _combinedPreview,
                        _view.CombinedPreview,
                        _combinedPreviewWarnings
                    );
                }

                var combinedPrimary = CombinedPreviewPrimary;

                if (combinedPrimary)
                {
                    if (combinedError == null)
                    {
                        combinedError = RenderPreviewSession(
                            ref _combinedPreview,
                            _view.CombinedPreview,
                            resolution,
                            _combinedPreviewWarnings
                        );
                    }
                }
                else if (selectedError == null)
                {
                    selectedError = RenderPreviewSession(
                        ref _selectedPreview,
                        _view.SelectedPreview,
                        resolution,
                        _selectedPreviewWarnings
                    );
                }

                var mainSession = combinedPrimary ? _combinedPreview : _selectedPreview;

                if (
                    mainSession?.IsValid == true
                    && _settings.CameraFor(mainSession.CaptureTarget).ShouldUseVisibleArea
                )
                {
                    var offset = mainSession.ConstrainOffset(_settings);
                    var pose = _settings.CameraFor(mainSession.CaptureTarget);

                    if (pose.offset != offset)
                    {
                        pose.offset = offset;
                        _settings.SetCamera(mainSession.CaptureTarget, pose);
                        _serializedSettings.Update();
                        _settingsUndoState = CaptureSettingsState();
                        _settingsSavePending = true;
                    }
                }

                if (!ShowCombinedPreview && !combinedPrimary)
                {
                    _view.CombinedPreview.Texture = null;
                }
                else if (ShowCombinedPreview && !InsetPreviewCollapsed)
                {
                    if (combinedPrimary && selectedError == null)
                    {
                        selectedError = RenderPreviewSession(
                            ref _selectedPreview,
                            _view.SelectedPreview,
                            resolution,
                            _selectedPreviewWarnings
                        );
                    }
                    else if (!combinedPrimary && combinedError == null)
                    {
                        combinedError = RenderPreviewSession(
                            ref _combinedPreview,
                            _view.CombinedPreview,
                            resolution,
                            _combinedPreviewWarnings
                        );
                    }
                }

                Repaint();
                _view.RefreshSettings();
                var error = new[] { selectedError, combinedError }.FirstOrDefault(exception =>
                    exception != null
                    && !(
                        exception is CaptureValidationException { Key: "NoRenderers" }
                        && (
                            (PreviewTarget != null && ReferenceEquals(exception, selectedError))
                            || HasIndividualCaptureTargets()
                        )
                    )
                );

                if (error == null)
                {
                    ShowCaptureWarnings();
                }
                else
                {
                    ShowError(error);
                }
            }
            catch (Exception exception)
            {
                DestroyPreview();
                ShowError(exception);
            }
            finally
            {
                _capturing = false;
            }
        }

        private bool HasIndividualCaptureTargets()
        {
            if (_settings.generationMode == IconGenerationMode.Combined)
            {
                return false;
            }

            var selection = ResolveCaptureSelection(out var tree);

            return IconGeneration
                .IndividualCaptureTargets(
                    selection,
                    tree,
                    _settings.generationMode,
                    _settings.ShouldIncludeInactiveObjects(individual: true),
                    _settings
                )
                .Any(CanCaptureTarget);
        }

        private Exception PreparePreviewSession(
            ref IconPreviewSession session,
            IconPreview preview,
            GameObject target,
            int resolution,
            List<string> warnings,
            ref ResolvedCaptureSelection selection,
            ref CaptureEffectsCache effectsCache,
            ref bool environmentValidated
        )
        {
            try
            {
                var includeInactive = _settings.ShouldIncludeInactiveObjects(target != null);

                if (
                    session?.IsValid != true
                    || session.CaptureTarget != target
                    || session.IncludeInactiveObjects != includeInactive
                    || session.BlendShapeWeightMode != _settings.blendShapeWeightMode
                )
                {
                    DestroyPreviewSession(ref session, preview, warnings);

                    if (!environmentValidated)
                    {
                        CaptureEnvironment.Validate();
                        environmentValidated = true;
                    }

                    selection ??= ResolveCaptureSelection(out _);
                    effectsCache ??= new CaptureEffectsCache();
                    session = new IconPreviewSession(
                        selection,
                        resolution,
                        warning => warnings.Add(GetErrorMessage(warning)),
                        _settings.blendShapeWeightMode,
                        target,
                        includeInactive,
                        effectsCache,
                        validateEnvironment: false
                    );
                }

                return null;
            }
            catch (Exception exception)
            {
                DestroyPreviewSession(ref session, preview, warnings);

                return exception;
            }
        }

        private Exception RenderPreviewSession(
            ref IconPreviewSession session,
            IconPreview preview,
            int resolution,
            List<string> warnings
        )
        {
            try
            {
                session.SetResolution(resolution);
                session.Render(_settings);
                preview.Texture = session.Texture;
                preview.MarkDirtyRepaint();

                return null;
            }
            catch (Exception exception)
            {
                DestroyPreviewSession(ref session, preview, warnings);

                return exception;
            }
        }

        private void ShowCaptureWarnings() =>
            _view.SetStatus(
                string.Join(
                    "\n\n",
                    _selectedPreviewWarnings.Concat(_combinedPreviewWarnings).Distinct()
                ),
                false
            );
    }
}
