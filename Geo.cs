using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace RetainingWallFormwork
{
    /// <summary>
    /// Utilidades geometricas sobre solidos de Revit. Todas las magnitudes en pies
    /// (unidades internas); las conversiones a mm / m2 estan aqui mismo.
    /// </summary>
    public static class Geo
    {
        /// <summary>1 pie = 304.8 mm exactos.</summary>
        public const double MmPerFt = 304.8;

        public static double Mm(double mm) => mm / MmPerFt;
        public static double ToMm(double ft) => Math.Round(ft * MmPerFt);
        public static double ToM(double ft) => ft * MmPerFt / 1000.0;
        public static double ToM2(double ft2) => ft2 * (MmPerFt / 1000.0) * (MmPerFt / 1000.0);
        public static double FromM2(double m2) => m2 / ((MmPerFt / 1000.0) * (MmPerFt / 1000.0));
        public static string M2(double ft2) => ToM2(ft2).ToString("0.00");

        // ------------------------------------------------------------------
        // Solidos de un elemento
        // ------------------------------------------------------------------

        private static Options GeometryOptions() =>
            new Options { DetailLevel = ViewDetailLevel.Fine, ComputeReferences = false, IncludeNonVisibleObjects = false };

        /// <summary>
        /// Todos los solidos con volumen del elemento tal y como los ve Revit (con uniones y
        /// cortes aplicados: es el concreto real que se va a vaciar), de mayor a menor.
        /// </summary>
        public static List<Solid> Solids(Element e)
        {
            var list = new List<Solid>();
            GeometryElement ge;
            try { ge = e.get_Geometry(GeometryOptions()); }
            catch { return list; }
            if (ge == null) return list;

            void Scan(IEnumerable<GeometryObject> objs)
            {
                foreach (GeometryObject go in objs)
                {
                    if (go is Solid sol) { if (sol.Volume > 1e-9) list.Add(sol); }
                    else if (go is GeometryInstance gi) Scan(gi.GetInstanceGeometry());
                }
            }
            Scan(ge);
            return list.OrderByDescending(x => x.Volume).ToList();
        }

        /// <summary>Vertices (teselados) de todas las aristas del solido.</summary>
        public static List<XYZ> Vertices(Solid s)
        {
            var pts = new List<XYZ>();
            foreach (Edge ed in s.Edges)
                foreach (XYZ p in ed.Tessellate()) pts.Add(p);
            return pts;
        }

        /// <summary>Vertices (teselados) de todas las aristas de la cara.</summary>
        public static List<XYZ> Vertices(Face f)
        {
            var pts = new List<XYZ>();
            foreach (EdgeArray loop in f.EdgeLoops)
                foreach (Edge ed in loop)
                    foreach (XYZ p in ed.Tessellate()) pts.Add(p);
            return pts;
        }

        /// <summary>Caja envolvente de una lista de puntos (null si esta vacia).</summary>
        public static BoundingBoxXYZ Box(IList<XYZ> pts)
        {
            if (pts == null || pts.Count == 0) return null;
            var bb = new BoundingBoxXYZ
            {
                Min = new XYZ(pts.Min(p => p.X), pts.Min(p => p.Y), pts.Min(p => p.Z)),
                Max = new XYZ(pts.Max(p => p.X), pts.Max(p => p.Y), pts.Max(p => p.Z))
            };
            return bb;
        }

        /// <summary>Punto medio de los vertices de una cara (centro de gravedad aproximado).</summary>
        public static XYZ Centroid(Face f)
        {
            List<XYZ> pts = Vertices(f);
            if (pts.Count == 0) return XYZ.Zero;
            double x = 0, y = 0, z = 0;
            foreach (XYZ p in pts) { x += p.X; y += p.Y; z += p.Z; }
            return new XYZ(x / pts.Count, y / pts.Count, z / pts.Count);
        }

        /// <summary>Normal exterior de la cara en su centro parametrico (unitaria).</summary>
        public static XYZ Normal(Face f)
        {
            if (f is PlanarFace pf) return pf.FaceNormal.Normalize();
            try
            {
                BoundingBoxUV bb = f.GetBoundingBox();
                var uv = new UV((bb.Min.U + bb.Max.U) * 0.5, (bb.Min.V + bb.Max.V) * 0.5);
                return f.ComputeNormal(uv).Normalize();
            }
            catch { return XYZ.BasisZ; }
        }

        /// <summary>Componente horizontal unitaria de un vector, o null si es (casi) vertical.</summary>
        public static XYZ Flat(XYZ v)
        {
            var f = new XYZ(v.X, v.Y, 0);
            return f.GetLength() < 1e-6 ? null : f.Normalize();
        }

        // ------------------------------------------------------------------
        // Booleanas
        // ------------------------------------------------------------------

        /// <summary>Interseccion de dos solidos; null si esta vacia o falla la operacion.</summary>
        public static Solid Intersect(Solid a, Solid b)
        {
            if (a == null || b == null) return null;
            try
            {
                Solid r = BooleanOperationsUtils.ExecuteBooleanOperation(a, b, BooleanOperationsType.Intersect);
                return (r != null && r.Volume > 1e-9) ? r : null;
            }
            catch { return null; }
        }

        /// <summary>
        /// Caja alineada con los ejes del proyecto entre dos cotas z. Se usa como
        /// "rebanada horizontal" y como semiespacio (por debajo / por encima de un plano).
        /// </summary>
        public static Solid ZBox(double xMin, double xMax, double yMin, double yMax, double z0, double z1)
        {
            if (z1 <= z0 || xMax <= xMin || yMax <= yMin) return null;
            var loop = new CurveLoop();
            // antihorario visto desde +Z: su normal apunta como la direccion de extrusion
            var p0 = new XYZ(xMin, yMin, z0);
            var p1 = new XYZ(xMax, yMin, z0);
            var p2 = new XYZ(xMax, yMax, z0);
            var p3 = new XYZ(xMin, yMax, z0);
            loop.Append(Line.CreateBound(p0, p1));
            loop.Append(Line.CreateBound(p1, p2));
            loop.Append(Line.CreateBound(p2, p3));
            loop.Append(Line.CreateBound(p3, p0));
            try
            {
                return GeometryCreationUtilities.CreateExtrusionGeometry(new List<CurveLoop> { loop }, XYZ.BasisZ, z1 - z0);
            }
            catch { return null; }
        }

        /// <summary>
        /// Prisma fino levantado sobre una cara plana: la cara extruida "depth" hacia fuera
        /// (outward = true, segun su normal) o hacia dentro del solido. Null si no se puede
        /// construir (caras degeneradas).
        /// </summary>
        public static Solid FacePrism(PlanarFace f, double depth, bool outward)
        {
            if (f == null || depth <= 0) return null;
            XYZ n = f.FaceNormal.Normalize();
            IList<CurveLoop> loops;
            try { loops = f.GetEdgesAsCurveLoops(); }
            catch { return null; }
            if (loops == null || loops.Count == 0) return null;

            // Hacia dentro: se desplaza el contorno "depth" hacia dentro y se extruye hacia fuera,
            // asi la orientacion de los lazos (coherente con la normal) no cambia.
            if (!outward)
            {
                Transform t = Transform.CreateTranslation(n.Negate() * depth);
                loops = loops.Select(l => CurveLoop.CreateViaTransform(l, t)).ToList();
            }
            try
            {
                Solid s = GeometryCreationUtilities.CreateExtrusionGeometry(loops, n, depth);
                if (s != null && s.Volume > 1e-12) return s;
            }
            catch { }
            // segundo intento con los lazos invertidos, por si vinieran con la orientacion contraria
            try
            {
                var flipped = new List<CurveLoop>();
                foreach (CurveLoop l in loops)
                {
                    CurveLoop c = CurveLoop.CreateViaTransform(l, Transform.Identity);
                    c.Flip();
                    flipped.Add(c);
                }
                Solid s = GeometryCreationUtilities.CreateExtrusionGeometry(flipped, n, depth);
                if (s != null && s.Volume > 1e-12) return s;
            }
            catch { }
            return null;
        }

        /// <summary>
        /// Area de un solido "vista" a lo largo de una direccion: la mayor de las sumas de
        /// caras planas con normal +n y con normal -n. Para un prisma fino de profundidad d
        /// es el area de su base. Si el solido no tiene caras planas en esa direccion se
        /// estima con volumen / depth (depth > 0).
        /// </summary>
        public static double AreaAlong(Solid s, XYZ n, double depth)
        {
            if (s == null) return 0;
            double plus = 0, minus = 0;
            foreach (Face f in s.Faces)
            {
                if (!(f is PlanarFace pf)) continue;
                double d = pf.FaceNormal.DotProduct(n);
                if (d > 0.999) plus += pf.Area;
                else if (d < -0.999) minus += pf.Area;
            }
            double a = Math.Max(plus, minus);
            if (a <= 0 && depth > 0) a = s.Volume / depth;
            return a;
        }

        // ------------------------------------------------------------------
        // Rayos y puntos
        // ------------------------------------------------------------------

        /// <summary>
        /// Longitud total de un rayo dentro del solido: desde "from" en direccion "dir"
        /// hasta "maxLen". 0 si no toca el solido.
        /// </summary>
        public static double LengthInside(Solid s, XYZ from, XYZ dir, double maxLen)
        {
            if (s == null || maxLen <= 1e-9) return 0;
            try
            {
                Line ln = Line.CreateBound(from, from + dir.Normalize() * maxLen);
                SolidCurveIntersection sci = s.IntersectWithCurve(ln, new SolidCurveIntersectionOptions());
                if (sci == null) return 0;
                double total = 0;
                for (int i = 0; i < sci.SegmentCount; i++)
                    total += sci.GetCurveSegment(i).Length;
                return total;
            }
            catch { return 0; }
        }

        /// <summary>
        /// Distancia desde "from" hasta la primera entrada del rayo en el solido (0 si
        /// "from" ya esta dentro), o -1 si el rayo no toca el solido en "maxLen".
        /// </summary>
        public static double FirstEntry(Solid s, XYZ from, XYZ dir, double maxLen)
        {
            if (s == null || maxLen <= 1e-9) return -1;
            try
            {
                XYZ d = dir.Normalize();
                Line ln = Line.CreateBound(from, from + d * maxLen);
                SolidCurveIntersection sci = s.IntersectWithCurve(ln, new SolidCurveIntersectionOptions());
                if (sci == null || sci.SegmentCount == 0) return -1;
                double best = double.MaxValue;
                for (int i = 0; i < sci.SegmentCount; i++)
                {
                    Curve seg = sci.GetCurveSegment(i);
                    double t = Math.Min((seg.GetEndPoint(0) - from).DotProduct(d), (seg.GetEndPoint(1) - from).DotProduct(d));
                    if (t < best) best = t;
                }
                return Math.Max(0, best);
            }
            catch { return -1; }
        }

        /// <summary>True si el punto esta dentro del solido (se prueba un segmento de 2 mm centrado en el).</summary>
        public static bool Inside(Solid s, XYZ p)
        {
            double h = Mm(1);
            double len = LengthInside(s, p - XYZ.BasisZ * h, XYZ.BasisZ, 2 * h);
            return len > 1.5 * h;
        }

        /// <summary>Extension de una lista de puntos a lo largo de un eje.</summary>
        public static double Extent(IList<XYZ> pts, XYZ axis)
        {
            if (pts == null || pts.Count == 0) return 0;
            double a = double.MaxValue, b = double.MinValue;
            foreach (XYZ p in pts)
            {
                double d = p.DotProduct(axis);
                if (d < a) a = d;
                if (d > b) b = d;
            }
            return b - a;
        }

        /// <summary>Ejes X e Y horizontales de la familia (o del proyecto si el elemento no es una instancia).</summary>
        public static XYZ[] PlanAxes(Element host)
        {
            if (host is FamilyInstance fi)
            {
                try
                {
                    Transform t = fi.GetTransform();
                    XYZ x = Flat(t.BasisX), y = Flat(t.BasisY);
                    if (x != null && y != null && Math.Abs(x.DotProduct(y)) < 1e-6) return new[] { x, y };
                    if (x != null) return new[] { x, XYZ.BasisZ.CrossProduct(x).Normalize() };
                }
                catch { }
            }
            if (host is Wall w && w.Location is LocationCurve lc && lc.Curve is Line ln)
            {
                XYZ d = Flat(ln.Direction);
                if (d != null) return new[] { d, XYZ.BasisZ.CrossProduct(d).Normalize() };
            }
            return new[] { XYZ.BasisX, XYZ.BasisY };
        }

        public static string TypeNameOf(Document doc, Element e)
        {
            ElementId tid = e.GetTypeId();
            Element t = (tid != null && tid != ElementId.InvalidElementId) ? doc.GetElement(tid) : null;
            return t?.Name ?? e.Name;
        }

        /// <summary>Descripcion corta "[id nombre]" de un elemento para mensajes.</summary>
        public static string Describe(Document doc, ElementId id)
        {
            Element o = doc.GetElement(id);
            return o == null ? id.ToString() : "[" + id.Value + " " + o.Name + "]";
        }
    }
}
