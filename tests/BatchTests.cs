using System;
using System.IO;
using System.Linq;
using CadPublishKit;

class BatchTests
{
    static int passed;
    static void Check(bool condition, string name) { if (!condition) throw new Exception(name); passed++; Console.WriteLine("PASS " + name); }
    static ScanReport Bodies(int count)
    {
        var scan = new ScanReport { document_kind = "part_bodies", state_check_passed = true };
        for (int i = 0; i < count; i++) scan.bodies.Add(new BodyRecord { body_name = "body" + i, body_type = "solid", face_count = 6 });
        return scan;
    }
    static string Assess(ScanReport scan) { var row = new BatchItem(); SplitAssessment.Apply(scan, row); return row.status; }
    static int Main()
    {
        try {
            string root = Path.Combine(Path.GetTempPath(), "CADPublishKit-batch-tests-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
            File.WriteAllText(Path.Combine(root, "same.STEP"), "Synthetic file, no geometry");
            File.WriteAllText(Path.Combine(root, "drawing.pdf"), "Synthetic PDF companion, unchanged");
            string child = Path.Combine(root, "child"); Directory.CreateDirectory(child); File.WriteAllText(Path.Combine(child, "same.stp"), "Synthetic file");
            string deep = root;
            for (int i = 1; i <= 11; i++) { deep = Path.Combine(deep, "level" + i); Directory.CreateDirectory(deep); if (i >= 10) File.WriteAllText(Path.Combine(deep, "depth" + i + ".step"), "Synthetic file"); }
            var found = BatchFileScanner.Discover(root);
            Check(found.Files.Count == 3 && found.Files.Any(f => f.EndsWith("depth10.step")) && !found.Files.Any(f => f.EndsWith("depth11.step")) && found.Issues.Count == 1, "root plus ten descendant levels, explicit depth warning");
            Check(found.Files.Count(f => Path.GetFileNameWithoutExtension(f) == "same") == 2 && !found.Files.Any(f => f.EndsWith(".pdf")), "case-insensitive STEP/STP, duplicate names preserved, PDF not scanned");
            Check(File.ReadAllText(Path.Combine(root, "drawing.pdf")) == "Synthetic PDF companion, unchanged", "companion file unchanged");
            Check(BatchFileScanner.Discover(root, child).Files.Count == 2 && !BatchFileScanner.IsWithin(child + "Extra", child), "internal directory exclusion respects path boundaries");
            Check(BatchFileScanner.Discover(root, null, () => true).Cancelled && BatchFileScanner.Discover(root, null, () => true).Files.Count == 0, "discovery cancellation is explicit");
            Check(Assess(Bodies(1)) == "unsplittable" && Assess(Bodies(2)) == "splittable", "single solid vs multi-body structure assessment");
            Check(Assess(Bodies(0)) == "review", "empty geometry is review, not a false no-split result");
            var failed = Bodies(2); failed.warnings.Add("solid enumeration failed: unavailable");
            Check(Assess(failed) == "review", "incomplete enumeration cannot pass");
            var sheets = Bodies(2); sheets.bodies[1].body_type = "sheet";
            Check(Assess(sheets) == "review", "unsupported sheet bodies need review");
            var assembly = new ScanReport { document_kind = "assembly" }; assembly.parts.Add(new Part { part_name = "p", instance_count = 3, export_filename = "p.step" });
            Check(Assess(assembly) == "splittable", "one unique part with repeated instances is an assembly");
            assembly.parts[0].export_issue = "Reference geometry unavailable";
            Check(Assess(assembly) == "review", "failed assembly group cannot pass");
            Check(ExportSelection.Indices(new[] { "body_001.step", "body_002.step", "body_003.step" }, new[] { "body_003.step", "body_001.step" }).SequenceEqual(new[] { 0, 2 }), "selection preserves original file identity and order");
            bool rejected = false; try { ExportSelection.Indices(new[] { "a.step" }, new[] { "b.step" }); } catch (InvalidOperationException) { rejected = true; }
            Check(rejected, "stale selection rejected");
            rejected = false; try { ExportSelection.Indices(new[] { "a.step" }, new string[0]); } catch (InvalidOperationException) { rejected = true; }
            Check(rejected, "empty selection rejected");
            rejected = false; try { ExportSelection.Indices(new[] { "a.step", "A.STEP" }, null); } catch (InvalidOperationException) { rejected = true; }
            Check(rejected, "case-insensitive output collisions rejected");
            Check(PreviewModel.FromScan(Bodies(2)).Fingerprint() != PreviewModel.FromScan(Bodies(3)).Fingerprint(), "preview change detection");
            Console.WriteLine("Passed: " + passed + " batch/preview checks; synthetic files, no CAD calls."); return 0;
        } catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
