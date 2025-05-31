using System.Collections.Generic;

public static class Data
{
    public const int CAPTURE_LAYER = 21;
    public const int DEFAULT_TEMP_CAPTURE_RESOLUTION = 2048;
    public const int ICON_THUMBNAIL_DISPLAY_RESOLUTION = 256;
    public const int DEFAULT_ZOOM_LEVEL = 100;
    public const float ORTHO_SIZE_MULTIPLIER_AT_DEFAULT_ZOOM = 1.1f;
    public const string LANGUAGE_PREF_KEY = "IconGenLanguage";
    public const string OUTPUT_PATH_PREF_KEY = "IconGenOutputPath";
    public const string TEMP_RESOLUTION_PREF_KEY = "IconGenTempResolution";
    public const string ZOOM_LEVEL_PREF_KEY = "IconGenZoomLevel";
    public const string ICON_SIZE_PREF_KEY = "IconGenIconSize";
    public const string CAPTURE_DIRECTION_PREF_KEY = "IconGenCaptureDirection";
    public const string USE_CUSTOM_ANGLE_PREF_KEY = "IconGenUseCustomAngle";
    public const string CUSTOM_ANGLE_PREF_KEY_X = "IconGenCustomAngleX";
    public const string CUSTOM_ANGLE_PREF_KEY_Y = "IconGenCustomAngleY";
    public const string CUSTOM_ANGLE_PREF_KEY_Z = "IconGenCustomAngleZ";
    public const string PING_ASSET_PREF_KEY = "IconGenPingAsset";
    public const string LOCALIZATION_EN_JSON_GUID = "d20fd17cbeee2c348963acb1ff23e006";
    public const string LOCALIZATION_KO_JSON_GUID = "92fe17625d1955140844bb5347c8262e";
    public const string LOCALIZATION_JA_JSON_GUID = "993dabeee68601f4eb8fab9386ec6a2e";


    public enum CaptureDirection
    {
        Front,
        Rear,
        Left,
        Right
    }

    public enum UILanguage
    {
        English,
        Korean,
        Japanese
    }

    public struct LanguageInfo
    {
        public string Code;
        public string DisplayName;
        public string Guid;
    }

    public static readonly Dictionary<UILanguage, LanguageInfo> LanguageMapping = new Dictionary<UILanguage, LanguageInfo>()
    {
        { UILanguage.English, new LanguageInfo { Code = "en", DisplayName = "English", Guid = LOCALIZATION_EN_JSON_GUID } },
        { UILanguage.Korean, new LanguageInfo { Code = "ko", DisplayName = "한국어 (Korean)", Guid = LOCALIZATION_KO_JSON_GUID } },
        { UILanguage.Japanese, new LanguageInfo { Code = "ja", DisplayName = "日本語 (Japanese)", Guid = LOCALIZATION_JA_JSON_GUID } }
    };
}
