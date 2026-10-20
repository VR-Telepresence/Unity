using UnityEngine;

[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class CurvedScreenGenerator : MonoBehaviour
{
    [Range(60f, 120f)] public float fovHorizontal = 100f; // Sichtfeldbreite
    [Range(40f, 100f)] public float fovVertical = 80f;    // Sichtfeldhöhe
    public float radius = 1.5f;                           // Abstand zum Auge
    public int segmentsX = 24;
    public int segmentsY = 16;

    void Awake()
    {
        GenerateMesh();
    }

    [ContextMenu("Regenerate Mesh")]
    public void GenerateMesh()
    {
        Mesh mesh = new Mesh { name = "CurvedScreen" };

        int numVertices = (segmentsX + 1) * (segmentsY + 1);
        Vector3[] vertices = new Vector3[numVertices];
        Vector2[] uvs = new Vector2[numVertices];
        int[] triangles = new int[segmentsX * segmentsY * 6];

        float hHalfRad = (fovHorizontal * 0.5f) * Mathf.Deg2Rad;
        float vHalfRad = (fovVertical * 0.5f) * Mathf.Deg2Rad;

        int vertIndex = 0;
        for (int y = 0; y <= segmentsY; y++)
        {
            float vFraction = (float)y / segmentsY;
            float pitch = Mathf.Lerp(vHalfRad, -vHalfRad, vFraction);

            for (int x = 0; x <= segmentsX; x++)
            {
                float uFraction = (float)x / segmentsX;
                float yaw = Mathf.Lerp(-hHalfRad, hHalfRad, uFraction);

                // Kugelkoordinaten zur Kamera hin wölben (konstanter Radius!)
                float vx = radius * Mathf.Sin(yaw) * Mathf.Cos(pitch);
                float vy = radius * Mathf.Sin(pitch);
                float vz = radius * Mathf.Cos(yaw) * Mathf.Cos(pitch);

                vertices[vertIndex] = new Vector3(vx, vy, vz);
                uvs[vertIndex] = new Vector2(uFraction, 1f - vFraction);
                vertIndex++;
            }
        }

        int triIndex = 0;
        for (int y = 0; y < segmentsY; y++)
        {
            for (int x = 0; x < segmentsX; x++)
            {
                int current = y * (segmentsX + 1) + x;
                int next = current + segmentsX + 1;

                // Dreiecke so setzen, dass die Innenseite zur Kamera blickt
                triangles[triIndex++] = current;
                triangles[triIndex++] = current + 1;
                triangles[triIndex++] = next;

                triangles[triIndex++] = next;
                triangles[triIndex++] = current + 1;
                triangles[triIndex++] = next + 1;
            }
        }

        mesh.vertices = vertices;
        mesh.uv = uvs;
        mesh.triangles = triangles;
        mesh.RecalculateNormals();

        GetComponent<MeshFilter>().mesh = mesh;
    }
}