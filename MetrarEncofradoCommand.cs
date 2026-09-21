using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Interop;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;

namespace RetainingWallFormwork
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class MetrarEncofradoCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;

            AppConfig cfg;
            try { cfg = AppConfig.Load(); }
            catch (Exception ex)
            {
                message = "No se pudo leer config.json (" + AppConfig.ConfigPath() + "): " + ex.Message;
                return Result.Failed;
            }

            IList<Element> hosts;
            try { hosts = GetHosts(uidoc, cfg); }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }

            if (hosts.Count == 0)
            {
                message = "No se selecciono ningun muro de contencion.";
                return Result.Cancelled;
            }

            // --- 1. Analisis geometrico de cada elemento (solo lectura, sin transaccion) ---
            Func<AppConfig, List<FormworkAnalysis>> analyze = c => hosts.Select(h => FormworkAnalysis.Analyze(doc, h, c)).ToList();
            List<FormworkAnalysis> items = analyze(cfg);

            // --- 2. Ventana: el usuario revisa caras, contactos y reglas ---
            var win = new FormworkOptionsWindow(cfg.Clone(), items, analyze);
            try { new WindowInteropHelper(win).Owner = commandData.Application.MainWindowHandle; } catch { }
            bool? ok = win.ShowDialog();
            if (ok != true || win.Result == null) return Result.Cancelled;
            cfg = win.Result;
            items = win.Items;

            if (!cfg.WriteParameters && !cfg.CreateSchedule)
            {
                TaskDialog.Show("Metrado de encofrado", "No se ha pedido escribir parametros ni crear la tabla: no se ha cambiado nada en el modelo.\n\n" + Summary(items, cfg));
                return Result.Succeeded;
            }

            // --- 3. Escritura: parametros y tabla ---
            var log = new List<string>();
            var warnings = new List<string>();
            int written = 0, rejected = 0;
            double grand = 0;
            ViewSchedule schedule = null;
            string stamp = FormworkParameters.StampText(cfg);

            using (Transaction tx = new Transaction(doc, "Metrar encofrado de muros de contencion"))
            {
                tx.Start();
                try
                {
                    warnings.AddRange(FormworkParameters.Ensure(doc, cfg));
                }
                catch (Exception ex)
                {
                    tx.RollBack();
                    message = "No se pudieron crear los parametros compartidos: " + ex.Message;
                    return Result.Failed;
                }

                foreach (FormworkAnalysis item in items)
                {
                    if (!item.CanBuild)
                    {
                        rejected++;
                        log.Add(item.Tag + "SIN METRAR -> " + item.Error);
                        continue;
                    }
                    Totals t = item.Compute(cfg);
                    if (cfg.WriteParameters)
                    {
                        try { FormworkParameters.Write(item, t, cfg, stamp); }
                        catch (Exception ex)
                        {
                            rejected++;
                            log.Add(item.Tag + "SIN ESCRIBIR -> " + ex.Message);
                            continue;
                        }
                    }
                    written++;
                    grand += t.Total;
                    log.Add(item.Tag + Line(t));
                }

                if (cfg.CreateSchedule)
                {
                    try { schedule = FormworkSchedule.Ensure(doc, cfg, warnings); }
                    catch (Exception ex) { warnings.Add("no se pudo crear la tabla: " + ex.Message); }
                }
                tx.Commit();
            }

            var td = new TaskDialog("Metrado de encofrado")
            {
                MainInstruction = Geo.M2(grand) + " m2 de encofrado en " + written + " de " + hosts.Count + " elemento(s)" +
                                  (schedule != null ? ". Tabla: " + schedule.Name : ""),
                MainContent = string.Join(Environment.NewLine, log)
            };
            if (rejected > 0 || warnings.Count > 0)
            {
                td.MainIcon = TaskDialogIcon.TaskDialogIconWarning;
                if (rejected > 0)
                    td.MainInstruction += Environment.NewLine + "ATENCION: " + rejected + " elemento(s) sin metrar (ver detalle).";
                if (warnings.Count > 0)
                    td.ExpandedContent = "Avisos:" + Environment.NewLine + string.Join(Environment.NewLine, warnings.Select(w => "- " + w));
            }
            td.Show();

            if (schedule != null && cfg.OpenSchedule)
            {
                try { uidoc.ActiveView = schedule; } catch { }
            }
            return Result.Succeeded;
        }

        private static string Line(Totals t)
        {
            return "trasdos " + Geo.M2(t.StemBack) + " + intrados " + Geo.M2(t.StemFront) + " + extremos " + Geo.M2(t.StemEnds) +
                   " + vanos " + Geo.M2(t.Openings) + " + zapata " + Geo.M2(t.FootingSides) + " = " + Geo.M2(t.Total) + " m2" +
                   (t.DiscountOthers > 0 || t.DiscountSlabs > 0
                       ? " (descontado: muros " + Geo.M2(t.DiscountOthers) + ", losas " + Geo.M2(t.DiscountSlabs) + " m2)"
                       : "") +
                   (t.Lifts > 1 ? ", " + t.Lifts + " vaciados" : "");
        }

        /// <summary>Resumen de texto (tambien lo usa la ventana para copiar al portapapeles).</summary>
        public static string Summary(IList<FormworkAnalysis> items, AppConfig cfg)
        {
            var lines = new List<string>();
            double grand = 0;
            foreach (FormworkAnalysis a in items)
            {
                if (!a.CanBuild) { lines.Add(a.Tag + "SIN METRAR -> " + a.Error); continue; }
                Totals t = a.Compute(cfg);
                grand += t.Total;
                lines.Add(a.Tag + Line(t));
            }
            lines.Add("TOTAL: " + Geo.M2(grand) + " m2");
            return string.Join(Environment.NewLine, lines);
        }

        private static IList<Element> GetHosts(UIDocument uidoc, AppConfig cfg)
        {
            Document doc = uidoc.Document;
            var allowed = FormworkParameters.HostCategories(cfg).Select(c => (long)c).ToList();

            bool IsCandidate(Element e) => e != null && e.Category != null && allowed.Contains(e.Category.Id.Value);

            var sel = uidoc.Selection.GetElementIds()
                .Select(id => doc.GetElement(id))
                .Where(IsCandidate)
                .ToList();
            if (sel.Count > 0) return sel;

            IList<Reference> refs = uidoc.Selection.PickObjects(
                ObjectType.Element, new HostFilter(allowed),
                "Selecciona los muros de contencion a metrar y pulsa Finalizar");

            return refs.Select(r => doc.GetElement(r)).ToList();
        }

        private class HostFilter : ISelectionFilter
        {
            private readonly List<long> _allowed;
            public HostFilter(List<long> allowed) { _allowed = allowed; }
            public bool AllowElement(Element e) => e != null && e.Category != null && _allowed.Contains(e.Category.Id.Value);
            public bool AllowReference(Reference r, XYZ p) => false;
        }
    }
}
