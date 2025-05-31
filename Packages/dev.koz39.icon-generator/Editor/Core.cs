using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using UnityEngine.Experimental.Rendering;

public static class Core
{
    public static GameObject CreateTemporaryCombinedObject(GameObject sourceObject, List<GameObject> objectsToCombine)
    {
        if (objectsToCombine == null || objectsToCombine.Count == 0)
        {
            return null;
        }

        GameObject tempCombinedParent = new GameObject("IconGen_TempParent");
        tempCombinedParent.transform.position = sourceObject.transform.position;
        tempCombinedParent.transform.rotation = sourceObject.transform.rotation;

        foreach (GameObject obj in objectsToCombine)
        {
            if (obj != null && obj.activeInHierarchy)
            {
                GameObject clonedObj = Object.Instantiate(obj, tempCombinedParent.transform);
                clonedObj.transform.localPosition = obj.transform.localPosition;
                clonedObj.transform.localRotation = obj.transform.localRotation;
                clonedObj.transform.localScale = obj.transform.localScale;
                clonedObj.SetActive(true);
                Utils.ChangeLayerRecursively(clonedObj, Data.CAPTURE_LAYER);
            }
        }
        return tempCombinedParent;
    }

    public static Texture2D GenerateIconInternal(GameObject objectToCapture, int finalSize, Data.CaptureDirection direction, bool useCustomAngle, Vector3 customAngle, int tempResolution, int zoom, Localization localization)
    {
        GameObject clonedObject = null;
        GameObject cameraObject = null;
        RenderTexture rt = null;
        Texture2D tempImage = null;

        try
        {
            clonedObject = Object.Instantiate(objectToCapture);
            clonedObject.name = objectToCapture.name + "_IconClone";
            Utils.SetSelfAndChildrenActive(clonedObject, true);
            Utils.ChangeLayerRecursively(clonedObject, Data.CAPTURE_LAYER);

            foreach (var renderer in clonedObject.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                renderer.updateWhenOffscreen = true;
            }

            var allRenderers = clonedObject.GetComponentsInChildren<Renderer>(true);
            if (allRenderers.Length == 0)
            {
                Debug.LogWarning(localization.GetLocalizedText("NoRenderersInClone"));
                return null;
            }

            Bounds combinedBounds = new Bounds();
            bool boundsInitialized = false;
            foreach (var renderer in allRenderers)
            {
                if (!renderer.enabled || renderer.gameObject.layer != Data.CAPTURE_LAYER) continue;
                Bounds currentBounds = renderer.bounds;
                if (!boundsInitialized)
                {
                    combinedBounds = currentBounds;
                    boundsInitialized = true;
                }
                else
                {
                    combinedBounds.Encapsulate(currentBounds);
                }
            }

            if (!boundsInitialized || combinedBounds.size == Vector3.zero)
            {
                Debug.LogWarning(localization.GetLocalizedText("InvalidBoundsForClone", clonedObject.name));
                if (!boundsInitialized) combinedBounds = new Bounds(clonedObject.transform.position, Vector3.one * 0.1f);
                else if (combinedBounds.size == Vector3.zero) combinedBounds.size = Vector3.one * 0.1f;
                boundsInitialized = false;
            }

            int currentZoomLevel = zoom;
            if (currentZoomLevel <= 0)
            {
                currentZoomLevel = Data.DEFAULT_ZOOM_LEVEL;
            }

            float orthoSizeMultiplier = Data.ORTHO_SIZE_MULTIPLIER_AT_DEFAULT_ZOOM * (Data.DEFAULT_ZOOM_LEVEL / (float)currentZoomLevel);

            cameraObject = SetupCaptureCamera(combinedBounds, direction, useCustomAngle, customAngle, tempResolution, orthoSizeMultiplier);
            var camera = cameraObject.GetComponent<Camera>();

            rt = new RenderTexture(tempResolution, tempResolution, 24, RenderTextureFormat.ARGB32);
            rt.antiAliasing = 4;
            camera.targetTexture = rt;

            RenderTexture.active = rt;
            camera.Render();

            tempImage = new Texture2D(tempResolution, tempResolution, TextureFormat.ARGB32, false);
            tempImage.ReadPixels(new Rect(0, 0, tempResolution, tempResolution), 0, 0);
            tempImage.Apply();

            camera.targetTexture = null;
            RenderTexture.active = null;

            Texture2D finalIcon = ProcessCapturedTexture(tempImage, finalSize, objectToCapture.name, localization);

            return finalIcon;
        }
        catch (System.Exception e)
        {
            Debug.LogError(localization.GetLocalizedText("InternalGenerationError", objectToCapture.name, e.Message, e.StackTrace));
            return null;
        }
        finally
        {
            if (rt != null) Object.DestroyImmediate(rt);
            if (cameraObject != null) Object.DestroyImmediate(cameraObject);
            if (clonedObject != null) Object.DestroyImmediate(clonedObject);
            if (tempImage != null) Object.DestroyImmediate(tempImage);
        }
    }

