using UnityEngine;
using UnityEngine.Rendering;

public class DecalUpdateCulledSystem
{
    private DecalEntityManager m_EntityManager;
    private ProfilingSampler m_Sampler;

    public DecalUpdateCulledSystem(DecalEntityManager entityManager)
    {
        m_EntityManager = entityManager;
        m_Sampler = new ProfilingSampler("DecalUpdateCulledSystem.Execute");
    }

    public void Execute()
    {
        using (new ProfilingScope(null, m_Sampler))
        {
            for (int i = 0; i < m_EntityManager.chunkCount; ++i)
                Execute(m_EntityManager.culledChunks[i], m_EntityManager.culledChunks[i].count);
        }
    }

    private void Execute(DecalCulledChunk culledChunk, int count)
    {
        CullingGroup cullingGroup = culledChunk.cullingGroups;
        culledChunk.visibleDecalCount = cullingGroup.QueryIndices(true, culledChunk.visibleDecalIndices, 0);
        culledChunk.visibleDecalIndices2.CopyFrom(culledChunk.visibleDecalIndices);
    }
}
