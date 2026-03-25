Shader "Custom/PointCloud"
{
    Properties
    {
        _PointColor("Point Color", Color) = (0, 1, 0, 1)
    }
    SubShader
    {
        // Render on top of passthrough
        Tags { "RenderType"="Overlay" "Queue"="Overlay" "RenderPipeline"="UniversalPipeline" }
        LOD 100

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }

            ZTest Always
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION; // World positions baked by CPU
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_Position;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            float4 _PointColor;

            Varyings vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                // The PointCloudRenderer GameObject has identity transform,
                // so Object Space == World Space. Just go straight to clip space.
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                return _PointColor;
            }
            ENDHLSL
        }
    }
}
