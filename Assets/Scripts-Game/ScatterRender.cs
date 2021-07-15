using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[ExecuteInEditMode]
public class ScatterRender : MonoBehaviour {

    // References
    public Mesh mesh;
    public Material material;
    public ComputeShader cullKernel;

    // Settings
    [Header("Scatter Settings")]
    public Vector2 origin = Vector2.one * 0.5f;
    public float scatterRange = 30;
    public int instanceCount = 32;

    // Cache
    class Cache
    {
        internal bool valid;

        // Layout
        internal Vector3 startPos;

        // Tile transforms & positions
        internal Matrix4x4[] transforms;
        internal ComputeBuffer pointsBuffer;
        internal ComputeBuffer visibleBuffer;
        internal ComputeBuffer argsBuffer;
        internal uint[] argsValues;

        // Graphics
        internal MaterialPropertyBlock properties;
        internal int renderLayer;

        internal Cache(ScatterRender context)
        {
            // Check settings
            if (context.instanceCount <= 0 || context.instanceCount > 1000 || context.mesh == null)
            {
                return;
            }

            // Misc graphics params
            renderLayer = LayerMask.NameToLayer("Default");
            properties = new MaterialPropertyBlock();

            // Calculate instance positions & transforms
            var positions = new Vector4[context.instanceCount];
            transforms = new Matrix4x4[context.instanceCount];
            for (int i = 0; i < positions.Length; i++)
            {
                var scatter = Random.insideUnitCircle * context.scatterRange;
                var pos = startPos + new Vector3(scatter.x, 0, scatter.y);
                positions[i] = pos;
                transforms[i] = Matrix4x4.Translate(pos);
            }

            // Make compute buffers
            pointsBuffer = new ComputeBuffer(positions.Length, 4 * 4);
            pointsBuffer.SetData(positions);
            visibleBuffer = new ComputeBuffer(positions.Length, 4 * 4, ComputeBufferType.Append);
            visibleBuffer.SetCounterValue(0);

            argsValues = new uint[5] { 0, 0, 0, 0, 0 };
            argsValues[0] = (uint)context.mesh.GetIndexCount(0);
            argsValues[1] = (uint)positions.Length;

            argsBuffer = new ComputeBuffer(1, argsValues.Length * sizeof(uint), ComputeBufferType.IndirectArguments);
            argsBuffer.SetData(argsValues);

            // All good, flag done
            valid = true;
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
        if (material == null)
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
            var kernelIndex = cullKernel.FindKernel("SphereCull");
            cullKernel.SetBuffer(kernelIndex, "_Points", cache.pointsBuffer);
            cullKernel.SetBuffer(kernelIndex, "_Visible", cache.visibleBuffer);
            cullKernel.SetInt("_PointCount", cache.transforms.Length);

            cache.visibleBuffer.SetCounterValue(0);
            cullKernel.Dispatch(kernelIndex, Mathf.CeilToInt(cache.transforms.Length/128f), 1, 1);

            ComputeBuffer.CopyCount(cache.visibleBuffer, cache.argsBuffer, sizeof(uint));
            material.SetBuffer("_Points", cache.visibleBuffer);
        }

        Graphics.DrawMeshInstanced(mesh, 0, material, cache.transforms, cache.transforms.Length, null, UnityEngine.Rendering.ShadowCastingMode.On, true);
        //Graphics.DrawMeshInstancedIndirect(mesh, 0, material, new Bounds(Vector3.zero, Vector3.one*1000), cache.argsBuffer);
        Vector3 basePos = transform.position + cache.startPos;
        //for (int x = 0; x < cache.tilesX; x++)
        //{
        //    for (int z = 0; z < cache.tilesZ; z++)
        //    {
        //        //var uvStride = cache.worldUVStride;
        //        //cache.properties.SetVector(cache.idWorldSample, new Vector4(x * uvStride.x, z * uvStride.y, uvStride.x, uvStride.y));
        //        Graphics.DrawMesh(cache.mesh, cache.transforms[x + z * cache.tilesX],
        //                          material, cache.renderLayer, null, 0, cache.properties);
        //    }
        //}
	}

    void OnDisable()
    {
        ClearCache();
    }

    void OnDrawGizmosSelected()
    {
        Vector3 basePos = transform.position + cache.startPos;
        Gizmos.color = Color.blue;
        Gizmos.DrawWireSphere(basePos, scatterRange);
    }

    void ClearCache()
    {
        if (_cache == null)
        {
            return;
        }

        if (cache.pointsBuffer != null)
        {
            cache.pointsBuffer.Dispose();
        }
        if (cache.visibleBuffer!= null)
        {
            cache.visibleBuffer.Dispose();
        }
        if (cache.argsBuffer != null)
        {
            cache.argsBuffer.Dispose();
        }

        _cache = null;
    }
}
