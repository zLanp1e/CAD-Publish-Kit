using System;
using System.IO;
using System.Runtime.InteropServices;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace CadPublishKit
{
    public static class SolidWorksConnector
    {
        public static ISldWorks Connect()
        { return (ISldWorks)Marshal.GetActiveObject("SldWorks.Application"); }
    }
    public sealed class AssemblyScanner
    {
        private readonly ScanReport report = new ScanReport();
        public ScanReport Scan(ISldWorks app)
        {
            IModelDoc2 model = app.ActiveDoc as IModelDoc2;
            if (model == null) throw new InvalidOperationException("Open an assembly in SolidWorks first.");
            int documentType = model.GetType();
            if (documentType != (int)swDocumentTypes_e.swDocASSEMBLY)
                throw new InvalidOperationException(String.Format(
                    "Active document is not an assembly. Type={0} ({1}); Title={2}; Path={3}; SolidWorks revision={4}. STEP may have imported as a part or multibody part.",
                    documentType, (swDocumentTypes_e)documentType, model.GetTitle(), model.GetPathName(), app.RevisionNumber()));
            IConfiguration config = model.ConfigurationManager.ActiveConfiguration;
            string originalConfig = config.Name;
            bool originalDirty = model.GetSaveFlag();
            report.revision = app.RevisionNumber();
            report.assembly_path = model.GetPathName(); report.configuration = originalConfig;
            report.warnings.Add("Observed instances only. Suppressed/unloaded subassembly descendants may be unavailable. No geometry or transforms validated.");
            try {
                IComponent2 root = config.GetRootComponent3(false);
                if (root == null) throw new InvalidOperationException("Active configuration root is unavailable.");
                Walk(root, null, 0);
            }
            finally {
                if (model.ConfigurationManager.ActiveConfiguration.Name != originalConfig || model.GetSaveFlag() != originalDirty)
                    report.warnings.Add("Document state changed during scan; not restored. Inspect SolidWorks. No save was requested.");
            }
            report.parts = PartDeduplicator.Build(report.instances);
            return report;
        }
        private void Walk(IComponent2 parent, string parentId, int depth)
        {
            if (depth > 256) { report.warnings.Add("Depth limit reached at " + parentId); return; }
            object[] children;
            try { children = parent.GetChildren() as object[]; }
            catch (Exception ex) { report.warnings.Add("Children unavailable at " + parentId + ": " + ex.Message); return; }
            if (children == null) return;
            foreach (object raw in children)
            {
                var item = new Instance { instance_id = "instance_" + (report.instances.Count + 1), parent_id = parentId, kind = "unknown" };
                report.instances.Add(item);
                try {
                    var component = (IComponent2)raw;
                    Read(item, "name", () => item.component_name = component.Name2);
                    Read(item, "path", () => item.source_path = component.GetPathName());
                    Read(item, "configuration", () => item.configuration = component.ReferencedConfiguration);
                    Read(item, "status", () => {
                        int state = component.GetSuppression2();
                        item.status = state + ":" + ((swComponentSuppressionState_e)state).ToString();
                        if (state != (int)swComponentSuppressionState_e.swComponentFullyResolved && state != (int)swComponentSuppressionState_e.swComponentResolved)
                            item.issues.Add("Suppressed, lightweight, or unavailable component; inspect coverage.");
                    });
                    bool isVirtual = false;
                    Read(item, "virtual", () => isVirtual = component.IsVirtual);
                    string ext = Path.GetExtension(item.source_path ?? "");
                    item.kind = ext.Equals(".sldprt", StringComparison.OrdinalIgnoreCase) ? "part" :
                        ext.Equals(".sldasm", StringComparison.OrdinalIgnoreCase) ? "assembly" : "unknown";
                    if (isVirtual) { item.kind = "virtual"; item.issues.Add("Virtual component is not deduplicated in stage one."); }
                    if (String.IsNullOrWhiteSpace(item.source_path) || !File.Exists(item.source_path)) item.issues.Add("Source path missing or inaccessible.");
                    if (String.IsNullOrWhiteSpace(item.configuration)) item.issues.Add("Referenced configuration missing.");
                    Read(item, "loaded model", () => { if (component.GetModelDoc2() == null) item.issues.Add("Model is not loaded; geometry not verified."); });
                    Walk(component, item.instance_id, depth + 1);
                }
                catch (Exception ex) { item.issues.Add("Component read failed: " + ex.Message); }
            }
        }
        private static void Read(Instance item, string field, Action action)
        { try { action(); } catch (Exception ex) { item.issues.Add(field + ": " + ex.Message); } }
    }
}
