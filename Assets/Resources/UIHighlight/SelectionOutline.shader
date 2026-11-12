// 选中描边专用 shader：只画外扩的背面（inverted hull），不参与光照。
//
// 用法：选中时把本材质作为**追加材质槽**挂到单位的 MeshRenderer 上
// （sharedMaterials = 原材质数组 + 本材质），取消选中时把材质数组还原。
// 单位本体始终使用原材质（URP Lit），因此选中/取消不会改变亮度或外观。
//
// 放在 Resources 下是为了让 Shader.Find 在 Player 构建中也能找到它。
Shader "Vindicator/SelectionOutline"
{
    Properties
    {
        _OutlineColor ("Outline Color", Color) = (0.25, 0.85, 1, 1)
        _OutlineWidth ("Outline Width", Range(0, 0.5)) = 0.035
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Geometry"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "Outline"
            Tags { "LightMode" = "UniversalForward" }

            // 只画背面：外扩后的背面会在物体轮廓外露出一圈。
            Cull Front
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma vertex OutlineVertex
            #pragma fragment OutlineFragment
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _OutlineColor;
                float _OutlineWidth;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings OutlineVertex(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);

                // 沿世界法线外扩，保证正交俯视下描边粗细均匀。
                positionWS += normalWS * _OutlineWidth;

                output.positionCS = TransformWorldToHClip(positionWS);
                return output;
            }

            half4 OutlineFragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                return _OutlineColor;
            }
            ENDHLSL
        }
    }

    FallBack Off
}
