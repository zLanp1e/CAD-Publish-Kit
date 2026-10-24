using System;
using System.IO;
using System.Linq;
namespace CadPublishKit
{
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            bool exportBodies = (args.Length == 2 || (args.Length == 4 && args[2] == "--template")) && args[0] == "--export-bodies";
            if (args.Length != 1 && !exportBodies) { Console.WriteLine("Usage: CadPublishKit.exe <output-parent-directory> OR --export-bodies <output-parent-directory> [--template <part.prtdot>]"); return 1; }
            try {
                var app = SolidWorksConnector.Connect();
                if (exportBodies) return BodyStepExporter.Run(app, args[1], args.Length == 4 ? args[3] : null);
                var report = ModelScanner.Scan(app);
                string directory = Path.Combine(Path.GetFullPath(args[0]), "scan_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + "_" + Guid.NewGuid().ToString("N"));
                ManifestWriter.Write(report, directory);
                Console.WriteLine("Instances: {0}; unique file/config parts: {1}", report.instances.Count, report.parts.Count);
                Console.WriteLine("Document kind: {0}; bodies: {1}", report.document_kind, report.bodies.Count);
                Console.WriteLine("Reports: " + directory);
                Console.WriteLine("Coverage limitations are recorded in manifest.json. No CAD files saved.");
                return report.instances.Any(i => i.issues.Count > 0) || report.bodies.Any(b => b.issues.Count > 0) || report.warnings.Count > 1 ? 2 : 0;
            }
            catch (Exception ex) {
                Console.Error.WriteLine("Scan failed (0x{0:X8}): {1}", ex.HResult, ex.Message);
                Console.Error.WriteLine("Run SolidWorks with an active assembly or part under the same Windows account/elevation. No CAD save requested.");
                return 1;
            }
        }
    }
}
