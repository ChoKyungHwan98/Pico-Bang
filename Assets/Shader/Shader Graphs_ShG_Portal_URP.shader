Shader "Shader Graphs/ShG_Portal_URP" {
	Properties {
		Vector1_b79165269b4c4aa3aea26eee8750a325 ("Color_Lighten", Range(0.9, 10)) = 4
		Vector1_eb86ebf3ceb446ba893fde4145c46867 ("Alpha", Range(0, 1)) = 0.5
		[NoScaleOffset] Texture2D_c538574cce4541c9a9476887e9a64a54 ("Noise_Text", 2D) = "white" {}
		_ColorMain ("ColorMain_ScriptVar", Vector) = (1,1,1,1)
		_PortalFade ("PortalFade_ScriptVar", Range(0, 1)) = 1
		[NoScaleOffset] Texture2D_05aedaf145a741eb9bd841740779345d ("EdgeMask", 2D) = "black" {}
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