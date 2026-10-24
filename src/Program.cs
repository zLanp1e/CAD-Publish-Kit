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
            if (args.Length != 1) { Console.WriteLine("Usage: CadPublishKit.exe <output-parent-directory>"); return 1; }
            try {
                var app = SolidWorksConnector.Connect();
                var report = new AssemblyScanner().Scan(app);
                string directory = Path.Combine(Path.GetFullPath(args[0]), "scan_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + "_" + Guid.NewGuid().ToString("N"));
                ManifestWriter.Write(report, directory);
                Console.WriteLine("Instances: {0}; unique file/config parts: {1}", report.instances.Count, report.parts.Count);
                Console.WriteLine("Reports: " + directory);
                Console.WriteLine("Coverage limitations are recorded in manifest.json. No CAD files saved.");
                return report.instances.Any(i => i.issues.Count > 0) || report.warnings.Count > 1 ? 2 : 0;
            }
            catch (Exception ex) {
                Console.Error.WriteLine("Scan failed (0x{0:X8}): {1}", ex.HResult, ex.Message);
                Console.Error.WriteLine("Run SolidWorks with an active assembly under the same Windows account/elevation. No CAD save requested.");
                return 1;
            }
        }
    }
}
