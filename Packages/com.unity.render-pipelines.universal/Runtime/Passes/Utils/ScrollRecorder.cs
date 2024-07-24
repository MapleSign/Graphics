using System;
using System.Threading.Tasks;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class ScrollRecorder : MonoBehaviour
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

    int[] newLayerValues;
    Vector3[] projPositions;

    Visibility[,] lastVisibilities;

    [SerializeReference]
    GameObject holders;

    [SerializeReference]
    UniversalRenderPipelineAsset asset;

    //[SerializeReference]
    Camera cam;

    [SerializeReference]
    Light dirLight;

    void Awake()
    {
        k_DefaultLayer = LayerMask.NameToLayer("Default");
        k_IgnoreStaticShadowLayer = LayerMask.NameToLayer("Ignore Static Shadow");

        statics = new GameObject[k_MaxStatics];
        boundingSpheres = new BoundingSphere[k_MaxStatics];
        lastVisibilities = new Visibility[k_MaxStatics, k_MaxCascades];
        count = Mathf.Min(k_MaxStatics, holders.transform.childCount);

        newLayerValues = new int[k_MaxStatics];
        projPositions = new Vector3[k_MaxStatics];

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

    void LateUpdate()
    {
        cam = Camera.main;

        if (asset.shadowUpdateMode == ShadowUpdateMode.Dynamic || !asset.supportsShadowScrolling)
            return;

        float[] fars = new float[k_MaxCascades];
        switch (asset.shadowCascadeCount)
        {
            case 1:
                fars[0] = asset.shadowDistance;
                break;
            case 2:
                fars[0] = asset.shadowDistance * asset.cascade2Split;
                fars[1] = asset.shadowDistance;
                break;
            case 3:
                fars[0] = asset.shadowDistance * asset.cascade3Split.x;
                fars[1] = asset.shadowDistance * asset.cascade3Split.y;
                fars[2] = asset.shadowDistance;
                break;
            case 4:
                fars[0] = asset.shadowDistance * asset.cascade4Split.x;
                fars[1] = asset.shadowDistance * asset.cascade4Split.y;
                fars[2] = asset.shadowDistance * asset.cascade4Split.z;
                fars[3] = asset.shadowDistance;
                break;
            default:
                throw new System.Exception("Invalid cascade count");
        }

        Array.Fill(newLayerValues, k_IgnoreStaticShadowLayer, 0, count);
        var proj = dirLight.transform.worldToLocalMatrix;
        Parallel.For(0, count, i =>
        {
            projPositions[i] = proj.MultiplyPoint(boundingSpheres[i].position);
            projPositions[i].z = 0f;
        });

        for (int i = 0; i < asset.shadowCascadeCount; i++)
        {
            UpdateCascade(i, fars[i]);
        }

        var profScope = new ProfilingScope(null, new ProfilingSampler("Modify Layers"));
        for (int i = 0; i < count; i++)
        {
            statics[i].layer = newLayerValues[i];
        }
    }

    void UpdateCascade(int cascadeIndex, float far)
    {
        var proj = dirLight.transform.worldToLocalMatrix;
        ComputeCullingSphereSimple(cam.aspect, cam.fieldOfView, cam.nearClipPlane, far, out var cullingSphereCenter, out var cullingSphereRadius);
        cullingSphereCenter = cam.cameraToWorldMatrix.MultiplyPoint(cullingSphereCenter);
        cullingSphereCenter = proj.MultiplyPoint(cullingSphereCenter);
        cullingSphereCenter.z = 0f;
        Parallel.For(0, count, i =>
        {
            var pos = projPositions[i];
            var distance = Vector3.Distance(pos, cullingSphereCenter);
            Visibility visibility = distance <= (cullingSphereRadius - boundingSpheres[i].radius) ? Visibility.Complete :
                distance >= (cullingSphereRadius + boundingSpheres[i].radius) ? Visibility.Invisible : Visibility.Partial;
            if (lastVisibilities[i, cascadeIndex] != Visibility.Complete && visibility != Visibility.Invisible)
            {
                newLayerValues[i] = k_DefaultLayer;
            }

            lastVisibilities[i, cascadeIndex] = visibility;
        });
    }

    void ComputeCullingSphere(float aspect, float fov, float near, float far, out Vector3 center, out float radius)
    {
        float r_aspect2 = 1f / (aspect * aspect);
        float tanHalfFov = Mathf.Tan(fov * Mathf.Deg2Rad * 0.5f);

        float k = Mathf.Sqrt(1 + r_aspect2) * tanHalfFov;
        float k2 = k * k;

        float FMinusN = far - near;
        float FPlusN = far + near;

        if (k2 >= FMinusN / FPlusN)
        {
            center = new Vector3(0f, 0f, -far);
            radius = far * k;
        }
        else
        {
            center = new Vector3(0f, 0f, -0.5f * (FPlusN) * (1 + k2));
            radius = 0.5f * Mathf.Sqrt( FMinusN * FMinusN + 2f * (far * far + near * near) * k2 + FPlusN * FPlusN * k2 * k2 );
        }
    }

    void ComputeCullingSphereSimple(float aspect, float fov, float near, float far, out Vector3 center, out float radius)
    {
        center = new Vector3(0f, 0f, -(far + near) * 0.5f);
        float wf = 2f * far * Mathf.Tan(fov * Mathf.Deg2Rad * 0.5f);
        float hf = wf / aspect;
        Vector3 corner = new Vector3(wf, hf, -far);
        radius = Vector3.Distance(center, corner);
    }
}
