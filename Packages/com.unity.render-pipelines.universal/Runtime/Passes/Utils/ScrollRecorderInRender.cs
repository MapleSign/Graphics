using System;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.Universal.Internal;

public class ScrollRecorderInRender
{
    enum Visibility
    {
        Invisible,
        Partial,
        Complete
    }

    const int k_MaxStatics = 10000;
    const int k_MaxCascades = UniversalRenderPipelineAsset.k_ShadowCascadeMaxCount;

    int k_IgnoreStaticShadowLayer;
    int k_DefaultLayer;

    GameObject[] statics;
    BoundingSphere[] boundingSpheres;
    int count = 0;

    FrustumPlanes[] frustumPlanes;
    Bounds[] frustumBounds;
    float[] radiusScales;

    int[] newLayerValues;

    Visibility[,] lastVisibilities;

    GameObject holders;
    MainLightShadowCacheSystem cacheSystem;

    ProfilingSampler profilingSampler = new ProfilingSampler("Modify Layers");

    public ScrollRecorderInRender(MainLightShadowCacheSystem cacheSystem)
    {
        this.cacheSystem = cacheSystem;

        k_DefaultLayer = LayerMask.NameToLayer("Default");
        k_IgnoreStaticShadowLayer = LayerMask.NameToLayer("Ignore Static Shadow");

        statics = new GameObject[k_MaxStatics];
        boundingSpheres = new BoundingSphere[k_MaxStatics];
        lastVisibilities = new Visibility[k_MaxStatics, k_MaxCascades];

        frustumPlanes = new FrustumPlanes[k_MaxCascades];
        frustumBounds = new Bounds[k_MaxCascades];
        radiusScales = new float[k_MaxCascades];

        newLayerValues = new int[k_MaxStatics];
    }

    void Reset()
    {
        for (int i = 0; i < count; i++)
        {
            for (int c = 0; c < k_MaxCascades; c++)
            {
                lastVisibilities[i, c] = Visibility.Invisible;
            }
        }
    }

    public void Setup(ref RenderingData renderingData)
    {
        if (holders == null)
        {
            holders = GameObject.Find("STATIC_HOLDERS");
            if (holders == null)
            {
                return;
            }

            count = Mathf.Min(k_MaxStatics, holders.transform.childCount);

            for (int i = 0; i < count; i++)
            {
                statics[i] = holders.transform.GetChild(i).gameObject;
                if (statics[i].TryGetComponent<Renderer>(out var renderer))
                {
                    boundingSpheres[i].position = renderer.bounds.center;
                    boundingSpheres[i].radius = renderer.bounds.extents.magnitude;
                }
                for (int c = 0; c < k_MaxCascades; c++)
                {
                    lastVisibilities[i, c] = Visibility.Invisible;
                }
            }
        }

        ref var shadowData = ref renderingData.shadowData;
        if (shadowData.shadowUpdateMode == ShadowUpdateMode.Dynamic || !shadowData.supportsShadowScrolling)
            return;

        Array.Fill(newLayerValues, k_IgnoreStaticShadowLayer, 0, count);

        UpdateCascades();

        //var profScope = new ProfilingScope(null, profilingSampler);
        for (int i = 0; i < count; i++)
        {
            statics[i].layer = newLayerValues[i];
        }
    }

    void UpdateCascades()
    {
        for (int cascadeIndex = 0; cascadeIndex < cacheSystem.cascadesCount; cascadeIndex++)
        {
            frustumPlanes[cascadeIndex] = cacheSystem.cascadeSlices[cascadeIndex].projectionMatrix.decomposeProjection;

            var fpCenter = new Vector3(0f, 0f, (frustumPlanes[cascadeIndex].zFar + frustumPlanes[cascadeIndex].zNear) * 0.5f);
            var fpSize = new Vector3(
                frustumPlanes[cascadeIndex].right - frustumPlanes[cascadeIndex].left,
                frustumPlanes[cascadeIndex].top - frustumPlanes[cascadeIndex].bottom,
                frustumPlanes[cascadeIndex].zFar - frustumPlanes[cascadeIndex].zNear
                );
            frustumBounds[cascadeIndex] = new Bounds(fpCenter, fpSize);

            radiusScales[cascadeIndex] = Mathf.Log10(fpSize.z) * 2f;
        }

        var parallelOptions = new ParallelOptions() { MaxDegreeOfParallelism = -1 };
        Parallel.For(0, count, parallelOptions, i =>
        {
            for (int cascadeIndex = 0; cascadeIndex < cacheSystem.cascadesCount; cascadeIndex++)
            {
                var visibility = IntersectWithShadowVolume(cascadeIndex, i);
                if (lastVisibilities[i, cascadeIndex] != Visibility.Complete && visibility != Visibility.Invisible)
                {
                    newLayerValues[i] = k_DefaultLayer;
                }

                lastVisibilities[i, cascadeIndex] = visibility;
            }
        });
    }

