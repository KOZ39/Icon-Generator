using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor;
using System.IO;
using System.Text.RegularExpressions;

public static class Utils
{
    public static void AddSectionTitle(VisualElement parent, string title)
    {
        var label = new Label(title)
        {
            style =
            {
                fontSize = 12,
                unityFontStyleAndWeight = FontStyle.Bold,
                marginTop = 10,
                marginBottom = 2,
                marginLeft = 2
            }
        };
        parent.Add(label);
    }

    public static void SetStyleMarginBottom(VisualElement element, float marginBottom = 10)
    {
        element.style.marginBottom = marginBottom;
    }

    public static void SetSelfAndChildrenActive(GameObject go, bool active)
    {
        if (go == null) return;
        go.SetActive(active);
        foreach (Transform child in go.transform)
        {
            SetSelfAndChildrenActive(child.gameObject, active);
        }
    }

    public static void ChangeLayerRecursively(GameObject gameObject, int layer)
    {
        if (gameObject == null) return;
        gameObject.layer = layer;
        foreach (Transform child in gameObject.transform)
        {
            ChangeLayerRecursively(child.gameObject, layer);
        }
    }

    public static Texture2D ResizeTexture(Texture2D source, int targetWidth, int targetHeight)
    {
        if (source == null) return null;
        Texture2D tex2D = new Texture2D(targetWidth, targetHeight, TextureFormat.ARGB32, false);
        RenderTexture rt = null;

        try
        {
            rt = RenderTexture.GetTemporary(targetWidth, targetHeight, 0, RenderTextureFormat.ARGB32);
            RenderTexture.active = rt;
            Graphics.Blit(source, rt);
            tex2D.ReadPixels(new Rect(0, 0, targetWidth, targetHeight), 0, 0);
            tex2D.Apply();
            return tex2D;
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Error resizing texture: {e.Message}");
            if (tex2D != null) {
                Object.DestroyImmediate(tex2D);
            }
            return null;
        }
        finally
        {
            RenderTexture.active = null;
            if (rt != null)
            {
                RenderTexture.ReleaseTemporary(rt);
            }
        }
    }

    public static void MakeTexture2DClear(Texture2D tex2D, int width, int height)
    {
        if (tex2D == null) return;
        Color[] clearColors = new Color[width * height];
        Color clear = Color.clear;
        for (int i = 0; i < clearColors.Length; i++)
        {
            clearColors[i] = clear;
        }
        tex2D.SetPixels(0, 0, width, height, clearColors);
        tex2D.Apply();
    }

    public static string SanitizeFileName(string name)
    {
        if (string.IsNullOrEmpty(name)) return "Unnamed";
        string invalidChars = Regex.Escape(new string(Path.GetInvalidFileNameChars()) + new string(Path.GetInvalidPathChars()));
        string invalidRegStr = string.Format(@"([{0}]*\.+$)|([{0}])", invalidChars);
        return Regex.Replace(name, invalidRegStr, "_");
    }

    public static void SaveAndImportIconTexture(string filePath, byte[] bytes, Localization localization, bool pingAsset)
    {
        try
        {
            File.WriteAllBytes(filePath, bytes);
            AssetDatabase.ImportAsset(filePath);

            TextureImporter importer = AssetImporter.GetAtPath(filePath) as TextureImporter;
            if (importer != null)
            {
                importer.alphaIsTransparency = true;
                importer.textureType = TextureImporterType.Sprite;
                importer.SaveAndReimport();
            }
            else
            {
                Debug.LogWarning(localization.GetLocalizedText("TextureImporterWarning", filePath));
            }
            Debug.Log(localization.GetLocalizedText("GenerationComplete", filePath));

            if (pingAsset)
            {
                Object obj = AssetDatabase.LoadAssetAtPath<Texture2D>(filePath);
                if (obj != null) EditorGUIUtility.PingObject(obj);
            }
        }
        catch (System.IO.IOException ex)
        {
            Debug.LogError(localization.GetLocalizedText("ErrorSavingTextureFile", filePath, ex.Message));
        }
        catch (System.UnauthorizedAccessException ex)
        {
            Debug.LogError(localization.GetLocalizedText("PermissionErrorSavingTextureFile", filePath, ex.Message));
        }
        catch (System.Exception e)
        {
            Debug.LogError(localization.GetLocalizedText("GenerationFailedError", e.Message, e.StackTrace));
        }
    }
}
