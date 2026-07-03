// GPU-instanced Vertex Animation Texture shader (URP).
// The mesh is static; the vertex stage reads the animated pose from _PositionMap:
//   texel = frame*vertexCount + vertexId (uv2.x), where frame is the per-instance _Frame slot.
// Per-instance data (frame + tint) is supplied via MaterialPropertyBlock float/vector arrays,
// so a whole crowd draws in one Graphics.DrawMeshInstanced call.
Shader "Crowds/CrowdVat"
{
    Properties
    {
        _BaseMap ("Base Map", 2D) = "white" {}
        _BaseColor ("Base Color", Color) = (1,1,1,1)
        _PositionMap ("VAT Position Map", 2D) = "black" {}
        _NormalMap ("VAT Normal Map", 2D) = "bump" {}
        [Toggle(_NORMALMAP_ON)] _UseNormalMap ("Use Baked Normals", Float) = 1
        // VAT layout (set by the baker). Declared here so they serialize on the material.
        _VatWidth ("VAT Width", Float) = 1
        _VatHeight ("VAT Height", Float) = 1
        _VatVertexCount ("VAT Vertex Count", Float) = 1
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        // Inline point-clamp sampler: VAT data must never be filtered/wrapped.
        SamplerState sampler_point_clamp;

        TEXTURE2D(_PositionMap);
        TEXTURE2D(_NormalMap);
        TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);

        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            float4 _BaseColor;
            // VAT layout: data is a flat (frame*vertexCount + vertexId) array wrapped into a WxH texture.
            float _VatWidth;
            float _VatHeight;
            float _VatVertexCount;
        CBUFFER_END

        // Per-instance: current frame slot and tint. Filled from MaterialPropertyBlock arrays.
        UNITY_INSTANCING_BUFFER_START(Props)
            UNITY_DEFINE_INSTANCED_PROP(float, _Frame)
            UNITY_DEFINE_INSTANCED_PROP(float4, _InstColor)
        UNITY_INSTANCING_BUFFER_END(Props)

        // Reads the animated object-space position/normal for this vertex+instance.
        // uv2.x carries the raw vertex index; frame is the per-instance frame slot.
        void SampleVat(float2 uv2, float frame, out float3 positionOS, out float3 normalOS)
        {
            float linearIndex = frame * _VatVertexCount + uv2.x;
            float x = fmod(linearIndex, _VatWidth);
            float y = floor(linearIndex / _VatWidth);
            float2 vatUV = float2((x + 0.5) / _VatWidth, (y + 0.5) / _VatHeight);
            positionOS = SAMPLE_TEXTURE2D_LOD(_PositionMap, sampler_point_clamp, vatUV, 0).xyz;
            normalOS   = SAMPLE_TEXTURE2D_LOD(_NormalMap,  sampler_point_clamp, vatUV, 0).xyz;
        }
        ENDHLSL

        // ------------------------------------------------------------------ Forward lit
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma shader_feature_local _NORMALMAP_ON
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _SHADOWS_SOFT
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;   // ignored (overwritten by VAT), kept for layout
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
                float2 uv2        : TEXCOORD1;   // uv2.x = vertex column
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv          : TEXCOORD0;
                float3 normalWS    : TEXCOORD1;
                float3 positionWS  : TEXCOORD2;
                float4 color       : TEXCOORD3;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);

                float frame = UNITY_ACCESS_INSTANCED_PROP(Props, _Frame);

                float3 posOS, nrmOS;
                SampleVat(IN.uv2, frame, posOS, nrmOS);
            #ifndef _NORMALMAP_ON
                nrmOS = IN.normalOS;
            #endif

                OUT.positionWS = TransformObjectToWorld(posOS);
                OUT.positionHCS = TransformWorldToHClip(OUT.positionWS);
                OUT.normalWS = normalize(TransformObjectToWorldNormal(nrmOS));
                OUT.uv = TRANSFORM_TEX(IN.uv, _BaseMap);
                OUT.color = UNITY_ACCESS_INSTANCED_PROP(Props, _InstColor);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);

                half4 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv) * _BaseColor * IN.color;

                float3 normalWS = normalize(IN.normalWS);
                float4 shadowCoord = TransformWorldToShadowCoord(IN.positionWS);
                Light mainLight = GetMainLight(shadowCoord);

                float ndotl = saturate(dot(normalWS, mainLight.direction));
                float3 diffuse = mainLight.color * (ndotl * mainLight.shadowAttenuation);
                float3 ambient = SampleSH(normalWS);

                float3 color = albedo.rgb * (diffuse + ambient);
                return half4(color, albedo.a);
            }
            ENDHLSL
        }

        // ------------------------------------------------------------------ Shadow caster
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            ZWrite On
            ZTest LEqual
            ColorMask 0

            HLSLPROGRAM
            #pragma vertex shadowVert
            #pragma fragment shadowFrag
            #pragma multi_compile_instancing
            #pragma shader_feature_local _NORMALMAP_ON

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv2        : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings shadowVert(Attributes IN)
            {
                Varyings OUT = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(IN);

                float frame = UNITY_ACCESS_INSTANCED_PROP(Props, _Frame);
                float3 posOS, nrmOS;
                SampleVat(IN.uv2, frame, posOS, nrmOS);
            #ifndef _NORMALMAP_ON
                nrmOS = IN.normalOS;
            #endif

                float3 positionWS = TransformObjectToWorld(posOS);
                float3 normalWS = normalize(TransformObjectToWorldNormal(nrmOS));
                float4 positionHCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, _LightDirection));
            #if UNITY_REVERSED_Z
                positionHCS.z = min(positionHCS.z, UNITY_NEAR_CLIP_VALUE);
            #else
                positionHCS.z = max(positionHCS.z, UNITY_NEAR_CLIP_VALUE);
            #endif
                OUT.positionHCS = positionHCS;
                return OUT;
            }

            half4 shadowFrag(Varyings IN) : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }
    }

    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
