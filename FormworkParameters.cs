using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.DB;

namespace RetainingWallFormwork
{
    /// <summary>Un parametro compartido "ENC ..." del metrado.</summary>
    public sealed class FormworkParam
    {
        public string Name;
        public Guid Guid;
        public ForgeTypeId Spec;
        public string Description;
        /// <summary>Encabezado de columna en la tabla.</summary>
        public string Heading;
        /// <summary>Se suma en la tabla (areas).</summary>
        public bool Sum;
    }

    /// <summary>
    /// Parametros compartidos en los que se escribe el metrado de cada elemento y desde
    /// los que se alimenta la tabla de planificacion. Los GUID son fijos para que cada
    /// ejecucion reutilice los mismos parametros (y la misma tabla) en cualquier proyecto.
    /// </summary>
    public static class FormworkParameters
    {
        public const string GroupName = "ARBA Encofrado";

        public static readonly FormworkParam StemBack = new FormworkParam
        {
            Name = "ENC Pantalla trasdos", Guid = new Guid("a1f3c8d2-6b41-4e9a-9c07-1d2e3f4a5b60"), Spec = SpecTypeId.Area,
            Description = "Encofrado de la cara de trasdos de la pantalla (lado del talon)", Heading = "Pantalla trasdos (m2)", Sum = true
        };
        public static readonly FormworkParam StemFront = new FormworkParam
        {
            Name = "ENC Pantalla intrados", Guid = new Guid("a1f3c8d2-6b41-4e9a-9c07-1d2e3f4a5b61"), Spec = SpecTypeId.Area,
            Description = "Encofrado de la cara de intrados de la pantalla (lado de la puntera, cara vista)", Heading = "Pantalla intrados (m2)", Sum = true
        };
        public static readonly FormworkParam StemEnds = new FormworkParam
        {
            Name = "ENC Pantalla extremos", Guid = new Guid("a1f3c8d2-6b41-4e9a-9c07-1d2e3f4a5b62"), Spec = SpecTypeId.Area,
            Description = "Encofrado de los extremos libres de la pantalla", Heading = "Extremos (m2)", Sum = true
        };
        public static readonly FormworkParam Openings = new FormworkParam
        {
            Name = "ENC Vanos", Guid = new Guid("a1f3c8d2-6b41-4e9a-9c07-1d2e3f4a5b63"), Spec = SpecTypeId.Area,
            Description = "Encofrado de jambas y dinteles de vanos y otras caras inferiores en el aire", Heading = "Vanos (m2)", Sum = true
        };
        public static readonly FormworkParam FootingSides = new FormworkParam
        {
            Name = "ENC Zapata laterales", Guid = new Guid("a1f3c8d2-6b41-4e9a-9c07-1d2e3f4a5b64"), Spec = SpecTypeId.Area,
            Description = "Encofrado de las caras laterales de la zapata (0 si se vacia contra el terreno)", Heading = "Zapata laterales (m2)", Sum = true
        };
        public static readonly FormworkParam Total = new FormworkParam
        {
            Name = "ENC Total", Guid = new Guid("a1f3c8d2-6b41-4e9a-9c07-1d2e3f4a5b65"), Spec = SpecTypeId.Area,
            Description = "Encofrado total del elemento (suma de las partidas)", Heading = "Total (m2)", Sum = true
        };
        public static readonly FormworkParam ContactOthers = new FormworkParam
        {
            Name = "ENC Contacto muros", Guid = new Guid("a1f3c8d2-6b41-4e9a-9c07-1d2e3f4a5b66"), Spec = SpecTypeId.Area,
            Description = "Area descontada por contacto con muros, cimentaciones, columnas y vigas de concreto", Heading = "Desc. muros (m2)", Sum = true
        };
        public static readonly FormworkParam ContactSlabs = new FormworkParam
        {
            Name = "ENC Contacto losas", Guid = new Guid("a1f3c8d2-6b41-4e9a-9c07-1d2e3f4a5b67"), Spec = SpecTypeId.Area,
            Description = "Area descontada por contacto con losas", Heading = "Desc. losas (m2)", Sum = true
        };
        public static readonly FormworkParam Length = new FormworkParam
        {
            Name = "ENC Longitud", Guid = new Guid("a1f3c8d2-6b41-4e9a-9c07-1d2e3f4a5b68"), Spec = SpecTypeId.Length,
            Description = "Mayor extension en planta de la coronacion", Heading = "Longitud (m)"
        };
        public static readonly FormworkParam StemHeight = new FormworkParam
        {
            Name = "ENC Altura pantalla", Guid = new Guid("a1f3c8d2-6b41-4e9a-9c07-1d2e3f4a5b69"), Spec = SpecTypeId.Length,
            Description = "Altura de la pantalla sobre la cara superior de zapata", Heading = "H pantalla (m)"
        };
        public static readonly FormworkParam FootingThickness = new FormworkParam
        {
            Name = "ENC Canto zapata", Guid = new Guid("a1f3c8d2-6b41-4e9a-9c07-1d2e3f4a5b6a"), Spec = SpecTypeId.Length,
            Description = "Canto de la zapata (0 si no se reconocio zapata)", Heading = "Canto zapata (m)"
        };
        public static readonly FormworkParam Lifts = new FormworkParam
        {
            Name = "ENC Etapas vaciado", Guid = new Guid("a1f3c8d2-6b41-4e9a-9c07-1d2e3f4a5b6b"), Spec = SpecTypeId.Int.Integer,
            Description = "Numero de vaciados de la pantalla segun la altura maxima por vaciado", Heading = "Etapas"
        };
        public static readonly FormworkParam Observations = new FormworkParam
        {
            Name = "ENC Observaciones", Guid = new Guid("a1f3c8d2-6b41-4e9a-9c07-1d2e3f4a5b6c"), Spec = SpecTypeId.String.Text,
            Description = "Avisos del metrado (sin zapata, caras curvas, contactos no descontados...)", Heading = "Observaciones"
        };
        public static readonly FormworkParam Stamp = new FormworkParam
        {
            Name = "ENC Metrado", Guid = new Guid("a1f3c8d2-6b41-4e9a-9c07-1d2e3f4a5b6d"), Spec = SpecTypeId.String.Text,
            Description = "Fecha y reglas del ultimo metrado", Heading = "Metrado"
        };

