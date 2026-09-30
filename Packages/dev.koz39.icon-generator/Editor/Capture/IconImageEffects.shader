Shader "Hidden/KOZ39/IconGenerator/Preview"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Unpremultiply ("Straight alpha", Float) = 1
        _OutlineMask ("Outline mask", 2D) = "black" {}
    }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            sampler2D _OutlineMask;
            float _Unpremultiply;
            float _Outline, _OpaqueBackground;
            float4 _OutlineColor, _BackgroundColor;

            float4 frag(v2f_img input) : SV_Target
            {
                float4 color = tex2D(_MainTex, input.uv);
                if (_Outline > 0.5)
                {
                    float alpha = tex2D(_OutlineMask, input.uv).a * _OutlineColor.a * (1.0 - color.a);
                    color.rgb += _OutlineColor.rgb * alpha;
                    color.a += alpha;
                    if (_OpaqueBackground > 0.5)
                    {
                        color.rgb += _BackgroundColor.rgb * (1.0 - color.a);
                        color.a = 1.0;
                    }
                }
                if (_Unpremultiply > 0.5)
                    color.rgb = color.a > 0.0 ? saturate(color.rgb / color.a) : 0.0;
                return color;
            }
            ENDCG
        }
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            float4 _MainTex_TexelSize;
            float _OutlineRadius;

            float4 frag(v2f_img input) : SV_Target
            {
                float alpha = 0.0;
                int radius = (int)ceil(_OutlineRadius);
                for (int y = -radius; y <= radius; y++)
                for (int x = -radius; x <= radius; x++)
                {
                    float coverage = saturate(_OutlineRadius + 1.0 - length(float2(x, y)));
                    if (coverage <= 0.0)
                        continue;
                    float2 uv = input.uv + float2(x, y) * abs(_MainTex_TexelSize.xy);
                    if (all(uv >= 0.0) && all(uv <= 1.0))
                        alpha = max(alpha, tex2Dlod(_MainTex, float4(uv, 0, 0)).a * coverage);
                }
                return float4(0, 0, 0, alpha);
            }
            ENDCG
        }
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            float4 _MainTex_TexelSize;

            float4 frag(v2f_img input) : SV_Target
            {
                float2 offset = abs(_MainTex_TexelSize.xy) * 0.5;
                float alpha = tex2D(_MainTex, input.uv + float2(-offset.x, -offset.y)).a;
                alpha += tex2D(_MainTex, input.uv + float2(offset.x, -offset.y)).a;
                alpha += tex2D(_MainTex, input.uv + float2(-offset.x, offset.y)).a;
                alpha += tex2D(_MainTex, input.uv + float2(offset.x, offset.y)).a;
                return float4(0, 0, 0, alpha * 0.25);
            }
            ENDCG
        }
    }
}
