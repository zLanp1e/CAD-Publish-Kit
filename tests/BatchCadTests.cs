using System;
using System.Linq;
using CadPublishKit;

// Opt-in integration check. Requires a real SolidWorks session and a folder with at least two STEP files.
// Does not publish deliveries or move source/PDF files. Analysis uses the normal internal working copies.
class BatchCadTests
{
    [STAThread]
    static int Main(string[] args)
    {
        if (args.Length != 2 || args[0] != "--cancel-after-first") {
            Console.Error.WriteLine("Usage: BatchCadTests.exe --cancel-after-first <fixture-folder-with-at-least-two-STEP-files>"); return 1;
        }
        var workflow = new DesktopWorkflow(); bool cancel = false;
        try {
            var report = BatchProcessor.Run(workflow, args[1], false, null, () => cancel, Console.WriteLine,
                r => { if (r.files.Count < 2) throw new InvalidOperationException("At least two files are required."); },
                row => { if (row.status != "running") cancel = true; });
            if (!report.cancelled || report.stop_reason != null || report.files.Count(f => f.status != "pending") != 1)
                throw new InvalidOperationException("Cancellation did not preserve the remaining pending files.");
            Console.WriteLine("PASS real batch cancellation: one file processed, remaining files pending. Report: " + report.report_directory);
            return 0;
        } catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
