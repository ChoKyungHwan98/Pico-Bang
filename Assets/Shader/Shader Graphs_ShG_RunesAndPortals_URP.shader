Shader "Shader Graphs/ShG_RunesAndPortals_URP" {
	Properties {
		[NoScaleOffset] Texture2D_c3b67da3560e484c80e736c03ab9819b ("Color", 2D) = "white" {}
		[NoScaleOffset] Texture2D_3f171bf3be98437795a2e8bb0c2fcb3d ("MetallicSmoothness", 2D) = "white" {}
		[NoScaleOffset] Texture2D_e6e81150d3a44ab68394676073fdda8b ("Normal", 2D) = "white" {}
		[NoScaleOffset] Texture2D_b568b8477d314dc1ae778d04b4dbbe89 ("Emission", 2D) = "white" {}
		_EmissionColor ("EmissionColor", Vector) = (1,1,1,1)
		_EmissionStrength ("EmissionStrength", Range(0, 1)) = 0
		[HideInInspector] _QueueOffset ("_QueueOffset", Float) = 0
		[HideInInspector] _QueueControl ("_QueueControl", Float) = -1
		[HideInInspector] [NoScaleOffset] unity_Lightmaps ("unity_Lightmaps", 2DArray) = "" {}
		[HideInInspector] [NoScaleOffset] unity_LightmapsInd ("unity_LightmapsInd", 2DArray) = "" {}
		[HideInInspector] [NoScaleOffset] unity_ShadowMasks ("unity_ShadowMasks", 2DArray) = "" {}
	}
	//DummyShaderTextExporter
	SubShader{
		Tags { "RenderType" = "Opaque" }
		LOD 200

		Pass
		{
			HLSLPROGRAM
			#pragma vertex vert
			#pragma fragment frag

			float4x4 unity_ObjectToWorld;
			float4x4 unity_MatrixVP;

			struct Vertex_Stage_Input
			{
				float4 pos : POSITION;
			};

			struct Vertex_Stage_Output
			{
				float4 pos : SV_POSITION;
			};

			Vertex_Stage_Output vert(Vertex_Stage_Input input)
			{
				Vertex_Stage_Output output;
				output.pos = mul(unity_MatrixVP, mul(unity_ObjectToWorld, input.pos));
				return output;
			}

			float4 frag(Vertex_Stage_Output input) : SV_TARGET
			{
				return float4(1.0, 1.0, 1.0, 1.0); // RGBA
			}

			ENDHLSL
		}
	}
	Fallback "Hidden/Shader Graph/FallbackError"
	//CustomEditor "UnityEditor.ShaderGraph.GenericShaderGraphMaterialGUI"
}