    Visibility IntersectWithCullingSphere(int cascadeIndex, int i)
    {
        var pos = boundingSpheres[i].position;
        var radius = boundingSpheres[i].radius;
        Vector3 cullingSphereCenter = cacheSystem.cascadeSplitDistances[cascadeIndex];
        float cullingSphereRadius = cacheSystem.cascadeSplitDistances[cascadeIndex].w;
        float distance = Vector3.Distance(pos, cullingSphereCenter);

        Visibility visibility = distance < Mathf.Abs(cullingSphereRadius - radius) ? Visibility.Complete :
            distance > (cullingSphereRadius + radius) ? Visibility.Invisible : Visibility.Partial;

        return visibility;
    }

    Visibility IntersectWithShadowVolume(int cascadeIndex, int i)
    {
        var pos = cacheSystem.cascadeSlices[cascadeIndex].viewMatrix.MultiplyPoint(boundingSpheres[i].position);
        pos.z *= -1f;

        var visibility = IntersectBoundsSphere(frustumBounds[cascadeIndex], new BoundingSphere(pos, boundingSpheres[i].radius * radiusScales[cascadeIndex]));

        return visibility;
    }

    Visibility IntersectLine(float a0, float a1, float b0, float b1)
    {
        Visibility v;
        if (a1 <= b0 || b1 <= a0) v = Visibility.Invisible;
        else if (a0 <= b0 && b1 <= a1) v = Visibility.Complete;
        else v = Visibility.Partial;

        return v;
    }

    Visibility IntersectRectSphere(Rect bounds, Vector3 sphere)
    {
        Vector2 sphereCenter = sphere;
        float radius = sphere.z;
        var radiusSqr = radius * radius;
        Vector2 closetPoint;
        float closetDistanceSqr;

        Visibility visibility;

        if (bounds.Contains(sphereCenter))
        {
            closetPoint = sphereCenter - bounds.center;
            float minValue = float.MaxValue;
            int minIndex = 0;
            for (int i = 0; i < 2; ++i)
            {
                float distanceToEdge = Mathf.Abs(Mathf.Abs(closetPoint[i]) - bounds.size[i] * 0.5f);
                if (distanceToEdge < minValue)
                {
                    minValue = distanceToEdge;
                    minIndex = i;
                }
            }
            closetPoint[minIndex] = Mathf.Sign(closetPoint[minIndex]) * bounds.size[minIndex] * 0.5f;
            closetPoint += bounds.center;
            closetDistanceSqr = Vector2.SqrMagnitude(closetPoint - sphereCenter);

            visibility = closetDistanceSqr > radiusSqr ? Visibility.Complete : Visibility.Partial;
        }
        else
        {
            closetPoint = Vector2.zero;
            for (int i = 0; i < 2; ++i)
            {
                closetPoint[i] = Mathf.Clamp(sphereCenter[i], bounds.min[i], bounds.max[i]);
            }
            closetDistanceSqr = Vector2.SqrMagnitude(closetPoint - sphereCenter);

            visibility = closetDistanceSqr > radiusSqr ? Visibility.Invisible : Visibility.Partial;
        }

        return visibility;
    }


    Visibility IntersectBoundsSphere(Bounds bounds, BoundingSphere sphere)
    {
        var radiusSqr = sphere.radius * sphere.radius;
        Vector3 closetPoint;
        float closetDistanceSqr;

        Visibility visibility;

        if (bounds.Contains(sphere.position))
        {
            closetPoint = sphere.position - bounds.center;
            float minValue = float.MaxValue;
            int minIndex = 0;
            for (int i = 0; i < 3; ++i)
            {
                float distanceToEdge = Mathf.Abs(Mathf.Abs(closetPoint[i]) - bounds.extents[i]);
                if (distanceToEdge < minValue)
                {
                    minValue = distanceToEdge;
                    minIndex = i;
                }
            }
            closetPoint[minIndex] = Mathf.Sign(closetPoint[minIndex]) * bounds.size[minIndex] * 0.5f;
            closetPoint += bounds.center;
            closetDistanceSqr = Vector3.SqrMagnitude(closetPoint - sphere.position);

            visibility = closetDistanceSqr >= radiusSqr ? Visibility.Complete : Visibility.Partial;
        }
        else
        {
            closetPoint = bounds.ClosestPoint(sphere.position);
            closetDistanceSqr = Vector3.SqrMagnitude(closetPoint - sphere.position);

            visibility = closetDistanceSqr >= radiusSqr ? Visibility.Invisible : Visibility.Partial;
        }

        return visibility;
    }
}
