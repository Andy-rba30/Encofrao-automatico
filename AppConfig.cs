using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RetainingWallFormwork
{
    /// <summary>Ajustes por nombre de tipo de familia (por si la deteccion automatica falla en una geometria rara).</summary>
    public class SectionOverride
    {
        /// <summary>Si >0 fuerza la cota de la cara superior de zapata, medida desde la base del elemento (mm).</summary>
        public double FootingTopMm { get; set; } = 0;

        /// <summary>true = el elemento no tiene zapata (todo es pantalla), aunque la deteccion crea ver una.</summary>
        public bool NoFooting { get; set; } = false;
    }

    /// <summary>
    /// Configuracion del metrado. Se lee de config.json junto a la DLL, la ventana la
    /// edita para la ejecucion en curso y puede guardarla como nuevos valores por defecto.
    /// </summary>
    public class AppConfig
    {
        // --- reglas constructivas ---

        /// <summary>
        /// Contacto con muros, cimentaciones, columnas, vigas (y modelos genericos si se
        /// incluyen) de concreto:
        ///  "auto"   = segun la union de geometria de Revit. Elementos UNIDOS (Unir geometria)
        ///             se vacian monoliticos: el contacto no se encofra en ninguno de los dos.
        ///             Elementos SIN unir se vacian por separado: el que se detiene contra el
        ///             otro (cara de extremo, jamba o fondo) se vacia despues y no encofra ese
        ///             contacto; la cara principal del otro se encofra completa.
        ///  "always" = todo contacto se descuenta (vaciado monolitico).
        ///  "never"  = no se descuenta nada (el muro se encofra completo).
        /// </summary>
        public string WallContactRule { get; set; } = "auto";

        /// <summary>
        /// Contacto con losas:
        ///  "auto"   = losa UNIDA al muro (Unir geometria) = vaciado monolitico o losa vaciada
        ///             antes: se descuenta la franja de contacto. Losa SIN unir = se vacia
        ///             despues que el muro: la cara del muro se encofra completa.
        ///  "always" = se descuenta siempre la franja de contacto.
        ///  "never"  = no se descuenta nunca.
        /// En todos los casos el contacto se informa en la ventana y en "ENC Contacto losas".
        /// </summary>
        public string SlabContactRule { get; set; } = "auto";

        [JsonIgnore] public int WallRuleIndex => RuleIndex(WallContactRule);
        [JsonIgnore] public int SlabRuleIndex => RuleIndex(SlabContactRule);

        /// <summary>0 auto, 1 siempre, 2 nunca (orden de los desplegables).</summary>
        public static int RuleIndex(string rule)
        {
            string m = (rule ?? "").Trim().ToLowerInvariant();
            if (m == "always" || m == "siempre") return 1;
            if (m == "never" || m == "nunca") return 2;
            return 0;
        }

        public static string RuleName(int index) => index == 1 ? "always" : index == 2 ? "never" : "auto";

        /// <summary>Solo cuentan como vecinos los elementos cuyo material es concreto (o no se puede saber).</summary>
        public bool OnlyConcreteNeighbors { get; set; } = true;

        /// <summary>Incluir modelos genericos como vecinos (por si las losas o muros se modelaron asi).</summary>
        public bool NeighborGenericModels { get; set; } = false;

        /// <summary>
        /// "form" = se encofran las caras laterales de la zapata (se miden en "ENC Zapata laterales").
        /// "ground" = la zapata se vacia contra el terreno: sus caras laterales no se encofran
        /// (se informan en la ventana pero no suman).
        /// </summary>
        public string FootingSides { get; set; } = "form";

        [JsonIgnore]
        public bool FootingSidesFormed => !string.Equals((FootingSides ?? "").Trim(), "ground", StringComparison.OrdinalIgnoreCase);

        /// <summary>Altura maxima de pantalla por vaciado (mm). 0 = un solo vaciado. Solo informa las etapas.</summary>
        public double PourLiftMm { get; set; } = 0;

        // --- geometria ---

        /// <summary>
        /// Holgura de contacto (mm): profundidad del prisma que se levanta sobre cada cara
        /// para buscar vecinos. Un elemento a menos de esta distancia cuenta como en contacto.
        /// </summary>
        public double ContactToleranceMm { get; set; } = 20;

        /// <summary>
        /// Una cara que mira hacia arriba con esta inclinacion o menos respecto a la
        /// horizontal es superficie libre (coronacion, cara superior de zapata) y no se encofra.
        /// Mas inclinada, se encofra como una cara lateral.
        /// </summary>
        public double TopFaceMaxTiltDeg { get; set; } = 30;

        /// <summary>Idem para caras que miran hacia abajo apoyadas en el terreno o solado (fondo).</summary>
        public double BottomFaceMaxTiltDeg { get; set; } = 30;

        /// <summary>
        /// Para reconocer la cara superior de zapata: la seccion justo debajo tiene que ser al
        /// menos esto mas ancha (mm) que la de justo encima, en alguna direccion de planta.
        /// </summary>
        public double MinFootingWideningMm { get; set; } = 100;

        /// <summary>Altura minima de pantalla sobre la zapata (mm) para aceptar una cota como cara superior de zapata.</summary>
        public double MinStemHeightMm { get; set; } = 300;

        /// <summary>Canto minimo de zapata (mm).</summary>
        public double MinFootingThicknessMm { get; set; } = 150;

        /// <summary>Caras con menos area que esto (cm2) se ignoran.</summary>
        public double MinFaceAreaCm2 { get; set; } = 1;

        /// <summary>Paso de muestreo (mm) para el contacto en caras curvas (que no admiten el prisma exacto).</summary>
        public double SampleStepMm { get; set; } = 50;

        /// <summary>Espesor de las rebanadas de sondeo (mm).</summary>
        public double ProbeSliceMm { get; set; } = 10;

        // --- salida ---

        /// <summary>Escribir los resultados en los parametros compartidos "ENC ..." de cada elemento.</summary>
        public bool WriteParameters { get; set; } = true;

        /// <summary>Crear (o actualizar) la tabla de planificacion con los resultados.</summary>
        public bool CreateSchedule { get; set; } = true;

        public string ScheduleName { get; set; } = "ARBA - Metrado de encofrado (muros de contencion)";

        /// <summary>Abrir la tabla al terminar.</summary>
        public bool OpenSchedule { get; set; } = true;

        /// <summary>Categorias admitidas como muro de contencion (nombres de BuiltInCategory sin el prefijo OST_).</summary>
        public List<string> HostCategories { get; set; } = new List<string> { "StructuralFoundation", "Walls" };

        /// <summary>Ajustes por nombre de tipo.</summary>
        public Dictionary<string, SectionOverride> SectionOverrides { get; set; }
            = new Dictionary<string, SectionOverride>(StringComparer.OrdinalIgnoreCase);

        public SectionOverride OverrideFor(string typeName)
        {
            if (typeName != null && SectionOverrides != null)
            {
                // el diccionario deserializado no conserva el comparador: se busca sin distinguir mayusculas
                foreach (KeyValuePair<string, SectionOverride> kv in SectionOverrides)
                    if (string.Equals(kv.Key, typeName, StringComparison.OrdinalIgnoreCase) && kv.Value != null)
                        return kv.Value;
            }
            return new SectionOverride();
        }

        /// <summary>Deja la configuracion en un estado coherente.</summary>
        public void Normalize()
        {
            if (ContactToleranceMm < 1) ContactToleranceMm = 1;
            if (TopFaceMaxTiltDeg < 0) TopFaceMaxTiltDeg = 0;
            if (TopFaceMaxTiltDeg > 89) TopFaceMaxTiltDeg = 89;
            if (BottomFaceMaxTiltDeg < 0) BottomFaceMaxTiltDeg = 0;
            if (BottomFaceMaxTiltDeg > 89) BottomFaceMaxTiltDeg = 89;
            if (MinFootingWideningMm < 10) MinFootingWideningMm = 10;
            if (MinStemHeightMm < 50) MinStemHeightMm = 50;
            if (MinFootingThicknessMm < 50) MinFootingThicknessMm = 50;
            if (MinFaceAreaCm2 < 0) MinFaceAreaCm2 = 0;
            if (SampleStepMm < 10) SampleStepMm = 10;
            if (ProbeSliceMm < 2) ProbeSliceMm = 2;
            if (PourLiftMm < 0) PourLiftMm = 0;
            if (string.IsNullOrWhiteSpace(ScheduleName)) ScheduleName = "ARBA - Metrado de encofrado (muros de contencion)";
            if (!FootingSidesFormed) FootingSides = "ground"; else FootingSides = "form";
            WallContactRule = RuleName(RuleIndex(WallContactRule));
            SlabContactRule = RuleName(RuleIndex(SlabContactRule));
            if (HostCategories == null || HostCategories.Count == 0)
                HostCategories = new List<string> { "StructuralFoundation", "Walls" };
            if (SectionOverrides == null) SectionOverrides = new Dictionary<string, SectionOverride>(StringComparer.OrdinalIgnoreCase);
        }

        public static string ConfigPath()
        {
            string dir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            return Path.Combine(dir, "config.json");
        }

        private static JsonSerializerOptions ReadOptions() => new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };

        private static JsonSerializerOptions WriteOptions() => new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        public static AppConfig Load()
        {
            string path = ConfigPath();
            AppConfig cfg = File.Exists(path)
                ? JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(path), ReadOptions()) ?? new AppConfig()
                : new AppConfig();
            cfg.Normalize();
            return cfg;
        }

        /// <summary>Guarda esta configuracion como config.json junto a la DLL (valores por defecto de la ventana).</summary>
        public void Save(string path = null)
        {
            File.WriteAllText(path ?? ConfigPath(), JsonSerializer.Serialize(this, WriteOptions()));
        }

        /// <summary>Copia independiente, para que la ventana edite sin tocar la configuracion cargada.</summary>
        public AppConfig Clone()
        {
            AppConfig c = JsonSerializer.Deserialize<AppConfig>(JsonSerializer.Serialize(this, WriteOptions()), ReadOptions())
                          ?? new AppConfig();
            c.Normalize();
            return c;
        }
    }
}
