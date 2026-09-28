using System.Collections.Generic;
using System.Text;
using Unity.Profiling;
using UnityEngine;

namespace ExcelHeroes.Core
{
    /// <summary>
    /// Frame cost while a window is open: frame time, draw calls, SetPass calls, triangles and
    /// managed allocation per frame, read through ProfilerRecorder (these render and memory
    /// counters work in release players). The capture pass opens one over the fight and writes
    /// the averages and worst frames to tools/out/perf.txt — the numbers the optimisation pass
    /// is judged by. Nothing runs unless Begin is called.
    /// </summary>
    public class PerfProbe : MonoBehaviour
    {
        ProfilerRecorder _draw, _setPass, _tris, _gc, _gcCount, _main;
        long _alloc0; int _vsync, _fps, _gc0;
        readonly List<(float ms, long draw, long setPass, long tris, long gc, long gcCount)> _frames = new();
        bool _on;
        string _label;

        public static PerfProbe Instance { get; private set; }

        public static PerfProbe Ensure()
        {
            if (Instance != null) return Instance;
            var go = new GameObject("PerfProbe");
            DontDestroyOnLoad(go);
            return Instance = go.AddComponent<PerfProbe>();
        }

        public void Begin(string label)
        {
            _label = label;
            _frames.Clear();
            _draw = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count");
            _setPass = ProfilerRecorder.StartNew(ProfilerCategory.Render, "SetPass Calls Count");
            _tris = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Triangles Count");
            _gc = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame");
            _gcCount = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocation In Frame Count");
            _main = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "Main Thread", 15);
            _alloc0 = System.GC.GetTotalMemory(false); _gc0 = System.GC.CollectionCount(0);
            // uncapped while measuring: under vsync every frame reads 16.7 ms whatever it cost
            _vsync = QualitySettings.vSyncCount; _fps = Application.targetFrameRate;
            QualitySettings.vSyncCount = 0; Application.targetFrameRate = 1000;
            _on = true;
        }

        void Update()
        {
            if (!_on) return;
            // managed allocation on the main thread since the last frame (the GC counters read 0 in a release player)
            var a = System.GC.GetTotalMemory(false);
            var grew = System.Math.Max(0, a - _alloc0);   // a collection shows as a drop; count growth only
            _frames.Add((Time.unscaledDeltaTime * 1000f, _draw.LastValue, _setPass.LastValue, _tris.LastValue, grew, _gcCount.LastValue));
            _alloc0 = a;
        }

        /// <summary>Closes the window and returns the report (also logged).</summary>
        public string End()
        {
            _on = false;
            QualitySettings.vSyncCount = _vsync; Application.targetFrameRate = _fps;
            _draw.Dispose(); _setPass.Dispose(); _tris.Dispose(); _gc.Dispose(); _gcCount.Dispose(); _main.Dispose();
            if (_frames.Count < 3) return "";
            var f = _frames.GetRange(2, _frames.Count - 2);   // the window's first frames carry the switch-over
            double ms = 0, draw = 0, sp = 0, tris = 0, gc = 0, gcn = 0; float worst = 0; long maxDraw = 0, maxGc = 0;
            foreach (var x in f)
            {
                ms += x.ms; draw += x.draw; sp += x.setPass; tris += x.tris; gc += x.gc; gcn += x.gcCount;
                worst = Mathf.Max(worst, x.ms); maxDraw = System.Math.Max(maxDraw, x.draw); maxGc = System.Math.Max(maxGc, x.gc);
            }
            var n = f.Count;
            var sb = new StringBuilder();
            sb.AppendLine($"[perf] {_label}: {n} frames");
            sb.AppendLine($"  frame (uncapped) avg {ms / n:F2} ms = {1000.0 * n / ms:F0} fps   worst {worst:F1} ms");
            sb.AppendLine($"  draws     avg {draw / n:F0}   max {maxDraw}   setpass avg {sp / n:F0}");
            sb.AppendLine($"  tris      avg {tris / n / 1000:F0}k");
            sb.AppendLine($"  GC/frame  avg {gc / n / 1024:F1} KB   max {maxGc / 1024f:F1} KB   collections {System.GC.CollectionCount(0) - _gc0}");
            var slow = 0; foreach (var x in f) if (x.ms > 8f) slow++;
            sb.AppendLine($"  slow      {slow} frames over 8 ms ({100f * slow / n:F1}%)");
            // what is on screen: renderers, distinct materials, and which renderers carry a property block
            var rs = FindObjectsByType<Renderer>(FindObjectsSortMode.None);
            var mats = new HashSet<Material>(); int live = 0, blocks = 0; var byName = new Dictionary<string, int>();
            foreach (var r in rs)
            {
                if (!r.enabled || !r.gameObject.activeInHierarchy) continue;
                live++;
                if (r.HasPropertyBlock()) blocks++;
                foreach (var m in r.sharedMaterials) if (m != null) mats.Add(m);
                var key = r.gameObject.name.Split(':')[0];
                byName[key] = byName.TryGetValue(key, out var c) ? c + 1 : 1;
            }
            sb.AppendLine($"  scene     {live} renderers, {mats.Count} materials, {blocks} with property blocks");
            var top = new List<KeyValuePair<string, int>>(byName); top.Sort((x, y) => y.Value.CompareTo(x.Value));
            sb.Append("  by name   "); for (var i = 0; i < Mathf.Min(12, top.Count); i++) sb.Append($"{top[i].Key} {top[i].Value}, "); sb.AppendLine();
            var text = sb.ToString();
            Debug.Log(text);
            return text;
        }
    }
}
