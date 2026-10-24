using System;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace CadPublishKit
{
    public static class ModelScanner
    {
        public static ScanReport Scan(ISldWorks app)
        {
            var model = app.ActiveDoc as IModelDoc2;
            if (model == null) throw new InvalidOperationException("Open a model in SolidWorks first.");
            if (model.GetType() == (int)swDocumentTypes_e.swDocASSEMBLY)
                return new AssemblyScanner().Scan(app);
            if (model.GetType() != (int)swDocumentTypes_e.swDocPART)
                throw new InvalidOperationException("Only assembly and part documents are supported.");
            return BodyScanner.Scan(app, model);
        }
    }
    public static class BodyScanner
    {
        public static ScanReport Scan(ISldWorks app, IModelDoc2 model)
        {
            var report = new ScanReport {
                mode = "real_com_body_scan", document_kind = "part_bodies",
                document_title = model.GetTitle(), source_path = model.GetPathName(),
                configuration = model.ConfigurationManager.ActiveConfiguration.Name,
                revision = app.RevisionNumber()
            };
            bool dirty = model.GetSaveFlag();
            var selection = (ISelectionMgr)model.SelectionManager;
            int selected = selection.GetSelectedObjectCount2(-1);
            report.warnings.Add("Body inventory only: bodies are not original component files or BOM quantities. No geometric deduplication or export verification. IDs are scan-local.");
            if (String.IsNullOrEmpty(report.source_path))
                report.warnings.Add("Active document is unsaved; original STEP path cannot be inferred from its title.");
            try {
                var part = (IPartDoc)model;
                ReadBodies(part, swBodyType_e.swSolidBody, "solid", report);
                ReadBodies(part, swBodyType_e.swSheetBody, "sheet", report);
                if (report.bodies.Count == 0) report.warnings.Add("No solid or sheet bodies found; other geometry types are not scanned.");
            }
            finally {
                try {
                    report.state_check_passed = model.GetSaveFlag() == dirty &&
                        model.ConfigurationManager.ActiveConfiguration.Name == report.configuration &&
                        selection.GetSelectedObjectCount2(-1) == selected &&
                        Object.ReferenceEquals(app.ActiveDoc, model);
                    if (report.state_check_passed != true)
                        report.warnings.Add("Active document, configuration, dirty flag or selection count changed; no restoration claimed. Inspect SolidWorks.");
                }
                catch (Exception ex) { report.state_check_passed = false; report.warnings.Add("State check failed: " + ex.Message); }
            }
            return report;
        }
        private static void ReadBodies(IPartDoc part, swBodyType_e type, string label, ScanReport report)
        {
            object[] bodies;
            try { bodies = part.GetBodies2((int)type, false) as object[]; }
            catch (Exception ex) { report.warnings.Add(label + " enumeration failed: " + ex.Message); return; }
            if (bodies == null) return;
            foreach (var raw in bodies)
            {
                var item = new BodyRecord { body_id = "body_" + (report.bodies.Count + 1), body_type = label, status = "read_failed" };
                report.bodies.Add(item);
                try {
                    var body = (IBody2)raw;
                    try { item.body_name = body.Name; }
                    catch (Exception ex) { item.issues.Add("Name: " + ex.Message); }
                    item.face_count = body.GetFaceCount();
                    if (item.face_count <= 0) item.issues.Add("No accessible faces; inspect geometry.");
                    item.status = item.issues.Count == 0 ? "readable" : "needs_review";
                }
                catch (Exception ex) { item.issues.Add(ex.Message); }
            }
        }
    }
}
