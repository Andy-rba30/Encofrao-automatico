using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace RetainingWallFormwork
{
    /// <summary>Parte del muro a la que pertenece una cara.</summary>
    public enum Zone { Stem, Footing }

    /// <summary>Que es cada cara del solido, decidido por su orientacion y su posicion.</summary>
    public enum FaceRole
    {
        /// <summary>Cara principal de la pantalla del lado del talon (o exterior): se encofra.</summary>
        TrasdosMain,
        /// <summary>Cara principal de la pantalla del lado de la puntera (o interior): se encofra.</summary>
        IntradosMain,
        /// <summary>Cara de extremo (o cara principal de un tramo corto de zapata): se encofra si esta libre.</summary>
        End,
        /// <summary>Jamba de un vano: se encofra.</summary>
        OpeningJamb,
        /// <summary>Cara que mira hacia abajo en el aire (dintel de vano, voladizo): se encofra.</summary>
        Soffit,
        /// <summary>Superficie libre hacia arriba (coronacion, cara superior de zapata, escalon): no se encofra.</summary>
        Top,
        /// <summary>Fondo apoyado en el terreno o solado: no se encofra.</summary>
        Bottom
    }

    /// <summary>Contacto de una cara con un vecino.</summary>
    public sealed class Contact
    {
        public Neighbor Neighbor;
        /// <summary>Area de contacto (pies2).</summary>
        public double Area;
    }

    /// <summary>Una cara del solido (o la parte de una cara a un lado del plano de zapata) con su clasificacion.</summary>
    public sealed class FacePart
    {
        public int Index;
        public Face Face;
        public bool Planar;
        public Zone Zone;
        public FaceRole Role;
        public XYZ Normal;
        /// <summary>Area bruta de esta parte (pies2).</summary>
        public double GrossArea;
        public List<Contact> Contacts = new List<Contact>();
        public string Note = "";
        /// <summary>Por que se ha tomado como trasdos/intrados (solo caras principales).</summary>
        public string SideNote = "";

        /// <summary>La cara se detiene contra lo que toque (extremo, jamba, fondo en el aire).</summary>
        public bool Stops => Role == FaceRole.End || Role == FaceRole.OpeningJamb || Role == FaceRole.Soffit;

        /// <summary>Candidata a encofrado por su orientacion (antes de aplicar reglas de zapata y contactos).</summary>
        public bool Lateral => Role != FaceRole.Top && Role != FaceRole.Bottom;

        /// <summary>True si con esta configuracion la parte se encofra (salvo lo que le quiten los contactos).</summary>
        public bool Formed(AppConfig cfg) => Lateral && (Zone == Zone.Stem || cfg.FootingSidesFormed);

        /// <summary>Area de contacto que se descuenta, acotada al area bruta.</summary>
        public double Discount(AppConfig cfg)
        {
            double d = 0;
            foreach (Contact c in Contacts)
                if (c.Neighbor.Discounts(cfg, Stops)) d += c.Area;
            return Math.Min(d, GrossArea);
        }

        public double DiscountSlabs(AppConfig cfg) =>
            Math.Min(GrossArea, Contacts.Where(c => c.Neighbor.IsSlab && c.Neighbor.Discounts(cfg, Stops)).Sum(c => c.Area));

        public double DiscountOthers(AppConfig cfg) =>
            Math.Min(GrossArea, Contacts.Where(c => !c.Neighbor.IsSlab && c.Neighbor.Discounts(cfg, Stops)).Sum(c => c.Area));

        /// <summary>Contacto que NO se descuenta (se informa).</summary>
        public double ContactKept(AppConfig cfg) =>
            Contacts.Where(c => !c.Neighbor.Discounts(cfg, Stops)).Sum(c => c.Area);

        /// <summary>Area que se encofra (pies2).</summary>
        public double FormedArea(AppConfig cfg) => Formed(cfg) ? Math.Max(0, GrossArea - Discount(cfg)) : 0;

        public string ZoneText => Zone == Zone.Stem ? "pantalla" : "zapata";

        public string RoleText
        {
            get
            {
                switch (Role)
                {
                    case FaceRole.TrasdosMain: return Zone == Zone.Stem ? "trasdos" : "lateral (talon)";
                    case FaceRole.IntradosMain: return Zone == Zone.Stem ? "intrados" : "lateral (puntera)";
                    case FaceRole.End: return "extremo";
                    case FaceRole.OpeningJamb: return "jamba de vano";
                    case FaceRole.Soffit: return "cara inferior en el aire";
                    case FaceRole.Top: return "superficie libre";
                    default: return "fondo";
                }
            }
        }

        /// <summary>Texto de la orientacion de la cara para la ventana.</summary>
        public string OrientationText
        {
            get
            {
                double tilt = Math.Acos(Math.Max(-1, Math.Min(1, Math.Abs(Normal.Z)))) * 180 / Math.PI;
                string dir;
                if (Math.Abs(Normal.Z) > 0.999) dir = Normal.Z > 0 ? "arriba" : "abajo";
                else
                {
                    XYZ h = Geo.Flat(Normal);
                    double ang = Math.Atan2(h.Y, h.X) * 180 / Math.PI;
                    dir = "az " + ang.ToString("0") + (Math.Abs(Normal.Z) > 0.02 ? (Normal.Z > 0 ? " (mira arriba)" : " (mira abajo)") : "");
                }
                string t = Math.Abs(Normal.Z) > 0.999 ? "" : Math.Abs(Normal.Z) < 0.02 ? " vertical" : " inclinada " + (90 - tilt).ToString("0") + " grados";
                return dir + t + (Planar ? "" : ", curva");
            }
        }

        public string ContactsText(AppConfig cfg)
        {
            if (Contacts.Count == 0) return "";
            return string.Join("; ", Contacts.Select(c =>
                c.Neighbor.KindText + " " + c.Neighbor.Label + " " + Geo.M2(c.Area) + " m2: " + c.Neighbor.RuleText(cfg, Stops)));
        }
    }

    /// <summary>Resultado numerico de un elemento con una configuracion dada (todo en pies / pies2 salvo Lifts).</summary>
    public sealed class Totals
    {
        public double StemBack, StemFront, StemEnds, Openings, FootingSides;
        public double DiscountOthers, DiscountSlabs, ContactKept, FreeSurfaces, FootingSidesNotFormed;
        public double Length, StemHeight, FootingThickness;
        public int Lifts = 1;
        public double Total => StemBack + StemFront + StemEnds + Openings + FootingSides;
    }

    /// <summary>
    /// Analisis de encofrado de un elemento: cada cara de su solido (tal y como lo ve
    /// Revit, con uniones y cortes) clasificada por orientacion y posicion, y el contacto
    /// de cada cara con los elementos vecinos. Es solo lectura; los totales se calculan
    /// despues con Compute(cfg), asi la ventana puede cambiar las reglas sin volver a
    /// recorrer la geometria.
    /// </summary>
    public sealed class FormworkAnalysis
    {
        public Element Host;
        public string Tag;
        public string Mark = "", TypeName = "", FamilyName = "";
        public string Error;
        public List<FacePart> Parts = new List<FacePart>();
        public List<Neighbor> Neighbors = new List<Neighbor>();
        public List<string> Warnings = new List<string>();

        public double BaseZ, CrownZ, FootingTopZ;
        public bool HasFooting;
        public string FootingNote = "";
        /// <summary>Mayor extension en planta de la coronacion (pies).</summary>
        public double LengthFt;

        public bool CanBuild => Error == null && Parts.Count > 0;
        public string Kind => Error != null ? "SIN METRAR" : HasFooting ? "Muro con zapata" : "Muro sin zapata";

        private static double Mm(double mm) => Geo.Mm(mm);

        /// <summary>Descripcion corta para la ventana y el informe final.</summary>
        public string Detail(AppConfig cfg)
        {
            if (Error != null) return Error;
            Totals t = Compute(cfg);
            string s = "L=" + Geo.ToMm(t.Length) + " H pantalla=" + Geo.ToMm(t.StemHeight) +
                       (HasFooting ? " canto zapata=" + Geo.ToMm(t.FootingThickness) : " (sin zapata)") + " mm; " +
                       Parts.Count + " caras, " + Neighbors.Count + " vecino(s)";
            if (Warnings.Count > 0) s += "; " + string.Join("; ", Warnings);
            return s;
        }

        /// <summary>Totales con las reglas de la configuracion.</summary>
        public Totals Compute(AppConfig cfg)
        {
            var t = new Totals
            {
                Length = LengthFt,
                StemHeight = HasFooting ? CrownZ - FootingTopZ : CrownZ - BaseZ,
                FootingThickness = HasFooting ? FootingTopZ - BaseZ : 0
            };
            foreach (FacePart p in Parts)
            {
                if (!p.Lateral) { t.FreeSurfaces += p.GrossArea; continue; }
                double formed = p.FormedArea(cfg);
                if (p.Zone == Zone.Footing)
                {
                    if (cfg.FootingSidesFormed) t.FootingSides += formed;
                    else { t.FootingSidesNotFormed += p.GrossArea; continue; }
                }
                else
                {
                    switch (p.Role)
                    {
                        case FaceRole.TrasdosMain: t.StemBack += formed; break;
                        case FaceRole.IntradosMain: t.StemFront += formed; break;
                        case FaceRole.End: t.StemEnds += formed; break;
                        default: t.Openings += formed; break;
                    }
                }
                t.DiscountOthers += p.DiscountOthers(cfg);
                t.DiscountSlabs += p.DiscountSlabs(cfg);
                t.ContactKept += p.ContactKept(cfg);
            }
            if (cfg.PourLiftMm > 0 && t.StemHeight > 0)
                t.Lifts = Math.Max(1, (int)Math.Ceiling(t.StemHeight / Mm(cfg.PourLiftMm) - 1e-6));
            return t;
        }

        /// <summary>Texto para el parametro "ENC Observaciones".</summary>
        public string Observations(AppConfig cfg)
        {
            var notes = new List<string>();
            if (!HasFooting) notes.Add("sin zapata reconocida");
            notes.AddRange(Warnings);
            Totals t = Compute(cfg);
            if (t.ContactKept > Mm(10) * Mm(10))
                notes.Add("contacto no descontado " + Geo.M2(t.ContactKept) + " m2");
            if (!cfg.FootingSidesFormed && t.FootingSidesNotFormed > 0)
                notes.Add("zapata contra terreno: " + Geo.M2(t.FootingSidesNotFormed) + " m2 laterales sin encofrar");
            if (t.Lifts > 1) notes.Add(t.Lifts + " vaciados de " + cfg.PourLiftMm.ToString("0") + " mm max");
            return string.Join("; ", notes);
        }

        // ==================================================================
        // Analisis
        // ==================================================================

        public static FormworkAnalysis Analyze(Document doc, Element host, AppConfig cfg)
        {
            var a = new FormworkAnalysis { Host = host, Tag = "[" + host.Id.Value + " " + host.Name + "] " };
            try
            {
                a.Mark = host.get_Parameter(BuiltInParameter.ALL_MODEL_MARK)?.AsString() ?? "";
                a.TypeName = Geo.TypeNameOf(doc, host) ?? "";
                if (host is FamilyInstance fi) a.FamilyName = fi.Symbol?.Family?.Name ?? "";
                else a.FamilyName = host.Category?.Name ?? "";

                List<Solid> solids = Geo.Solids(host);
                if (solids.Count == 0) { a.Error = "no se encontro solido en el elemento"; return a; }
                double vMax = solids[0].Volume;
                solids = solids.Where(s => s.Volume > vMax * 0.001).ToList();
                if (solids.Count > 1)
                    a.Warnings.Add(solids.Count + " solidos independientes (se metran todos)");

                var allPts = new List<XYZ>();
                foreach (Solid s in solids) allPts.AddRange(Geo.Vertices(s));
                if (allPts.Count < 4) { a.Error = "el solido no tiene aristas legibles"; return a; }
                BoundingBoxXYZ bb = Geo.Box(allPts);
                a.BaseZ = bb.Min.Z;
                a.CrownZ = bb.Max.Z;

                // --- zapata ---
                SectionOverride ov = cfg.OverrideFor(a.TypeName);
                if (ov.NoFooting)
                {
                    a.HasFooting = false;
                    a.FootingNote = "sin zapata (forzado en sectionOverrides)";
                }
                else if (ov.FootingTopMm > 0)
                {
                    a.FootingTopZ = a.BaseZ + Mm(ov.FootingTopMm);
                    a.HasFooting = a.FootingTopZ < a.CrownZ - Mm(50);
                    a.FootingNote = a.HasFooting ? "canto de zapata forzado en sectionOverrides" : "canto forzado incoherente con la altura: sin zapata";
                }
                else
                {
                    a.HasFooting = FindFootingTop(solids[0], bb, cfg, out a.FootingTopZ, out a.FootingNote);
                    if (!a.HasFooting) a.Warnings.Add("sin zapata reconocida: todo se mide como pantalla (" + a.FootingNote + ")");
                }

                // --- longitud: extension de la coronacion en planta ---
                XYZ[] fam = Geo.PlanAxes(host);
                var axes = new List<XYZ> { fam[0], fam[1], XYZ.BasisX, XYZ.BasisY };
                var top = allPts.Where(p => p.Z > a.CrownZ - Mm(20)).ToList();
                if (top.Count < 2) top = allPts;
                a.LengthFt = axes.Max(ax => Geo.Extent(top, ax));

                // --- vecinos ---
                a.Neighbors = RetainingWallFormwork.Neighbors.Around(doc, host, cfg);

                // --- caras ---
                double minArea = cfg.MinFaceAreaCm2 * Mm(10) * Mm(10);
                var ctx = new Ctx { A = a, Cfg = cfg, AllPts = allPts, Axes = axes, Bb = bb };
                ctx.Diag = (bb.Max - bb.Min).GetLength() + Mm(100);
                foreach (XYZ ax in axes)
                {
                    ctx.AxisMin.Add(allPts.Min(p => p.DotProduct(ax)));
                    ctx.AxisMax.Add(allPts.Max(p => p.DotProduct(ax)));
                }
                int idx = 0;
                int curved = 0;
                foreach (Solid s in solids)
                {
                    foreach (Face f in s.Faces)
                    {
                        if (f.Area < minArea) continue;
                        idx++;
                        if (!(f is PlanarFace)) curved++;
                        Classify(ctx, s, f, idx);
                    }
                }
                if (curved > 0) a.Warnings.Add(curved + " cara(s) curva(s): contacto estimado por muestreo");
                if (a.Parts.Count == 0) a.Error = "el solido no tiene caras medibles";
            }
            catch (Exception ex)
            {
                a.Error = "ERROR: " + ex.Message;
            }
            return a;
        }

        private sealed class Ctx
        {
            public FormworkAnalysis A;
            public AppConfig Cfg;
            public List<XYZ> AllPts;
            public List<XYZ> Axes;
            public List<double> AxisMin = new List<double>(), AxisMax = new List<double>();
            public BoundingBoxXYZ Bb;
            public double Diag;
        }

        // ------------------------------------------------------------------
        // Cara superior de zapata
        // ------------------------------------------------------------------

        /// <summary>
        /// Busca la cota de la cara superior de zapata: la cota mas alta con caras planas
        /// horizontales hacia arriba en la que la seccion de justo debajo es claramente mas
        /// ancha (en alguna direccion de planta) que la de justo encima. Un escalon de
        /// coronacion no cumple (la seccion no se ensancha, se acorta); un contrafuerte
        /// cumple pero ensancha menos que una zapata, y se elige el candidato con mayor
        /// relacion de areas.
        /// </summary>
        private static bool FindFootingTop(Solid solid, BoundingBoxXYZ bb, AppConfig cfg, out double zTop, out string why)
        {
            zTop = 0;
            why = null;
            double tol = Mm(2);
            double zMin = bb.Min.Z, zMax = bb.Max.Z;
            double minStem = Mm(cfg.MinStemHeightMm), minFoot = Mm(cfg.MinFootingThicknessMm);

            // candidatos: cotas de caras planas horizontales hacia arriba, agrupadas
            var cands = new List<double>();
            foreach (Face f in solid.Faces)
            {
                if (!(f is PlanarFace pf) || pf.FaceNormal.Z < 0.99) continue;
                double z = pf.Origin.Z;
                if (z > zMax - minStem || z < zMin + minFoot) continue;
                if (pf.Area < Mm(50) * Mm(50)) continue;
                if (!cands.Any(c => Math.Abs(c - z) <= tol)) cands.Add(z);
            }
            if (cands.Count == 0) { why = "no hay caras horizontales hacia arriba entre la base y la coronacion"; return false; }

            double pad = Mm(100);
            double x0 = bb.Min.X - pad, x1 = bb.Max.X + pad, y0 = bb.Min.Y - pad, y1 = bb.Max.Y + pad;
            double d = Mm(cfg.ProbeSliceMm);
            double widen = Mm(cfg.MinFootingWideningMm);
            XYZ[] axes = { XYZ.BasisX, XYZ.BasisY, new XYZ(1, 1, 0).Normalize(), new XYZ(1, -1, 0).Normalize() };

            double bestRatio = 0, bestZ = 0;
            var reasons = new List<string>();
            foreach (double z in cands.OrderByDescending(c => c))
            {
                Solid below = Geo.Intersect(solid, Geo.ZBox(x0, x1, y0, y1, z - 2 * d, z - d));
                Solid above = Geo.Intersect(solid, Geo.ZBox(x0, x1, y0, y1, z + d, z + 2 * d));
                if (below == null || above == null) { reasons.Add("z=" + Geo.ToMm(z - zMin) + " sin rebanadas"); continue; }
                double aBelow = Geo.AreaAlong(below, XYZ.BasisZ, d), aAbove = Geo.AreaAlong(above, XYZ.BasisZ, d);
                List<XYZ> pb = Geo.Vertices(below), pa = Geo.Vertices(above);
                double grow = axes.Max(ax => Geo.Extent(pb, ax) - Geo.Extent(pa, ax));
                if (grow < widen)
                { reasons.Add("z=" + Geo.ToMm(z - zMin) + " mm: la seccion no se ensancha (" + Geo.ToMm(grow) + " mm)"); continue; }
                if (aAbove <= 0 || aBelow < aAbove * 1.2)
                { reasons.Add("z=" + Geo.ToMm(z - zMin) + " mm: el area apenas crece"); continue; }
                double ratio = aBelow / aAbove;
                if (ratio > bestRatio) { bestRatio = ratio; bestZ = z; }
            }
            if (bestRatio <= 0)
            {
                why = reasons.Count > 0 ? string.Join("; ", reasons.Take(3)) : "ningun candidato valido";
                return false;
            }
            zTop = bestZ;
            why = "cara superior de zapata a " + Geo.ToMm(bestZ - zMin) + " mm de la base (seccion x" + bestRatio.ToString("0.0") + ")";
            return true;
        }

        // ------------------------------------------------------------------
        // Clasificacion de una cara
        // ------------------------------------------------------------------

        private static void Classify(Ctx c, Solid solid, Face f, int index)
        {
            FormworkAnalysis a = c.A;
            AppConfig cfg = c.Cfg;
            double tol = Mm(3);
            XYZ n = Geo.Normal(f);
            bool planar = f is PlanarFace;
            List<XYZ> pts = Geo.Vertices(f);
            if (pts.Count == 0) return;
            double zMin = pts.Min(p => p.Z), zMax = pts.Max(p => p.Z);
            double cosTop = Math.Cos(cfg.TopFaceMaxTiltDeg * Math.PI / 180);
            double cosBot = Math.Cos(cfg.BottomFaceMaxTiltDeg * Math.PI / 180);

            // --- superficie libre hacia arriba ---
            if (n.Z >= cosTop)
            {
                Zone z = a.HasFooting && zMax <= a.FootingTopZ + tol ? Zone.Footing : Zone.Stem;
                string note = z == Zone.Footing
                    ? (Math.Abs(zMax - a.FootingTopZ) <= tol ? "cara superior de zapata" : "escalon de zapata")
                    : (zMax >= a.CrownZ - tol ? "coronacion" : "superficie libre (escalon / alfeizar)");
                if (Math.Abs(n.Z) < 0.999) note += ", inclinada";
                a.Parts.Add(new FacePart { Index = index, Face = f, Planar = planar, Zone = z, Role = FaceRole.Top, Normal = n, GrossArea = f.Area, Note = note + ": no se encofra" });
                return;
            }

            // --- hacia abajo ---
            if (n.Z <= -cosBot)
            {
                if (zMin <= a.BaseZ + tol)
                {
                    a.Parts.Add(new FacePart { Index = index, Face = f, Planar = planar, Zone = a.HasFooting ? Zone.Footing : Zone.Stem, Role = FaceRole.Bottom, Normal = n, GrossArea = f.Area, Note = "fondo sobre terreno / solado: no se encofra" });
                    return;
                }
                Zone zs = a.HasFooting && zMax <= a.FootingTopZ + tol ? Zone.Footing : Zone.Stem;
                var soffit = new FacePart { Index = index, Face = f, Planar = planar, Zone = zs, Role = FaceRole.Soffit, Normal = n, GrossArea = f.Area, Note = "cara inferior en el aire (dintel de vano / voladizo / escalon): se encofra" };
                AddContacts(c, solid, soffit, null);
                a.Parts.Add(soffit);
                return;
            }

            // --- cara lateral (vertical o inclinada) ---
            XYZ nh = Geo.Flat(n);
            if (nh == null) nh = XYZ.BasisX;
            XYZ p = XYZ.BasisZ.CrossProduct(nh).Normalize();      // horizontal, en el plano de la cara
            XYZ d = p.CrossProduct(n).Normalize();                 // en el plano, "hacia arriba"
            if (d.Z < 0) d = d.Negate();
            XYZ cen = Geo.Centroid(f);

            double width = Geo.Extent(pts, p);
            double height = Geo.Extent(pts, d);
            // en una cara que cruza el plano de zapata (frente de un muro en L) la profundidad
            // se mide en la pantalla, no en la zapata, que es mucho mas ancha
            XYZ cenDepth = cen;
            if (a.HasFooting && zMin < a.FootingTopZ && zMax > a.FootingTopZ && planar && Math.Abs(d.Z) > 1e-6)
            {
                double zMid = a.FootingTopZ + (zMax - a.FootingTopZ) * 0.5;
                cenDepth = cen + d * ((zMid - cen.Z) / d.Z);
            }
            double depth = DepthBehind(c, solid, n, cenDepth, p, d, width, Math.Min(height, Math.Max(zMax - (a.HasFooting ? Math.Max(zMin, a.FootingTopZ) : zMin), Mm(50))));
            bool endLike = width < depth * 0.95;

            FaceRole role;
            string sideNote = "";
            if (endLike)
            {
                role = OnBoundary(c, pts) ? FaceRole.End : FaceRole.OpeningJamb;
            }
            else
            {
                role = SideOf(c, solid, f, n, nh, p, d, cen, zMin, out sideNote);
            }

            // --- zona: pantalla, zapata o las dos (cara que cruza el plano de zapata) ---
            bool spans = a.HasFooting && zMin < a.FootingTopZ - tol && zMax > a.FootingTopZ + tol;
            if (!spans)
            {
                Zone z = a.HasFooting && zMax <= a.FootingTopZ + tol ? Zone.Footing : Zone.Stem;
                var part = new FacePart { Index = index, Face = f, Planar = planar, Zone = z, Role = role, Normal = n, GrossArea = f.Area, SideNote = sideNote };
                part.Note = NoteFor(part);
                AddContacts(c, solid, part, null);
                a.Parts.Add(part);
                return;
            }

            // parte de zapata (por debajo del plano) y parte de pantalla (por encima)
            double areaBelow;
            Solid halfBelow = Geo.ZBox(c.Bb.Min.X - 1, c.Bb.Max.X + 1, c.Bb.Min.Y - 1, c.Bb.Max.Y + 1, a.BaseZ - 1, a.FootingTopZ);
            Solid halfAbove = Geo.ZBox(c.Bb.Min.X - 1, c.Bb.Max.X + 1, c.Bb.Min.Y - 1, c.Bb.Max.Y + 1, a.FootingTopZ, a.CrownZ + 1);
            Solid prism = planar ? Geo.FacePrism((PlanarFace)f, Mm(cfg.ContactToleranceMm), true) : null;
            if (prism != null)
            {
                areaBelow = Geo.AreaAlong(Geo.Intersect(prism, halfBelow), n, Mm(cfg.ContactToleranceMm));
                if (areaBelow > f.Area) areaBelow = f.Area;
            }
            else
            {
                // cara curva: reparto lineal en altura (aproximado)
                areaBelow = f.Area * (a.FootingTopZ - zMin) / Math.Max(1e-9, zMax - zMin);
                a.Warnings.Add("cara " + index + " curva cruza el plano de zapata: reparto aproximado");
            }
            var foot = new FacePart { Index = index, Face = f, Planar = planar, Zone = Zone.Footing, Role = role, Normal = n, GrossArea = areaBelow, SideNote = sideNote };
            var stem = new FacePart { Index = index, Face = f, Planar = planar, Zone = Zone.Stem, Role = role, Normal = n, GrossArea = Math.Max(0, f.Area - areaBelow), SideNote = sideNote };
            foot.Note = NoteFor(foot) + " (parte bajo el plano de zapata)";
            stem.Note = NoteFor(stem) + " (parte sobre el plano de zapata)";
            AddContacts(c, solid, foot, halfBelow, prism);
            AddContacts(c, solid, stem, halfAbove, prism);
            a.Parts.Add(foot);
            a.Parts.Add(stem);
        }

        private static string NoteFor(FacePart p)
        {
            switch (p.Role)
            {
                case FaceRole.TrasdosMain: return p.Zone == Zone.Stem ? "cara principal, trasdos" + Suffix(p) : "lateral de zapata (lado del talon)";
                case FaceRole.IntradosMain: return p.Zone == Zone.Stem ? "cara principal, intrados" + Suffix(p) : "lateral de zapata (lado de la puntera)";
                case FaceRole.End: return p.Zone == Zone.Stem ? "extremo de pantalla" : "extremo de zapata";
                case FaceRole.OpeningJamb: return "jamba de vano (cara corta que no llega al borde del muro)";
                default: return "";
            }
        }

        private static string Suffix(FacePart p) => string.IsNullOrEmpty(p.SideNote) ? "" : " (" + p.SideNote + ")";

        /// <summary>
        /// Profundidad de concreto detras de la cara (a lo largo de -n) medida desde varios
        /// puntos de la cara; se toma la mayor. En una cara de extremo es la longitud del
        /// muro; en una cara principal, el espesor.
        /// </summary>
        private static double DepthBehind(Ctx c, Solid solid, XYZ n, XYZ cen, XYZ p, XYZ d, double width, double height)
        {
            var samples = new List<XYZ> { cen, cen + p * width * 0.3, cen - p * width * 0.3, cen + d * height * 0.3, cen - d * height * 0.3 };
            double best = 0;
            foreach (XYZ q in samples)
            {
                double len = Geo.LengthInside(solid, q - n * Mm(1), n.Negate(), c.Diag);
                if (len > best) best = len;
            }
            return best;
        }

        /// <summary>True si algun vertice de la cara esta en un extremo de la planta del elemento (borde de la caja en alguno de los ejes).</summary>
        private static bool OnBoundary(Ctx c, List<XYZ> pts)
        {
            double tol = Mm(5);
            for (int k = 0; k < c.Axes.Count; k++)
            {
                XYZ ax = c.Axes[k];
                foreach (XYZ q in pts)
                {
                    double v = q.DotProduct(ax);
                    if (v <= c.AxisMin[k] + tol || v >= c.AxisMax[k] - tol) return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Trasdos o intrados de una cara principal: se compara el vuelo de zapata de este
        /// lado con el del lado opuesto (el talon, mayor, es el trasdos). Sin zapata o con
        /// vuelos iguales se usa la orientacion de la familia (cara frontal = trasdos).
        /// </summary>
        private static FaceRole SideOf(Ctx c, Solid solid, Face f, XYZ n, XYZ nh, XYZ p, XYZ d, XYZ cen, double zMin, out string note)
        {
            FormworkAnalysis a = c.A;
            note = "";
            if (a.HasFooting && cen.Z <= a.FootingTopZ && zMin < a.FootingTopZ - Mm(3))
            {
                // cara lateral de la zapata: desde un punto en el aire sobre el borde de la
                // zapata, hacia dentro, el rayo entra en la pantalla a la distancia del vuelo
                double stemH = a.CrownZ - a.FootingTopZ, footH = a.FootingTopZ - a.BaseZ;
                double zStem = a.FootingTopZ + Math.Min(Mm(50), stemH * 0.5);
                double zFoot = a.FootingTopZ - Math.Min(Mm(50), footH * 0.5);
                XYZ edge = new XYZ(cen.X, cen.Y, zStem) - nh * Mm(5);
                double vueloThis = Geo.FirstEntry(solid, edge, nh.Negate(), c.Diag);
                double thick = Geo.LengthInside(solid, edge, nh.Negate(), c.Diag);
                double width = Geo.LengthInside(solid, new XYZ(cen.X, cen.Y, zFoot) - nh * Mm(5), nh.Negate(), c.Diag) + Mm(5);
                if (vueloThis >= 0 && thick > 0)
                {
                    double vueloOther = width - vueloThis - thick;
                    double diff = vueloThis - vueloOther;
                    if (diff > Mm(20))
                    {
                        note = "talon " + Geo.ToMm(vueloThis) + " mm de este lado frente a " + Geo.ToMm(Math.Max(0, vueloOther)) + " mm del opuesto";
                        return FaceRole.TrasdosMain;
                    }
                    if (diff < -Mm(20))
                    {
                        note = "puntera " + Geo.ToMm(Math.Max(0, vueloThis)) + " mm de este lado frente a " + Geo.ToMm(vueloOther) + " mm del opuesto";
                        return FaceRole.IntradosMain;
                    }
                    note = "vuelos iguales, segun orientacion de la familia";
                }
                else note = "no se pudo medir el vuelo, segun orientacion de la familia";
            }
            else if (a.HasFooting && (zMin >= a.FootingTopZ - Mm(3) || cen.Z > a.FootingTopZ))
            {
                double stemH = a.CrownZ - a.FootingTopZ, footH = a.FootingTopZ - a.BaseZ;
                double zFoot = a.FootingTopZ - Math.Min(Mm(50), footH * 0.5);
                double zStem = a.FootingTopZ + Math.Min(Mm(50), stemH * 0.5);

                // punto de la cara a la altura del arranque de la pantalla
                XYZ b = cen;
                if (f is PlanarFace && Math.Abs(d.Z) > 1e-6)
                    b = cen + d * ((a.FootingTopZ - cen.Z) / d.Z);
                XYZ inFoot = new XYZ(b.X, b.Y, zFoot) - nh * Mm(5);
                XYZ inStem = new XYZ(b.X, b.Y, zStem) - nh * Mm(5);

                double vueloThis = Geo.LengthInside(solid, inFoot, nh, c.Diag);
                double through = Geo.LengthInside(solid, inFoot, nh.Negate(), c.Diag);
                double thick = Geo.LengthInside(solid, inStem, nh.Negate(), c.Diag);
                double vueloOther = through - thick;
                double diff = vueloThis - vueloOther;
                if (diff > Mm(20))
                {
                    note = "talon " + Geo.ToMm(vueloThis) + " mm de este lado frente a " + Geo.ToMm(Math.Max(0, vueloOther)) + " mm del opuesto";
                    return FaceRole.TrasdosMain;
                }
                if (diff < -Mm(20))
                {
                    note = "puntera " + Geo.ToMm(Math.Max(0, vueloThis)) + " mm de este lado frente a " + Geo.ToMm(vueloOther) + " mm del opuesto";
                    return FaceRole.IntradosMain;
                }
                note = "vuelos iguales, segun orientacion de la familia";
            }
            else if (string.IsNullOrEmpty(note)) note = a.HasFooting ? "segun orientacion de la familia" : "sin zapata, segun orientacion de la familia";

            XYZ facing = null;
            if (a.Host is FamilyInstance fi) { try { facing = Geo.Flat(fi.FacingOrientation); } catch { } }
            else if (a.Host is Wall w) { try { facing = Geo.Flat(w.Orientation); } catch { } }
            if (facing == null) facing = c.Axes[1];
            double dot = nh.DotProduct(facing);
            if (Math.Abs(dot) < 0.2)
            {
                // la cara es paralela a la orientacion: se decide con el otro eje de la familia
                dot = nh.DotProduct(c.Axes[0]);
            }
            return dot >= 0 ? FaceRole.TrasdosMain : FaceRole.IntradosMain;
        }

        // ------------------------------------------------------------------
        // Contactos
        // ------------------------------------------------------------------

        /// <summary>
        /// Contacto de una parte de cara con cada vecino. Caras planas: prisma fino hacia
        /// fuera de la cara (limitado al semiespacio de la parte, si lo hay) intersecado con
        /// los solidos del vecino; el area de contacto es el area del resultado vista a lo
        /// largo de la normal. Caras curvas: muestreo de puntos a media holgura de la cara.
        /// </summary>
        private static void AddContacts(Ctx c, Solid solid, FacePart part, Solid half, Solid prismReady = null)
        {
            FormworkAnalysis a = c.A;
            AppConfig cfg = c.Cfg;
            if (a.Neighbors.Count == 0) return;
            double depth = Mm(cfg.ContactToleranceMm);
            double minArea = Mm(10) * Mm(10);   // 1 cm2

            Solid prism = prismReady ?? (part.Planar ? Geo.FacePrism((PlanarFace)part.Face, depth, true) : null);
            if (prism != null && half != null) prism = Geo.Intersect(prism, half);

            if (prism != null)
            {
                foreach (Neighbor nb in a.Neighbors)
                {
                    double area = 0;
                    foreach (Solid ns in nb.Solids)
                        area += Geo.AreaAlong(Geo.Intersect(prism, ns), part.Normal, depth);
                    area = Math.Min(area, part.GrossArea);
                    if (area > minArea) part.Contacts.Add(new Contact { Neighbor = nb, Area = area });
                }
                return;
            }

            // --- muestreo (caras curvas o prisma imposible) ---
            if (part.Planar) a.Warnings.Add("cara " + part.Index + ": no se pudo levantar el prisma, contacto por muestreo");
            Face f = part.Face;
            BoundingBoxUV uv;
            try { uv = f.GetBoundingBox(); } catch { return; }
            double du = uv.Max.U - uv.Min.U, dv = uv.Max.V - uv.Min.V;
            if (du <= 0 || dv <= 0) return;
            // longitud aproximada en cada direccion parametrica para elegir el numero de pasos
            double lenU = 0, lenV = 0;
            try
            {
                XYZ p00 = f.Evaluate(uv.Min), p10 = f.Evaluate(new UV(uv.Max.U, uv.Min.V)), p01 = f.Evaluate(new UV(uv.Min.U, uv.Max.V));
                lenU = p00.DistanceTo(p10); lenV = p00.DistanceTo(p01);
            }
            catch { lenU = lenV = Mm(1000); }
            double step = Mm(cfg.SampleStepMm);
            int nu = Math.Max(3, Math.Min(80, (int)Math.Ceiling(lenU / step)));
            int nv = Math.Max(3, Math.Min(80, (int)Math.Ceiling(lenV / step)));

            var hits = new Dictionary<Neighbor, int>();
            int total = 0;
            double zLo = half == null ? double.MinValue : (part.Zone == Zone.Footing ? double.MinValue : a.FootingTopZ);
            double zHi = half == null ? double.MaxValue : (part.Zone == Zone.Footing ? a.FootingTopZ : double.MaxValue);
            for (int i = 0; i < nu; i++)
                for (int j = 0; j < nv; j++)
                {
                    var q = new UV(uv.Min.U + du * (i + 0.5) / nu, uv.Min.V + dv * (j + 0.5) / nv);
                    XYZ pt, nn;
                    try
                    {
                        if (!f.IsInside(q)) continue;
                        pt = f.Evaluate(q);
                        nn = f.ComputeNormal(q).Normalize();
                    }
                    catch { continue; }
                    if (pt.Z < zLo || pt.Z > zHi) continue;
                    total++;
                    XYZ probe = pt + nn * depth * 0.5;
                    foreach (Neighbor nb in a.Neighbors)
                    {
                        bool inside = false;
                        foreach (Solid ns in nb.Solids)
                            if (Geo.Inside(ns, probe)) { inside = true; break; }
                        if (!inside) continue;
                        hits.TryGetValue(nb, out int k);
                        hits[nb] = k + 1;
                    }
                }
            if (total == 0) return;
            foreach (KeyValuePair<Neighbor, int> kv in hits)
            {
                double area = part.GrossArea * kv.Value / total;
                if (area > minArea) part.Contacts.Add(new Contact { Neighbor = kv.Key, Area = area });
            }
        }
    }
}