        /// <summary>Todos, en el orden de las columnas de la tabla.</summary>
        public static readonly FormworkParam[] All =
        {
            StemBack, StemFront, StemEnds, Openings, FootingSides, Total, ContactOthers, ContactSlabs,
            Length, StemHeight, FootingThickness, Lifts, Observations, Stamp
        };

        /// <summary>Categorias a las que se enlazan los parametros (las de los anfitriones admitidos).</summary>
        public static List<BuiltInCategory> HostCategories(AppConfig cfg)
        {
            var list = new List<BuiltInCategory>();
            foreach (string name in cfg.HostCategories ?? new List<string>())
            {
                string n = name.Trim();
                if (!n.StartsWith("OST_", StringComparison.OrdinalIgnoreCase)) n = "OST_" + n;
                if (Enum.TryParse(n, true, out BuiltInCategory bic) && !list.Contains(bic)) list.Add(bic);
            }
            if (list.Count == 0) { list.Add(BuiltInCategory.OST_StructuralFoundation); list.Add(BuiltInCategory.OST_Walls); }
            return list;
        }

        /// <summary>Archivo de parametros compartidos propio del add-in (junto a la DLL, o en %AppData% si no se puede escribir ahi).</summary>
        public static string SharedFilePath()
        {
            string dir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            string path = Path.Combine(dir, "ARBA_Encofrado_parametros_compartidos.txt");
            try
            {
                if (!File.Exists(path)) File.WriteAllText(path, "");
                return path;
            }
            catch
            {
                string alt = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ARBA", "Encofrado");
                Directory.CreateDirectory(alt);
                return Path.Combine(alt, "ARBA_Encofrado_parametros_compartidos.txt");
            }
        }

