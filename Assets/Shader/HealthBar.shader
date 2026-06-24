// Полоска здоровья для мобов: один квад, рисуется без Canvas.
// _Fill и _FillColor меняются по инстансу (через MaterialPropertyBlock),
// поэтому все бары мобов сходятся в инстансированные draw call'ы при
// включённом на материале "Enable GPU Instancing". _BackColor — общий фон.
Shader "Crowds/HealthBar"
{
    Properties
    {
        [PerRendererData] _Fill ("Fill", Range(0,1)) = 1
        _FillColor ("Fill Color", Color) = (0,1,0,1)
        _BackColor ("Background Color", Color) = (0,0,0,0.65)
    }

    SubShader
    {
        Tags
        {
            "RenderType"     = "Transparent"
            "Queue"          = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        ZTest LEqual
        Cull Off

        Pass
        {
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv          : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            // Общий для всех баров фон (не инстансируется).
            half4 _BackColor;

            // Меняется по инстансу через MaterialPropertyBlock.
            UNITY_INSTANCING_BUFFER_START(Props)
                UNITY_DEFINE_INSTANCED_PROP(float, _Fill)
                UNITY_DEFINE_INSTANCED_PROP(half4, _FillColor)
            UNITY_INSTANCING_BUFFER_END(Props)

            Varyings vert (Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = IN.uv;
                return OUT;
            }

            half4 frag (Varyings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);
                float fill      = UNITY_ACCESS_INSTANCED_PROP(Props, _Fill);
                half4 fillColor = UNITY_ACCESS_INSTANCED_PROP(Props, _FillColor);
                return IN.uv.x <= fill ? fillColor : _BackColor;
            }
            ENDHLSL
        }
    }
}
