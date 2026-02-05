Shader "AnimationBaker/VAT_MotionVectors"
{
    Properties
    {
        [Header(Textures)]
        _MainTex ("Albedo", 2D) = "white" {}
        _PosTex ("Position Texture", 2D) = "black" {}
        _NmlTex ("Normal Texture", 2D) = "gray" {}
        _TanTex ("Tangent Texture", 2D) = "gray" {}
        _VelTex ("Velocity Texture", 2D) = "black" {}
        
        [Header(Animation)]
        // AnimDurationAndOffset is used for single clip bakes (legacy/simple)
        _AnimDurationAndOffset ("Duration (X) and Offset (Y)", Vector) = (1, 0, 0, 0)
        
        // Properties used by AnimationFramePlayer for combined bakes
        _Timer ("Timer", Float) = 0
        _Duration ("Duration", Float) = 1
        _Frames ("Frames", Float) = 1
        _Offset ("Offset", Float) = 0
        
        [KeywordEnum(Shader, Script)] _TimerMode ("Timer Mode", Float) = 0
        _Lerp ("Animation Lerp", Range(0, 1)) = 1
        
        [Header(Surface)]
        _Color ("Color", Color) = (1, 1, 1, 1)
        _Smoothness ("Smoothness", Range(0, 1)) = 0.5
        _Metallic ("Metallic", Range(0, 1)) = 0.0

        [Header(Faked Motion Blur)]
        _SmearStrength ("Smear Strength", Range(0, 2)) = 0.4
        _SmearMinVelocity ("Smear Min Velocity", Range(0, 1.0)) = 0.7

        // Auto-generated properties for SRP Batcher compatibility
        [HideInInspector] _MainTex_ST("MainTex ST", Vector) = (1,1,0,0)
        [HideInInspector] _MainTex_TexelSize("MainTex TexelSize", Vector) = (1,1,1,1)
        [HideInInspector] _PosTex_TexelSize("PosTex TexelSize", Vector) = (1,1,1,1)
    }
    
    SubShader
    {
        Tags 
        { 
            "RenderType" = "Opaque" 
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Geometry"
        }
        
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
        
        // SRP Batcher compatible CBUFFER - Must match Properties block order exactly
        CBUFFER_START(UnityPerMaterial)
            float4 _AnimDurationAndOffset;
            float _Timer;
            float _Duration;
            float _Frames;
            float _Offset;
            float _TimerMode;
            float _Lerp;
            float4 _Color;
            float _Smoothness;
            float _Metallic;
            float _SmearStrength;
            float _SmearMinVelocity;

            // Auto-generated properties (order matches Properties block)
            float4 _MainTex_ST;
            float4 _MainTex_TexelSize;
            float4 _PosTex_TexelSize;
        CBUFFER_END
        
        // Main texture uses default sampler
        TEXTURE2D(_MainTex);    SAMPLER(sampler_MainTex);
        
        // VAT textures - MUST use point/nearest filtering to avoid interpolation between vertices!
        TEXTURE2D(_PosTex);     SAMPLER(sampler_PosTex);
        TEXTURE2D(_NmlTex);
        TEXTURE2D(_TanTex);
        TEXTURE2D(_VelTex);
        
        // Create a point sampler for VAT textures (no filtering, clamp addressing)
        // SAMPLER(sampler_PosTex);  // Will be set by texture import settings
        
        struct Attributes
        {
            float4 positionOS : POSITION;
            float3 normalOS : NORMAL;
            float4 tangentOS : TANGENT;
            float2 uv : TEXCOORD0; 
            float2 uv2 : TEXCOORD1;
            uint vertexID : SV_VertexID;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };
        
        // Calculate UV for VAT texture lookup
        // Matches the ShaderGraph AnimationVertexCombined subgraph logic.
        float2 GetVATUV(float meshUVx)
        {
            float texWidth = max(_PosTex_TexelSize.z, 1.0);
            float texHeight = max(_PosTex_TexelSize.w, 1.0);
            
            float time;
            #if defined(_TIMERMODE_SCRIPT)
                time = _Timer;
            #else
                time = _Time.y;
            #endif

            // Animation playback logic:
            // duration = clip length in seconds
            // frames = number of frames in this clip
            // offset = starting frame index in the texture atlas
            // currentFrame = fractional frame index within the clip
            
            float duration = max(_Duration, 0.001);
            float frames = _Frames;
            float offset = _Offset;

            // Legacy fallback if _Frames is not set (e.g. 1.0 default and _AnimDurationAndOffset has data)
            // But for the new combined workflow, we prioritize separate properties.
            if (frames <= 1.0 && _AnimDurationAndOffset.x > 0.001)
            {
                duration = _AnimDurationAndOffset.x;
                float timeBasedFrame = (time + _AnimDurationAndOffset.y) / duration;
                float y = frac(timeBasedFrame);
                return float2(meshUVx + 0.5 / texWidth, y);
            }

            float normalizedProgress = frac(time / duration);
            float currentFrame = normalizedProgress * frames;
            float y = (offset + currentFrame + 0.5) / texHeight;
            
            return float2(meshUVx + 0.5 / texWidth, y);
        }
        
        // Sample VAT and get displaced position/normal/tangent
        // Position, Normal, Tangent, Velocity are all stored as float in their natural ranges
        void SampleVAT(float meshUVx, out float3 position, out float3 normal, out float3 tangent, out float3 velocity)
        {
            float2 vatUV = GetVATUV(meshUVx);
            
            // Sample with point filtering (LOD 0)
            position = SAMPLE_TEXTURE2D_LOD(_PosTex, sampler_PosTex, vatUV, 0).xyz;
            
            // Normals and tangents are stored as float in -1 to 1 range (ARGBHalf format)
            // No unpacking needed since the compute shader writes them directly
            normal = SAMPLE_TEXTURE2D_LOD(_NmlTex, sampler_PosTex, vatUV, 0).xyz;
            tangent = SAMPLE_TEXTURE2D_LOD(_TanTex, sampler_PosTex, vatUV, 0).xyz;
            
            velocity = SAMPLE_TEXTURE2D_LOD(_VelTex, sampler_PosTex, vatUV, 0).xyz;
        }
        
        ENDHLSL
        
        // =====================================================
        // FORWARD PASS
        // =====================================================
        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }
            
            HLSLPROGRAM
            #pragma vertex ForwardVert
            #pragma fragment ForwardFrag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile _TIMERMODE_SHADER _TIMERMODE_SCRIPT
            #pragma multi_compile_instancing
            
            struct ForwardVaryings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float3 normalWS : TEXCOORD2;
                float3 tangentWS : TEXCOORD3;
                float3 bitangentWS : TEXCOORD4;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            
            ForwardVaryings ForwardVert(Attributes input)
            {
                ForwardVaryings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                
                float texWidth = max(_PosTex_TexelSize.z, 1.0);
                float meshUVx = (float)input.vertexID / texWidth;
                
                float3 vatPos, vatNrm, vatTan, vatVel;
                SampleVAT(meshUVx, vatPos, vatNrm, vatTan, vatVel);
                
                // Lerp between original position and VAT position
                float3 finalPos = lerp(input.positionOS.xyz, vatPos, _Lerp);
                float3 finalNrm = lerp(input.normalOS, vatNrm, _Lerp);
                float3 finalTan = lerp(input.tangentOS.xyz, vatTan, _Lerp);

                // --- Velocity Smear Logic ---
                float velMag = length(vatVel);
                if (_SmearStrength > 0 && velMag > _SmearMinVelocity)
                {
                    float3 velDir = vatVel / velMag;
                    // Dot product: how much the vertex normal points AWAY from the velocity direction
                    // Vertices on the back side of the motion (dot < 0) are extruded
                    float smearWeight = saturate(-dot(finalNrm, velDir));
                    finalPos += velDir * velMag * smearWeight * _SmearStrength * _Lerp;
                }
                // ----------------------------
                
                output.positionWS = TransformObjectToWorld(finalPos);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.uv = TRANSFORM_TEX(input.uv, _MainTex);
                output.normalWS = TransformObjectToWorldNormal(finalNrm);
                output.tangentWS = TransformObjectToWorldDir(finalTan);
                output.bitangentWS = cross(output.normalWS, output.tangentWS) * input.tangentOS.w;
                
                return output;
            }
            
            half4 ForwardFrag(ForwardVaryings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                
                float2 uv = input.uv;
                half4 albedo = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv) * _Color;
                
                InputData inputData = (InputData)0;
                inputData.positionWS = input.positionWS;
                inputData.normalWS = normalize(input.normalWS);
                inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
                inputData.shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                inputData.bakedGI = SampleSH(inputData.normalWS);
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
                
                SurfaceData surfaceData = (SurfaceData)0;
                surfaceData.albedo = albedo.rgb;
                surfaceData.alpha = albedo.a;
                surfaceData.metallic = _Metallic;
                surfaceData.smoothness = _Smoothness;
                surfaceData.normalTS = float3(0, 0, 1);
                surfaceData.occlusion = 1;
                surfaceData.emission = 0;
                surfaceData.specular = 0;
                surfaceData.clearCoatMask = 0;
                surfaceData.clearCoatSmoothness = 0;
                
                return UniversalFragmentPBR(inputData, surfaceData);
            }
            ENDHLSL
        }
        
        // =====================================================
        // SHADOW CASTER PASS
        // =====================================================
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            
            ZWrite On
            ZTest LEqual
            ColorMask 0
            
            HLSLPROGRAM
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag
            #pragma multi_compile _TIMERMODE_SHADER _TIMERMODE_SCRIPT
            #pragma multi_compile_instancing
            
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
            
            float3 _LightDirection;
            
            struct ShadowVaryings
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            
            ShadowVaryings ShadowVert(Attributes input)
            {
                ShadowVaryings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                
                float texWidth = max(_PosTex_TexelSize.z, 1.0);
                float meshUVx = (float)input.vertexID / texWidth;
                
                float3 vatPos, vatNrm, vatTan, vatVel;
                SampleVAT(meshUVx, vatPos, vatNrm, vatTan, vatVel);
                
                float3 finalPos = lerp(input.positionOS.xyz, vatPos, _Lerp);
                float3 finalNrm = lerp(input.normalOS, vatNrm, _Lerp);

                // --- Velocity Smear Logic ---
                float velMag = length(vatVel);
                if (_SmearStrength > 0 && velMag > _SmearMinVelocity)
                {
                    float3 velDir = vatVel / velMag;
                    float smearWeight = saturate(-dot(finalNrm, velDir));
                    finalPos += velDir * velMag * smearWeight * _SmearStrength * _Lerp;
                }
                // ----------------------------
                
                float3 positionWS = TransformObjectToWorld(finalPos);
                float3 normalWS = TransformObjectToWorldNormal(finalNrm);
                
                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, _LightDirection));
                
                #if UNITY_REVERSED_Z
                    positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #endif
                
                output.positionCS = positionCS;
                return output;
            }
            
            half4 ShadowFrag(ShadowVaryings input) : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }
        
        // =====================================================
        // DEPTH ONLY PASS
        // =====================================================
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            
            ZWrite On
            ColorMask R
            
            HLSLPROGRAM
            #pragma vertex DepthVert
            #pragma fragment DepthFrag
            #pragma multi_compile _TIMERMODE_SHADER _TIMERMODE_SCRIPT
            #pragma multi_compile_instancing
            
            struct DepthVaryings
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            
            DepthVaryings DepthVert(Attributes input)
            {
                DepthVaryings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                
                float texWidth = max(_PosTex_TexelSize.z, 1.0);
                float meshUVx = (float)input.vertexID / texWidth;
                
                float3 vatPos, vatNrm, vatTan, vatVel;
                SampleVAT(meshUVx, vatPos, vatNrm, vatTan, vatVel);
                
                float3 finalPos = lerp(input.positionOS.xyz, vatPos, _Lerp);

                // --- Velocity Smear Logic ---
                float3 finalNrm = lerp(input.normalOS, vatNrm, _Lerp);
                float velMag = length(vatVel);
                if (_SmearStrength > 0 && velMag > _SmearMinVelocity)
                {
                    float3 velDir = vatVel / velMag;
                    float smearWeight = saturate(-dot(finalNrm, velDir));
                    finalPos += velDir * velMag * smearWeight * _SmearStrength * _Lerp;
                }
                // ----------------------------

                output.positionCS = TransformObjectToHClip(finalPos);
                return output;
            }
            
            half4 DepthFrag(DepthVaryings input) : SV_Target
            {
                return input.positionCS.z;
            }
            ENDHLSL
        }
        
        // =====================================================
        // MOTION VECTORS PASS - This is the key for motion blur!
        // =====================================================
        Pass
        {
            Name "MotionVectors"
            Tags { "LightMode" = "MotionVectors" }
            
            ZWrite On
            
            HLSLPROGRAM
            #pragma vertex MotionVert
            #pragma fragment MotionFrag
            #pragma multi_compile _TIMERMODE_SHADER _TIMERMODE_SCRIPT
            #pragma multi_compile_instancing
            
            // _NonJitteredViewProjMatrix and _PrevViewProjMatrix are defined in URP Core.hlsl
            
            struct MotionVaryings
            {
                float4 positionCS : SV_POSITION;
                float4 positionCSNoJitter : TEXCOORD0;
                float4 previousPositionCSNoJitter : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            
            MotionVaryings MotionVert(Attributes input)
            {
                MotionVaryings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                
                float texWidth = max(_PosTex_TexelSize.z, 1.0);
                float meshUVx = (float)input.vertexID / texWidth;
                
                // Sample current frame position and velocity
                float3 vatPos, vatNrm, vatTan, vatVel;
                SampleVAT(meshUVx, vatPos, vatNrm, vatTan, vatVel);
                
                float3 finalPos = lerp(input.positionOS.xyz, vatPos, _Lerp);
                float3 finalNrm = lerp(input.normalOS, vatNrm, _Lerp);

                // --- Velocity Smear Logic ---
                float velMag = length(vatVel);
                float3 smearedPos = finalPos;
                if (_SmearStrength > 0 && velMag > _SmearMinVelocity)
                {
                    float3 velDir = vatVel / velMag;
                    float smearWeight = saturate(-dot(finalNrm, velDir));
                    smearedPos += velDir * velMag * smearWeight * _SmearStrength * _Lerp;
                }
                // ----------------------------
                
                // Current position
                float3 positionWS = TransformObjectToWorld(smearedPos);
                output.positionCS = TransformWorldToHClip(positionWS);
                output.positionCSNoJitter = mul(_NonJitteredViewProjMatrix, float4(positionWS, 1.0));
                
                // Previous position = current position - velocity
                // We also apply the smear to the previous position to avoid artifacting if smear is constant
                float3 previousPosOS = smearedPos - vatVel * _Lerp;
                float3 previousPosWS = TransformObjectToWorld(previousPosOS);
                output.previousPositionCSNoJitter = mul(_PrevViewProjMatrix, float4(previousPosWS, 1.0));
                
                return output;
            }
            
            half4 MotionFrag(MotionVaryings input) : SV_Target
            {
                // Calculate motion vector in NDC space
                float3 positionNDC = input.positionCSNoJitter.xyz / input.positionCSNoJitter.w;
                float3 previousPositionNDC = input.previousPositionCSNoJitter.xyz / input.previousPositionCSNoJitter.w;
                
                // Motion vector is the difference
                float2 motionVector = (positionNDC.xy - previousPositionNDC.xy);
                
                // Convert from NDC [-1,1] to UV [0,1] motion
                motionVector *= 0.5;
                
                return half4(motionVector, 0, 1);
            }
            ENDHLSL
        }

        
        // =====================================================
        // DEPTH NORMALS PASS (for SSAO, etc.)
        // =====================================================
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            
            ZWrite On
            
            HLSLPROGRAM
            #pragma vertex DepthNormalsVert
            #pragma fragment DepthNormalsFrag
            #pragma multi_compile _TIMERMODE_SHADER _TIMERMODE_SCRIPT
            #pragma multi_compile_instancing
            
            struct DepthNormalsVaryings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            
            DepthNormalsVaryings DepthNormalsVert(Attributes input)
            {
                DepthNormalsVaryings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                
                float texWidth = max(_PosTex_TexelSize.z, 1.0);
                float meshUVx = (float)input.vertexID / texWidth;
                
                float3 vatPos, vatNrm, vatTan, vatVel;
                SampleVAT(meshUVx, vatPos, vatNrm, vatTan, vatVel);
                
                float3 finalPos = lerp(input.positionOS.xyz, vatPos, _Lerp);
                float3 finalNrm = lerp(input.normalOS, vatNrm, _Lerp);

                // --- Velocity Smear Logic ---
                float velMag = length(vatVel);
                if (_SmearStrength > 0 && velMag > _SmearMinVelocity)
                {
                    float3 velDir = vatVel / velMag;
                    float smearWeight = saturate(-dot(finalNrm, velDir));
                    finalPos += velDir * velMag * smearWeight * _SmearStrength * _Lerp;
                }
                // ----------------------------
                
                output.positionCS = TransformObjectToHClip(finalPos);
                output.normalWS = TransformObjectToWorldNormal(finalNrm);
                return output;
            }
            
            half4 DepthNormalsFrag(DepthNormalsVaryings input) : SV_Target
            {
                float3 normalWS = normalize(input.normalWS);
                return half4(normalWS * 0.5 + 0.5, 0);
            }
            ENDHLSL
        }
    }
    
    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
