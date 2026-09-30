using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace KOZ39.IconGenerator
{
    internal sealed partial class IconGeneratorWindow
    {
        internal GameObject CameraTarget => CombinedPreviewPrimary ? null : PreviewTarget;

        internal GameObject ItemCameraTarget =>
            CombinedPreviewPrimary ? null
            : PreviewTarget != null ? PreviewTarget
            : SelectedSourceObjects.Take(2).ToArray() is { Length: 1 } items ? items[0]
            : null;

        internal IconCameraPose EditingCamera => _settings.CameraFor(CameraTarget);

        internal bool HasCopiedItemCamera =>
            !string.IsNullOrEmpty(SessionState.GetString(CopiedItemCameraKey, ""));

        internal bool UsesItemCamera(GameObject target) =>
            _settings.FindItemCamera(target) is { enabled: true };

        internal void CopyItemCamera(GameObject target)
        {
            if (IsSourceObjectAvailable(target))
            {
                SessionState.SetString(
                    CopiedItemCameraKey,
                    JsonUtility.ToJson(_settings.CameraFor(target))
                );
            }
        }

        internal void PasteItemCamera(IEnumerable<GameObject> targets)
        {
            var copied = SessionState.GetString(CopiedItemCameraKey, "");

            if (string.IsNullOrEmpty(copied))
            {
                return;
            }

            var camera = JsonUtility.FromJson<IconCameraPose>(copied);
            var validTargets = targets.Where(IsSourceObjectAvailable).Distinct().ToArray();

            if (validTargets.Length == 0)
            {
                return;
            }

            Undo.RecordObject(_settings, "Icon Generator paste item camera");

            foreach (var target in validTargets)
            {
                _settings.UseItemCamera(target, true);
                _settings.SetCamera(target, camera);
            }

            if (validTargets.Length == 1)
            {
                _combinedPreviewPrimary = false;
                SetPreviewTarget(validTargets[0]);
            }

            _view?.RefreshSourceTree();
            SettingsChanged();
        }

        internal void UseSharedCamera(IEnumerable<GameObject> targets)
        {
            var validTargets = targets
                .Where(target => IsSourceObjectAvailable(target) && UsesItemCamera(target))
                .Distinct()
                .ToArray();

            if (validTargets.Length == 0)
            {
                return;
            }

            Undo.RecordObject(_settings, "Icon Generator use common camera");

            foreach (var target in validTargets)
            {
                _settings.UseItemCamera(target, false);
            }

            _view?.RefreshSourceTree();
            SettingsChanged();
        }

        internal void SetItemCameraEnabled(bool enabled)
        {
            var target = ItemCameraTarget;

            if (target == null)
            {
                return;
            }

            Undo.RecordObject(_settings, "Icon Generator item camera");
            _settings.UseItemCamera(target, enabled);
            _combinedPreviewPrimary = false;
            SetPreviewTarget(target);
            _view?.RefreshSourceTree();
            SettingsChanged();
        }

        internal void SetRotation(Vector3 value)
        {
            var pose = EditingCamera;
            pose.rotation = value;
            ApplyCamera(pose, "Icon Generator rotation");
        }

        private void ApplyCamera(IconCameraPose pose, string undoName) =>
            ChangeSettings(undoName, () => _settings.SetCamera(CameraTarget, pose));

        internal void BeginViewInteraction()
        {
            Undo.IncrementCurrentGroup();
            _interactionUndoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Icon Generator view");
            _previewRenderPending = true;
        }

        internal void EndViewInteraction()
        {
            if (_interactionUndoGroup < 0)
            {
                return;
            }

            Undo.CollapseUndoOperations(_interactionUndoGroup);
            _interactionUndoGroup = -1;
            _previewRenderPending = true;
        }

        internal void OrbitPreview(Vector2 delta) =>
            SetRotation(EditingCamera.rotation + new Vector3(delta.y * 0.4f, delta.x * 0.4f, 0));

        internal void PanPreview(Vector2 delta)
        {
            var pose = EditingCamera;
            pose.offset += new Vector2(-delta.x, delta.y);
            ApplyCamera(pose, "Icon Generator view");
        }

        internal void SetFramingPositionPercent(Vector2 positionPercent)
        {
            var pose = EditingCamera;
            pose.offset = -positionPercent / 100;
            ApplyCamera(pose, "Icon Generator position");
        }

        internal void ZoomPreview(float delta) =>
            SetZoom(
                Mathf.Clamp(
                    EditingCamera.zoom * Mathf.Exp(-delta * 0.06f),
                    IconGeneratorSettings.SliderMinZoom,
                    EditingCamera.SliderZoomMaximum
                )
            );

        internal void SetZoom(float value)
        {
            var pose = EditingCamera;
            pose.zoom = value;
            ApplyCamera(pose, "Icon Generator zoom");
        }

        internal void FramePreview()
        {
            var pose = EditingCamera;
            pose.offset = Vector2.zero;
            pose.zoom = IconGeneratorSettings.DefaultZoom;
            ApplyCamera(pose, "Icon Generator frame preview");
        }

        internal void ResetView()
        {
            var pose = EditingCamera;
            pose.rotation = new Vector3(0, 180, 0);
            pose.offset = Vector2.zero;
            pose.zoom = IconGeneratorSettings.DefaultZoom;
            ApplyCamera(pose, "Icon Generator reset view");
        }

        internal void SetKeepWholeObject(bool value)
        {
            var pose = EditingCamera;
            pose.keepWholeObject = value;
            ApplyCamera(pose, "Icon Generator keep in frame");
        }

        internal void SetUseVisibleArea(bool value)
        {
            var pose = EditingCamera;
            pose.useVisibleArea = value;
            ApplyCamera(pose, "Icon Generator use visible area");
        }

        internal void ResetZoom() => SetZoom(IconGeneratorSettings.DefaultZoom);

        internal void SetPadding(float value)
        {
            var pose = EditingCamera;
            pose.padding = value;
            ApplyCamera(pose, "Icon Generator padding");
        }

        internal void ResetPadding() => SetPadding(IconGeneratorSettings.DefaultPadding);
    }
}
