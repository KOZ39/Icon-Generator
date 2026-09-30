using System;
using UnityEditor;
using UnityEngine;

namespace KOZ39.IconGenerator
{
    internal static class IconImageEffects
    {
        internal static Material CreateMaterial()
        {
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(
                $"{IconGeneratorPackageInfo.AssetPath}/Editor/Capture/IconImageEffects.shader"
            );

            if (shader == null || !shader.isSupported)
            {
                throw new InvalidOperationException(
                    "The Icon Generator image effects shader is missing or unsupported."
                );
            }

            return new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
        }

        internal static void Apply(
            RenderTexture source,
            RenderTexture destination,
            Material material,
            IconGeneratorSettings settings
        )
        {
            var active = RenderTexture.active;
            var srgb = GL.sRGBWrite;
            RenderTexture reduced = null;
            RenderTexture mask = null;

            try
            {
                var linear = QualitySettings.activeColorSpace == ColorSpace.Linear;
                GL.sRGBWrite = linear;
                material.SetFloat(
                    "_Unpremultiply",
                    settings.transparentBackground || settings.outline ? 1 : 0
                );
                material.SetFloat("_Outline", settings.outline ? 1 : 0);

                if (settings.outline)
                {
                    var input = source;

                    while (input.width > settings.resolution)
                    {
                        var next = RenderTexture.GetTemporary(
                            input.width / 2,
                            input.height / 2,
                            0,
                            RenderTextureFormat.ARGB32
                        );

                        next.filterMode = FilterMode.Bilinear;
                        next.wrapMode = TextureWrapMode.Clamp;

                        try
                        {
                            Graphics.Blit(input, next, material, 2);
                        }
                        catch
                        {
                            RenderTexture.ReleaseTemporary(next);
                            throw;
                        }

                        if (reduced != null)
                        {
                            RenderTexture.ReleaseTemporary(reduced);
                        }

                        reduced = next;
                        input = reduced;
                    }

                    mask = RenderTexture.GetTemporary(
                        input.width,
                        input.height,
                        0,
                        RenderTextureFormat.ARGB32
                    );
                    mask.filterMode = FilterMode.Bilinear;
                    mask.wrapMode = TextureWrapMode.Clamp;
                    material.SetFloat(
                        "_OutlineRadius",
                        (float)settings.outlineWidth * input.width / settings.resolution
                    );
                    Graphics.Blit(input, mask, material, 1);
                    material.SetTexture("_OutlineMask", mask);
                    material.SetColor(
                        "_OutlineColor",
                        linear ? settings.outlineColor.linear : settings.outlineColor
                    );
                    material.SetColor(
                        "_BackgroundColor",
                        linear ? settings.backgroundColor.linear : settings.backgroundColor
                    );
                    material.SetFloat("_OpaqueBackground", settings.transparentBackground ? 0 : 1);
                }

                Graphics.Blit(source, destination, material, 0);
            }
            finally
            {
                material.SetTexture("_OutlineMask", null);
                RenderTexture.active = active;
                GL.sRGBWrite = srgb;

                if (mask != null)
                {
                    RenderTexture.ReleaseTemporary(mask);
                }

                if (reduced != null)
                {
                    RenderTexture.ReleaseTemporary(reduced);
                }
            }
        }
    }
}
