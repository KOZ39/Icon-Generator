using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using Object = UnityEngine.Object;

namespace KOZ39.IconGenerator
{
    internal readonly struct IconGenerationFailure
    {
        internal readonly GameObject Target;
        internal readonly string TargetName;
        internal readonly Exception Error;

        internal IconGenerationFailure(GameObject target, Exception error)
        {
            Target = target;
            TargetName = target != null ? target.name : null;
            Error = error;
        }
    }

    internal readonly struct IconOverwrite
    {
        internal readonly string Path;
        internal readonly byte[] Png;

        internal IconOverwrite(string path, byte[] png)
        {
            Path = path;
            Png = png;
        }
    }

    internal readonly struct IconGenerationSkip
    {
        internal readonly GameObject Target;
        internal readonly string TargetName;
        internal readonly string Reason;

        internal IconGenerationSkip(GameObject target, string reason)
        {
            Target = target;
            TargetName = target != null ? target.name : null;
            Reason = reason;
        }
    }

    internal readonly struct IconGenerationResult
    {
        internal readonly bool Cancelled;
        internal readonly IReadOnlyList<IconGenerationFailure> Failures;
        internal readonly IReadOnlyList<string> SavedPaths;
        internal readonly IReadOnlyList<IconGenerationSkip> SkippedTargets;
        internal readonly int CompletedTargetCount;
        internal readonly IReadOnlyList<CaptureValidationException> Warnings;
        internal readonly IReadOnlyList<(GameObject Target, string Path)> Icons;

        internal IconGenerationResult(
            bool cancelled,
            IReadOnlyList<IconGenerationFailure> failures,
            IReadOnlyList<string> savedPaths,
            IReadOnlyList<IconGenerationSkip> skippedTargets,
            int completedTargetCount,
            IReadOnlyList<CaptureValidationException> warnings = null,
            IReadOnlyList<(GameObject Target, string Path)> icons = null
        )
        {
            Warnings = warnings ?? Array.Empty<CaptureValidationException>();
            Icons = icons ?? Array.Empty<(GameObject Target, string Path)>();
            Cancelled = cancelled;
            Failures = failures;
            SavedPaths = savedPaths;
            SkippedTargets = skippedTargets;
            CompletedTargetCount = completedTargetCount;
        }
    }

    internal static class IconGeneration
    {
        internal static IEnumerable<GameObject> IndividualCaptureTargets(
            ResolvedCaptureSelection selection,
            CaptureSelectionTree tree,
            IconGenerationMode generationMode,
            bool includeInactiveObjects,
            IconGeneratorSettings settings = null
        )
        {
            IEnumerable<GameObject> targets = selection.CaptureTargets;

            if (targets != null && !includeInactiveObjects)
            {
                var included = tree
                    .Roots.SelectMany(root => root.DescendantsAndSelf())
                    .Where(node => node.Included)
                    .Select(node => node.Target)
                    .ToHashSet();
                targets = targets.Where(included.Contains);
            }

            targets ??= tree.IndividualCaptureTargets(selection.Sources, includeInactiveObjects);

            if (selection.CaptureTargets == null && settings != null)
            {
                targets = targets.Concat(
                    selection.Sources.Where(source =>
                        settings.FindItemCamera(source) is { enabled: true }
                        && !ResolvedCaptureSelection.IsEditorOnly(source.transform)
                        && (includeInactiveObjects || !selection.IsExcluded(source.transform))
                    )
                );
            }

            return targets
                .Distinct()
                .Where(target =>
                    generationMode != IconGenerationMode.Both
                    || selection.Sources.Count != 1
                    || target != selection.Sources[0]
                    || selection.IsExcluded(target.transform)
                    || settings?.FindItemCamera(target) is { enabled: true }
                );
        }