        /// <summary>
        /// Crea (si faltan) los parametros compartidos y los enlaza como parametros de
        /// ejemplar a las categorias de anfitrion. Debe llamarse dentro de una transaccion.
        /// Devuelve los avisos (por ejemplo un parametro de proyecto con el mismo nombre).
        /// </summary>
        public static List<string> Ensure(Document doc, AppConfig cfg)
        {
            var warnings = new List<string>();
            Application app = doc.Application;

            // categorias
            var cats = new CategorySet();
            foreach (BuiltInCategory bic in HostCategories(cfg))
            {
                Category c = null;
                try { c = Category.GetCategory(doc, bic); } catch { }
                if (c != null && c.AllowsBoundParameters) cats.Insert(c);
            }
            if (cats.IsEmpty) throw new InvalidOperationException("ninguna de las categorias de anfitrion admite parametros compartidos");

            // archivo de parametros compartidos
            string original = app.SharedParametersFilename;
            string path = SharedFilePath();
            DefinitionFile file;
            try
            {
                app.SharedParametersFilename = path;
                file = app.OpenSharedParameterFile();
                if (file == null)
                {
                    File.WriteAllText(path, "");
                    file = app.OpenSharedParameterFile();
                }
                if (file == null) throw new InvalidOperationException("no se pudo abrir el archivo de parametros compartidos " + path);

                DefinitionGroup group = file.Groups.get_Item(GroupName) ?? file.Groups.Create(GroupName);
                BindingMap map = doc.ParameterBindings;

                foreach (FormworkParam p in All)
                {
                    // ya existe en el proyecto (con este GUID)?
                    SharedParameterElement existing = SharedParameterElement.Lookup(doc, p.Guid);
                    Definition def = null;
                    if (existing != null)
                    {
                        def = existing.GetDefinition();
                    }
                    else
                    {
                        ExternalDefinition ext = group.Definitions.get_Item(p.Name) as ExternalDefinition;
                        if (ext != null && ext.GUID != p.Guid)
                        {
                            // archivo antiguo con otro GUID: se recrea con otro nombre de archivo no, se avisa
                            warnings.Add("el archivo de parametros compartidos tiene '" + p.Name + "' con otro GUID; se usa ese");
                        }
                        if (ext == null)
                        {
                            var opt = new ExternalDefinitionCreationOptions(p.Name, p.Spec)
                            {
                                GUID = p.Guid,
                                Description = p.Description ?? "",
                                Visible = true,
                                UserModifiable = true
                            };
                            ext = group.Definitions.Create(opt) as ExternalDefinition;
                        }
                        def = ext;
                        if (def == null) throw new InvalidOperationException("no se pudo crear la definicion '" + p.Name + "'");

                        // un parametro de proyecto (no compartido) con el mismo nombre estorba
                        if (existing == null && ProjectParameterNamed(doc, p.Name))
                            warnings.Add("ya existe un parametro de proyecto llamado '" + p.Name + "' que no es el del add-in; la tabla puede leer el equivocado");
                    }

                    // enlace de categorias
                    InstanceBinding binding = null;
                    if (map.Contains(def)) binding = map.get_Item(def) as InstanceBinding;
                    if (binding == null)
                    {
                        map.Insert(def, app.Create.NewInstanceBinding(cats), GroupTypeId.Data);
                    }
                    else
                    {
                        bool missing = false;
                        foreach (Category c in cats)
                            if (!binding.Categories.Contains(c)) { missing = true; break; }
                        if (missing)
                        {
                            foreach (Category c in cats) binding.Categories.Insert(c);
                            map.ReInsert(def, binding, GroupTypeId.Data);
                        }
                    }
                }
            }
            finally
            {
                try { app.SharedParametersFilename = original; } catch { }
            }
            return warnings;
        }

        private static bool ProjectParameterNamed(Document doc, string name)
        {
            try
            {
                foreach (ParameterElement pe in new FilteredElementCollector(doc).OfClass(typeof(ParameterElement)))
                {
                    if (pe is SharedParameterElement) continue;
                    if (string.Equals(pe.GetDefinition()?.Name, name, StringComparison.OrdinalIgnoreCase)) return true;
                }
            }
            catch { }
            return false;
        }

        /// <summary>Escribe los resultados de un elemento en sus parametros. Dentro de una transaccion.</summary>
        public static void Write(FormworkAnalysis a, Totals t, AppConfig cfg, string stamp)
        {
            Element e = a.Host;
            SetArea(e, StemBack, t.StemBack);
            SetArea(e, StemFront, t.StemFront);
            SetArea(e, StemEnds, t.StemEnds);
            SetArea(e, Openings, t.Openings);
            SetArea(e, FootingSides, t.FootingSides);
            SetArea(e, Total, t.Total);
            SetArea(e, ContactOthers, t.DiscountOthers);
            SetArea(e, ContactSlabs, t.DiscountSlabs);
            Set(e, Length, p => p.Set(t.Length));
            Set(e, StemHeight, p => p.Set(t.StemHeight));
            Set(e, FootingThickness, p => p.Set(t.FootingThickness));
            Set(e, Lifts, p => p.Set(t.Lifts));
            Set(e, Observations, p => p.Set(a.Observations(cfg)));
            Set(e, Stamp, p => p.Set(stamp));
        }

        private static void SetArea(Element e, FormworkParam fp, double ft2) => Set(e, fp, p => p.Set(ft2));

        private static void Set(Element e, FormworkParam fp, Action<Parameter> setter)
        {
            Parameter p = e.get_Parameter(fp.Guid);
            if (p == null || p.IsReadOnly) throw new InvalidOperationException("el elemento no tiene el parametro '" + fp.Name + "' (o es de solo lectura)");
            setter(p);
        }

        /// <summary>Texto de "ENC Metrado": fecha y reglas usadas.</summary>
        public static string StampText(AppConfig cfg)
        {
            string rule(int i) => i == 1 ? "siempre" : i == 2 ? "nunca" : "auto";
            return DateTime.Now.ToString("yyyy-MM-dd HH:mm") + " | muros: " + rule(cfg.WallRuleIndex) + " | losas: " + rule(cfg.SlabRuleIndex) +
                   " | zapata: " + (cfg.FootingSidesFormed ? "encofrada" : "contra terreno") +
                   (cfg.PourLiftMm > 0 ? " | vaciado max " + cfg.PourLiftMm.ToString("0") + " mm" : "");
        }
    }
}
