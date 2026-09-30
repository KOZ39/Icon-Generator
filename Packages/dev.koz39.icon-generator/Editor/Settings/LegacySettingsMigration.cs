using UnityEditor;
using UnityEngine;

namespace KOZ39.IconGenerator
{
    internal static class LegacySettingsMigration
    {
        internal static Vector3 ReadRotation()
        {
            if (EditorPrefs.GetBool("IconGenUseCustomAngle", false))
            {
                return ConvertCustomRotation(
                    new Vector3(
                        EditorPrefs.GetFloat("IconGenCustomAngleX", 0),
                        EditorPrefs.GetFloat("IconGenCustomAngleY", 180),
                        EditorPrefs.GetFloat("IconGenCustomAngleZ", 0)
                    )
                );
            }

            return ConvertDirection(EditorPrefs.GetInt("IconGenCaptureDirection", 0));
        }

        internal static Vector3 ConvertDirection(int direction) =>
            direction switch
            {
                1 => Vector3.zero,
                2 => new Vector3(0, 270, 0),
                3 => new Vector3(0, 90, 0),
                _ => new Vector3(0, 180, 0),
            };

        internal static Vector3 ConvertCustomRotation(Vector3 angle)
        {
            var value = Quaternion.Euler(angle).eulerAngles;

            value.x = Mathf.Repeat(value.x + 180, 360) - 180;

            if (value.x > 90 || value.x < -90)
            {
                value.x = (value.x > 0 ? 180 : -180) - value.x;
                value.y += 180;
                value.z += 180;
            }

            return IconGeneratorSettings.NormalizeRotation(value);
        }

        internal static void MigrateLocale()
        {
            var key = IconGeneratorPackageInfo.PreferencesPrefix + "Locale";

            if (EditorPrefs.HasKey(key))
            {
                return;
            }

            var locales = new[] { "en-US", "ko-KR", "ja-JP", "zh-CN" };
            var index = Mathf.Clamp(
                EditorPrefs.GetInt("IconGenLanguage", 0),
                0,
                locales.Length - 1
            );
            EditorPrefs.SetString(key, locales[index]);
        }
    }
}
