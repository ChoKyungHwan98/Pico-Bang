Shader "Shader Graphs/Ice Ball" {
	Properties {
		[NoScaleOffset] _IceRefraction_Texture ("IceRefraction Texture", 2D) = "grey" {}
		_Ice_tile ("Ice tile", Vector) = (1,1,0,0)
		_Refreact_strength ("Refreact strength", Range(0, 1)) = -1
		[HDR] _Color ("Color", Vector) = (0.03345032,1.058255,2.363827,0)
		_Frost_strength ("Frost strength", Range(0.001, 1)) = 0.2
		_Frost_tile ("Frost tile", Vector) = (1,1,0,0)
		_Frost_middle_stregth ("Frost middle stregth", Float) = 1
		_inner_frost_position ("inner frost position", Vector) = (0,0,0,0)
		_Inner_Fog_Power ("Inner Fog Power", Float) = 2
		_Inner_Fog_Noise_Size ("Inner Fog Noise Size", Float) = 10
		[NoScaleOffset] _SampleTexture2D_418dd7684adf4f2182c221d7809597e7_Texture_1_Texture2D ("Texture2D", 2D) = "white" {}
		[NoScaleOffset] [Normal] _SampleTexture2D_fa2d06cb22734338a65c6a92900d3bdd_Texture_1_Texture2D ("Texture2D", 2D) = "bump" {}
		[HideInInspector] _QueueOffset ("_QueueOffset", Float) = 0
		[HideInInspector] _QueueControl ("_QueueControl", Float) = -1
		[HideInInspector] [NoScaleOffset] unity_Lightmaps ("unity_Lightmaps", 2DArray) = "" {}
		[HideInInspector] [NoScaleOffset] unity_LightmapsInd ("unity_LightmapsInd", 2DArray) = "" {}
		[HideInInspector] [NoScaleOffset] unity_ShadowMasks ("unity_ShadowMasks", 2DArray) = "" {}
	}
	//DummyShaderTextExporter
	SubShader{
		Tags { "RenderType"="Opaque" }
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

			float4 _Color;

			float4 frag(Vertex_Stage_Output input) : SV_TARGET
			{
				return _Color; // RGBA
			}

			ENDHLSL
		}
	}
	Fallback "Hidden/Shader Graph/FallbackError"
	//CustomEditor "UnityEditor.ShaderGraph.GenericShaderGraphMaterialGUI"
}