using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;

namespace KOZ39.IconGenerator
{
    internal sealed class IconGeneratorAssetPostprocessor : AssetPostprocessor
    {
        internal static readonly Dictionary<string, int> PendingIconImports = new(
            StringComparer.OrdinalIgnoreCase
        );

        internal static event Action<string[]> AssetsChanged;

        private void OnPreprocessTexture()
        {
            if (
                PendingIconImports.TryGetValue(assetPath, out var maxTextureSize)
                && assetImporter is TextureImporter importer
            )
            {
                IconFileOutput.ApplyIconSettings(importer, maxTextureSize);
            }
        }

        private static void OnPostprocessAllAssets(
            string[] importedAssets,
            string[] deletedAssets,
            string[] movedAssets,
            string[] movedFromAssetPaths
        )
        {
            var paths = importedAssets
                .Concat(deletedAssets)
                .Concat(movedAssets)
                .Concat(movedFromAssetPaths)
                .ToArray();

            if (paths.Length > 0)
            {
                AssetsChanged?.Invoke(paths);
            }
        }
    }
}
