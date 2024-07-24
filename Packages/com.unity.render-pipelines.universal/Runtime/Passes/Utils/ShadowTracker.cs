using System.Collections.Generic;
using UnityEngine;

public class ShadowTracker
{
    public class CameraTrackData
    {
        public string name;
        public Vector3 position;
        public Quaternion rotation;
    }

    string lastCamera = "";
    List<CameraTrackData> cameras = new List<CameraTrackData>();

    public bool CameraChanged(Camera cam)
    {
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
                name = cam.name, position = cam.transform.position, rotation = cam.transform.rotation 
            });
            changed = true;
        }
        else if (cam.transform.position != cameras[index].position || cam.transform.rotation != cameras[index].rotation)
        {
            cameras[index].position = cam.transform.position;
            cameras[index].rotation = cam.transform.rotation;
            changed = true;
        }

        return changed;
    }
}
