using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;

[ExecuteInEditMode]
public class TerrainRender : MonoBehaviour {

    // References
    public WorldTopography world;
    public Material material;
    public ComputeShader cullKernel;

    // Settings
    [Header("Grid Settings")]
    public Vector2 normalisedOrigin = Vector2.one * 0.5f;
    public int tileCount = 32;
    public int cellCount = 2048;

    [Header("Debug")]
    public bool disableCulling = false;

    // Cache
    class Cache : IDisposable
    {
        // Data constants
        const int SIZE_COMPUTE_FLOAT = 4;
        const int SIZE_COMPUTE_VECTOR3 = SIZE_COMPUTE_FLOAT * 3;
        const int SIZE_COMPUTE_VECTOR4 = SIZE_COMPUTE_FLOAT * 4;

        internal bool valid;

        // Layout
        internal Vector3 startPos;
        internal Vector3 stride;
        internal Vector2 worldUVStride;
        internal int tilesX;
        internal int tilesZ;

        // Tile transforms & positions
        internal Matrix4x4[] transforms;
        internal ComputeBuffer pointsBuffer;
        internal ComputeBuffer visibleBuffer;
        internal ComputeBuffer argsBuffer;
        internal uint[] argsValues;

        // Culling parameters
        internal Vector3 worldBounds;
        internal float tileRadius;

        internal const int FRUSTUM_SIDES = 5; // Near distance is small so use a pyramid
        internal ComputeBuffer cullPlaneOriginsBuffer;
        internal ComputeBuffer cullPlaneNormalsBuffer;

        // Graphics
        internal Mesh mesh;
        internal MaterialPropertyBlock properties;

        internal int idWorldSample;
        internal int renderLayer;

