using System;
using System.IO;
using CadPublishKit;
internal static class DesktopPublishTests
{
    public static int Main()
    {
        string root = Path.Combine(Path.GetTempPath(), "CADPublishKit-publish-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string source = Path.Combine(root, "original.step"), body = Path.Combine(root, "body_001.step"), target = Path.Combine(root, "delivery");
        File.WriteAllText(source, "Synthetic source, not CAD geometry."); File.WriteAllText(body, "Synthetic body, not CAD geometry."); Directory.CreateDirectory(target);
        var report = new BodyExportReport { source_state_preserved = true };
        report.bodies.Add(new BodyExportResult { body_name = "=name,\"test", export_path = body, status = "verified", original_faces = 4, reopened_faces = 4, max_axis_delta = 0 });
        try {
            DesktopWorkflow.Publish(report, source, target);
            string bom = File.ReadAllText(Path.Combine(target, "BOM.csv"));
            if (!bom.Contains("'=name,\"\"test") || !bom.Contains("parts/body_001.step") || File.ReadAllText(Path.Combine(target, "parts", "body_001.step")) != File.ReadAllText(body)) throw new Exception("Publication content mismatch.");
            bool rejected = false;
            try { DesktopWorkflow.Publish(report, source, target); } catch (IOException) { rejected = true; }
            if (!rejected || File.ReadAllText(Path.Combine(target, "BOM.csv")) != bom) throw new Exception("Overwrite guard failed.");
            report.bodies[0].status = "failed";
            string invalid = Path.Combine(root, "invalid"); Directory.CreateDirectory(invalid);
            rejected = false; try { DesktopWorkflow.Publish(report, source, invalid); } catch (IOException) { rejected = true; }
            if (!rejected || File.Exists(Path.Combine(invalid, "BOM.csv"))) throw new Exception("Failed result was published.");
            report.document_kind = "assembly"; report.bodies[0].status = "verified";
            report.bodies[0].instance_count = 4; report.bodies[0].configuration = "Config,A";
            report.bodies[0].original_solids = 2; report.bodies[0].component_source_path = @"C:\parts\bracket.sldprt";
            string assemblyTarget = Path.Combine(root, "assembly"); Directory.CreateDirectory(assemblyTarget);
            DesktopWorkflow.Publish(report, source, assemblyTarget);
            string assemblyBom = File.ReadAllText(Path.Combine(assemblyTarget, "BOM.csv"));
            if (!assemblyBom.StartsWith("Item,PartName,InstanceCount,") || !assemblyBom.Contains("\"4\",\"parts/") || !assemblyBom.Contains("\"Config,A\"") || !assemblyBom.Contains("SolidCount"))
                throw new Exception("Assembly BOM lost quantity, configuration or body-group information.");
            string a = ExportNaming.PartFile("CON:?/x", "A", @"C:\one\part.sldprt");
            if (a != ExportNaming.PartFile("CON:?/x", "A", @"c:\ONE\part.sldprt") || a == ExportNaming.PartFile("CON:?/x", "B", @"C:\one\part.sldprt") ||
                a == ExportNaming.PartFile("CON:?/x", "A", @"C:\two\part.sldprt") || a.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                throw new Exception("Stable, collision-safe filename check failed.");
            report.warnings.Add("Unresolved assembly descendant");
            rejected = false; try { DesktopWorkflow.Publish(report, source, invalid); } catch (IOException) { rejected = true; }
            if (!rejected || File.Exists(Path.Combine(invalid, "BOM.csv"))) throw new Exception("Incomplete assembly was published.");
            Console.WriteLine("PASS: 6 desktop publication/naming checks (synthetic files, no CAD calls).");
            return 0;
        } catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        // Unique test artifacts retained for diagnosis; never delete user files.
    }
}
