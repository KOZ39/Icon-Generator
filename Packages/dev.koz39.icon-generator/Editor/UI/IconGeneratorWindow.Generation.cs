using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace KOZ39.IconGenerator
{
    internal sealed partial class IconGeneratorWindow
    {
        internal bool UsesSelectedSaveScope => _settings.saveScope == IconSaveScope.SelectedItems;

        private void LogGenerationResult(IconGenerationResult result)
        {
            var summary = _localization.Text(
                "messages.GenerationSummary",
                result.SavedPaths.Count,
                result.Failures.Count,
                result.SkippedTargets.Count
            );

            if (result.Cancelled)
            {
                summary = _localization.Text("messages.GenerationCancelled", summary);
            }

            var sections = new List<string> { $"{IconGeneratorPackageInfo.LogPrefix} {summary}" };
            AddSection("GenerationSucceeded", result.SavedPaths);
            AddSection(
                "GenerationFailed",
                result.Failures.Select(failure =>
                    $"{TargetDisplayName(failure.TargetName)}: {GetErrorMessage(failure.Error)}"
                )
            );
            AddSection(
                "GenerationSkipped",
                result.SkippedTargets.Select(item =>
                    $"{TargetDisplayName(item.TargetName)}: {_localization.Text($"messages.{item.Reason}")}"
                )
            );
            AddSection("GenerationWarnings", result.Warnings.Select(GetErrorMessage));
            var message = string.Join("\n\n", sections);

            if (result.Failures.Count > 0)
            {
                Debug.LogError(message);
            }
            else if (result.Warnings.Count > 0)
            {
                Debug.LogWarning(message);
            }
            else
            {
                Debug.Log(message);
            }

            void AddSection(string key, IEnumerable<string> entries)
            {
                var details = string.Join("\n", entries);

                if (details.Length > 0)
                {
                    sections.Add($"{_localization.Text($"messages.{key}")}\n{details}");
                }
            }
        }

        private string TargetDisplayName(string targetName) =>
            targetName ?? _localization.Text("options.GenerationModeCombined");

        private bool ReportProgressAndCheckCancel(GameObject target, int index, int count) =>
            EditorUtility.DisplayCancelableProgressBar(
                IconGeneratorPackageInfo.DisplayName,
                TargetDisplayName(target != null ? target.name : null),
                (float)index / count
            );

        internal void GenerateTarget(GameObject target)
        {
            if (CanCaptureTarget(target))
            {
                GenerateTargets(new[] { target });
            }
        }

        internal void GenerateTargets(IEnumerable<GameObject> targets)
        {
            var available = targets.Where(CanCaptureTarget).Distinct().ToArray();

            if (available.Length > 0)
            {
                Generate(available);
            }
        }

        internal void Generate() => Generate(null);

        private void Generate(IReadOnlyList<GameObject> captureTargets)
        {
            if (_capturing)
            {
                return;
            }

            _capturing = true;
            var showProgress = false;

            try
            {
                _selectionStore.Refresh();
                var resolved = _selectionStore.Resolve(out var tree);

                if (UsesSelectedSaveScope)
                {
                    resolved = resolved.WithCaptureTargets(captureTargets ?? SelectedSourceObjects);
                }

                _view.RefreshSourceTree(tree);
                var result = IconGeneration.Run(
                    resolved,
                    tree,
                    _settings,
                    (target, index, count) =>
                    {
                        showProgress = !Application.isBatchMode && count > 1;

                        return showProgress && ReportProgressAndCheckCancel(target, index, count);
                    },
                    existing =>
                    {
                        if (showProgress)
                        {
                            EditorUtility.ClearProgressBar();
                        }

                        return IconOverwriteDialog.Confirm(existing, _localization);
                    },
                    captureTargets: captureTargets
                );

                ApplyPendingProjectChanges();
                LogGenerationResult(result);
                ShowCaptureWarnings();

                if (!result.Cancelled)
                {
                    if (showProgress)
                    {
                        EditorUtility.ClearProgressBar();
                        showProgress = false;
                    }

                    LinkMenuIcons(resolved.Sources, tree, result.Icons);
                }

                if (result.Cancelled && result.Failures.Count == 0)
                {
                    return;
                }

                SaveSettings();
                _view.RefreshSettings();
                _view.RefreshOutputFolder();
            }
            catch (Exception exception)
            {
                ApplyPendingProjectChanges();
                var details =
                    exception is CaptureValidationException
                        ? GetErrorMessage(exception)
                        : exception.ToString();
                Debug.LogError(
                    $"{IconGeneratorPackageInfo.LogPrefix} {_localization.Text("messages.GenerationFailed")}\n{details}"
                );
            }
            finally
            {
                if (showProgress)
                {
                    EditorUtility.ClearProgressBar();
                }

                _capturing = false;
            }
        }

        private void LinkMenuIcons(
            IReadOnlyList<GameObject> sources,
            CaptureSelectionTree tree,
            IReadOnlyList<(GameObject Target, string Path)> icons
        )
        {
            if (
                !MenuIconLinker.IsAvailable
                || _settings.menuIconLinkMode == MenuIconLinkMode.Disabled
                || icons.Count == 0
            )
            {
                return;
            }

            try
            {
                var candidates = MenuIconLinker.FindCandidates(sources, tree, icons);

                if (candidates.Count == 0)
                {
                    return;
                }

                IReadOnlyList<MenuIconCandidate> selected =
                    _settings.menuIconLinkMode == MenuIconLinkMode.EmptyOnly
                        ? candidates
                            .Where(candidate => candidate.Selected && candidate.CurrentIcon == null)
                            .ToArray()
                    : Application.isBatchMode ? null
                    : MenuIconLinkDialog.Confirm(candidates, _localization);

                if (selected == null || selected.Count == 0)
                {
                    return;
                }

                var linked = MenuIconLinker.Apply(selected);
                Debug.Log(
                    $"{IconGeneratorPackageInfo.LogPrefix} {_localization.Text("messages.MenuIconsLinked", linked)}"
                );
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }

        internal void SetMenuIconLinkMode(MenuIconLinkMode value) =>
            ChangeSettings(
                "Icon Generator menu icon linking",
                () => _settings.menuIconLinkMode = value
            );

        internal void SetResolution(int value) =>
            ChangeSettings("Icon Generator resolution", () => _settings.resolution = value);

        internal void SetGenerationMode(IconGenerationMode value) =>
            ChangeSettings(
                "Icon Generator generation mode",
                () => _settings.generationMode = value
            );

        internal void SetExistingFileAction(ExistingFileAction value) =>
            ChangeSettings(
                "Icon Generator file conflict handling",
                () => _settings.existingFileAction = value
            );

        internal void SetSaveScope(IconSaveScope value)
        {
            Undo.RecordObject(_settings, "Icon Generator save scope");

            if (UsesSelectedSaveScope || value == IconSaveScope.SelectedItems)
            {
                _previewRebuildPending = true;
            }

            _settings.saveScope = value;
            SettingsChanged();
        }

        internal void BrowseOutput()
        {
            var directory = EditorUtility.OpenFolderPanel(
                _localization.Text("labels.SaveFolder"),
                Application.dataPath,
                ""
            );

            if (string.IsNullOrEmpty(directory))
            {
                return;
            }

            var root = $"{Path.GetDirectoryName(Application.dataPath).Replace('\\', '/')}/";
            Undo.RecordObject(_settings, "Icon Generator output directory");
            _settings.outputDirectory = directory.StartsWith(
                root,
                StringComparison.OrdinalIgnoreCase
            )
                ? directory.Substring(root.Length)
                : directory;
            SettingsChanged();
        }

        internal void OpenOutput()
        {
            try
            {
                var directory = IconFileOutput.ResolveDirectoryPath(_settings.outputDirectory);
                var assetPath = IconFileOutput.GetProjectFolderPath(directory);

                if (assetPath == null)
                {
                    return;
                }

                var folder = AssetDatabase.LoadAssetAtPath<DefaultAsset>(assetPath);

                if (folder == null)
                {
                    return;
                }

                ProjectFolderNavigation.Open(folder);
            }
            catch (Exception exception)
            {
                ShowError(exception);
            }
        }
    }
}
