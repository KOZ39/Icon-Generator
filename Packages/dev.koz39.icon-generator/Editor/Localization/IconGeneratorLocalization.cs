using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace KOZ39.IconGenerator
{
    internal sealed class IconGeneratorLocalization
    {
        internal sealed class Locale
        {
            internal string Code;
            internal string DisplayName;
            internal Dictionary<string, string> Strings;
        }

        internal readonly List<Locale> Locales = new();
        private string _loadedFolder;
        private string _loadedSignature;
        private string PreferenceKey => IconGeneratorPackageInfo.PreferencesPrefix + "Locale";

        internal string SelectedCode
        {
            get => EditorPrefs.GetString(PreferenceKey, "en-US");
            set => EditorPrefs.SetString(PreferenceKey, value);
        }

        private static string DefaultFolder =>
            $"{IconGeneratorPackageInfo.ResolvedPath}/Editor/Localization";

        private static IEnumerable<string> FindTranslationFiles(string folder) =>
            (
                Directory.Exists(folder)
                    ? Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
                    : Enumerable.Empty<string>()
            ).Where(path =>
                path.EndsWith(".jsonc", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
            );

        private static string CalculateSignature(string folder)
        {
            try
            {
                return string.Join(
                    "|",
                    FindTranslationFiles(folder)
                        .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                        .Select(path =>
                        {
                            var info = new FileInfo(path);

                            return $"{path}:{info.Length}:{info.LastWriteTimeUtc.Ticks}";
                        })
                );
            }
            catch (IOException)
            {
                return null;
            }
            catch (UnauthorizedAccessException)
            {
                return null;
            }
        }

        internal bool HasChangedOnDisk =>
            _loadedSignature == null
            || CalculateSignature(_loadedFolder ?? DefaultFolder) != _loadedSignature;

        internal void Reload(string folder = null)
        {
            Locales.Clear();
            folder ??= DefaultFolder;
            _loadedFolder = folder;
            _loadedSignature = CalculateSignature(folder);
            var files = FindTranslationFiles(folder)
                .GroupBy(Path.GetFileNameWithoutExtension, StringComparer.OrdinalIgnoreCase);

            foreach (var group in files)
            {
                try
                {
                    if (group.Count() != 1)
                    {
                        throw new FormatException(
                            "Multiple translation files use the same locale code."
                        );
                    }

                    var code = CultureInfo.GetCultureInfo(group.Key).Name;

                    if (
                        string.IsNullOrEmpty(code)
                        || !string.Equals(code, group.Key, StringComparison.OrdinalIgnoreCase)
                    )
                    {
                        throw new FormatException("The filename must be a valid locale code.");
                    }

                    var json = JObject.Parse(
                        File.ReadAllText(group.Single()),
                        new JsonLoadSettings
                        {
                            DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error,
                        }
                    );
                    var displayName = json["language"]?["displayName"]?.Value<string>();
                    var translationGroups = json.Properties()
                        .Where(property => property.Name != "language")
                        .ToArray();

                    if (string.IsNullOrWhiteSpace(displayName) || translationGroups.Length == 0)
                    {
                        throw new FormatException(
                            "The locale must define 'language.displayName' and at least one translation group."
                        );
                    }

                    var values = new Dictionary<string, string>();

                    foreach (var translationGroup in translationGroups)
                    {
                        if (translationGroup.Value is not JObject strings)
                        {
                            throw new FormatException(
                                $"Translation group '{translationGroup.Name}' must be an object."
                            );
                        }

                        foreach (var property in strings.Properties())
                        {
                            if (property.Value.Type != JTokenType.String)
                            {
                                throw new FormatException(
                                    $"Translation '{property.Path}' must be a string."
                                );
                            }

                            var key = $"{translationGroup.Name}.{property.Name}";

                            if (!values.TryAdd(key, property.Value.Value<string>()))
                            {
                                throw new FormatException(
                                    $"Translation key '{key}' is duplicated."
                                );
                            }
                        }
                    }

                    Locales.Add(
                        new Locale
                        {
                            Code = code,
                            DisplayName = displayName,
                            Strings = values,
                        }
                    );
                }
                catch (Exception exception)
                {
                    Debug.LogWarning(
                        $"{IconGeneratorPackageInfo.LogPrefix} Could not load locale '{group.Key}': {exception.Message}"
                    );
                }
            }

            if (Locales.All(locale => locale.Code != "en-US"))
            {
                Debug.LogWarning(
                    $"{IconGeneratorPackageInfo.LogPrefix} The English fallback locale 'en-US' is missing. Untranslated entries will display their keys."
                );
                Locales.Add(
                    new Locale
                    {
                        Code = "en-US",
                        DisplayName = "English",
                        Strings = new Dictionary<string, string>(),
                    }
                );
            }

            Locales.Sort((left, right) => string.CompareOrdinal(left.Code, right.Code));
        }

        internal string Text(string key, params object[] arguments)
        {
            var selectedCode = SelectedCode;
            var locale = Locales.FirstOrDefault(item => item.Code == selectedCode);
            var fallback = Locales.FirstOrDefault(item => item.Code == "en-US");
            var fallbackText = Lookup(fallback, key) ?? key;
            var text = Lookup(locale, key) ?? fallbackText;

            if (arguments.Length == 0)
            {
                return text;
            }

            try
            {
                return string.Format(CultureInfo.CurrentCulture, text, arguments);
            }
            catch (FormatException)
            {
                try
                {
                    return string.Format(CultureInfo.CurrentCulture, fallbackText, arguments);
                }
                catch (FormatException)
                {
                    return key;
                }
            }
        }

        private static string Lookup(Locale locale, string key) =>
            locale != null && locale.Strings.TryGetValue(key, out var text) ? text : null;
    }
}