        internal Cache(TerrainRender context)
        {
            // Check world
            if (context.world == null)
            {
                return;
            }

            // Check zeroes
            if (context.tileCount <= 0 || context.world.horizontalScale <= 0 || context.cellCount <= 0)
            {
                return;
            }

            // Calculate layout
            float worldSize = context.cellCount * context.world.horizontalScale;
            startPos = new Vector3(context.normalisedOrigin.x * -worldSize, 0, context.normalisedOrigin.y * -worldSize);

            // Round tile count up to cover full extent
            int tileDetail = Mathf.CeilToInt(context.cellCount / (float)context.tileCount);
            // Check invalid sizes
            if (context.tileCount >= context.cellCount || tileDetail < 2)
            {
                return;
            }

            tilesX = Mathf.CeilToInt(context.cellCount / (float)tileDetail);
            tilesZ = tilesX;
            stride = new Vector3(tileDetail * context.world.horizontalScale, 0, tileDetail * context.world.horizontalScale);

            // Calculate culling values
            worldBounds = new Vector3(worldSize, context.world.elevationScale, worldSize);
            tileRadius = stride.magnitude;

            // Misc graphics params
            renderLayer = LayerMask.NameToLayer("Default");
            idWorldSample = Shader.PropertyToID("_WorldSample");
            properties = new MaterialPropertyBlock();

            // Generate mesh
            mesh = new Mesh();

            // Vertices & UV
            int downSample = 6;
            int vertSideCount = Mathf.CeilToInt((float)tileDetail / downSample) + 1;
            var vertices = new Vector3[vertSideCount * vertSideCount];
            var uv = new Vector2[vertices.Length];
            worldUVStride.x = (1f / context.cellCount) * vertSideCount;
            worldUVStride.y = worldUVStride.x;

            float vertStride = context.world.horizontalScale * ((float)tileDetail / (vertSideCount-1));
            float uvStride = (float)downSample / vertSideCount;
            for (int x = 0; x < vertSideCount; x++)
            {
                for (int z = 0; z < vertSideCount; z++)
                {
                    int index = x + z * vertSideCount;
                    vertices[index].x = x * vertStride;
                    vertices[index].z = z * vertStride;
                    uv[index].x = x * uvStride;
                    uv[index].y = z * uvStride;
                }
            }
            mesh.vertices = vertices;
            mesh.uv = uv;

            // Indices
            var cellIndexProto = new int[] { 0, vertSideCount, 1, 1, vertSideCount, vertSideCount+1 };
            var quadSideCount = vertSideCount - 1;
            var indices = new int[cellIndexProto.Length * quadSideCount * quadSideCount];
            for (int x = 0; x < quadSideCount; x++)
            {
                for (int z = 0; z < quadSideCount; z++)
                {
                    int vertIndex = x + z * vertSideCount;
                    int quadIndex = cellIndexProto.Length * (x + z * quadSideCount);

                    for (int i = 0; i < cellIndexProto.Length; i++)
                    {
                        indices[quadIndex + i] = cellIndexProto[i] + vertIndex;
                    }
                }
            }
            mesh.SetIndices(indices, MeshTopology.Triangles, 0, true);
            var bounds = mesh.bounds;

            var heightScale = context.material.GetFloat("_HeightScale");
            bounds.Encapsulate(Vector3.up * heightScale);
            mesh.bounds = bounds;

            mesh.RecalculateNormals();
            mesh.RecalculateTangents();

            // Calculate tile positions & transforms
            var positions = new Vector4[tilesX * tilesZ];
            transforms = new Matrix4x4[tilesX * tilesZ];
            for (int x = 0; x < tilesX; x++)
            {
                for (int z = 0; z < tilesZ; z++)
                {
                    var pos = startPos + new Vector3(x * stride.x, 0, z * stride.z);
                    positions[x + z * tilesX] = pos;
                    transforms[x + z * tilesX] = Matrix4x4.Translate(pos);
                }
            }

            // Make compute buffers
            pointsBuffer = new ComputeBuffer(positions.Length, SIZE_COMPUTE_VECTOR4);
            pointsBuffer.SetData(positions);
            visibleBuffer = new ComputeBuffer(positions.Length, SIZE_COMPUTE_VECTOR4, ComputeBufferType.Append);
            visibleBuffer.SetCounterValue(0);

            argsValues = new uint[5] { 0, 0, 0, 0, 0 };
            argsValues[0] = (uint)mesh.GetIndexCount(0);
            argsValues[1] = (uint)positions.Length;

            argsBuffer = new ComputeBuffer(1, argsValues.Length * sizeof(uint), ComputeBufferType.IndirectArguments);
            argsBuffer.SetData(argsValues);

            cullPlaneOriginsBuffer = new ComputeBuffer(FRUSTUM_SIDES, SIZE_COMPUTE_VECTOR3);
            cullPlaneNormalsBuffer = new ComputeBuffer(FRUSTUM_SIDES, SIZE_COMPUTE_VECTOR3);

            // All good, flag done
            valid = true;
        }

        public void Dispose()
        {
            ComputeBuffer[] buffersToRelease = {
                pointsBuffer,
                visibleBuffer,
                argsBuffer,
                cullPlaneOriginsBuffer,
                cullPlaneNormalsBuffer
            };
            foreach (var buffer in buffersToRelease)
            {
                if (buffer != null)
                {
                    buffer.Dispose();
                }
            }
        }
    }

    Cache _cache;
    Cache cache
    {
        get
        {
            if (_cache == null)
            {
                _cache = new Cache(this);
            }
            return _cache;
        }
    }

