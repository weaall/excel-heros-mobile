using System;
using System.Collections.Generic;
using UnityEngine;

namespace ExcelHeroes.World
{
    /// <summary>
    /// Vertex-coloured primitives for the 3D SD cast and the office set. Everything in the 3D
    /// layer is built from these at runtime — no imported models — so 55 characters come from the
    /// paper-doll specs the web build already has, and a change to a spec is a change to the model.
    ///
    /// A Builder accumulates primitives under a current transform and bakes them into one Mesh,
    /// so a body part is one draw call however many pieces it has.
    /// </summary>
    public static class MeshKit
    {
        public class Builder
        {
            readonly List<Vector3> _v = new();
            readonly List<Vector3> _n = new();
            readonly List<Color> _c = new();
            readonly List<Vector2> _uv = new();
            readonly List<int> _t = new();
            Matrix4x4 _m = Matrix4x4.identity, _nm = Matrix4x4.identity;

            /// <summary>The transform applied to everything added; the normal matrix is kept with it.</summary>
            public Matrix4x4 M
            {
                get => _m;
                set { _m = value; _nm = value.inverse.transpose; }
            }

            public int Count => _v.Count;

            int Add(Vector3 p, Vector3 n, Color c, Vector2 uv)
            {
                _v.Add(_m.MultiplyPoint3x4(p));
                _n.Add(_nm.MultiplyVector(n).normalized);
                _c.Add(c);
                _uv.Add(uv);
                return _v.Count - 1;
            }

            void Tri(int a, int b, int c) { _t.Add(a); _t.Add(b); _t.Add(c); }

            /// <summary>
            /// An ellipsoid, optionally cut: <paramref name="thetaMax"/> maps an azimuth (radians,
            /// 0 = +X, the way the character faces) to how far down from the top the surface runs,
            /// 0..PI. A full sphere is PI everywhere; a hair cap is short at the front, long behind.
            /// </summary>
            public void Ellipsoid(Vector3 c, Vector3 r, Color col, int seg = 20, Func<float, float> thetaMax = null,
                                  float thetaMin = 0f)
            {
                var rings = Mathf.Max(6, seg / 2 + 2);
                var start = _v.Count;
                for (var j = 0; j <= seg; j++)
                {
                    var phi = j / (float)seg * Mathf.PI * 2f;
                    var tmax = thetaMax?.Invoke(phi) ?? Mathf.PI;
                    for (var i = 0; i <= rings; i++)
                    {
                        var th = Mathf.Lerp(thetaMin, tmax, i / (float)rings);
                        var unit = new Vector3(Mathf.Sin(th) * Mathf.Cos(phi), Mathf.Cos(th), Mathf.Sin(th) * Mathf.Sin(phi));
                        var p = c + Vector3.Scale(unit, r);
                        var n = new Vector3(unit.x / Mathf.Max(0.0001f, r.x), unit.y / Mathf.Max(0.0001f, r.y), unit.z / Mathf.Max(0.0001f, r.z));
                        Add(p, n, col, new Vector2(j / (float)seg, i / (float)rings));
                    }
                }
                var stride = rings + 1;
                for (var j = 0; j < seg; j++)
                    for (var i = 0; i < rings; i++)
                    {
                        var a = start + j * stride + i;
                        var b = start + (j + 1) * stride + i;
                        Tri(a, b, a + 1);
                        Tri(b, b + 1, a + 1);
                    }
            }

            /// <summary>A tapered tube along +Y, elliptical in section (depth = z scale), capped.</summary>
            public void Frustum(Vector3 bottom, float r0, float height, float r1, Color col, float depth = 1f,
                                int seg = 18, bool caps = true, float r0z = -1f, float r1z = -1f)
            {
                if (r0z < 0f) r0z = r0 * depth;
                if (r1z < 0f) r1z = r1 * depth;
                var start = _v.Count;
                var slope = (r0 - r1) / Mathf.Max(0.0001f, height);
                for (var j = 0; j <= seg; j++)
                {
                    var a = j / (float)seg * Mathf.PI * 2f;
                    var ca = Mathf.Cos(a); var sa = Mathf.Sin(a);
                    var n = new Vector3(ca, slope, sa).normalized;
                    Add(bottom + new Vector3(ca * r0, 0f, sa * r0z), n, col, new Vector2(j / (float)seg, 0f));
                    Add(bottom + new Vector3(ca * r1, height, sa * r1z), n, col, new Vector2(j / (float)seg, 1f));
                }
                for (var j = 0; j < seg; j++)
                {
                    var a = start + j * 2;
                    Tri(a, a + 1, a + 2);
                    Tri(a + 2, a + 1, a + 3);
                }
                if (!caps) return;
                Disc(bottom, r0, r0z, col, false, seg);
                Disc(bottom + Vector3.up * height, r1, r1z, col, true, seg);
            }

