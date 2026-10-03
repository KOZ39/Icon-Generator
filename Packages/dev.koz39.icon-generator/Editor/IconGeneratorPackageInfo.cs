using UnityEditor.PackageManager;

namespace KOZ39.IconGenerator
{
    internal static class IconGeneratorPackageInfo
    {
        internal const string Name = "dev.koz39.icon-generator";
        internal const string DisplayName = "Icon Generator";
        internal const string LogPrefix = "[" + DisplayName + "]";
        internal const string PreferencesPrefix = "KOZ39.IconGenerator.";

        private static readonly PackageInfo _info = PackageInfo.FindForAssembly(
            typeof(IconGeneratorPackageInfo).Assembly
        );

        internal static string Version => _info?.version ?? "unknown";
        internal static string AssetPath => _info?.assetPath ?? "Packages/" + Name;
        internal static string ResolvedPath => _info?.resolvedPath ?? AssetPath;
    }
}
