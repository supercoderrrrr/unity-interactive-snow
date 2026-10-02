Shader "Custom/Snow Interactive"
{
    Properties
    {
        [Header(Main)]
        _Noise("Snow Noise", 2D) = "gray" {}
        _NoiseScale("Noise Scale", Range(0, 2)) = 0.1
        _NoiseWeight("Noise Weight", Range(0, 2)) = 0.1
        [HDR] _ShadowColor("Shadow Color", Color) = (0.5, 0.5, 0.5, 1)

        [Space]
        [Header(Tessellation)]
        _MaxTessDistance("Max Tessellation Distance", Range(10, 100)) = 50
        _Tess("Tessellation", Range(1, 32)) = 20

        [Space]
        [Header(Snow)]
        [HDR] _Color("Snow Color", Color) = (0.5, 0.5, 0.5, 1)
        [HDR] _PathColorIn("Snow Path Color In", Color) = (0.5, 0.5, 0.7, 1)
        [HDR] _PathColorOut("Snow Path Color Out", Color) = (0.5, 0.5, 0.7, 1)
        _PathBlending("Snow Path Blending", Range(0, 3)) = 0.3
        _MainTex("Snow Texture", 2D) = "white" {}
        _SnowHeight("Snow Height", Range(0, 2)) = 0.3
        _SnowDepth("Snow Path Depth", Range(0, 100)) = 0.3
        _SnowTextureOpacity("Snow Texture Opacity", Range(0, 2)) = 0.3
        _SnowTextureScale("Snow Texture Scale", Range(0, 2)) = 0.3

        [Space]
        [Header(Sparkles)]
        _SparkleScale("Sparkle Scale", Range(0, 10)) = 10
        _SparkCutoff("Sparkle Cutoff", Range(0, 10)) = 0.8
        _SparkleNoise("Sparkle Noise", 2D) = "gray" {}

        [Space]
        [Header(Rim)]
        _RimPower("Rim Power", Range(0, 20)) = 20
        [HDR] _RimColor("Rim Color Snow", Color) = (0.5, 0.5, 0.5, 1)
    }

    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
    #include "SnowTessellation.hlsl"

    #pragma require tessellation tessHW
    #pragma vertex TessellationVertexProgram
    #pragma hull hull
    #pragma domain domain

    #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
    #pragma multi_compile _ _ADDITIONAL_LIGHT_SHADOWS
    #pragma multi_compile _ _SHADOWS_SOFT
    #pragma multi_compile_fog

    ControlPoint TessellationVertexProgram(Attributes input)
    {
        ControlPoint output;
        output.vertex = input.vertex;
        output.uv = input.uv;
        output.normal = input.normal;
        return output;
    }
    ENDHLSL

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma fragment frag
            #pragma target 4.0

            sampler2D _MainTex;
            sampler2D _SparkleNoise;
            float4 _Color;
            float4 _RimColor;
            float _RimPower;
            float4 _PathColorIn;
            float4 _PathColorOut;
            float _PathBlending;
            float _SparkleScale;
            float _SparkCutoff;
            float _SnowTextureOpacity;
            float _SnowTextureScale;
            float4 _ShadowColor;

            half4 frag(Varyings input) : SV_Target
            {
                float2 effectUv = input.worldPos.xz - _Position.xz;
                effectUv /= (_OrthographicCamSize * 2);
                effectUv += 0.5;

                float4 effect = tex2D(_GlobalEffectRT, effectUv);
                effect *= smoothstep(0.99, 0.9, effectUv.x) * smoothstep(0.99, 0.9, 1 - effectUv.x);
                effect *= smoothstep(0.99, 0.9, effectUv.y) * smoothstep(0.99, 0.9, 1 - effectUv.y);

                float3 topdownNoise = tex2D(_Noise, input.worldPos.xz * _NoiseScale).rgb;
                float3 snowTexture = tex2D(_MainTex, input.worldPos.xz * _SnowTextureScale).rgb;
                float3 snowTex = lerp(_Color.rgb, snowTexture * _Color.rgb, _SnowTextureOpacity);
                float3 path = lerp(
                    _PathColorOut.rgb * effect.g,
                    _PathColorIn.rgb,
                    saturate(effect.g * _PathBlending));
                float3 mainColors = lerp(snowTex, path, saturate(effect.g));

                float shadow = 0;
                half4 shadowCoord = TransformWorldToShadowCoord(input.worldPos);

                #if _MAIN_LIGHT_SHADOWS_CASCADE || _MAIN_LIGHT_SHADOWS
                    Light mainLight = GetMainLight(shadowCoord);
                    shadow = mainLight.shadowAttenuation;
                #else
                    Light mainLight = GetMainLight();
                #endif

                float3 extraLights = 0;
                int pixelLightCount = GetAdditionalLightsCount();
                for (int lightIndex = 0; lightIndex < pixelLightCount; ++lightIndex)
                {
                    Light light = GetAdditionalLight(lightIndex, input.worldPos, half4(1, 1, 1, 1));
                    extraLights += light.color * (light.distanceAttenuation * light.shadowAttenuation);
                }

                float4 litMainColors = float4(mainColors, 1);
                extraLights *= litMainColors.rgb;

                float sparklesStatic = tex2D(_SparkleNoise, input.worldPos.xz * _SparkleScale).r;
                float cutoffSparkles = step(_SparkCutoff, sparklesStatic);
                litMainColors += cutoffSparkles * saturate(1 - effect.g * 2) * 4;

                half rim = 1.0 - dot(input.viewDir, input.normal) * topdownNoise.r;
                litMainColors += _RimColor * pow(abs(rim), _RimPower);

                half4 extraColors;
                extraColors.rgb = litMainColors.rgb * mainLight.color.rgb * (shadow + unity_AmbientSky.rgb);
                extraColors.a = 1;

                float3 coloredShadows = shadow + _ShadowColor.rgb * (1 - shadow);
                litMainColors.rgb *= mainLight.color * coloredShadows;

                float4 finalColor = litMainColors + extraColors + float4(extraLights, 0);
                finalColor.rgb = MixFog(finalColor.rgb, input.fogFactor);
                return finalColor;
            }
            ENDHLSL
        }
    }
}
