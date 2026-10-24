using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace CadPublishKit
{
    internal sealed class ExportGeometry
    {
        public string Name, Configuration, SourcePath, FileName, Error;
        public int Quantity = 1;
        public IBody2[] Bodies;
    }
    internal sealed class AssemblyExportPlan
    {
        public ScanReport Scan;
        public readonly List<ExportGeometry> Jobs = new List<ExportGeometry>();
        public readonly List<string> Errors = new List<string>();
        private readonly List<Func<bool>> stateChecks = new List<Func<bool>>();

        public static AssemblyExportPlan Build(IModelDoc2 model)
        {
            var plan = new AssemblyExportPlan();
            var components = new Dictionary<string, IComponent2>(StringComparer.Ordinal);
            var root = model.ConfigurationManager.ActiveConfiguration.GetRootComponent3(false);
            if (root == null) throw new InvalidOperationException("Assembly root unavailable.");
            plan.Scan = new ScanReport { document_kind = "assembly", configuration = model.ConfigurationManager.ActiveConfiguration.Name };
            plan.Walk(root, null, 0, components);
            plan.Scan.parts = PartDeduplicator.Build(plan.Scan.instances);
            foreach (var part in plan.Scan.parts) {
                var job = new ExportGeometry { Name = part.part_name, Configuration = part.configuration,
                    SourcePath = part.source_path, Quantity = part.instance_count,
                    FileName = ExportNaming.PartFile(part.part_name, part.configuration, part.source_path) };
                plan.Jobs.Add(job);
                part.export_filename = job.FileName;
                try {
                    foreach (var instance in plan.Scan.instances.Where(x => x.part_id == part.part_id)) {
                        if (instance.issues.Count != 0) throw new InvalidOperationException(String.Join("; ", instance.issues));
                        IComponent2 component = components[instance.instance_id];
                        object info, sheetInfo;
                        var bodies = ((component.GetBodies3((int)swBodyType_e.swSolidBody, out info) as object[]) ?? new object[0]).Cast<IBody2>().ToArray();
                        var sheets = component.GetBodies3((int)swBodyType_e.swSheetBody, out sheetInfo) as object[];
                        if (bodies.Length == 0 || (sheets != null && sheets.Length != 0)) throw new InvalidOperationException("Missing solids or unsupported sheet bodies.");
                        var flags = info as Array;
                        if (flags == null || flags.Length != bodies.Length || flags.Cast<object>().Any(x => Convert.ToInt32(x) != (int)swBodyInfo_e.swNormalBody_e))
                            throw new InvalidOperationException("Assembly-modified or unknown body types require review; not merged.");
                        if (job.Bodies == null) job.Bodies = bodies;
                        else if (bodies.Length != job.Bodies.Length || bodies.Sum(b => b.GetFaceCount()) != job.Bodies.Sum(b => b.GetFaceCount()) ||
                            BodyStepExporter.CombinedLimits(bodies).Zip(BodyStepExporter.CombinedLimits(job.Bodies), (a,b) => Math.Abs(a-b)).Max() > 1e-6)
                            throw new InvalidOperationException("Repeated instances have differing local geometry; not merged.");
                    }
                } catch (Exception ex) { job.Error = ex.Message; part.export_issue = ex.Message; }
                if (job.Bodies != null) { part.solid_count = job.Bodies.Length; part.face_count = job.Bodies.Sum(b => b.GetFaceCount()); }
            }
            plan.Scan.warnings.AddRange(plan.Errors);
            return plan;
        }
        private void Walk(IComponent2 parent, string parentId, int depth, Dictionary<string, IComponent2> components)
        {
            if (depth > 256) { Errors.Add("Assembly depth limit reached."); return; }
            object[] children;
            try { children = parent.GetChildren() as object[]; }
            catch (Exception ex) { Errors.Add("Cannot enumerate assembly: " + ex.Message); return; }
            if (children == null) return;
            foreach (IComponent2 component in children) {
                var item = new Instance { instance_id = "instance_" + (Scan.instances.Count + 1), parent_id = parentId, kind = "unknown" };
                Scan.instances.Add(item); components.Add(item.instance_id, component);
                try {
                    item.component_name = component.Name2; item.source_path = component.GetPathName(); item.configuration = component.ReferencedConfiguration;
                    int suppression = component.GetSuppression2(); item.status = ((swComponentSuppressionState_e)suppression).ToString();
                    if (suppression != (int)swComponentSuppressionState_e.swComponentFullyResolved && suppression != (int)swComponentSuppressionState_e.swComponentResolved)
                        throw new InvalidOperationException("Suppressed/lightweight/unavailable component; resolve it first.");
                    if (component.IsVirtual) throw new InvalidOperationException("Virtual component is not supported.");
                    var doc = component.GetModelDoc2() as IModelDoc2;
                    if (doc == null) throw new InvalidOperationException("Component document unavailable.");
                    item.kind = doc.GetType() == (int)swDocumentTypes_e.swDocPART ? "part" : doc.GetType() == (int)swDocumentTypes_e.swDocASSEMBLY ? "assembly" : "unknown";
                    if (item.kind == "unknown" || String.IsNullOrEmpty(item.configuration) || String.IsNullOrEmpty(item.source_path))
                        throw new InvalidOperationException("Component identity unavailable.");
                    var transform = component.Transform2 as IMathTransform;
                    item.transform = transform == null ? null : transform.ArrayData as double[];
                    if (item.transform == null || item.transform.Length != 16 || item.transform.Any(v => Double.IsNaN(v) || Double.IsInfinity(v)))
                        throw new InvalidOperationException("Component placement unavailable.");
                    bool dirty = doc.GetSaveFlag(); string config = doc.ConfigurationManager.ActiveConfiguration.Name;
                    string path = doc.GetPathName(); string reference = item.configuration;
                    double[] original = (double[])item.transform.Clone();
                    stateChecks.Add(() => doc.GetSaveFlag() == dirty && doc.GetPathName() == path && doc.ConfigurationManager.ActiveConfiguration.Name == config &&
                        component.ReferencedConfiguration == reference && component.GetSuppression2() == suppression &&
                        ((double[])component.Transform2.ArrayData).SequenceEqual(original));
                    if (item.kind == "assembly") Walk(component, item.instance_id, depth + 1, components);
                } catch (Exception ex) { item.issues.Add(ex.Message); Errors.Add(item.component_name + ": " + ex.Message); }
            }
        }
        public bool StateUnchanged() { return stateChecks.All(check => check()); }
    }
    public static class ExportNaming
    {
        public static string PartFile(string name, string configuration, string path)
        {
            string stem = name + "_" + configuration;
            stem = new string(stem.Select(c => c < 32 || "<>:\"/\\|?*".Contains(c) ? '_' : c).ToArray()).Trim(' ', '.');
            if (stem.Length > 80) stem = stem.Substring(0, 80);
            using (var sha = SHA256.Create()) {
                string hash = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(Path.GetFullPath(path).ToUpperInvariant() + "\0" + configuration))).Replace("-", "").Substring(0, 12).ToLowerInvariant();
                return "part_" + stem + "_" + hash + ".step";
            }
        }
    }
}