        internal static IconGenerationResult Run(
            ResolvedCaptureSelection selection,
            CaptureSelectionTree tree,
            IconGeneratorSettings settings,
            Func<GameObject, int, int, bool> shouldCancel = null,
            Func<IReadOnlyList<IconOverwrite>, ISet<string>> confirmOverwrite = null,
            GameObject captureTarget = null,
            IReadOnlyList<GameObject> captureTargets = null
        )
        {
            CaptureEnvironment.Validate();
            var folder = IconFileOutput.ResolveDirectoryPath(settings.outputDirectory);
            var targets = new List<GameObject>();
            var explicitTargets = captureTargets != null || !ReferenceEquals(captureTarget, null);

            if (captureTargets != null)
            {
                targets.AddRange(captureTargets);
            }
            else if (!ReferenceEquals(captureTarget, null))
            {
                targets.Add(captureTarget);
            }
            else if (settings.generationMode != IconGenerationMode.Individual)
            {
                targets.Add(null);
            }

            if (!explicitTargets && settings.generationMode != IconGenerationMode.Combined)
            {
                targets.AddRange(
                    IndividualCaptureTargets(
                        selection,
                        tree,
                        settings.generationMode,
                        settings.ShouldIncludeInactiveObjects(
                            individual: true,
                            scope: settings.saveScope
                        ),
                        settings
                    )
                );
            }

            var savedCaptures = new IconCapture.CaptureSet();
            var namingSources =
                selection.CaptureTargets != null
                    ? ResolvedCaptureSelection.Normalize(selection.CaptureTargets)
                    : selection.Sources;
            var effectsCache = new CaptureEffectsCache();
            var timestamp = DateTime.Now;

            string FileNameFor(GameObject target, Vector3 rotation) =>
                IconFileNameTemplate.Expand(
                    settings.fileName,
                    namingSources,
                    settings.resolution,
                    timestamp,
                    rotation,
                    target
                );

            var reservedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var failures = new List<(int Index, IconGenerationFailure Failure)>();
            var savedPaths = new List<(int Index, string Path)>();
            var skippedTargets = new List<(int Index, IconGenerationSkip Skip)>();
            var pendingOverwrites =
                new List<(int Index, GameObject Target, string Path, byte[] Png, int Width)>();
            var icons = new List<(int Index, GameObject Target, string Path)>();
            var warnings = new List<CaptureValidationException>();
            var warningIdentities = new HashSet<(string Key, string Arguments)>();

            IconGenerationResult Result(bool cancelled, int completedTargetCount) =>
                new(
                    cancelled,
                    failures.OrderBy(item => item.Index).Select(item => item.Failure).ToArray(),
                    savedPaths.OrderBy(item => item.Index).Select(item => item.Path).ToArray(),
                    skippedTargets.OrderBy(item => item.Index).Select(item => item.Skip).ToArray(),
                    completedTargetCount,
                    warnings,
                    icons
                        .OrderBy(item => item.Index)
                        .Select(item => (item.Target, item.Path))
                        .ToArray()
                );

            void CollectWarning(CaptureValidationException warning)
            {
                if (warningIdentities.Add(warning.Identity))
                {
                    warnings.Add(warning);
                }
            }

            for (var index = 0; index < targets.Count; index++)
            {
                var target = targets[index];

                if (shouldCancel?.Invoke(target, index, targets.Count) == true)
                {
                    return Result(true, index);
                }

                Texture2D texture = null;

                try
                {
                    if (
                        !ReferenceEquals(target, null)
                        && (target == null || !selection.BelongsToSources(target))
                    )
                    {
                        throw new CaptureValidationException("CaptureTargetUnavailable");
                    }

                    var includeInactiveObjects = settings.ShouldIncludeInactiveObjects(
                        target != null,
                        settings.saveScope
                    );
                    var captureSelection =
                        explicitTargets && target != null && !includeInactiveObjects
                            ? selection.WithTargetBranchEnabled(target, includeDescendants: false)
                            : selection;
                    using var clone = CaptureClone.Build(
                        captureSelection,
                        target,
                        settings.blendShapeWeightMode,
                        includeInactiveObjects,
                        effectsCache
                    );

                    foreach (var warning in clone.Warnings)
                    {
                        CollectWarning(warning);
                    }

                    var pose = settings.CameraFor(target);
                    var signature = new IconCapture.CaptureSignature(clone, pose);

                    if (savedCaptures.Contains(signature))
                    {
                        var duplicatePath = savedCaptures.FindPath(signature);

                        if (duplicatePath != null)
                        {
                            icons.Add((index, target, duplicatePath));
                        }

                        skippedTargets.Add(
                            (index, new IconGenerationSkip(target, "DuplicateCapture"))
                        );
                        continue;
                    }

                    texture = IconCapture.Render(
                        clone,
                        settings,
                        settings.resolution,
                        rejectEmpty: true,
                        pose: pose,
                        validateEnvironment: false,
                        reportWarning: CollectWarning
                    );

                    var name = FileNameFor(target, pose.rotation);
                    var path = IconFileOutput.ChoosePath(
                        folder,
                        name,
                        settings.existingFileAction,
                        reservedPaths
                    );

                    if (settings.existingFileAction == ExistingFileAction.Ask && File.Exists(path))
                    {
                        reservedPaths.Add(path);
                        savedCaptures.Add(signature, path);
                        pendingOverwrites.Add(
                            (index, target, path, texture.EncodeToPNG(), texture.width)
                        );
                        continue;
                    }

                    IconFileOutput.Save(texture, path);
                    reservedPaths.Add(path);
                    savedPaths.Add((index, path));
                    icons.Add((index, target, path));
                    savedCaptures.Add(signature, path);
                }
                catch (CaptureValidationException exception) when (exception.Key == "EmptyCapture")
                {
                    skippedTargets.Add((index, new IconGenerationSkip(target, exception.Key)));
                }
                catch (CaptureValidationException exception)
                    when (!explicitTargets
                        && exception.Key == "NoRenderers"
                        && (
                            target != null || settings.generationMode != IconGenerationMode.Combined
                        )
                    )
                {
                    skippedTargets.Add((index, new IconGenerationSkip(target, exception.Key)));
                }
                catch (Exception exception)
                {
                    failures.Add((index, new IconGenerationFailure(target, exception)));
                }
                finally
                {
                    if (texture != null)
                    {
                        Object.DestroyImmediate(texture);
                    }
                }
            }

            if (pendingOverwrites.Count > 0)
            {
                var selected = confirmOverwrite?.Invoke(
                    pendingOverwrites
                        .Select(item => new IconOverwrite(item.Path, item.Png))
                        .ToArray()
                );

                if (selected == null)
                {
                    return Result(true, targets.Count);
                }

                var approvedOverwrites = new HashSet<string>(
                    selected,
                    StringComparer.OrdinalIgnoreCase
                );

                foreach (var pending in pendingOverwrites)
                {
                    if (!approvedOverwrites.Contains(pending.Path))
                    {
                        icons.Add((pending.Index, pending.Target, pending.Path));
                        skippedTargets.Add(
                            (
                                pending.Index,
                                new IconGenerationSkip(pending.Target, "ExistingFileSkipped")
                            )
                        );
                        continue;
                    }

                    try
                    {
                        IconFileOutput.Save(pending.Png, pending.Path, pending.Width);
                        savedPaths.Add((pending.Index, pending.Path));
                        icons.Add((pending.Index, pending.Target, pending.Path));
                    }
                    catch (Exception exception)
                    {
                        failures.Add(
                            (pending.Index, new IconGenerationFailure(pending.Target, exception))
                        );
                    }
                }
            }

            if (
                savedPaths.Count == 0
                && failures.Count == 0
                && skippedTargets.All(item =>
                    item.Skip.Reason != "EmptyCapture" && item.Skip.Reason != "ExistingFileSkipped"
                )
            )
            {
                throw new CaptureValidationException("NoRenderers");
            }

            return Result(false, targets.Count);
        }
    }
}
