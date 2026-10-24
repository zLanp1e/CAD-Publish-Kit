using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

namespace CadPublishKit
{
    public static class ManifestWriter
    {
        public static string Csv(string value)
        {
            value = value ?? "";
            if (value.TrimStart().Length > 0 && "=+-@".IndexOf(value.TrimStart()[0]) >= 0) value = "'" + value;
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }
        public static void Write(ScanReport report, string directory)
        {
            Directory.CreateDirectory(directory);
            var serializer = new JavaScriptSerializer { MaxJsonLength = Int32.MaxValue };
            WriteNew(Path.Combine(directory, "manifest.json"), serializer.Serialize(report));
            if (report.document_kind == "part_bodies")
            {
                var bodies = new StringBuilder("body_id,body_name,body_type,face_count,status,issues\r\n");
                foreach (var b in report.bodies)
                    bodies.AppendLine(String.Join(",", new[] { b.body_id, b.body_name, b.body_type, b.face_count.HasValue ? b.face_count.Value.ToString() : "", b.status, String.Join("; ", b.issues) }.Select(Csv)));
                WriteNew(Path.Combine(directory, "bodies.csv"), bodies.ToString());
                return;
            }
            var csv = new StringBuilder("part_id,part_name,source_path,configuration,observed_instance_count\r\n");
            foreach (var part in report.parts)
                csv.AppendLine(String.Join(",", new[] { part.part_id, part.part_name, part.source_path, part.configuration, part.instance_count.ToString() }.Select(Csv)));
            WriteNew(Path.Combine(directory, "parts.csv"), csv.ToString());
            var rows = new StringBuilder("instance_id,parent_id,part_id,component_name,source_path,configuration,kind,status,issues\r\n");
            foreach (var i in report.instances)
                rows.AppendLine(String.Join(",", new[] { i.instance_id, i.parent_id, i.part_id, i.component_name, i.source_path, i.configuration, i.kind, i.status, String.Join("; ", i.issues) }.Select(Csv)));
            WriteNew(Path.Combine(directory, "instances.csv"), rows.ToString());
        }
        private static void WriteNew(string path, string text)
        {
            using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write))
            using (var writer = new StreamWriter(stream, new UTF8Encoding(true))) writer.Write(text);
        }
    }
}
