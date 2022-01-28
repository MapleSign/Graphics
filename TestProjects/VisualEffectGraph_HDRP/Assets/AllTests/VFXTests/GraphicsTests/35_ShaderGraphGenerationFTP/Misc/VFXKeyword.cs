using System.Linq;
using System.Collections;
using System.Collections.Generic;
using JetBrains.Annotations;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.VFX;

[ExecuteAlways]
public class VFXKeyword : MonoBehaviour
{
    private bool green;
    private float waiting;

    void Update()
    {
        waiting -= Time.deltaTime;
        if (waiting < 0)
        {
            waiting = 0.7f;
            green = !green;
        }

        Shader.DisableKeyword("SG_KEYWORD_GLOBAL_EXPOSED_RED");
        Shader.DisableKeyword("SG_KEYWORD_GLOBAL_EXPOSED_GREEN");
        Shader.DisableKeyword("SG_KEYWORD_GLOBAL_EXPOSED_BLUE");

        Shader.DisableKeyword("SG_KEYWORD_GLOBAL_NOT_EXPOSED_RED");
        Shader.DisableKeyword("SG_KEYWORD_GLOBAL_NOT_EXPOSED_GREEN");
        Shader.DisableKeyword("SG_KEYWORD_GLOBAL_NOT_EXPOSED_BLUE");

        Shader.DisableKeyword("SG_KEYWORD_LOCAL_EXPOSED_RED");
        Shader.DisableKeyword("SG_KEYWORD_LOCAL_EXPOSED_GREEN");
        Shader.DisableKeyword("SG_KEYWORD_LOCAL_EXPOSED_BLUE");

        Shader.DisableKeyword("SG_KEYWORD_LOCAL_NOT_EXPOSED_RED");
        Shader.DisableKeyword("SG_KEYWORD_LOCAL_NOT_EXPOSED_GREEN");
        Shader.DisableKeyword("SG_KEYWORD_LOCAL_NOT_EXPOSED_BLUE");

        var vfxRenderer = GetComponent<Renderer>();

        //a. Global not exposed (most common usage)
        Shader.EnableKeyword(green ? "SG_KEYWORD_GLOBAL_NOT_EXPOSED_GREEN" : "SG_KEYWORD_GLOBAL_NOT_EXPOSED_BLUE");

        //b. Local not exposed
        foreach (var material in vfxRenderer.sharedMaterials)
        {
            if (material.name.ToUpperInvariant().Contains("SG_KEYWORD_LOCAL_NOT_EXPOSED"))
            {
                var localKeywordRed = new LocalKeyword(material.shader, "SG_KEYWORD_LOCAL_NOT_EXPOSED_RED");
                var localKeywordGreen = new LocalKeyword(material.shader, "SG_KEYWORD_LOCAL_NOT_EXPOSED_GREEN");
                var localKeywordBlue = new LocalKeyword(material.shader, "SG_KEYWORD_LOCAL_NOT_EXPOSED_BLUE");

                material.SetKeyword(localKeywordRed, false);
                material.SetKeyword(localKeywordGreen, green);
                material.SetKeyword(localKeywordBlue, !green);
            }
        }
        Shader.EnableKeyword("SG_KEYWORD_LOCAL_EXPOSED_RED"); // Shouldn't have any impact since it's local

        //c. Global exposed
        foreach (var material in vfxRenderer.sharedMaterials) //Remove default keyword
        {
            if (material.name.ToUpperInvariant().Contains("SG_KEYWORD_GLOBAL_EXPOSED"))
            {
                var globalKeywordRed = new LocalKeyword(material.shader, "SG_KEYWORD_GLOBAL_EXPOSED_RED");
                var globalKeywordGreen = new LocalKeyword(material.shader, "SG_KEYWORD_GLOBAL_EXPOSED_GREEN");
                var globalKeywordBlue = new LocalKeyword(material.shader, "SG_KEYWORD_GLOBAL_EXPOSED_BLUE");

                material.SetKeyword(globalKeywordRed, false);
                material.SetKeyword(globalKeywordGreen, false);
                material.SetKeyword(globalKeywordBlue, false);
            }
        }
        Shader.EnableKeyword(green ? "SG_KEYWORD_GLOBAL_EXPOSED_GREEN" : "SG_KEYWORD_GLOBAL_EXPOSED_BLUE");

        //d. Local exposed
        foreach (var material in vfxRenderer.sharedMaterials)
        {
            if (material.name.ToUpperInvariant().Contains("SG_KEYWORD_LOCAL_EXPOSED"))
            {
                var localKeywordRed = new LocalKeyword(material.shader, "SG_KEYWORD_LOCAL_EXPOSED_RED");
                var localKeywordGreen = new LocalKeyword(material.shader, "SG_KEYWORD_LOCAL_EXPOSED_GREEN");
                var localKeywordBlue = new LocalKeyword(material.shader, "SG_KEYWORD_LOCAL_EXPOSED_BLUE");

                material.SetKeyword(localKeywordRed, false);
                material.SetKeyword(localKeywordGreen, green);
                material.SetKeyword(localKeywordBlue, !green);
            }
        }
    }
}
