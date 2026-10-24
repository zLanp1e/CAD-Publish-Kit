using System;
using System.IO;
using System.Linq;
using CadPublishKit;

internal static class CoreTests
{
    private static int count;
    private static void Check(bool ok, string name) { if (!ok) throw new Exception(name); count++; Console.WriteLine("PASS " + name); }
    private static Instance I(string path, string config) { return new Instance { kind = "part", source_path = path, configuration = config }; }
    public static int Main()
    {
        string directory = Path.Combine(Path.GetTempPath(), "CadPublishKit_Test_" + Guid.NewGuid().ToString("N"));
        try {
            var items = new[] { I(@"C:\A\gear.sldprt", "Default"), I(@"c:\a\sub\..\GEAR.sldprt", "Default"), I(@"C:\B\gear.sldprt", "Default"), I(@"C:\A\gear.sldprt", "Other"), I("", "Default"), I(@"C:\A\gear.sldprt", "") };
            var parts = PartDeduplicator.Build(items);
            Check(parts.Count == 3 && parts[0].instance_count == 2, "normalized path and duplicate count");
            Check(items[0].part_id != items[2].part_id, "same name different paths");
            Check(items[0].part_id != items[3].part_id, "same file different configurations");
            Check(items[4].part_id == null && items[5].part_id == null && items[4].issues.Count > 0, "unknown identities flagged");
            var assembly = I(@"C:\A\sub.sldasm", "Default"); assembly.kind = "assembly";
            var virtualPart = I(@"C:\A\virtual.sldprt", "Default"); virtualPart.kind = "virtual";
            Check(PartDeduplicator.Build(new[] { assembly, virtualPart }).Count == 0, "subassemblies and virtual parts excluded from parts list");
            Check(ManifestWriter.Csv("a,\"b\n") == "\"a,\"\"b\n\"", "CSV escaping");
            Check(ManifestWriter.Csv("=1+1") == "\"'=1+1\"", "CSV formula protection");
            var report = new ScanReport { mode = "synthetic_unit_test", parts = parts, instances = items.ToList() };
            ManifestWriter.Write(report, directory);
            byte[] bytes = File.ReadAllBytes(Path.Combine(directory, "parts.csv"));
            Check(bytes[0] == 239 && bytes[1] == 187 && bytes[2] == 191, "UTF8 BOM");
            string original = File.ReadAllText(Path.Combine(directory, "manifest.json"));
            bool refused = false;
            try { ManifestWriter.Write(report, directory); } catch (IOException) { refused = true; }
            Check(refused && File.ReadAllText(Path.Combine(directory, "manifest.json")) == original, "existing output preserved");
            Console.WriteLine("Passed: " + count); return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally {
            // Delete only explicitly named files in this test's newly generated directory.
            foreach (string name in new[] { "manifest.json", "parts.csv", "instances.csv" }) {
                string file = Path.Combine(directory, name); if (File.Exists(file)) File.Delete(file);
            }
            if (Directory.Exists(directory)) Directory.Delete(directory, false);
        }
    }
}
