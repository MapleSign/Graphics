using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;

public class ShadowTracker
{
    public class CameraTrackData
    {
        public string name;
        public Matrix4x4 view, proj;
    }

    string lastCamera = "";
    List<CameraTrackData> cameras = new List<CameraTrackData>();

    public bool CameraChanged(ref CameraData data)
    {
        var cam = data.camera;
        bool changed = false;
        if (cam.name != lastCamera)
        {
            lastCamera = cam.name;
            changed = true;
        }

        var index = cameras.FindIndex((CameraTrackData c)=>cam.name == c.name);
        if (index == -1)
        {
            cameras.Add(new CameraTrackData() { 
                name = cam.name, view = data.GetViewMatrix(), proj = data.GetProjectionMatrixNoJitter()
            });
            changed = true;
        }
        else if (data.GetViewMatrix() != cameras[index].view || data.GetProjectionMatrixNoJitter() != cameras[index].proj)
        {
            cameras[index].view = data.GetViewMatrix();
            cameras[index].proj = data.GetProjectionMatrix();
            changed = true;
        }

        return changed;
    }
}
