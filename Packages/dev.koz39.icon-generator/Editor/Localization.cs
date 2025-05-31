using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

public class Localization
{
    private Dictionary<Data.UILanguage, Dictionary<string, string>> localizationData;

    public Localization()
    {
        localizationData = new Dictionary<Data.UILanguage, Dictionary<string, string>>();
    }

    public void SetupLocalization()
    {
        foreach (var pair in Data.LanguageMapping)
        {
            Data.UILanguage lang = pair.Key;
            Dictionary<string, string> langData = LoadLocalizationDataFile(lang);
            if (langData != null)
            {
                localizationData[lang] = langData;
            }
            else
            {
                if (lang != Data.UILanguage.English)
                {
                    Debug.LogWarning($"Localization file not found or failed to load for language: {lang} (Code: {pair.Value.Code}). Expected path: {Path.Combine(Data.LOCALIZATION_BASE_PATH, $"{pair.Value.Code}.json")}");
                }
            }
        }
        if (!localizationData.ContainsKey(Data.UILanguage.English))
        {
            Dictionary<string, string> englishData = LoadLocalizationDataFile(Data.UILanguage.English);
            if(englishData != null)
            {
                localizationData[Data.UILanguage.English] = englishData;
            }
            else
            {
                Debug.LogError($"Critical Error: English localization data (en.json) not loaded from {Data.LOCALIZATION_BASE_PATH}. UI will use raw keys. Check file existence and format.");
            }
        }
    }

    private Dictionary<string, string> LoadLocalizationDataFile(Data.UILanguage language)
    {
        if (!Data.LanguageMapping.TryGetValue(language, out Data.LanguageInfo langInfo))
        {
            Debug.LogError($"No language info defined for UILanguage: {language}");
            return null;
        }
        string filePath = Path.Combine(Data.LOCALIZATION_BASE_PATH, $"{langInfo.Code}.json");
        if (!File.Exists(filePath))
        {
            return null;
        }
        try
        {
            string jsonString = File.ReadAllText(filePath);
            var data = JsonConvert.DeserializeObject<Dictionary<string, string>>(jsonString);
            if (data == null)
            {
                Debug.LogError($"Failed to deserialize localization data from {filePath}. JSON format might be incorrect.");
                return null;
            }
            return data;
        }
        catch (System.IO.IOException ex)
        {
            Debug.LogError($"Error reading localization file {filePath}: {ex.Message}");
            return null;
        }
        catch (Newtonsoft.Json.JsonException ex)
        {
            Debug.LogError($"Error parsing localization file {filePath}: {ex.Message}");
            return null;
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"An unexpected error occurred loading localization file {filePath}: {ex.Message}");
            return null;
        }
    }

    public string GetLocalizedText(string key, params object[] args)
    {
        if (localizationData == null || localizationData.Count == 0 || !localizationData.ContainsKey(Data.UILanguage.English))
        {
            if (args != null && args.Length > 0)
            {
                try { return string.Format(key, args); }
                catch { return key; }
            }
            return key;
        }

        Data.UILanguage selectedLanguage = EditorPrefs.HasKey(Data.LANGUAGE_PREF_KEY) ? (Data.UILanguage)EditorPrefs.GetInt(Data.LANGUAGE_PREF_KEY) : Data.UILanguage.English;
        Data.UILanguage currentLanguage = localizationData.ContainsKey(selectedLanguage) ? selectedLanguage : Data.UILanguage.English;

        if (localizationData.TryGetValue(currentLanguage, out var texts) && texts.TryGetValue(key, out var text))
        {
            if (args != null && args.Length > 0)
            {
                try
                {
                    return string.Format(text, args);
                }
                catch (System.FormatException ex)
                {
                    Debug.LogError($"Error formatting localized string for key '{key}' and language '{currentLanguage}': {ex.Message}");
                    return text;
                }
            }
            return text;
        }

        if (currentLanguage != Data.UILanguage.English && localizationData.TryGetValue(Data.UILanguage.English, out var fallbackTexts) && fallbackTexts.TryGetValue(key, out var fallbackText))
        {
            if (args != null && args.Length > 0)
            {
                try { return string.Format(fallbackText, args); }
                catch (System.FormatException ex) { Debug.LogError($"Error formatting English fallback string for key '{key}': {ex.Message}"); return fallbackText; }
            }
            return fallbackText;
        }

        Debug.LogWarning($"Localization key '{key}' not found in any loaded data. Using raw key.");
        if (args != null && args.Length > 0)
        {
            try { return string.Format(key, args); }
            catch { return key; }
        }
        return key;
    }
}