    void Update()
    {
        if (world == null || material == null)
        {
            return;
        }
        if (!cache.valid)
        {
            ClearCache();
            return;
        }

        if (cullKernel != null)
        {
            var kernelIndex = cullKernel.FindKernel("CameraCull");
            // Assign buffers & sizes
            {
                cullKernel.SetBuffer(kernelIndex, "_Points", cache.pointsBuffer);
                cullKernel.SetBuffer(kernelIndex, "_Visible", cache.visibleBuffer);
                cullKernel.SetInt("_PointCount", cache.transforms.Length);

                cullKernel.SetBuffer(kernelIndex, "_PlaneOrigins", cache.cullPlaneOriginsBuffer);
                cullKernel.SetBuffer(kernelIndex, "_PlaneNormals", cache.cullPlaneNormalsBuffer);
            }

            // Calculate & assign cull parameters
            {
                var cam = Camera.main;
                var camTrans = cam.transform;
                var camOrigin = camTrans.position;
                Vector3[] corners = new Vector3[4];
                cam.CalculateFrustumCorners(new Rect(0, 0, 1, 1), cam.farClipPlane, Camera.MonoOrStereoscopicEye.Mono, corners);

                for (int i = 0; i < corners.Length; i++)
                {
                    // Convert corners to world space
                    corners[i] = camTrans.TransformPoint(corners[i]);
                }

                var planeOrigins = new Vector3[5];
                var planeNormals = new Vector3[5];
                // First, back plane normal is just camera backwards vector
                planeOrigins[0] = camOrigin + camTrans.forward * cam.farClipPlane;
                planeNormals[0] = -camTrans.forward;
                // Create side planes in last 4 spots
                // Corner order is:
                // 1---2
                // |   |
                // 0---3
                // Get normals by cross product of vector from origin to corner,
                // with vector from corner to (wrapping) next corner.
                for (int i = 0; i < corners.Length; i++)
                {
                    // All 4 side planes share camera origin
                    planeOrigins[1 + i] = camOrigin;
                    var corner = corners[i];
                    var sideA = corner - camOrigin;
                    var sideB = corner - corners[(i + 1) % corners.Length];
                    var normal = Vector3.Cross(sideA, sideB).normalized;
                    planeNormals[1 + i] = normal;
                }

                // Move every plane back by cull radius
                for (int i = 0; i < planeOrigins.Length; i++)
                {
                    planeOrigins[i] -= planeNormals[i] * cache.tileRadius;
                }

                cache.cullPlaneOriginsBuffer.SetData(planeOrigins);
                cache.cullPlaneNormalsBuffer.SetData(planeNormals);
            }

                // Cull dispatch
                {
                    cache.visibleBuffer.SetCounterValue(0);
                    uint sizex, sizey, sizez;
                    cullKernel.GetKernelThreadGroupSizes(kernelIndex, out sizex, out sizey, out sizez);
                    cullKernel.Dispatch(kernelIndex, (int)sizex, (int)sizey, (int)sizez);
            }

            // Use cull result
            {
                ComputeBuffer.CopyCount(cache.visibleBuffer, cache.argsBuffer, sizeof(uint));
                material.SetBuffer("_Points", cache.visibleBuffer);
            }
        }

        if (disableCulling || cullKernel == null)
        {
            Graphics.DrawMeshInstanced(cache.mesh, 0, material, cache.transforms, cache.transforms.Length, null, UnityEngine.Rendering.ShadowCastingMode.On, true);
        }
        else
        {
            Graphics.DrawMeshInstancedIndirect(cache.mesh, 0, material, new Bounds(Vector3.zero, cache.worldBounds), cache.argsBuffer);
        }
    }

    void OnDisable()
    {
        ClearCache();
    }

    void OnDrawGizmosSelected()
    {
        Vector3 basePos = transform.position + cache.startPos;
        Gizmos.color = Color.blue;
        for (int x = 0; x < cache.tilesX; x++)
        {
            for (int z = 0; z < cache.tilesZ; z++)
            {
                var pos = basePos + new Vector3(x * cache.stride.x, 0, z * cache.stride.z);
                Gizmos.DrawRay(pos, Vector3.right * cache.stride.x);
                Gizmos.DrawRay(pos, Vector3.forward * cache.stride.z);
            }
        }
    }

    void ClearCache()
    {
        if (_cache == null)
        {
            return;
        }

        _cache.Dispose();
        _cache = null;
    }
}
