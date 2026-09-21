using System;
using System.Reflection;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Autodesk.Revit.UI;

namespace RetainingWallFormwork
{
    /// <summary>
    /// Al arrancar Revit anade a la pestana "ARBA" (panel "Muros de contencion", el mismo
    /// que usa el add-in de armado) el boton "Metrar encofrado", que lanza
    /// MetrarEncofradoCommand. El comando sigue disponible ademas en Complementos,
    /// Herramientas externas. El icono se dibuja en codigo para no depender de imagenes.
    /// </summary>
    public class RibbonApp : IExternalApplication
    {
        public const string TabName = "ARBA";
        private const string PanelName = "Muros de contencion";

        public Result OnStartup(UIControlledApplication app)
        {
            try
            {
                // la pestana puede existir ya si otro add-in la creo antes
                try { app.CreateRibbonTab(TabName); } catch (Exception) { }

                RibbonPanel panel = null;
                foreach (RibbonPanel p in app.GetRibbonPanels(TabName))
                    if (p.Name == PanelName) { panel = p; break; }
                if (panel == null) panel = app.CreateRibbonPanel(TabName, PanelName);

                string assembly = Assembly.GetExecutingAssembly().Location;
                var data = new PushButtonData("MetrarEncofradoMuroContencion", "Metrar\nencofrado", assembly,
                                              typeof(MetrarEncofradoCommand).FullName)
                {
                    ToolTip = "Metra el encofrado (m2) de muros de contencion y crea la tabla de planificacion",
                    LongDescription = "Selecciona uno o varios muros y pulsa el boton. Se abre una ventana con cada cara " +
                                      "del muro clasificada (pantalla, zapata, extremos, vanos), lo que toca (muros, losas, " +
                                      "columnas) y lo que se descuenta segun las reglas constructivas. Al metrar se escriben " +
                                      "los parametros ENC de cada elemento y se crea o actualiza la tabla. Si no hay nada " +
                                      "seleccionado, el comando pide que elijas los muros.",
                    LargeImage = Icon(32),
                    Image = Icon(16)
                };
                panel.AddItem(data);
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                TaskDialog.Show("ARBA", "No se pudo crear el boton de encofrado: " + ex.Message +
                                "\nEl comando sigue disponible en Complementos > Herramientas externas.");
                return Result.Failed;
            }
        }

        public Result OnShutdown(UIControlledApplication app) => Result.Succeeded;

        /// <summary>
        /// Icono: seccion de un muro de contencion (zapata + pantalla) con los tableros de
        /// encofrado en las dos caras de la pantalla y en los laterales de la zapata.
        /// </summary>
        private static BitmapSource Icon(int size)
        {
            double s = size / 32.0;
            var visual = new DrawingVisual();
            using (DrawingContext dc = visual.RenderOpen())
            {
                var concrete = new SolidColorBrush(Color.FromRgb(0xD9, 0xD9, 0xD9));
                var edge = new Pen(new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)), 1.2 * s);
                var board = new Pen(new SolidColorBrush(Color.FromRgb(0xC8, 0x7A, 0x1E)), 2.6 * s)
                {
                    StartLineCap = PenLineCap.Flat, EndLineCap = PenLineCap.Flat
                };

                // hormigon: zapata y pantalla con talud en el trasdos (izquierda)
                var outline = new StreamGeometry();
                using (StreamGeometryContext g = outline.Open())
                {
                    g.BeginFigure(new Point(3 * s, 30 * s), true, true);
                    g.LineTo(new Point(29 * s, 30 * s), true, false);
                    g.LineTo(new Point(29 * s, 23 * s), true, false);
                    g.LineTo(new Point(19 * s, 23 * s), true, false);
                    g.LineTo(new Point(18 * s, 2 * s), true, false);
                    g.LineTo(new Point(13 * s, 2 * s), true, false);
                    g.LineTo(new Point(10 * s, 23 * s), true, false);
                    g.LineTo(new Point(3 * s, 23 * s), true, false);
                }
                dc.DrawGeometry(concrete, edge, outline);

                // tableros: trasdos (inclinado), intrados (vertical) y laterales de zapata
                dc.DrawLine(board, new Point(11.6 * s, 3 * s), new Point(8.6 * s, 22.5 * s));
                dc.DrawLine(board, new Point(19.5 * s, 3 * s), new Point(20.5 * s, 22.5 * s));
                dc.DrawLine(board, new Point(1.6 * s, 23 * s), new Point(1.6 * s, 30 * s));
                dc.DrawLine(board, new Point(30.4 * s, 23 * s), new Point(30.4 * s, 30 * s));
            }

            var bmp = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
            bmp.Render(visual);
            bmp.Freeze();
            return bmp;
        }
    }
}
