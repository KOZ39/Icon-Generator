using System.Collections.Generic;

public static class Data
{
    public const int CAPTURE_LAYER = 21;
    public const int DEFAULT_TEMP_CAPTURE_RESOLUTION = 2048;
    public const int ICON_THUMBNAIL_DISPLAY_RESOLUTION = 256;
    public const int DEFAULT_ZOOM_LEVEL = 100;
    public const float ORTHO_SIZE_MULTIPLIER_AT_DEFAULT_ZOOM = 1.1f;
    public const string LANGUAGE_PREF_KEY = "IconGenLanguage";
    public const string LOCALIZATION_BASE_PATH = "Assets/KOZ39/IconGenerator/Editor/Localization/";
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
    }

    public static readonly Dictionary<UILanguage, LanguageInfo> LanguageMapping = new Dictionary<UILanguage, LanguageInfo>()
    {
        { UILanguage.English, new LanguageInfo { Code = "en", DisplayName = "English" } },
        { UILanguage.Korean, new LanguageInfo { Code = "ko", DisplayName = "한국어 (Korean)" } },
        { UILanguage.Japanese, new LanguageInfo { Code = "ja", DisplayName = "日本語 (Japanese)" } }
    };
}
