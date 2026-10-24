using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CadPublishKit
{
    public class Instance
    {
        public string instance_id, parent_id, part_id, component_name, source_path, configuration, kind, status;
        public List<string> issues = new List<string>();
    }
    public class Part
    {
        public string part_id, part_name, source_path, configuration;
        public int instance_count;
    }
    public class ScanReport
    {
        public string mode = "real_com_scan", revision, assembly_path, configuration;
        public string created_at = DateTimeOffset.Now.ToString("o");
        public List<Instance> instances = new List<Instance>();
        public List<Part> parts = new List<Part>();
        public List<string> warnings = new List<string>();
    }
    public static class PartDeduplicator
    {
        public static List<Part> Build(IEnumerable<Instance> instances)
        {
            var result = new List<Part>();
            var keys = new Dictionary<string, Part>(StringComparer.Ordinal);
            foreach (var item in instances)
            {
                item.part_id = null;
                if (item.kind != "part") continue;
                if (String.IsNullOrWhiteSpace(item.source_path) || String.IsNullOrWhiteSpace(item.configuration))
                { item.issues.Add("Missing path or configuration; not deduplicated."); continue; }
                string path;
                try {
                    if (!Path.IsPathRooted(item.source_path)) throw new ArgumentException("Path is not absolute.");
                    path = Path.GetFullPath(item.source_path);
                }
                catch (Exception ex) { item.issues.Add("Invalid path: " + ex.Message); continue; }
                // Configuration is deliberately case-sensitive to avoid accidental merging.
                string key = path.ToUpperInvariant() + "\0" + item.configuration;
                Part part;
                if (!keys.TryGetValue(key, out part))
                {
                    part = new Part { part_id = "part_" + (result.Count + 1),
                        part_name = Path.GetFileNameWithoutExtension(path), source_path = path,
                        configuration = item.configuration };
                    keys.Add(key, part); result.Add(part);
                }
                part.instance_count++; item.part_id = part.part_id;
            }
            return result;
        }
    }
}
