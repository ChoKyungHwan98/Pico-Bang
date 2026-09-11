Shader "Shader Graphs/FS_Speed"
{
    // 달릴 때 화면 가장자리에서 흐르는 속도선.
    // URP Full Screen Pass Renderer Feature 전용.
    //
    // 원본 Shader Graph는 소실됐지만 머티리얼에 프로퍼티와 텍스처 참조가 남아 있었다.
    // 그 이름을 그대로 선언해 CNoise 텍스처 연결과 기존 튜닝값(_Tiling 3x9,
    // _Mask_Size 0.56, _Smoothness 4 …)을 되살린다.
    //
    // 강도는 PlayerController가 _FullscreenIntensity로 조절한다.
    Properties
    {
        _FullscreenIntensity ("Intensity", Range(0,1)) = 0

        // Shader Graph가 만들어 두었던 이름 — 바꾸면 텍스처 연결이 끊긴다
        [NoScaleOffset] _Texture2DAsset_292ccc56fdb549c5a247278663838d8c_Out_0_Texture2D ("Noise (원본 CNoise)", 2D) = "black" {}

        [HDR]_Color   ("Line Color", Color) = (2,2,2,1)
        _Tiling       ("Tiling (각도 / 반경)", Vector) = (3,9,0,0)
        _Speed        ("Flow Speed", Range(0,8)) = 1
        _Smoothness   ("Contrast", Range(1,12)) = 4
        _Mask_Size    ("Clear Center", Range(0,1)) = 0.56
        _Mask_Contrast("Mask Contrast", Range(0.2,6)) = 1
        _Strength     ("Strength", Range(0,4)) = 1
        _BlurStrength ("Radial Blur", Range(0,0.06)) = 0.012
        _Desaturate   ("Edge Desaturate", Range(0,1)) = 0.25
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        ZTest Always
        ZWrite Off
        Cull Off

        Pass
        {
            Name "SpeedLines"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.0

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            TEXTURE2D(_Texture2DAsset_292ccc56fdb549c5a247278663838d8c_Out_0_Texture2D);
            SAMPLER(sampler_Texture2DAsset_292ccc56fdb549c5a247278663838d8c_Out_0_Texture2D);
            #define _NoiseTex        _Texture2DAsset_292ccc56fdb549c5a247278663838d8c_Out_0_Texture2D
            #define sampler_NoiseTex sampler_Texture2DAsset_292ccc56fdb549c5a247278663838d8c_Out_0_Texture2D

            float  _FullscreenIntensity;
            float4 _Color;
            float4 _Tiling;
            float  _Speed;
            float  _Smoothness;
            float  _Mask_Size;
            float  _Mask_Contrast;
            float  _Strength;
            float  _BlurStrength;
            float  _Desaturate;

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;

                float intensity = saturate(_FullscreenIntensity);

                float3 src = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv).rgb;

                // 강도가 0이면 아무것도 하지 않는다 — 평상시 비용 0
                if (intensity <= 0.001)
                {
                    return half4(src, 1.0);
                }

                // 중심 기준 좌표. 화면비를 보정하지 않으면 선이 타원이 된다.
                float2 c = uv - 0.5;
                float aspect = _ScreenParams.x / max(_ScreenParams.y, 1.0);
                c.x *= aspect;

                float r   = length(c) * 2.0;
                float ang = atan2(c.y, c.x) * 0.15915494 + 0.5;   // 1/(2pi)

                // ── 중심을 향한 방사형 블러 ──────────────────────
                // 속도감의 절반은 선이 아니라 이 흐림에서 나온다.
                if (_BlurStrength > 0.0001)
                {
                    float2 toCenter = 0.5 - uv;
                    float amount = _BlurStrength * intensity * saturate(r);
                    float3 acc = src;
                    [unroll]
                    for (int i = 1; i <= 4; i++)
                    {
                        float t = (float)i / 4.0;
                        acc += SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + toCenter * (amount * t)).rgb;
                    }
                    src = acc / 5.0;
                }

                // ── 극좌표로 노이즈를 흘린다 ────────────────────
                // 두 번 겹쳐 뽑아야 규칙적인 줄무늬가 아니라 흐르는 결이 된다.
                float scroll = _Time.y * _Speed;

                float2 uv1 = float2(ang * _Tiling.x,        r * _Tiling.y        - scroll);
                float2 uv2 = float2(ang * _Tiling.x * 1.43, r * _Tiling.y * 0.67 - scroll * 1.7);

                float n1 = SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, uv1).r;
                float n2 = SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, uv2).r;

                float n = saturate(n1 * n2 * 2.0);
                n = pow(n, _Smoothness);        // 대비를 올려 가는 선으로

                // ── 중앙은 비운다 ────────────────────────────────
                // 조준선이 가려지면 게임이 안 된다.
                float mask = saturate((r - _Mask_Size) / max(0.001, 1.4 - _Mask_Size));
                mask = pow(mask, _Mask_Contrast);

                float lines = n * mask * _Strength * intensity;

                float3 col = src;

                // 가장자리 채도를 조금 빼서 시선이 중앙에 모이게
                float lum = dot(col, float3(0.299, 0.587, 0.114));
                col = lerp(col, lum.xxx, _Desaturate * mask * intensity);

                col += _Color.rgb * lines;

                return half4(col, 1.0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
