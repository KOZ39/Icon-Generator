using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace KOZ39.IconGenerator
{
    internal sealed partial class IconGeneratorWindow : EditorWindow
    {
        private const string CopiedItemCameraKey =
            IconGeneratorPackageInfo.PreferencesPrefix + "CopiedItemCamera";

        private const string GridKey = IconGeneratorPackageInfo.PreferencesPrefix + "Grid";

        private const string SourceTreeHeightKey =
            IconGeneratorPackageInfo.PreferencesPrefix + "SourceTreeHeight";

        private const string PreviewResolutionKey =
            IconGeneratorPackageInfo.PreferencesPrefix + "PreviewResolution";

        private IconGeneratorSettings _settings;
        private SerializedObject _serializedSettings;
        private CaptureSelectionStore _selectionStore;
        private IconGeneratorLocalization _localization;
        private IconGeneratorView _view;
        private IconPreviewSession _selectedPreview;
        private IconPreviewSession _combinedPreview;
        private readonly List<string> _selectedPreviewWarnings = new();
        private readonly List<string> _combinedPreviewWarnings = new();
        private bool _previewRebuildPending = true;
        private bool _previewValidationPending = true;
        private int _interactionUndoGroup = -1;
        private bool _previewRenderPending;
        private bool _settingsSavePending;
        private bool _referenceRefreshPending;
        private bool _projectRefreshPending;
        private bool _capturing;
        private bool _previewVisible = true;
        private double _nextPreviewRenderTime;
        private double _nextSettingsSaveTime;
        private double _nextStatePollTime;
        private int _sourceHierarchyHash;
        private Behaviour[] _sceneLighting;
        private string _settingsUndoState;
        private readonly List<Transform> _sourceTransforms = new();
        private HashSet<Object> _rebuildAssets;
        private HashSet<Object> _renderAssets;
        private HashSet<string> _rebuildAssetPaths;
        private HashSet<string> _renderAssetPaths;

        [SerializeField]
        private GameObject _previewTarget;

        [SerializeField]
        private bool _previewAtOutputSize;

        [SerializeField]
        private bool _combinedPreviewPrimary;

        [SerializeField]
        private bool _insetPreviewCollapsed;

        [SerializeField]
        private string _sourceSearch = "";

        [SerializeField]
        private SourceListFilter _sourceFilter;

        [SerializeField]
        private bool _saveSettingsExpanded;

        [SerializeField]
        private List<int> _selectedSourceIds = new();

        internal bool SaveSettingsExpanded
        {
            get => _saveSettingsExpanded;
            set => _saveSettingsExpanded = value;
        }

        [MenuItem("Tools/" + IconGeneratorPackageInfo.DisplayName)]
        internal static void Open()
        {
            var window = GetWindow<IconGeneratorWindow>();
            window.minSize = new Vector2(680, 600);
            window.Show();
        }

        private void OnEnable()
        {
            CaptureClone.CleanupAbandoned();
            _settings = IconGeneratorSettings.instance;
            _settings.Initialize();
            _serializedSettings = new SerializedObject(_settings);
            _selectionStore = new CaptureSelectionStore(_settings);
            _settingsUndoState = CaptureSettingsState();
            _localization = new IconGeneratorLocalization();
            _localization.Reload();
            titleContent = new GUIContent(IconGeneratorPackageInfo.DisplayName);
            EditorApplication.update += UpdateEditor;
            EditorApplication.hierarchyChanged += HierarchyChanged;
            EditorApplication.projectChanged += ProjectChanged;
            EditorApplication.playModeStateChanged += PlayModeChanged;
            ObjectChangeEvents.changesPublished += ObjectChanged;
            Undo.undoRedoPerformed += UndoChanged;
            EditorSceneManager.sceneSaved += SceneSaved;
            EditorSceneManager.sceneOpened += SceneOpened;
            EditorSceneManager.sceneClosed += SceneChanged;
            IconGeneratorAssetPostprocessor.AssetsChanged += AssetsChanged;
            AssemblyReloadEvents.beforeAssemblyReload += Cleanup;
            _previewRenderPending = _referenceRefreshPending = true;
        }

        public void CreateGUI()
        {
            if (_serializedSettings != null)
            {
                RebuildView();
            }
        }

        internal void RebuildView()
        {
            rootVisualElement.Unbind();
            _view = new IconGeneratorView(
                this,
                _localization,
                _settings,
                _serializedSettings,
                _selectionStore
            );
            _view.Build(rootVisualElement);
            _view.SelectedPreview.Texture = _selectedPreview?.Texture;
            _view.CombinedPreview.Texture = _combinedPreview?.Texture;
            _previewRenderPending = true;
        }

        private void OnBecameInvisible() => _previewVisible = false;

        private void OnBecameVisible()
        {
            if (_previewVisible)
            {
                return;
            }

            _previewVisible = true;
            _nextStatePollTime = 0;
            RequestPreviewRebuild();
        }

        private void OnDisable()
        {
            EditorApplication.update -= UpdateEditor;
            EditorApplication.hierarchyChanged -= HierarchyChanged;
            EditorApplication.projectChanged -= ProjectChanged;
            EditorApplication.playModeStateChanged -= PlayModeChanged;
            ObjectChangeEvents.changesPublished -= ObjectChanged;
            Undo.undoRedoPerformed -= UndoChanged;
            EditorSceneManager.sceneSaved -= SceneSaved;
            EditorSceneManager.sceneOpened -= SceneOpened;
            EditorSceneManager.sceneClosed -= SceneChanged;
            IconGeneratorAssetPostprocessor.AssetsChanged -= AssetsChanged;
            AssemblyReloadEvents.beforeAssemblyReload -= Cleanup;
            Cleanup();
            rootVisualElement.Unbind();
            _serializedSettings?.Dispose();
            _serializedSettings = null;
        }

        private void Cleanup()
        {
            SaveSettings();
            DestroyPreview();
            CaptureClone.CleanupAbandoned();
        }

        private void HierarchyChanged()
        {
            if (_capturing)
            {
                return;
            }

            _referenceRefreshPending = true;
            _previewValidationPending = true;
            _nextStatePollTime = 0;
        }

        private void SceneChanged(Scene scene) => RequestPreviewRebuild();

        private void SceneOpened(Scene scene, OpenSceneMode mode) => SceneChanged(scene);

        private void SceneSaved(Scene scene)
        {
            _referenceRefreshPending = true;
            _nextStatePollTime = 0;
        }

        private void PlayModeChanged(PlayModeStateChange state)
        {
            DestroyPreview();
            _previewRenderPending = true;
        }

        private void ProjectChanged()
        {
            _referenceRefreshPending = _previewValidationPending = _previewRenderPending = true;
            _nextStatePollTime = 0;
            _projectRefreshPending = true;
        }

        private void AssetsChanged(string[] paths)
        {
            if (_capturing || _selectionStore == null)
            {
                return;
            }

            EnsurePreviewDependencies();

            if (paths.Any(_rebuildAssetPaths.Contains))
            {
                RequestPreviewRebuild();
            }
            else if (paths.Any(_renderAssetPaths.Contains))
            {
                RequestPreviewRender();
            }
        }

        private void ApplyPendingProjectChanges()
        {
            if (!_projectRefreshPending || _view == null)
            {
                return;
            }

            _projectRefreshPending = false;

            if (_localization == null || _localization.HasChangedOnDisk)
            {
                _localization?.Reload();
                RebuildView();
                return;
            }

            _view.RefreshSourceTree();
            _view.RefreshSettings();
        }

        private void ObjectChanged(ref ObjectChangeEventStream stream)
        {
            if (_capturing)
            {
                return;
            }

            HashSet<Transform> scopeRoots = null;

            bool IsInCaptureScope(Transform transform)
            {
                scopeRoots ??= SourceTargets
                    .Where(target => target != null)
                    .Select(target => target.transform.root)
                    .ToHashSet();

                return scopeRoots.Contains(transform.root);
            }

            bool LightingRemoved() =>
                _sceneLighting != null && _sceneLighting.Any(item => item == null);

            bool AffectsLighting(Transform transform) =>
                transform.TryGetComponent<Light>(out _)
                || transform.TryGetComponent<ReflectionProbe>(out _)
                || _sceneLighting != null
                    && _sceneLighting.Any(item =>
                        item == null || item.transform.IsChildOf(transform)
                    );

            for (var i = 0; i < stream.length; i++)
            {
                if (stream.GetEventType(i) == ObjectChangeKind.ChangeAssetObjectProperties)
                {
                    stream.GetChangeAssetObjectPropertiesEvent(i, out var assetChange);
                    var asset = EditorUtility.InstanceIDToObject(assetChange.instanceId);

                    if (asset == null || !EditorUtility.IsPersistent(asset))
                    {
                        continue;
                    }

                    EnsurePreviewDependencies();
                    var owner = asset is Component assetComponent
                        ? assetComponent.gameObject
                        : asset as GameObject;

                    if (
                        (owner != null && IsInCaptureScope(owner.transform))
                        || _rebuildAssets.Contains(asset)
                    )
                    {
                        _previewRebuildPending =
                            _previewValidationPending =
                            _previewRenderPending =
                                true;
                    }
                    else if (
                        _renderAssets.Contains(asset)
                        || _renderAssetPaths.Contains(AssetDatabase.GetAssetPath(asset))
                    )
                    {
                        RequestPreviewRender();
                    }

                    continue;
                }

                if (stream.GetEventType(i) == ObjectChangeKind.UpdatePrefabInstances)
                {
                    stream.GetUpdatePrefabInstancesEvent(i, out var prefabChange);

                    foreach (var id in prefabChange.instanceIds)
                    {
                        if (
                            EditorUtility.InstanceIDToObject(id) is GameObject instance
                            && IsInCaptureScope(instance.transform)
                        )
                        {
                            RequestPreviewRebuild();
                            break;
                        }
                    }

                    continue;
                }

                if (stream.GetEventType(i) == ObjectChangeKind.CreateGameObjectHierarchy)
                {
                    stream.GetCreateGameObjectHierarchyEvent(i, out var created);

                    if (
                        EditorUtility.InstanceIDToObject(created.instanceId)
                            is GameObject createdObject
                        && (
                            createdObject.GetComponentInChildren<Light>(true) != null
                            || createdObject.GetComponentInChildren<ReflectionProbe>(true) != null
                        )
                    )
                    {
                        RequestPreviewRebuild();
                    }

                    continue;
                }

                if (stream.GetEventType(i) == ObjectChangeKind.DestroyGameObjectHierarchy)
                {
                    if (LightingRemoved())
                    {
                        RequestPreviewRebuild();
                    }

                    continue;
                }

                int instanceId;

                switch (stream.GetEventType(i))
                {
                    case ObjectChangeKind.ChangeGameObjectOrComponentProperties:
                        stream.GetChangeGameObjectOrComponentPropertiesEvent(i, out var change);
                        instanceId = change.instanceId;
                        break;
                    case ObjectChangeKind.ChangeGameObjectStructure:
                        stream.GetChangeGameObjectStructureEvent(i, out var structureChange);
                        instanceId = structureChange.instanceId;
                        break;
                    case ObjectChangeKind.ChangeGameObjectStructureHierarchy:
                        stream.GetChangeGameObjectStructureHierarchyEvent(
                            i,
                            out var hierarchyChange
                        );
                        instanceId = hierarchyChange.instanceId;
                        break;
                    default:
                        continue;
                }

                var target = EditorUtility.InstanceIDToObject(instanceId);

                if (
                    target is Component component
                    && component.gameObject.hideFlags == HideFlags.None
                )
                {
                    if (IsInCaptureScope(component.transform))
                    {
                        _previewRebuildPending =
                            _previewValidationPending =
                            _previewRenderPending =
                                true;
                    }
                    else if (AffectsLighting(component.transform))
                    {
                        RequestPreviewRebuild();
                    }
                }

                if (
                    target is GameObject gameObject
                    && gameObject.hideFlags == HideFlags.None
                    && (
                        IsInCaptureScope(gameObject.transform)
                        || AffectsLighting(gameObject.transform)
                    )
                )
                {
                    RequestPreviewRebuild();
                }
            }
        }

        private string CaptureSettingsState() =>
            _settings != null ? EditorJsonUtility.ToJson(_settings) : null;

        private void UndoChanged()
        {
            RequestPreviewRebuild();
            var state = CaptureSettingsState();

            if (state == _settingsUndoState)
            {
                return;
            }

            _settingsUndoState = state;
            _selectionStore = new CaptureSelectionStore(_settings);
            RebuildView();
        }

        internal void SettingsChanged()
        {
            if (_settings == null || _view == null)
            {
                return;
            }

            _settings.Normalize();
            var mainSession = CombinedPreviewPrimary ? _combinedPreview : _selectedPreview;
            var mainTarget = CombinedPreviewPrimary ? null : PreviewTarget;

            if (
                mainSession?.IsValid == true
                && !_settings.CameraFor(mainTarget).ShouldUseVisibleArea
                && !_previewRebuildPending
                && mainSession.CaptureTarget == mainTarget
                && mainSession.IncludeInactiveObjects
                    == _settings.ShouldIncludeInactiveObjects(mainTarget != null)
                && mainSession.BlendShapeWeightMode == _settings.blendShapeWeightMode
            )
            {
                var pose = _settings.CameraFor(mainTarget);
                pose.offset = mainSession.ConstrainOffset(_settings);
                _settings.SetCamera(mainTarget, pose);
            }

            _serializedSettings.Update();
            _settingsUndoState = CaptureSettingsState();
            _view.RefreshSettings();
            _previewRenderPending = _settingsSavePending = true;
            _nextSettingsSaveTime = EditorApplication.timeSinceStartup + 0.4;
        }

        private void SaveSettings()
        {
            if (_settings == null || _selectionStore == null)
            {
                return;
            }

            _selectionStore.Store();
            _settings.Persist();
            _settingsSavePending = false;
        }

        private void UpdateEditor()
        {
            if (
                _view == null
                || _capturing
                || EditorApplication.isCompiling
                || EditorApplication.isUpdating
            )
            {
                return;
            }

            var now = EditorApplication.timeSinceStartup;

            if (_settingsSavePending && now >= _nextSettingsSaveTime)
            {
                SaveSettings();
            }

            if (!_previewVisible)
            {
                return;
            }

            if (_referenceRefreshPending)
            {
                _referenceRefreshPending = false;
                var before = GetSelectionReferenceSignature();
                _selectionStore.Refresh();
                var after = GetSelectionReferenceSignature();

                if (before != after)
                {
                    _previewRebuildPending = _previewRenderPending = true;
                    _selectionStore.Store();
                    _settingsUndoState = CaptureSettingsState();
                    _settingsSavePending = true;
                }

                if (!_projectRefreshPending)
                {
                    _view.RefreshSourceTree();
                }
            }

            ApplyPendingProjectChanges();

            if (now >= _nextStatePollTime)
            {
                _nextStatePollTime = now + 0.5;
                _view.RefreshOutputFolder();
                var hierarchyHash = CalculateSourceHierarchyHash();

                if (hierarchyHash != _sourceHierarchyHash)
                {
                    _sourceHierarchyHash = hierarchyHash;
                    _referenceRefreshPending =
                        _previewRebuildPending =
                        _previewRenderPending =
                            true;
                }
            }

            if (!_previewRenderPending || now < _nextPreviewRenderTime)
            {
                return;
            }

            _previewRenderPending = false;
            _nextPreviewRenderTime = now + 1.0 / 60.0;
            RenderPreview();
        }

        private string GetSelectionReferenceSignature() =>
            string.Join(
                "|",
                _selectionStore
                    .Sources.Concat(_selectionStore.Excluded)
                    .Concat(_selectionStore.Enabled)
                    .Concat(_selectionStore.IgnoredBlendShapeObjects)
                    .Select(item => $"{item.Id}{item.State}{item.Label}")
            );

        private int CalculateSourceHierarchyHash()
        {
            unchecked
            {
                var hash = 17;

                foreach (var source in _selectionStore.Sources)
                {
                    if (source.Target == null)
                    {
                        continue;
                    }

                    hash =
                        hash * 31
                        + ResolvedCaptureSelection
                            .IsEditorOnly(source.Target.transform)
                            .GetHashCode();

                    source.Target.GetComponentsInChildren(true, _sourceTransforms);

                    foreach (var transform in _sourceTransforms)
                    {
                        hash = hash * 31 + transform.GetInstanceID();
                        hash = hash * 31 + transform.localToWorldMatrix.GetHashCode();
                        hash = hash * 31 + transform.gameObject.activeSelf.GetHashCode();
                        hash = hash * 31 + transform.CompareTag("EditorOnly").GetHashCode();
                    }
                }

                _sourceTransforms.Clear();

                return hash;
            }
        }

        private static Behaviour[] FindSceneLighting() =>
            Object
                .FindObjectsByType<Light>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .Cast<Behaviour>()
                .Concat(
                    Object.FindObjectsByType<ReflectionProbe>(
                        FindObjectsInactive.Include,
                        FindObjectsSortMode.None
                    )
                )
                .Where(item => !EditorSceneManager.IsPreviewScene(item.gameObject.scene))
                .ToArray();

        private void ShowError(Exception exception)
        {
            _view?.SetStatus(GetErrorMessage(exception), true);

            if (exception is not CaptureValidationException)
            {
                Debug.LogException(exception);
            }
        }

        private string GetErrorMessage(Exception exception) =>
            exception is CaptureValidationException validation
                ? _localization.Text($"messages.{validation.Key}", validation.Arguments)
                : exception.Message;

        private void ChangeSettings(string undoName, Action change)
        {
            Undo.RecordObject(_settings, undoName);
            change();
            SettingsChanged();
        }
    }
}