            public void Disc(Vector3 c, float rx, float rz, Color col, bool up, int seg = 18)
            {
                var n = up ? Vector3.up : Vector3.down;
                var centre = Add(c, n, col, new Vector2(0.5f, 0.5f));
                var first = _v.Count;
                for (var j = 0; j <= seg; j++)
                {
                    var a = j / (float)seg * Mathf.PI * 2f;
                    Add(c + new Vector3(Mathf.Cos(a) * rx, 0f, Mathf.Sin(a) * rz), n, col,
                        new Vector2(0.5f + Mathf.Cos(a) * 0.5f, 0.5f + Mathf.Sin(a) * 0.5f));
                }
                for (var j = 0; j < seg; j++)
                    if (up) Tri(centre, first + j + 1, first + j);
                    else Tri(centre, first + j, first + j + 1);
            }

            /// <summary>An axis-aligned box with flat faces (split normals).</summary>
            public void Box(Vector3 c, Vector3 size, Color col)
            {
                var h = size * 0.5f;
                Face(c, new Vector3(h.x, 0, 0), new Vector3(0, 0, h.z), new Vector3(0, h.y, 0), col);   // top
                Face(c, new Vector3(-h.x, 0, 0), new Vector3(0, 0, h.z), new Vector3(0, -h.y, 0), col); // bottom
                Face(c, new Vector3(0, 0, -h.z), new Vector3(0, h.y, 0), new Vector3(h.x, 0, 0), col);  // +x
                Face(c, new Vector3(0, 0, h.z), new Vector3(0, h.y, 0), new Vector3(-h.x, 0, 0), col);  // -x
                Face(c, new Vector3(h.x, 0, 0), new Vector3(0, h.y, 0), new Vector3(0, 0, h.z), col);   // +z
                Face(c, new Vector3(-h.x, 0, 0), new Vector3(0, h.y, 0), new Vector3(0, 0, -h.z), col); // -z
            }

            /// <summary>One quad at c + off, spanned by u and v, facing along off.</summary>
            void Face(Vector3 c, Vector3 u, Vector3 v, Vector3 off, Color col)
            {
                var n = off.normalized;
                var p = c + off;
                var a = Add(p - u - v, n, col, new Vector2(0, 0));
                var b = Add(p + u - v, n, col, new Vector2(1, 0));
                var d = Add(p + u + v, n, col, new Vector2(1, 1));
                var e = Add(p - u + v, n, col, new Vector2(0, 1));
                // Winding so the face points along n.
                // Unity's front face is cross(b - a, c - a); keep it along n.
                if (Vector3.Dot(Vector3.Cross(u, v), n) > 0f) { Tri(a, b, d); Tri(a, d, e); }
                else { Tri(a, e, d); Tri(a, d, b); }
            }

            /// <summary>A double-sided free quad (centre, half-extents along two axes).</summary>
            public void Quad(Vector3 c, Vector3 u, Vector3 v, Color col, Vector2 uv0 = default, Vector2 uv1 = default)
            {
                if (uv1 == default) uv1 = Vector2.one;
                var n = Vector3.Cross(v, u).normalized;
                var a = Add(c - u - v, n, col, new Vector2(uv0.x, uv0.y));
                var b = Add(c + u - v, n, col, new Vector2(uv1.x, uv0.y));
                var d = Add(c + u + v, n, col, new Vector2(uv1.x, uv1.y));
                var e = Add(c - u + v, n, col, new Vector2(uv0.x, uv1.y));
                Tri(a, e, d); Tri(a, d, b);
            }

