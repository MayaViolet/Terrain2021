// Upgrade NOTE: upgraded instancing buffer 'Props' to new syntax.

// Upgrade NOTE: replaced '_Object2World' with 'unity_ObjectToWorld'

Shader "Terrain/TerrainSurface" {
	Properties {
		_Color ("Color", Color) = (1,1,1,1)
		_MainTex ("Albedo (RGB)", 2D) = "white" {}
        _SmoothTex ("Smoothness (A)", 2D) = "grey" {}
		_Glossiness ("Smoothness", Range(0,2)) = 1
		_Metallic ("Metallic", Range(0,1)) = 0.0

        [Header(Elevation)][HDR][NoScaleOffset]_ElevationTex ("Elevation Map (HDR)", 2D) = "black" {}
        _HeightScale ("Height Scale", Float) = 1
        _HeightOffset ("Height Offset", Float) = 0
        [PerRendererData]_WorldSample ("World Height Sample Offset & Scale", Vector) = (0,0,1,1)
	}
	SubShader {
		Tags { "RenderType"="Opaque" }
		LOD 200
		
		CGPROGRAM
		// Physically based Standard lighting model, and enable shadows on all light types
		#pragma surface surf Standard vertex:vert addshadow nolightmap nometa //finalcolor:overrideColour
        #pragma multi_compile_instancing
        #pragma instancing_options procedural:setupMatrices
        #pragma target 5.0
        #include "Tessellation.cginc"

        #ifdef UNITY_PROCEDURAL_INSTANCING_ENABLED
        StructuredBuffer<float4> _Points;
        #endif

		sampler2D _MainTex;
        sampler2D _SmoothTex;
        
        sampler2D_half _ElevationTex;
        float4 _ElevationTex_TexelSize;
        float4 _WorldSample;
        float _HeightScale;
        float _HeightOffset;

		struct Input {
			float2 uv_MainTex;
            float4 worldUV;
            fixed mapHeight;
            half viewDistance;
		};

		half _Glossiness;
		half _Metallic;
		fixed4 _Color;

		// Add instancing support for this shader. You need to check 'Enable Instancing' on materials that use the shader.
		// See https://docs.unity3d.com/Manual/GPUInstancing.html for more information about instancing.      
		UNITY_INSTANCING_BUFFER_START(Props)
			// put more per-instance properties here
		UNITY_INSTANCING_BUFFER_END(Props)

        // Setup transformation matrices from buffer data
        void setupMatrices()
        {
            #ifdef UNITY_PROCEDURAL_INSTANCING_ENABLED
            float4 data = _Points[unity_InstanceID];

            unity_ObjectToWorld._11_21_31_41 = float4(1, 0, 0, 0);
            unity_ObjectToWorld._12_22_32_42 = float4(0, 1, 0, 0);
            unity_ObjectToWorld._13_23_33_43 = float4(0, 0, 1, 0);
            unity_ObjectToWorld._14_24_34_44 = float4(data.xyz, 1);

            unity_WorldToObject = unity_ObjectToWorld;
            unity_WorldToObject._14_24_34 *= -1;
            unity_WorldToObject._11_22_33 = 1.0f / unity_WorldToObject._11_22_33;
            #endif
        }

        // Source: Terrain Rendering in Frostbite Using Procedural Shader Splatting - Johan Andersson
        float3 filterNormal(float2 uv, float texelSize, float texelAspect)
        {
            float4 h;
            h[0] = tex2Dlod(_ElevationTex, float4(uv + texelSize*float2( 0,-1),0,0)).r * texelAspect;
            h[1] = tex2Dlod(_ElevationTex, float4(uv + texelSize*float2(-1, 0),0,0)).r * texelAspect;
            h[2] = tex2Dlod(_ElevationTex, float4(uv + texelSize*float2( 1, 0),0,0)).r * texelAspect;
            h[3] = tex2Dlod(_ElevationTex, float4(uv + texelSize*float2( 0, 1),0,0)).r * texelAspect;

            float3 n;
            n.z = h[0] - h[3];
            n.x = h[1] - h[2];
            n.y = 2;

            return normalize(n);
        }
        float3 filterNormalFrag(float2 uv, float texelSize, float texelAspect)
        {
            float4 h;
            h[0] = tex2D(_ElevationTex, uv + texelSize*float2( 0,-1)).r * texelAspect;
            h[1] = tex2D(_ElevationTex, uv + texelSize*float2(-1, 0)).r * texelAspect;
            h[2] = tex2D(_ElevationTex, uv + texelSize*float2( 1, 0)).r * texelAspect;
            h[3] = tex2D(_ElevationTex, uv + texelSize*float2( 0, 1)).r * texelAspect;

            float3 n;
            n.z = h[0] - h[3];
            n.x = h[1] - h[2];
            n.y = 2;

            return normalize(n);
        }

        float4 tessFactor (appdata_tan v0, appdata_tan v1, appdata_tan v2) {
                float minDist = 50.0;
                float maxDist = 5000.0;
                return UnityDistanceBasedTess(v0.vertex, v1.vertex, v2.vertex, minDist, maxDist, 32);
            }

        void vert (inout appdata_tan v, out Input o)
        {
            UNITY_INITIALIZE_OUTPUT(Input,o);

            float4 worldPos = mul(unity_ObjectToWorld, v.vertex);
            //float2 uvWorld = _WorldSample.xy + v.texcoord.xy * _WorldSample.zw;
            float2 uvWorld = worldPos.xz / 2048 / 5 + float2(0.5,0.5);
            float rawElevation = tex2Dlod(_ElevationTex, float4(uvWorld,0,0)).r - 0.0905;
            float d = rawElevation * _HeightScale;
            v.vertex.y += d;

            half viewDistance = length(UnityObjectToViewPos(v.vertex).xyz);
            #ifdef UNITY_PASS_SHADOWCASTER
                v.normal = float4(0,1,0,0);
            #else
            {
                v.normal = filterNormal(uvWorld, _ElevationTex_TexelSize.x, _HeightScale / 5);
            }
            #endif
            {
                //v.normal = float4(0,1,0,0);
            }

            half ambient =  saturate(5 * (rawElevation));
            o.mapHeight = pow(ambient, 4.5);
            o.worldUV = float4(uvWorld.xy, _ElevationTex_TexelSize.xy);
            o.viewDistance = viewDistance;
        }

		void surf (Input IN, inout SurfaceOutputStandard o) {
			// Albedo comes from a texture tinted by color
			fixed4 c = tex2D (_MainTex, IN.uv_MainTex) * _Color;
			o.Albedo = c.rgb;// * (0.25 + IN.mapHeight * 0.75);
            // Smoothness comes from a texture, modified by slider
            fixed s = tex2D (_SmoothTex, IN.uv_MainTex).a * _Glossiness;
            o.Smoothness = s;
			// Metallic and smoothness come from slider variables
			o.Metallic = _Metallic;
			o.Alpha = c.a;

            // Calculate normal from heightmap gradient
            //if (IN.viewDistance < 3000)
            {
                //o.Normal = filterNormalFrag(IN.worldUV.xy, IN.worldUV.z, _HeightScale / 5).xzy;
            }
		}

        void overrideColour (Input IN, SurfaceOutputStandard o, inout fixed4 color)
        {
            color = IN.mapHeight;
        }
		ENDCG
	}
	FallBack "Diffuse"
}
