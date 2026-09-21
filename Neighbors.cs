using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;

namespace RetainingWallFormwork
{
    /// <summary>Que tipo de elemento vecino toca una cara. Decide la regla constructiva que se aplica.</summary>
    public enum NeighborKind
    {
        Wall,
        Foundation,
        Slab,
        Column,
        Beam,
        Other
    }

    /// <summary>Un elemento cercano al muro con sus solidos, listo para las comprobaciones de contacto.</summary>
    public sealed class Neighbor
    {
        public Element Element;
        public NeighborKind Kind;
        public List<Solid> Solids = new List<Solid>();
        /// <summary>false si su material es claramente distinto de concreto (se ignora si OnlyConcreteNeighbors).</summary>
        public bool IsConcrete = true;
        /// <summary>true si el elemento esta unido (Unir geometria) con el muro analizado.</summary>
        public bool Joined;

        public string Label => "[" + Element.Id.Value + " " + Element.Name + "]";

        public string KindText
        {
            get
            {
                switch (Kind)
                {
                    case NeighborKind.Wall: return "muro";
                    case NeighborKind.Foundation: return "cimentacion";
                    case NeighborKind.Slab: return "losa";
                    case NeighborKind.Column: return "columna";
                    case NeighborKind.Beam: return "viga";
                    default: return "otro";
                }
            }
        }

        /// <summary>
        /// Con la configuracion dada, el contacto con este vecino se descuenta del
        /// encofrado de una cara con el rol indicado (ver AppConfig.WallContactRule y
        /// SlabContactRule). <paramref name="stops"/> = la cara es un extremo, una jamba o
        /// un fondo: el muro se detiene contra el vecino.
        /// </summary>
        public bool Discounts(AppConfig cfg, bool stops)
        {
            if (cfg.OnlyConcreteNeighbors && !IsConcrete) return false;
            int rule = IsSlab ? cfg.SlabRuleIndex : cfg.WallRuleIndex;
            if (rule == 1) return true;
            if (rule == 2) return false;
            // auto: unidos = monolitico; sin unir = el que se detiene se vacia despues
            if (Joined) return true;
            return IsSlab ? false : stops;
        }

        /// <summary>Texto corto del motivo por el que se descuenta o no (para la ventana).</summary>
        public string RuleText(AppConfig cfg, bool stops)
        {
            if (cfg.OnlyConcreteNeighbors && !IsConcrete) return "no es concreto: se encofra igual";
            int rule = IsSlab ? cfg.SlabRuleIndex : cfg.WallRuleIndex;
            if (rule == 1) return "se descuenta (regla: siempre)";
            if (rule == 2) return "no se descuenta (regla: nunca)";
            if (Joined) return "unido: vaciado monolitico, no se encofra";
            if (IsSlab) return "losa sin unir: se vacia despues del muro, la cara se encofra completa";
            return stops ? "sin unir: el muro se detiene contra el, se vacia despues y no encofra el contacto"
                         : "sin unir: el otro se detiene contra este muro, esta cara se encofra completa";
        }

        /// <summary>El contacto con este vecino se informa como "losa" (columna propia en la tabla).</summary>
        public bool IsSlab => Kind == NeighborKind.Slab;
    }

    /// <summary>Busca los elementos que pueden estar en contacto con un muro.</summary>
    public static class Neighbors
    {
        /// <summary>Elementos cuya caja envolvente toca la del muro (ampliada con la holgura), con sus solidos.</summary>
        public static List<Neighbor> Around(Document doc, Element host, AppConfig cfg)
        {
            var list = new List<Neighbor>();
            BoundingBoxXYZ bb = host.get_BoundingBox(null);
            if (bb == null) return list;

            double pad = Geo.Mm(cfg.ContactToleranceMm) + Geo.Mm(10);
            var outline = new Outline(bb.Min - new XYZ(pad, pad, pad), bb.Max + new XYZ(pad, pad, pad));

            var cats = new List<BuiltInCategory>
            {
                BuiltInCategory.OST_Walls,
                BuiltInCategory.OST_StructuralFoundation,
                BuiltInCategory.OST_Floors,
                BuiltInCategory.OST_StructuralColumns,
                BuiltInCategory.OST_Columns,
                BuiltInCategory.OST_StructuralFraming
            };
            if (cfg.NeighborGenericModels) cats.Add(BuiltInCategory.OST_GenericModel);

            HashSet<long> joined = JoinedIds(doc, host);

            var collector = new FilteredElementCollector(doc)
                .WhereElementIsNotElementType()
                .WherePasses(new ElementMulticategoryFilter(cats))
                .WherePasses(new BoundingBoxIntersectsFilter(outline));

            foreach (Element e in collector)
            {
                if (e.Id == host.Id) continue;
                if (e.Category == null) continue;
                var n = new Neighbor
                {
                    Element = e,
                    Kind = KindOf(e),
                    Solids = Geo.Solids(e),
                    IsConcrete = IsConcrete(doc, e),
                    Joined = joined.Contains(e.Id.Value)
                };
                if (n.Solids.Count == 0) continue;
                list.Add(n);
            }
            return list;
        }

        private static HashSet<long> JoinedIds(Document doc, Element host)
        {
            var set = new HashSet<long>();
            try
            {
                foreach (ElementId id in JoinGeometryUtils.GetJoinedElements(doc, host)) set.Add(id.Value);
            }
            catch { }
            return set;
        }

        public static NeighborKind KindOf(Element e)
        {
            long bic = e.Category.Id.Value;
            if (bic == (long)BuiltInCategory.OST_Walls) return NeighborKind.Wall;
            if (bic == (long)BuiltInCategory.OST_StructuralFoundation) return NeighborKind.Foundation;
            if (bic == (long)BuiltInCategory.OST_Floors) return NeighborKind.Slab;
            if (bic == (long)BuiltInCategory.OST_StructuralColumns || bic == (long)BuiltInCategory.OST_Columns) return NeighborKind.Column;
            if (bic == (long)BuiltInCategory.OST_StructuralFraming) return NeighborKind.Beam;
            return NeighborKind.Other;
        }

        /// <summary>
        /// True si el material del elemento es concreto, o si no se puede saber. Solo se
        /// devuelve false cuando hay materiales legibles y ninguno parece concreto
        /// (albanileria, acero, madera...): esos elementos se construyen despues del
        /// muro y no le quitan encofrado.
        /// </summary>
        public static bool IsConcrete(Document doc, Element e)
        {
            try
            {
                if (e is FamilyInstance fi)
                {
                    StructuralMaterialType smt = fi.StructuralMaterialType;
                    if (smt == StructuralMaterialType.Concrete || smt == StructuralMaterialType.PrecastConcrete) return true;
                    if (smt == StructuralMaterialType.Steel || smt == StructuralMaterialType.Wood || smt == StructuralMaterialType.Aluminum)
                        return false;
                }

                var names = new List<string>();
                foreach (ElementId id in e.GetMaterialIds(false))
                {
                    if (doc.GetElement(id) is Material m)
                    {
                        names.Add(m.Name ?? "");
                        names.Add(m.MaterialClass ?? "");
                    }
                }
                if (names.Count == 0) return true;   // sin material legible: se asume concreto
                foreach (string n in names)
                    if (LooksConcrete(n)) return true;
                return false;
            }
            catch { return true; }
        }

        private static bool LooksConcrete(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return false;
            string t = s.ToLowerInvariant();
            return t.Contains("concret") || t.Contains("hormig") || t.Contains("beton") || t.Contains("f'c") || t.Contains("fc=");
        }
    }
}
