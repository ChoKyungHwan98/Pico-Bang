// Anime Speed Lines — URP Full Screen Pass 이식판
//
// 원본: Mirza Beig, "Anime Speed Lines" (https://github.com/MirzaBeig/Anime-Speed-Lines), Unlicense — LICENSE.txt 참조
// 원본은 Built-in 파이프라인용(UnityCG, _MainTex)이고 URP 연결 스크립트는 Unity 6(URP 17)에서 제거된 API를 쓴다.
// 그래서 셰이더 계산(극좌표 노이즈 선 + 가장자리 마스크)만 그대로 옮기고,
// 입력을 URP 기본 FullScreenPassRendererFeature가 넘겨주는 _BlitTexture로 바꿨다.
//
// 추가한 것: _FullscreenIntensity (0~1). PlayerController가 달리기 속도에 따라 올리고 내린다.
Shader "Pico-Bang/Anime Speed Lines"
{
	Properties
	{
		_Colour("Colour", Color) = (1,1,1,1)
		_SpeedLinesTiling("Speed Lines Tiling", Float) = 200
		_SpeedLinesRadialScale("Speed Lines Radial Scale", Range(0, 10)) = 0.1
		_SpeedLinesPower("Speed Lines Power", Float) = 1
		_SpeedLinesRemap("Speed Lines Remap", Range(0, 1)) = 0.8
		_SpeedLinesAnimation("Speed Lines Animation", Float) = 3
		_MaskScale("Mask Scale", Range(0, 2)) = 1
		_MaskHardness("Mask Hardness", Range(0, 1)) = 0
		_MaskPower("Mask Power", Float) = 5
		_FullscreenIntensity("Intensity", Range(0, 1)) = 0
	}

	SubShader
	{
		Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Opaque" }
		ZTest Always
		ZWrite Off
		Cull Off

		Pass
		{
			Name "AnimeSpeedLines"

			HLSLPROGRAM
			#pragma vertex Vert
			#pragma fragment Frag

			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
			#include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

			CBUFFER_START(UnityPerMaterial)
				float4 _Colour;
				float _SpeedLinesTiling;
				float _SpeedLinesRadialScale;
				float _SpeedLinesPower;
				float _SpeedLinesRemap;
				float _SpeedLinesAnimation;
				float _MaskScale;
				float _MaskHardness;
				float _MaskPower;
				float _FullscreenIntensity;
			CBUFFER_END

			// 원본과 동일한 2D 심플렉스 노이즈
			float3 mod2D289(float3 x) { return x - floor(x * (1.0 / 289.0)) * 289.0; }
			float2 mod2D289(float2 x) { return x - floor(x * (1.0 / 289.0)) * 289.0; }
			float3 permute(float3 x) { return mod2D289(((x * 34.0) + 1.0) * x); }
			float snoise(float2 v)
			{
				const float4 C = float4(0.211324865405187, 0.366025403784439, -0.577350269189626, 0.024390243902439);
				float2 i = floor(v + dot(v, C.yy));
				float2 x0 = v - i + dot(i, C.xx);
				float2 i1 = (x0.x > x0.y) ? float2(1.0, 0.0) : float2(0.0, 1.0);
				float4 x12 = x0.xyxy + C.xxzz;
				x12.xy -= i1;
				i = mod2D289(i);
				float3 p = permute(permute(i.y + float3(0.0, i1.y, 1.0)) + i.x + float3(0.0, i1.x, 1.0));
				float3 m = max(0.5 - float3(dot(x0, x0), dot(x12.xy, x12.xy), dot(x12.zw, x12.zw)), 0.0);
				m = m * m;
				m = m * m;
				float3 x = 2.0 * frac(p * C.www) - 1.0;
				float3 h = abs(x) - 0.5;
				float3 ox = floor(x + 0.5);
				float3 a0 = x - ox;
				m *= 1.79284291400159 - 0.85373472095314 * (a0 * a0 + h * h);
				float3 g;
				g.x = a0.x * x0.x + h.x * x0.y;
				g.yz = a0.yz * x12.xz + h.yz * x12.yw;
				return 130.0 * dot(m, g);
			}

			half4 Frag(Varyings input) : SV_Target
			{
				UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
				float2 uv = input.texcoord;
				half4 scene = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);

				// 달리지 않을 때는 계산을 건너뛴다
				if (_FullscreenIntensity <= 0.0001)
				{
					return scene;
				}

				// 선: 화면 중심 기준 극좌표에 노이즈를 흘린다
				float2 centered = uv - float2(0.5, 0.5);
				float2 polar = float2(
					length(centered) * _SpeedLinesRadialScale * 2.0,
					atan2(centered.x, centered.y) * (1.0 / 6.28318548202515) * _SpeedLinesTiling);
				float n = snoise(polar + float2(-_SpeedLinesAnimation * _Time.y, 0.0)) * 0.5 + 0.5;
				float speedLines = saturate((pow(n, _SpeedLinesPower) - _SpeedLinesRemap) / (1.0 - _SpeedLinesRemap));

				// 마스크: 가운데는 비우고 가장자리로 갈수록 진하게
				float2 edge = uv * 2.0 - 1.0;
				float hardness = lerp(0.0, _MaskScale, _MaskHardness);
				float mask = pow(1.0 - saturate((length(edge) - _MaskScale) / ((hardness - 0.001) - _MaskScale)), _MaskPower);

				float masked = speedLines * mask;
				float amount = masked * _Colour.a * _FullscreenIntensity;
				return half4(lerp(scene.rgb, masked * _Colour.rgb, amount), scene.a);
			}
			ENDHLSL
		}
	}
}
