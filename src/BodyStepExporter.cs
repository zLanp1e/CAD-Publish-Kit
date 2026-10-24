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
    }
    public sealed class BodyExportReport
    {
        public string started_at = DateTimeOffset.Now.ToString("o"), finished_at, source_title, source_path, configuration, revision, template;
        public string validation = "Reopen in SolidWorks, require one solid and no sheets; compare six axis extrema in model coordinates within 1e-6 m. Not a full shape-equivalence test.";
        public string options = "SaveAs2: current version, silent, no selection, existing STEP preferences unchanged. Copy body into separate temporary document without applying transforms.";
        public int step_ap;
        public bool source_state_preserved;
        public List<string> warnings = new List<string>();
        public List<BodyExportResult> bodies = new List<BodyExportResult>();
    }
    public static class BodyStepExporter
    {
        public static int Run(ISldWorks app, string outputParent, string templateOverride)
        {
            var source = app.ActiveDoc as IModelDoc2;
            if (source == null || source.GetType() != (int)swDocumentTypes_e.swDocPART)
                throw new InvalidOperationException("Body export requires an active part document.");
            var bodies = ((IPartDoc)source).GetBodies2((int)swBodyType_e.swSolidBody, false) as object[];
            if (bodies == null || bodies.Length == 0) throw new InvalidOperationException("No solid bodies to export.");
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
                for (int i = 0; i < bodies.Length; i++) {
                    report.bodies.Add(new BodyExportResult { body_id = "body_" + (i + 1),
                        export_path = Path.Combine(run, "parts", "body_" + (i + 1).ToString("D3") + ".step") });
                }
                checkpoint();
                Console.WriteLine("Output: " + run);
                try {
                    for (int i = 0; i < bodies.Length; i++) {
                        BodyExportResult row = report.bodies[i];
                        IModelDoc2 temp = null, reopened = null;
                        try {
                            var body = (IBody2)bodies[i];
                            row.body_name = body.Name;
                            row.original_faces = body.GetFaceCount();
                            row.source_axis_limits = AxisLimits(body);
                            Console.WriteLine("Exporting {0}/{1}: {2}", i + 1, bodies.Length, row.body_name);
                            object copy = body.Copy();
                            if (copy == null) throw new InvalidOperationException("Body copy failed.");
                            temp = app.NewDocument(template, 0, 0, 0) as IModelDoc2;
                            if (temp == null || initialDocs.Any(d => Object.ReferenceEquals(d, temp))) throw new InvalidOperationException("Temporary document creation failed.");
                            if (Count((IPartDoc)temp, swBodyType_e.swSolidBody) != 0 || Count((IPartDoc)temp, swBodyType_e.swSheetBody) != 0)
                                throw new InvalidOperationException("Template contains geometry; refusing to export extra bodies.");
                            if (((IPartDoc)temp).CreateFeatureFromBody3(copy, false, (int)swCreateFeatureBodyOpts_e.swCreateFeatureBodyCheck) == null)
                                throw new InvalidOperationException("Creating imported body feature failed.");
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
                            if (Count((IPartDoc)reopened, swBodyType_e.swSolidBody) != 1 || Count((IPartDoc)reopened, swBodyType_e.swSheetBody) != 0)
                                throw new IOException("Expected exactly one solid and no sheets after reimport.");
                            var back = (IBody2)((object[])((IPartDoc)reopened).GetBodies2((int)swBodyType_e.swSolidBody, false))[0];
                            row.reopened_faces = back.GetFaceCount();
                            row.reopened_axis_limits = AxisLimits(back);
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
        private static double[] AxisLimits(IBody2 body)
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
