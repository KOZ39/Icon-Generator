using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace KOZ39.IconGenerator
{
    internal sealed class IconOverwriteDialog : IconComparisonDialog<IconOverwrite>
    {
        private readonly Dictionary<string, Texture2D> _textures = new();
        protected override string ElementPrefix => "overwrite";
        protected override string ListName => "overwrite-files";
        protected override string TitleKey => "titles.OverwriteFiles";
        protected override string MessageKey => "messages.ConfirmOverwriteFiles";

        internal ISet<string> SelectedPaths =>
            SelectedItems == null
                ? null
                : new HashSet<string>(
                    SelectedItems.Select(item => item.Path),
                    StringComparer.OrdinalIgnoreCase
                );

        internal static ISet<string> Confirm(
            IReadOnlyList<IconOverwrite> overwrites,
            IconGeneratorLocalization localization
        ) => ShowDialog<IconOverwriteDialog>(overwrites, localization).SelectedPaths;

        protected override bool IsInitiallySelected(IconOverwrite item) => true;

        protected override Texture CurrentIcon(IconOverwrite item) =>
            LoadTexture($"current:{item.Path}", () => File.ReadAllBytes(item.Path));

        protected override Texture NewIcon(IconOverwrite item) =>
            LoadTexture($"new:{item.Path}", () => item.Png);

        protected override string Name(IconOverwrite item) => Path.GetFileName(item.Path);

        protected override string Detail(IconOverwrite item) =>
            IconFileOutput.TryGetAssetPath(item.Path, out var assetPath)
                ? Path.GetDirectoryName(assetPath)?.Replace('\\', '/')
                : Path.GetDirectoryName(item.Path);

        protected override IEnumerable<string> SearchTexts(IconOverwrite item)
        {
            yield return Path.GetFileName(item.Path);
        }

        private Texture2D LoadTexture(string key, Func<byte[]> read)
        {
            if (_textures.TryGetValue(key, out var texture))
            {
                return texture;
            }

            try
            {
                var bytes = read();

                if (bytes != null)
                {
                    texture = new Texture2D(2, 2) { hideFlags = HideFlags.HideAndDontSave };

                    if (!texture.LoadImage(bytes))
                    {
                        DestroyImmediate(texture);
                        texture = null;
                    }
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }

            _textures[key] = texture;

            return texture;
        }

        private void OnDestroy()
        {
            foreach (var texture in _textures.Values.Where(texture => texture != null))
            {
                DestroyImmediate(texture);
            }

            _textures.Clear();
        }
    }
}