            /// <summary>
            /// A parametric patch: f(s, t) gives position, normal and uv for s, t in 0..1. Triangles
            /// are wound to face along the supplied normal, so the caller never thinks about it.
            /// </summary>
            public void Grid(int ns, int nt, Func<float, float, (Vector3 p, Vector3 n, Vector2 uv)> f, Color col)
            {
                var start = _v.Count;
                for (var i = 0; i <= ns; i++)
                    for (var j = 0; j <= nt; j++)
                    {
                        var (p, n, uv) = f(i / (float)ns, j / (float)nt);
                        Add(p, n, col, uv);
                    }
                var stride = nt + 1;
                for (var i = 0; i < ns; i++)
                    for (var j = 0; j < nt; j++)
                    {
                        var a = start + i * stride + j;
                        var b = a + stride;
                        var na = _n[a];
                        var cr = Vector3.Cross(_v[b] - _v[a], _v[a + 1] - _v[a]);
                        if (Vector3.Dot(cr, na) >= 0f) { Tri(a, b, a + 1); Tri(b, b + 1, a + 1); }
                        else { Tri(a, a + 1, b); Tri(b, a + 1, b + 1); }
                    }
            }

            public Mesh Bake(string name = "mesh")
            {
                var m = new Mesh { name = name };
                if (_v.Count > 65000) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                m.SetVertices(_v);
                m.SetNormals(_n);
                // Colours are written as sRGB hex; the project renders in linear space, so they are
                // converted here — without it every paint job comes out washed pale.
                var lin = new List<Color>(_c.Count);
                var linear = QualitySettings.activeColorSpace == ColorSpace.Linear;
                foreach (var c in _c) lin.Add(linear ? new Color(c.linear.r, c.linear.g, c.linear.b, c.a) : c);
                m.SetColors(lin);
                m.SetUVs(0, _uv);
                m.SetTriangles(_t, 0);
                m.RecalculateBounds();
                return m;
            }
        }

        // ------------------------------------------------------------------ materials --

        static Shader _toon, _glass;
        static Material _toonShared, _toonNoOutline, _glassShared;

        public static Shader ToonShader => _toon ??= Resources.Load<Shader>("Shaders/Toon") ?? Shader.Find("ExcelHeroes/Toon");
        public static Shader GlassShader => _glass ??= Resources.Load<Shader>("Shaders/Glass") ?? Shader.Find("ExcelHeroes/Glass");

        /// <summary>The shared outlined toon material; vertex colours carry the paint.</summary>
        public static Material Toon => _toonShared ??= NewToon(0.012f);

        /// <summary>Toon without the outline, for the set: floors and walls with a hull look like cardboard.</summary>
        public static Material ToonFlat => _toonNoOutline ??= NewToon(0f);

        public static Material NewToon(float outline, Texture tex = null)
        {
            var m = new Material(ToonShader) { name = "EhToon" };
            m.SetFloat("_OutlineWidth", outline);
            if (tex != null) m.SetTexture("_MainTex", tex);
            return m;
        }

        public static Material Glass => _glassShared ??= NewGlass(null);

        public static Material NewGlass(Texture tex, Color? tint = null)
        {
            var m = new Material(GlassShader) { name = "EhGlass" };
            if (tex != null) m.SetTexture("_MainTex", tex);
            m.SetColor("_Color", Lin(tint ?? Color.white));
            return m;
        }

        public static GameObject Part(string name, Transform parent, Mesh mesh, Material mat, int layer)
        {
            var go = new GameObject(name) { layer = layer };
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            return go;
        }

        public static Color Hex(string hex, Color fallback)
        {
            if (string.IsNullOrEmpty(hex)) return fallback;
            if (!hex.StartsWith("#")) hex = "#" + hex;
            return ColorUtility.TryParseHtmlString(hex, out var c) ? c : fallback;
        }

        /// <summary>An sRGB colour as the shader wants it (see Bake).</summary>
        public static Color Lin(Color c) => QualitySettings.activeColorSpace == ColorSpace.Linear
            ? new Color(c.linear.r, c.linear.g, c.linear.b, c.a) : c;

        public static Color Shade(Color c, float k) => new(c.r * k, c.g * k, c.b * k, c.a);

        /// <summary>A soft round blob, for contact shadows and sparks.</summary>
        static Texture2D _blob;
        public static Texture2D Blob
        {
            get
            {
                if (_blob != null) return _blob;
                const int n = 64;
                _blob = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "blob" };
                var px = new Color32[n * n];
                for (var y = 0; y < n; y++)
                    for (var x = 0; x < n; x++)
                    {
                        var d = new Vector2(x - n / 2f + 0.5f, y - n / 2f + 0.5f).magnitude / (n / 2f);
                        var a = Mathf.Clamp01(1f - d);
                        a = a * a * (3f - 2f * a);
                        px[y * n + x] = new Color32(255, 255, 255, (byte)(a * 255));
                    }
                _blob.SetPixels32(px);
                _blob.Apply();
                return _blob;
            }
        }
    }
}
