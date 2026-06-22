using UnityEngine;

namespace DumyatRuntime
{
    public class WoodPaintSurface : MonoBehaviour
    {
        RenderTexture _renderTexture;
        [SerializeField] private int _width = 512;
        [SerializeField] private int _height = 512;
        public RenderTexture PaintTexture => _renderTexture;
        void Awake()
        {
            Renderer render = GetComponent<Renderer>();
            _renderTexture = new RenderTexture(_width, _height, 0);
            _renderTexture.Create();
            ClearRenderTexture(_renderTexture);
            render.material.SetTexture("_RenderTexture", _renderTexture);
        }

        private void ClearRenderTexture(RenderTexture rt)
        {
            RenderTexture previousActive = RenderTexture.active;

            RenderTexture.active = rt;
            GL.Clear(true, true, Color.clear);

            RenderTexture.active = previousActive;
        }
        public Vector2 GetUVFromWorldPoint(Vector3 worldPoint)
        {
            Mesh mesh = GetComponent<MeshFilter>().sharedMesh;
            Vector3 localPoint = transform.InverseTransformPoint(worldPoint);

            Vector3[] vertices = mesh.vertices;
            int[] triangles = mesh.triangles;
            Vector2[] uvs = mesh.uv;

            int closestTri = -1;
            float minDist = float.MaxValue;
            Vector3 bestBary = Vector3.zero;

            for (int i = 0; i < triangles.Length; i += 3)
            {
                Vector3 v0 = vertices[triangles[i]];
                Vector3 v1 = vertices[triangles[i + 1]];
                Vector3 v2 = vertices[triangles[i + 2]];

                Vector3 closest = ClosestPointOnTriangle(localPoint, v0, v1, v2);
                float dist = (localPoint - closest).sqrMagnitude;

                if (dist < minDist)
                {
                    minDist = dist;
                    closestTri = i;
                    bestBary = Barycentric(closest, v0, v1, v2);
                }
            }

            if (closestTri < 0) return Vector2.zero;

            return uvs[triangles[closestTri]] * bestBary.x +
                   uvs[triangles[closestTri + 1]] * bestBary.y +
                   uvs[triangles[closestTri + 2]] * bestBary.z;
        }

        private Vector3 ClosestPointOnTriangle(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
        {
            Vector3 ab = b - a, ac = c - a, ap = p - a;
            float d1 = Vector3.Dot(ab, ap), d2 = Vector3.Dot(ac, ap);
            if (d1 <= 0 && d2 <= 0) return a;

            Vector3 bp = p - b;
            float d3 = Vector3.Dot(ab, bp), d4 = Vector3.Dot(ac, bp);
            if (d3 >= 0 && d4 <= d3) return b;

            Vector3 cp = p - c;
            float d5 = Vector3.Dot(ab, cp), d6 = Vector3.Dot(ac, cp);
            if (d6 >= 0 && d5 <= d6) return c;

            float vc = d1 * d4 - d3 * d2;
            if (vc <= 0 && d1 >= 0 && d3 <= 0)
            {
                float v = d1 / (d1 - d3);
                return a + v * ab;
            }

            float vb = d5 * d2 - d1 * d6;
            if (vb <= 0 && d2 >= 0 && d6 <= 0)
            {
                float w = d2 / (d2 - d6);
                return a + w * ac;
            }

            float va = d3 * d6 - d5 * d4;
            if (va <= 0 && (d4 - d3) >= 0 && (d5 - d6) >= 0)
            {
                float w = (d4 - d3) / ((d4 - d3) + (d5 - d6));
                return b + w * (c - b);
            }

            float denom = 1f / (va + vb + vc);
            float vf = vb * denom, wf = vc * denom;
            return a + ab * vf + ac * wf;
        }

        private Vector3 Barycentric(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
        {
            Vector3 v0 = b - a, v1 = c - a, v2 = p - a;
            float d00 = Vector3.Dot(v0, v0);
            float d01 = Vector3.Dot(v0, v1);
            float d11 = Vector3.Dot(v1, v1);
            float d20 = Vector3.Dot(v2, v0);
            float d21 = Vector3.Dot(v2, v1);
            float denom = d00 * d11 - d01 * d01;
            float v = (d11 * d20 - d01 * d21) / denom;
            float w = (d00 * d21 - d01 * d20) / denom;
            return new Vector3(1f - v - w, v, w);
        }
        public void CopyPaintFrom(RenderTexture source)
        {
            if (source == null) return;
            Graphics.Blit(source, _renderTexture);
        }
    }
}