    private static GameObject SetupCaptureCamera(Bounds combinedBounds, Data.CaptureDirection direction, bool useCustomAngle, Vector3 customAngle, int tempResolution, float orthoSizeMultiplier)
    {
        var cameraObject = new GameObject("IconGen_CaptureCamera");
        var camera = cameraObject.AddComponent<Camera>();

        camera.backgroundColor = Color.clear;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.nearClipPlane = 0.001f;
        camera.farClipPlane = 10000f;
        camera.cullingMask = 1 << Data.CAPTURE_LAYER;
        camera.orthographic = true;

        float cameraDistance = combinedBounds.extents.magnitude * 2.0f;

        if (useCustomAngle)
        {
            cameraObject.transform.position = combinedBounds.center;
            cameraObject.transform.rotation = Quaternion.Euler(customAngle);
            cameraObject.transform.position -= cameraObject.transform.forward * cameraDistance;
        }
        else
        {
            Vector3 cameraOffset = Vector3.zero;
            switch (direction)
            {
                case Data.CaptureDirection.Front: cameraOffset = Vector3.forward; break;
                case Data.CaptureDirection.Rear: cameraOffset = Vector3.back; break;
                case Data.CaptureDirection.Left: cameraOffset = Vector3.right; break;
                case Data.CaptureDirection.Right: cameraOffset = Vector3.left; break;
            }
            cameraObject.transform.position = combinedBounds.center + cameraOffset.normalized * cameraDistance;
            cameraObject.transform.LookAt(combinedBounds.center);
        }

        float boundsHeight = combinedBounds.extents.y;
        float boundsWidth = combinedBounds.extents.x;
        float cameraAspect = (float)tempResolution / tempResolution;

        float requiredOrthoSize = Mathf.Max(boundsHeight, boundsWidth / cameraAspect);
        float finalOrthoSize = requiredOrthoSize * orthoSizeMultiplier;
        finalOrthoSize *= (1.0f + 0.01f);

        camera.orthographicSize = finalOrthoSize;

        foreach (var renderer in cameraObject.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            renderer.updateWhenOffscreen = true;
        }

        return cameraObject;
    }

    private static Texture2D ProcessCapturedTexture(Texture2D sourceTexture, int finalSize, string objectName, Localization localization)
    {
        bool hasContent = false;
        Color32[] pixels32 = sourceTexture.GetPixels32();
        for (int i = 0; i < pixels32.Length; i++)
        {
            if (pixels32[i].a > 0)
            {
                hasContent = true;
                break;
            }
        }

        Texture2D finalIcon;
        if (!hasContent)
        {
            Debug.LogWarning(localization.GetLocalizedText("FullyTransparent", objectName, finalSize));
            finalIcon = new Texture2D(finalSize, finalSize, TextureFormat.ARGB32, false);
            Utils.MakeTexture2DClear(finalIcon, finalSize, finalSize);
        }
        else
        {
            finalIcon = Utils.ResizeTexture(sourceTexture, finalSize, finalSize);
        }

        return finalIcon;
    }
}
