using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace RetainingWallFormwork
{
    /// <summary>
    /// Tabla de planificacion (multicategoria) con el metrado de encofrado: una fila por
    /// elemento metrado, columnas por partida y totales al pie. Se crea una vez y en las
    /// ejecuciones siguientes se reutiliza (se anaden solo los campos que falten, para
    /// respetar el formato que el usuario le haya dado).
    /// </summary>
    public static class FormworkSchedule
    {
        /// <summary>Busca la tabla por nombre.</summary>
        public static ViewSchedule Find(Document doc, string name)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSchedule))
                .Cast<ViewSchedule>()
                .FirstOrDefault(v => !v.IsTemplate && string.Equals(v.Name, name, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>Crea o actualiza la tabla. Dentro de una transaccion. Devuelve la tabla y avisos.</summary>
        public static ViewSchedule Ensure(Document doc, AppConfig cfg, List<string> warnings)
        {
            ViewSchedule vs = Find(doc, cfg.ScheduleName);
            bool created = false;
            if (vs == null)
            {
                vs = ViewSchedule.CreateSchedule(doc, ElementId.InvalidElementId);   // multicategoria
                created = true;
                try { vs.Name = cfg.ScheduleName; }
                catch (Exception ex) { warnings.Add("no se pudo poner nombre a la tabla: " + ex.Message); }
            }

            ScheduleDefinition def = vs.Definition;
            IList<SchedulableField> avail = def.GetSchedulableFields();

            // campos ya presentes
            var present = new Dictionary<long, ScheduleField>();
            for (int i = 0; i < def.GetFieldCount(); i++)
            {
                ScheduleField f = def.GetField(i);
                try { present[f.ParameterId.Value] = f; } catch { }
            }

            ScheduleField AddIfMissing(ElementId paramId, string heading, bool sum, ForgeTypeId unit)
            {
                if (present.TryGetValue(paramId.Value, out ScheduleField have)) return have;
                SchedulableField sf = avail.FirstOrDefault(x => x.ParameterId.Value == paramId.Value && x.FieldType == ScheduleFieldType.Instance)
                                      ?? avail.FirstOrDefault(x => x.ParameterId.Value == paramId.Value);
                if (sf == null) { warnings.Add("la tabla no admite el campo '" + heading + "'"); return null; }
                ScheduleField f = def.AddField(sf);
                present[paramId.Value] = f;
                try { f.ColumnHeading = heading; } catch { }
                if (sum) { try { f.DisplayType = ScheduleFieldDisplayType.Totals; } catch { } }
                if (unit != null)
                {
                    try
                    {
                        var fo = new FormatOptions(unit) { Accuracy = 0.01 };
                        f.SetFormatOptions(fo);
                    }
                    catch { }
                }
                return f;
            }

            ScheduleField famType = AddIfMissing(new ElementId(BuiltInParameter.ELEM_FAMILY_AND_TYPE_PARAM), "Familia y tipo", false, null);
            ScheduleField mark = AddIfMissing(new ElementId(BuiltInParameter.ALL_MODEL_MARK), "Marca", false, null);
            ScheduleField totalField = null;
            foreach (FormworkParam p in FormworkParameters.All)
            {
                SharedParameterElement spe = SharedParameterElement.Lookup(doc, p.Guid);
                if (spe == null) { warnings.Add("falta el parametro compartido '" + p.Name + "'"); continue; }
                ForgeTypeId unit = p.Spec == SpecTypeId.Area ? UnitTypeId.SquareMeters : p.Spec == SpecTypeId.Length ? UnitTypeId.Meters : null;
                ScheduleField f = AddIfMissing(spe.Id, p.Heading, p.Sum, unit);
                if (p == FormworkParameters.Total) totalField = f;
            }

            if (created)
            {
                // solo los elementos metrados, ordenados por marca, con totales al pie
                try
                {
                    if (totalField != null) def.AddFilter(new ScheduleFilter(totalField.FieldId, ScheduleFilterType.HasValue));
                }
                catch (Exception ex) { warnings.Add("no se pudo filtrar la tabla: " + ex.Message); }
                try
                {
                    if (mark != null) def.AddSortGroupField(new ScheduleSortGroupField(mark.FieldId, ScheduleSortOrder.Ascending));
                    else if (famType != null) def.AddSortGroupField(new ScheduleSortGroupField(famType.FieldId, ScheduleSortOrder.Ascending));
                }
                catch (Exception ex) { warnings.Add("no se pudo ordenar la tabla: " + ex.Message); }
                try
                {
                    def.IsItemized = true;
                    def.ShowGrandTotal = true;
                    def.ShowGrandTotalTitle = true;
                    def.ShowGrandTotalCount = false;
                    def.GrandTotalTitle = "TOTAL";
                }
                catch (Exception ex) { warnings.Add("no se pudieron activar los totales: " + ex.Message); }
            }
            return vs;
        }
    }
}
