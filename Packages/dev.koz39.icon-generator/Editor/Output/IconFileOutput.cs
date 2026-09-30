using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace KOZ39.IconGenerator
{
    internal static class IconFileOutput
    {
        internal static string ResolveDirectoryPath(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new CaptureValidationException("InvalidSaveFolder");
            }

            try
            {
                var root = Path.GetDirectoryName(Application.dataPath);
                var path = Path.GetFullPath(
                    Path.IsPathRooted(value) ? value : Path.Combine(root, value)
                );

                if (File.Exists(path))
                {
                    throw new CaptureValidationException("InvalidSaveFolder");
                }

                return path;
            }
            catch (CaptureValidationException)
            {
                throw;
            }
            catch (Exception)
            {
                throw new CaptureValidationException("InvalidSaveFolder");
            }
        }

        internal static string GetProjectFolderPath(string absoluteDirectory)
        {
            var normalizedPath = absoluteDirectory.Replace('\\', '/').TrimEnd('/');
            var assetPath = FileUtil.GetProjectRelativePath(normalizedPath);

            return AssetDatabase.IsValidFolder(assetPath) ? assetPath : null;
        }

        internal static string SanitizeName(string value)
        {
            value = (value ?? "").Trim();

            if (value.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            {
                value = value.Substring(0, value.Length - 4);
            }

            foreach (var character in Path.GetInvalidFileNameChars())
            {
                value = value.Replace(character, '_');
            }

            value = value.TrimEnd('.', ' ');

            if (string.IsNullOrWhiteSpace(value))
            {
                return "Icon";
            }

            var stem = value.Split('.')[0].ToUpperInvariant();

            if (
                stem == "CON"
                || stem == "PRN"
                || stem == "AUX"
                || stem == "NUL"
                || (
                    stem.Length == 4
                    && (stem.StartsWith("COM") || stem.StartsWith("LPT"))
                    && stem[3] >= '1'
                    && stem[3] <= '9'
                )
            )
            {
                value = $"_{value}";
            }

            return value;
        }

        internal static string ChoosePath(
            string directory,
            string name,
            ExistingFileAction action,
            ISet<string> reservedPaths = null
        ) =>
            CandidatePaths(directory, name)
                .First(path =>
                    reservedPaths?.Contains(path) != true
                    && (
                        action != ExistingFileAction.AddNumber
                        || (!File.Exists(path) && !Directory.Exists(path))
                    )
                );

        private static IEnumerable<string> CandidatePaths(string directory, string name)
        {
            var stem = SanitizeName(name);
            yield return Path.Combine(directory, $"{stem}.png");

            for (var index = 1; ; index++)
            {
                yield return Path.Combine(directory, $"{stem}_{index}.png");
            }
        }

        internal static void Save(Texture2D texture, string path) =>
            Save(texture.EncodeToPNG(), path, texture.width);

        internal static void Save(byte[] png, string path, int width)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var contentsChanged = !File.Exists(path) || !png.SequenceEqual(File.ReadAllBytes(path));

            if (contentsChanged)
            {
                var temporary = $"{path}.{Guid.NewGuid():N}.tmp";

                try
                {
                    File.WriteAllBytes(temporary, png);

                    if (File.Exists(path))
                    {
                        File.Replace(temporary, path, null);
                    }
                    else
                    {
                        File.Move(temporary, path);
                    }
                }
                finally
                {
                    if (File.Exists(temporary))
                    {
                        File.Delete(temporary);
                    }
                }
            }

            if (!TryGetAssetPath(path, out var assetPath))
            {
                return;
            }

            var maxTextureSize = Mathf.NextPowerOfTwo(width);
            var importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;

            if (importer == null)
            {
                SaveNewIcon(assetPath, maxTextureSize);
                return;
            }

            if (contentsChanged)
            {
                AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);
                importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            }

            if (importer == null)
            {
                return;
            }

            var changed = false;

            if (HasUncompressedIconSettings(importer))
            {
                importer.textureCompression = TextureImporterCompression.CompressedHQ;
                changed = true;
            }

            if (importer.maxTextureSize < maxTextureSize)
            {
                importer.maxTextureSize = maxTextureSize;
                changed = true;
            }

            if (changed)
            {
                WriteAndReimport(assetPath);
            }
        }

        private static void SaveNewIcon(string assetPath, int maxTextureSize)
        {
            IconGeneratorAssetPostprocessor.PendingIconImports[assetPath] = maxTextureSize;

            try
            {
                AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);
            }
            finally
            {
                IconGeneratorAssetPostprocessor.PendingIconImports.Remove(assetPath);
            }

            var importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;

            if (importer == null || HasIconSettings(importer, maxTextureSize))
            {
                return;
            }

            ApplyIconSettings(importer, maxTextureSize);
            WriteAndReimport(assetPath);
        }

        private static void WriteAndReimport(string assetPath)
        {
            AssetDatabase.WriteImportSettingsIfDirty(assetPath);
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);
        }

        internal static bool TryGetAssetPath(string path, out string assetPath)
        {
            var assetsRoot =
                $"{Path.GetFullPath(Application.dataPath).TrimEnd(Path.DirectorySeparatorChar)}{Path.DirectorySeparatorChar}";

            if (!path.StartsWith(assetsRoot, StringComparison.OrdinalIgnoreCase))
            {
                assetPath = null;

                return false;
            }

            assetPath = $"Assets/{path.Substring(assetsRoot.Length).Replace('\\', '/')}";

            return true;
        }

        private static bool HasSpriteIconSettings(TextureImporter importer) =>
            importer.textureType == TextureImporterType.Sprite
            && importer.alphaIsTransparency
            && importer.sRGBTexture
            && !importer.mipmapEnabled;

        private static bool HasUncompressedIconSettings(TextureImporter importer) =>
            HasSpriteIconSettings(importer)
            && importer.textureCompression == TextureImporterCompression.Uncompressed
            && importer.maxTextureSize == Mathf.NextPowerOfTwo(importer.maxTextureSize)
            && importer.maxTextureSize <= IconGeneratorSettings.IconResolutions.Max();

        private static bool HasIconSettings(TextureImporter importer, int maxTextureSize) =>
            HasSpriteIconSettings(importer)
            && importer.textureCompression == TextureImporterCompression.CompressedHQ
            && importer.maxTextureSize == maxTextureSize;

        internal static void ApplyIconSettings(TextureImporter importer, int maxTextureSize)
        {
            importer.textureType = TextureImporterType.Sprite;
            importer.alphaIsTransparency = true;
            importer.sRGBTexture = true;
            importer.mipmapEnabled = false;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            importer.maxTextureSize = maxTextureSize;
        }
    }
}
