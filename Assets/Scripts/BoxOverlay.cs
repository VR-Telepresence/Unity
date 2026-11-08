using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable] public class Det { public string label; public float confidence; public float[] box; } // x1,y1,x2,y2 in 0..1, y down
[Serializable] public class DetFrame { public Det[] objects; }

/// Draws detection boxes as pooled LineRenderers. Put this on an empty child of StreamAnchor.
/// StreamDisplay positions it just in front of the stream quad and tells it the quad size in metres.
public class BoxOverlay : MonoBehaviour
{
    [SerializeField] private Material lineMaterial;      // URP Unlit, e.g. M_Box
    [SerializeField] private Color color = Color.green;
    [SerializeField] private float lineWidth = 0.015f;

    private float width = 4f, height = 2.31f;
    private readonly List<LineRenderer> pool = new List<LineRenderer>();
    private MaterialPropertyBlock mpb;

    public void Configure(float quadWidth, float quadHeight, float localZ)
    {
        width = quadWidth;
        height = quadHeight;
        transform.localPosition = new Vector3(0f, 0f, localZ);
        transform.localRotation = Quaternion.identity;
        transform.localScale = Vector3.one;
    }

    public void Show(Det[] objects)
    {
        int n = objects != null ? objects.Length : 0;
        while (pool.Count < n) pool.Add(CreateLine());

        for (int i = 0; i < pool.Count; i++)
        {
            bool active = i < n && objects[i].box != null && objects[i].box.Length == 4;
            pool[i].gameObject.SetActive(active);
            if (!active) continue;

            float[] b = objects[i].box;
            float x1 = (b[0] - 0.5f) * width;
            float x2 = (b[2] - 0.5f) * width;
            float yTop = (0.5f - b[1]) * height;   // image y points down, Unity y up
            float yBot = (0.5f - b[3]) * height;

            var lr = pool[i];
            lr.SetPosition(0, new Vector3(x1, yTop, 0f));
            lr.SetPosition(1, new Vector3(x2, yTop, 0f));
            lr.SetPosition(2, new Vector3(x2, yBot, 0f));
            lr.SetPosition(3, new Vector3(x1, yBot, 0f));
        }
    }

    private LineRenderer CreateLine()
    {
        if (mpb == null)
        {
            mpb = new MaterialPropertyBlock();
            mpb.SetColor("_BaseColor", color);   // URP
            mpb.SetColor("_Color", color);       // built-in
        }

        var go = new GameObject("Box");
        go.transform.SetParent(transform, false);
        var lr = go.AddComponent<LineRenderer>();
        lr.useWorldSpace = false;
        lr.loop = true;
        lr.positionCount = 4;
        lr.widthMultiplier = lineWidth;
        lr.sharedMaterial = lineMaterial;
        lr.SetPropertyBlock(mpb);
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows = false;
        return lr;
    }
}