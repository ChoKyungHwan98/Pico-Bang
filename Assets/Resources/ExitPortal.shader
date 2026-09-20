Shader "PicoBang/ExitPortal"
{
    Properties { _Unlocked("Unlocked", Float) = 0 }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" }
        Pass
        {
            Blend SrcAlpha One
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
            float _Unlocked;
            CBUFFER_END
            struct Attributes { float4 positionOS:POSITION; float2 uv:TEXCOORD0; };
            struct Varyings { float4 positionCS:SV_POSITION; float2 uv:TEXCOORD0; };
            Varyings vert(Attributes i) { Varyings o; o.positionCS=TransformObjectToHClip(i.positionOS.xyz); o.uv=i.uv; return o; }
            half4 frag(Varyings i):SV_Target
            {
                float2 p=i.uv*2-1;
                float r=length(p);
                clip(1-r);
                float a=atan2(p.y,p.x);
                float swirl=.5+.5*sin(a*5-r*18+_Time.y*2.5);
                float rings=pow(.5+.5*sin(r*38-_Time.y*3),8);
                float edge=pow(saturate(r),9);
                float strength=lerp(.04,.22+swirl*.12+rings*.3,_Unlocked);
                half3 color=lerp(half3(.1,.25,.3),half3(.08,.8,1.15),_Unlocked);
                return half4(color+edge*.35, strength*smoothstep(1,.92,r));
            }
            ENDHLSL
        }
    }
}
