// 感知范围体积（开发者模式可视化）。
// 关键点：Stencil 让“同一像素只画最先到达的那个球”，重叠区域不会越叠越深。
// 填充保持很淡，靠 Fresnel 边缘提亮勾出轮廓，因此即使几十个球重叠也能看清内容。
Shader "Vindicator/PerceptionVolume"
{
    Properties
    {
        _BaseColor ("填充颜色", Color) = (0.2, 0.6, 1, 0.10)
        _RimColor ("边缘颜色", Color) = (0.2, 0.6, 1, 0.55)
        _RimPower ("边缘收窄", Range(0.5, 8)) = 2.5
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
            "PreviewType" = "Sphere"
        }

        Pass
        {
            Name "PerceptionVolume"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            // 模板去重：第一个画到该像素的球写入 ref，之后重叠的球 Comp NotEqual 直接丢弃。
            Stencil
            {
                Ref 1
                Comp NotEqual
                Pass Replace
                ReadMask 255
                WriteMask 1
            }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
                float3 viewWS : TEXCOORD1;
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float4 _RimColor;
                float _RimPower;
            CBUFFER_END

            Varyings vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs positionInputs = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = positionInputs.positionCS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.viewWS = GetWorldSpaceViewDir(positionInputs.positionWS);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float3 normal = normalize(input.normalWS);
                float3 view = normalize(input.viewWS);

                // 正对球心 → facing 接近 1，填充为主；贴着轮廓 → facing 接近 0，边缘提亮。
                float facing = saturate(abs(dot(normal, view)));
                float rim = pow(1.0 - facing, _RimPower);

                half3 color = lerp(_BaseColor.rgb, _RimColor.rgb, rim * 0.7);
                half alpha = saturate(_BaseColor.a + _RimColor.a * rim);
                return half4(color, alpha);
            }
            ENDHLSL
        }
    }

    Fallback "Universal Render Pipeline/Unlit"
}
