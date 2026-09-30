using System.Reflection;
using UnityEditor;

namespace KOZ39.IconGenerator
{
    internal static class ProjectFolderNavigation
    {
        internal static void Open(DefaultAsset folder)
        {
            if (folder == null)
            {
                return;
            }

            EditorUtility.FocusProjectWindow();
            var browserType = typeof(Editor).Assembly.GetType("UnityEditor.ProjectBrowser");
            const BindingFlags flags =
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var projectWindow = EditorWindow.GetWindow(browserType);

            browserType.GetMethod("Init", flags)?.Invoke(projectWindow, null);

            if (browserType.GetMethod("IsTwoColumns", flags)?.Invoke(projectWindow, null) is true)
            {
                browserType
                    .GetField("m_InternalSelectionChange", flags)
                    ?.SetValue(projectWindow, true);
                Selection.activeObject = folder;
                browserType
                    .GetMethod("ShowFolderContents", flags)
                    ?.Invoke(projectWindow, new object[] { folder.GetInstanceID(), true });
            }
            else
            {
                ProjectWindowUtil.ShowCreatedAsset(folder);
            }

            projectWindow.Repaint();
        }
    }
}
