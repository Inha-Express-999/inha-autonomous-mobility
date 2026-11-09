using UnityEngine;
using UnityEngine.UI;

namespace InhaExpress.Client.Presentation
{
    // Small UI meshes avoid font-dependent weather glyphs and additional textures.
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class OperatorWeatherIcon : MaskableGraphic
    {
        public string Weather { get; set; }

        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            if (Weather == "clear")
            {
                Circle(mesh, Vector2.zero, 4);
                for (int i = 0; i < 8; i++)
                {
                    var direction = new Vector2(Mathf.Cos(i * Mathf.PI / 4), Mathf.Sin(i * Mathf.PI / 4));
                    Line(mesh, direction * 6, direction * 9);
                }
            }
            else if (Weather == "fog")
            {
                for (int i = -1; i <= 1; i++)
                    Line(mesh, new Vector2(i == 0 ? -7 : -9, i * 5), new Vector2(i == 0 ? 9 : 7, i * 5));
            }
            else if (Weather == "snow")
            {
                for (int i = 0; i < 6; i++)
                {
                    var direction = new Vector2(Mathf.Cos(i * Mathf.PI / 3), Mathf.Sin(i * Mathf.PI / 3));
                    var side = new Vector2(-direction.y, direction.x);
                    Line(mesh, Vector2.zero, direction * 9);
                    Line(mesh, direction * 5, direction * 7 + side * 2);
                    Line(mesh, direction * 5, direction * 7 - side * 2);
                }
            }
            else
            {
                Circle(mesh, new Vector2(-5, 2), 3);
                Circle(mesh, new Vector2(0, 4), 5);
                Circle(mesh, new Vector2(5, 2), 3);
                Line(mesh, new Vector2(-5, -3), new Vector2(-7, -8));
                Line(mesh, new Vector2(1, -3), new Vector2(-1, -8));
                Line(mesh, new Vector2(7, -3), new Vector2(5, -8));
            }
        }

        private void Circle(VertexHelper mesh, Vector2 center, float radius)
        {
            for (int i = 0; i < 20; i++)
            {
                float a = i * Mathf.PI / 10, b = (i + 1) * Mathf.PI / 10;
                int start = mesh.currentVertCount;
                Vertex(mesh, center);
                Vertex(mesh, center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius);
                Vertex(mesh, center + new Vector2(Mathf.Cos(b), Mathf.Sin(b)) * radius);
                mesh.AddTriangle(start, start + 1, start + 2);
            }
        }

        private void Line(VertexHelper mesh, Vector2 start, Vector2 end)
        {
            var normal = new Vector2(-(end - start).y, (end - start).x).normalized * .8f;
            int index = mesh.currentVertCount;
            Vertex(mesh, start - normal); Vertex(mesh, start + normal);
            Vertex(mesh, end + normal); Vertex(mesh, end - normal);
            mesh.AddTriangle(index, index + 1, index + 2);
            mesh.AddTriangle(index + 2, index + 3, index);
        }

        private void Vertex(VertexHelper mesh, Vector2 point)
        {
            var rect = GetPixelAdjustedRect();
            point = rect.center + point * (Mathf.Min(rect.width, rect.height) / 20);
            mesh.AddVert(new Vector3(point.x, point.y, 0), color, Vector2.zero);
        }
    }
}
