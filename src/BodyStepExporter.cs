using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace CadPublishKit
{
    public sealed class BodyExportResult
    {
        public string body_id, body_name, export_path, status = "pending", error;
        public int save_errors, save_warnings, load_errors, original_faces, reopened_faces;
        public long bytes;
        public double[] source_axis_limits, reopened_axis_limits;
        public double? max_axis_delta;
        public string configuration, component_source_path;
        public int instance_count = 1, original_solids, reopened_solids;
    }
    public sealed class BodyExportReport
    {
        public string started_at = DateTimeOffset.Now.ToString("o"), finished_at, source_title, source_path, configuration, revision, template;
        public string validation = "Reopen in SolidWorks, require one solid and no sheets; compare six axis extrema in model coordinates within 1e-6 m. Not a full shape-equivalence test.";
        public string options = "SaveAs2: current version, silent, no selection, existing STEP preferences unchanged. Copy body into separate temporary document without applying transforms.";
        public int step_ap;
        public string document_kind = "part_bodies";
        public string export_scope = "all";
        public int available_count;
        public ScanReport assembly_scan;
        public bool source_state_preserved;
        public List<string> warnings = new List<string>();
        public List<BodyExportResult> bodies = new List<BodyExportResult>();
    }
    public static class BodyStepExporter
    {
        public static int Run(ISldWorks app, string outputParent, string templateOverride, Action<string> progress = null, Action<string> runCreated = null, IEnumerable<string> selectedFiles = null)
        {
            var source = app.ActiveDoc as IModelDoc2;
            if (source == null) throw new InvalidOperationException("No active document.");
            bool assembly = source.GetType() == (int)swDocumentTypes_e.swDocASSEMBLY;
            var plan = assembly ? AssemblyExportPlan.Build(source) : null;
            var jobs = assembly ? plan.Jobs : (((IPartDoc)source).GetBodies2((int)swBodyType_e.swSolidBody, false) as object[] ?? new object[0])
                .Cast<IBody2>().Select(b => new ExportGeometry { Name = b.Name, Bodies = new[] { b } }).ToList();
            if (jobs.Count == 0) throw new InvalidOperationException("No solid parts to export.");
            for (int n = 0; n < jobs.Count; n++) if (!assembly) jobs[n].FileName = "body_" + (n + 1).ToString("D3") + ".step";
            int availableCount = jobs.Count;
            var indices = ExportSelection.Indices(jobs.Select(j => j.FileName), selectedFiles);
            jobs = indices.Select(i => jobs[i]).ToList();
            string template = templateOverride ?? app.GetDocumentTemplate((int)swDocumentTypes_e.swDocPART, "", 0, 0, 0);
            if (String.IsNullOrEmpty(template) || !File.Exists(template)) throw new InvalidOperationException("No usable default part template.");
            var selection = (ISelectionMgr)source.SelectionManager;
            var selectedObjects = new List<object>();
            var selectedMarks = new List<int>();
            for (int n = 1; n <= selection.GetSelectedObjectCount2(-1); n++) {
                selectedObjects.Add(selection.GetSelectedObject6(n, -1));
                selectedMarks.Add(selection.GetSelectedObjectMark(n));
            }
            bool dirty = source.GetSaveFlag();
            var report = new BodyExportReport { source_title = source.GetTitle(), source_path = source.GetPathName(),
                configuration = source.ConfigurationManager.ActiveConfiguration.Name, revision = app.RevisionNumber(), template = template,
                step_ap = app.GetUserPreferenceIntegerValue((int)swUserPreferenceIntegerValue_e.swStepAP) };
            report.available_count = availableCount; report.export_scope = jobs.Count == availableCount ? "all" : "selection";
            if (assembly) {
                report.document_kind = "assembly"; report.assembly_scan = plan.Scan;
                report.validation = "Reopen each unique referenced part; compare solid count, total face count and aggregate six axis extrema within 1e-6 m in component-local coordinates. Not full shape equivalence. Original assembly STEP is copied unchanged.";
                report.warnings.AddRange(plan.Errors);
            }
            var initialDocs = Documents(app);
            if (initialDocs.Count(d => d.GetTitle() == source.GetTitle()) != 1)
                throw new InvalidOperationException("Ambiguous source document title; cannot safely restore active document.");
            string run = Path.Combine(Path.GetFullPath(outputParent), "body_export_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + "_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(run);
            Directory.CreateDirectory(Path.Combine(run, "parts"));
            var serializer = new JavaScriptSerializer { MaxJsonLength = Int32.MaxValue };
            // These streams are created exclusively and remain owned by this run.
            using (var json = new FileStream(Path.Combine(run, "export_report.json"), FileMode.CreateNew, FileAccess.Write))
            using (var log = new StreamWriter(new FileStream(Path.Combine(run, "export_log.txt"), FileMode.CreateNew, FileAccess.Write), new UTF8Encoding(true)))
            {
                Action checkpoint = () => {
                    byte[] bytes = Encoding.UTF8.GetBytes(serializer.Serialize(report));
                    json.Position = 0; json.Write(bytes, 0, bytes.Length); json.SetLength(bytes.Length); json.Flush();
                };
                for (int i = 0; i < jobs.Count; i++) {
                    report.bodies.Add(new BodyExportResult { body_id = "body_" + (i + 1),
                        body_name = jobs[i].Name, configuration = jobs[i].Configuration, component_source_path = jobs[i].SourcePath,
                        instance_count = jobs[i].Quantity,
                        export_path = Path.Combine(run, "parts", jobs[i].FileName) });
                }
                checkpoint();
                if (runCreated != null) runCreated(run);
                Console.WriteLine("Output: " + run);
                try {
                    for (int i = 0; i < jobs.Count; i++) {
                        BodyExportResult row = report.bodies[i];
                        IModelDoc2 temp = null, reopened = null;
                        try {
                            var job = jobs[i];
                            if (job.Error != null) throw new InvalidOperationException(job.Error);
                            var geometry = job.Bodies;
                            row.original_solids = geometry.Length;
                            row.original_faces = geometry.Sum(b => b.GetFaceCount());
                            row.source_axis_limits = CombinedLimits(geometry);
                            if (progress != null) progress(String.Format("正在导出并验证 {0} / {1}：{2}", i + 1, jobs.Count, row.body_name));
                            temp = app.NewDocument(template, 0, 0, 0) as IModelDoc2;
                            if (temp == null || initialDocs.Any(d => Object.ReferenceEquals(d, temp))) throw new InvalidOperationException("Temporary document creation failed.");
                            if (Count((IPartDoc)temp, swBodyType_e.swSolidBody) != 0 || Count((IPartDoc)temp, swBodyType_e.swSheetBody) != 0)
                                throw new InvalidOperationException("Template contains geometry; refusing to export extra bodies.");
                            foreach (var body in geometry) {
                                object copy = body.Copy();
                                if (copy == null || ((IPartDoc)temp).CreateFeatureFromBody3(copy, false, (int)swCreateFeatureBodyOpts_e.swCreateFeatureBodyCheck) == null)
                                    throw new InvalidOperationException("Creating imported body feature failed.");
                            }
                            Activate(app, temp);
                            temp.ClearSelection2(true); // Never clear the source document selection.
                            if (File.Exists(row.export_path)) throw new IOException("Output already exists; refusing overwrite.");
                            bool saved = temp.Extension.SaveAs2(row.export_path, (int)swSaveAsVersion_e.swSaveAsCurrentVersion,
                                (int)swSaveAsOptions_e.swSaveAsOptions_Silent, null, "", false, ref row.save_errors, ref row.save_warnings);
                            if (!saved || row.save_errors != 0 || !File.Exists(row.export_path)) throw new IOException("STEP save failed; code " + row.save_errors);
                            row.bytes = new FileInfo(row.export_path).Length;
                            if (row.bytes == 0) throw new IOException("STEP file is empty.");
                            CloseOwned(app, temp, initialDocs, report); temp = null;
                            // A unique output path prevents reusing an already open user document.
                            reopened = app.LoadFile4(row.export_path, "r", null, ref row.load_errors) as IModelDoc2;
                            if (reopened == null || row.load_errors != 0) throw new IOException("STEP reopen failed; code " + row.load_errors);
                            if (reopened.GetType() != (int)swDocumentTypes_e.swDocPART) throw new IOException("Reopened STEP is not a part; validation unavailable.");
                            row.reopened_solids = Count((IPartDoc)reopened, swBodyType_e.swSolidBody);
                            if (row.reopened_solids != row.original_solids || Count((IPartDoc)reopened, swBodyType_e.swSheetBody) != 0)
                                throw new IOException("Solid/sheet counts changed after reimport.");
                            var back = ((object[])((IPartDoc)reopened).GetBodies2((int)swBodyType_e.swSolidBody, false)).Cast<IBody2>().ToArray();
                            row.reopened_faces = back.Sum(b => b.GetFaceCount());
                            row.reopened_axis_limits = CombinedLimits(back);
                            if (assembly && row.reopened_faces != row.original_faces) throw new IOException("Face count changed; review required.");
                            row.max_axis_delta = row.source_axis_limits.Zip(row.reopened_axis_limits, (a, b) => Math.Abs(a - b)).Max();
                            if (row.max_axis_delta > 1e-6) throw new IOException("Axis extents changed beyond 1e-6 m tolerance.");
                            row.status = row.save_warnings == 0 ? "verified" : "verified_with_save_warnings";
                        }
                        catch (Exception ex) { row.status = "failed"; row.error = ex.Message; }
                        finally {
                            CloseOwned(app, reopened, initialDocs, report);
                            CloseOwned(app, temp, initialDocs, report);
                            log.WriteLine(DateTimeOffset.Now.ToString("o") + " " + serializer.Serialize(row)); log.Flush();
                            checkpoint();
                        }
                        Console.WriteLine(row.body_id + ": " + row.status + " " + row.error);
                    }
                }
                finally {
                    try {
                        Activate(app, source);
                        bool sameSelection = selectedObjects.Count == selection.GetSelectedObjectCount2(-1);
                        for (int i = 0; sameSelection && i < selectedObjects.Count; i++)
                            sameSelection = Object.ReferenceEquals(selectedObjects[i], selection.GetSelectedObject6(i + 1, -1)) && selectedMarks[i] == selection.GetSelectedObjectMark(i + 1);
                        report.source_state_preserved = Object.ReferenceEquals(app.ActiveDoc, source) && source.GetSaveFlag() == dirty &&
                            source.GetPathName() == report.source_path && source.ConfigurationManager.ActiveConfiguration.Name == report.configuration && sameSelection;
                        if (assembly && !plan.StateUnchanged()) report.source_state_preserved = false;
                        if (!report.source_state_preserved) report.warnings.Add("Source state differs: inspect document/configuration/selection/dirty flag. No source save or forced restoration performed.");
                    }
                    catch (Exception ex) { report.warnings.Add("Source activation/state check failed: " + ex.Message); }
                    report.finished_at = DateTimeOffset.Now.ToString("o"); checkpoint();
                }
            }
            Console.WriteLine("Source state preserved: " + report.source_state_preserved);
            return report.bodies.All(b => b.status == "verified") && report.source_state_preserved && report.warnings.Count == 0 ? 0 : 2;
        }
        private static List<IModelDoc2> Documents(ISldWorks app)
        { return ((app.GetDocuments() as object[]) ?? new object[0]).Cast<IModelDoc2>().ToList(); }
        private static int Count(IPartDoc part, swBodyType_e type)
        { return ((part.GetBodies2((int)type, false) as object[]) ?? new object[0]).Length; }
        private static void Activate(ISldWorks app, IModelDoc2 model)
        {
            int error = 0;
            var active = app.ActivateDoc3(model.GetTitle(), false, (int)swRebuildOnActivation_e.swDontRebuildActiveDoc, ref error);
            if (!Object.ReferenceEquals(active, model) || !Object.ReferenceEquals(app.ActiveDoc, model))
                throw new InvalidOperationException("Could not activate intended document; code " + error);
        }
        private static void CloseOwned(ISldWorks app, IModelDoc2 doc, List<IModelDoc2> initial, BodyExportReport report)
        {
            if (doc == null) return;
            try {
                if (initial.Any(d => Object.ReferenceEquals(d, doc))) throw new InvalidOperationException("Refusing to close pre-existing document.");
                string title = doc.GetTitle();
                var matches = Documents(app).Where(d => d.GetTitle() == title).ToList();
                if (matches.Count != 1 || !Object.ReferenceEquals(matches[0], doc)) throw new InvalidOperationException("Ambiguous temporary document title.");
                app.CloseDoc(title);
                if (Documents(app).Any(d => Object.ReferenceEquals(d, doc))) throw new InvalidOperationException("Temporary document remains open.");
            }
            catch (Exception ex) { report.warnings.Add("Temporary document cleanup: " + ex.Message); }
        }
        internal static double[] CombinedLimits(IEnumerable<IBody2> bodies)
        {
            var all = bodies.Select(AxisLimits).ToArray();
            return Enumerable.Range(0, 6).Select(i => i < 3 ? all.Min(a => a[i]) : all.Max(a => a[i])).ToArray();
        }
        internal static double[] AxisLimits(IBody2 body)
        {
            var values = new double[6];
            for (int i = 0; i < 6; i++) {
                var dir = new double[3]; dir[i % 3] = i < 3 ? -1 : 1;
                double x, y, z;
                if (!body.GetExtremePoint(dir[0], dir[1], dir[2], out x, out y, out z)) throw new InvalidOperationException("Extreme point query failed.");
                values[i] = new[] { x, y, z }[i % 3];
                if (Double.IsNaN(values[i]) || Double.IsInfinity(values[i])) throw new InvalidOperationException("Invalid extreme point.");
            }
            return values;
        }
    }
}
