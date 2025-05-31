using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.IO;

public static class Processor
{
    public static bool EnsureOutputDirectoryExists(string directoryPath, Localization localization)
    {
        if (!Directory.Exists(directoryPath))
        {
            try
            {
                Directory.CreateDirectory(directoryPath);
                AssetDatabase.Refresh();
                return true;
            }
            catch (System.IO.IOException ex)
            {
                Debug.LogError(localization.GetLocalizedText("DirectoryCreationError", directoryPath, ex.Message));
                return false;
            }
            catch (System.UnauthorizedAccessException ex)
            {
                Debug.LogError(localization.GetLocalizedText("DirectoryCreationPermissionError", directoryPath, ex.Message));
                return false;
            }
            catch (System.Exception ex)
            {
                Debug.LogError(localization.GetLocalizedText("DirectoryCreationError", directoryPath, ex.Message));
                return false;
            }
        }
        return true;
    }

    public static List<GameObject> FindObjectsToProcess(GameObject sourceObject, bool includeInactive)
    {
        List<GameObject> objectsToProcess = new List<GameObject>();
        Renderer[] renderersInHierarchy = sourceObject.GetComponentsInChildren<Renderer>(includeInactive);

        if (renderersInHierarchy != null)
        {
            foreach (Renderer r in renderersInHierarchy)
            {
                if (r != null && r.gameObject != null && !r.gameObject.CompareTag("EditorOnly") && !(r is ParticleSystemRenderer))
                {
                    if (!objectsToProcess.Contains(r.gameObject))
                    {
                        objectsToProcess.Add(r.gameObject);
                    }
                }
            }
        }
        return objectsToProcess;
    }

    public static void GenerateCombinedIcon(GameObject sourceObject, List<GameObject> objectsToCombine, int targetSize, Data.CaptureDirection direction, bool useCustomAngle, Vector3 customAngle, string outputDirectory, int tempResolution, int zoom, bool goToOutputDirectory, Localization localization)
    {
        if (objectsToCombine == null || objectsToCombine.Count == 0)
        {
            Debug.LogWarning(localization.GetLocalizedText("NoActiveProcessableObjectsCombined"));
            return;
        }

        GameObject tempCombinedParent = null;
        try
        {
            tempCombinedParent = Core.CreateTemporaryCombinedObject(sourceObject, objectsToCombine);
            if (tempCombinedParent == null)
            {
                Debug.LogWarning(localization.GetLocalizedText("NoActiveProcessableObjectsCombined"));
                return;
            }

            Texture2D combinedIcon = Core.GenerateIconInternal(tempCombinedParent, targetSize, direction, useCustomAngle, customAngle, tempResolution, zoom, localization);

            if (combinedIcon != null)
            {
                byte[] bytes = combinedIcon.EncodeToPNG();
                string fileNameBase = Utils.SanitizeFileName(sourceObject.name);
                string directionInfo = useCustomAngle ? $"CustomAngle({customAngle.x:0},{customAngle.y:0},{customAngle.z:0})" : direction.ToString().ToLower();

                string filePath = Path.Combine(outputDirectory, $"{fileNameBase}_icon_{directionInfo}_{targetSize}x{targetSize}.png");

                Utils.SaveAndImportTexture(filePath, bytes, localization, "GenerationComplete", "TextureImporterWarning", "GenerationFailedError");

                if (goToOutputDirectory)
                {
                    Object obj = AssetDatabase.LoadAssetAtPath<Texture2D>(filePath);
                    if (obj != null) EditorGUIUtility.PingObject(obj);
                }
            }
            else
            {
                Debug.LogError(localization.GetLocalizedText("GenerationFailedError", sourceObject.name) + " (" + localization.GetLocalizedText("CombinedIcon") + ")");
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError(localization.GetLocalizedText("GenerationError", sourceObject.name, e.Message, e.StackTrace) + " (" + localization.GetLocalizedText("CombinedIcon") + ")");
        }
        finally
        {
            if (tempCombinedParent != null)
            {
                Object.DestroyImmediate(tempCombinedParent);
            }
        }
    }

    public static void GenerateIndividualIcons(List<GameObject> objectsToProcess, int targetSize, Data.CaptureDirection direction, bool useCustomAngle, Vector3 customAngle, string outputDirectory, bool generatedCombined, int tempResolution, int zoom, bool goToOutputDirectory, Localization localization)
    {
        for (int i = 0; i < objectsToProcess.Count; i++)
        {
            GameObject currentObject = objectsToProcess[i];
            if (currentObject == null)
            {
                Debug.LogWarning($"Skipping null or invalid object reference at index {i} in processing list for individual icon.");
                continue;
            }

            string progressTitle = localization.GetLocalizedText("IconGenerationProgress");
            string progressMessage = $"{localization.GetLocalizedText("ProcessingIndividual")} {currentObject.name}... ({i + 1}/{objectsToProcess.Count})";
            EditorUtility.DisplayProgressBar(progressTitle, progressMessage, (float)i / objectsToProcess.Count);

            Texture2D generatedIcon = null;
            try
            {
                generatedIcon = Core.GenerateIconInternal(currentObject, targetSize, direction, useCustomAngle, customAngle, tempResolution, zoom, localization);

                if (generatedIcon != null)
                {
                    byte[] bytes = generatedIcon.EncodeToPNG();
                    string fileNameBase = Utils.SanitizeFileName(currentObject.name);
                    string directionInfo = useCustomAngle ? $"CustomAngle({customAngle.x:0},{customAngle.y:0},{customAngle.z:0})" : direction.ToString().ToLower();

                    string filePath = Path.Combine(outputDirectory, $"{fileNameBase}_icon_{directionInfo}_{targetSize}x{targetSize}.png");

                    Utils.SaveAndImportTexture(filePath, bytes, localization, "GenerationComplete", "TextureImporterWarning", "GenerationFailedError");

                    if (goToOutputDirectory && (!generatedCombined || i == objectsToProcess.Count - 1))
                    {
                        Object obj = AssetDatabase.LoadAssetAtPath<Texture2D>(filePath);
                        if (obj != null) EditorGUIUtility.PingObject(obj);
                    }
                }
                else
                {
                    Debug.LogError(localization.GetLocalizedText("GenerationFailedError", currentObject.name));
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError(localization.GetLocalizedText("GenerationError", currentObject.name, e.Message, e.StackTrace));
            }
            finally
            {
                EditorUtility.DisplayProgressBar(progressTitle, $"{localization.GetLocalizedText("FinalizingProgress")} {currentObject.name}", (float)(i + 1) / objectsToProcess.Count);
            }
        }
    }
}
