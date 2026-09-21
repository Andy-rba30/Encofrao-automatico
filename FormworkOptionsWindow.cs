using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace RetainingWallFormwork
{
    /// <summary>
    /// Ventana previa al metrado: lista los elementos seleccionados con su diagnostico y
    /// sus partidas de encofrado, las caras del elemento marcado (zona, tipo, area bruta,
    /// contactos y area encofrada) y las reglas constructivas, que se pueden cambiar y
    /// ver el efecto al momento. Los valores iniciales vienen de config.json y se pueden
    /// guardar como nuevos valores por defecto. Construida en codigo (sin XAML).
    /// </summary>
    public sealed class FormworkOptionsWindow : Window
    {
        private readonly AppConfig _cfg;
        private readonly Func<AppConfig, List<FormworkAnalysis>> _analyze;

        /// <summary>Configuracion final si el usuario pulso "Metrar"; null si cancelo.</summary>
        public AppConfig Result { get; private set; }

        /// <summary>Analisis vigentes (pueden haberse recalculado desde la ventana).</summary>
        public List<FormworkAnalysis> Items { get; private set; }

        public sealed class ElementRow
        {
            public FormworkAnalysis Analysis;
            public string Elemento { get; set; }
            public string Marca { get; set; }
            public string Tipo { get; set; }
            public string Diagnostico { get; set; }
            public string Trasdos { get; set; }
            public string Intrados { get; set; }
            public string Extremos { get; set; }
            public string Vanos { get; set; }
            public string Zapata { get; set; }
            public string DescMuros { get; set; }
            public string DescLosas { get; set; }
            public string Total { get; set; }
            public string Etapas { get; set; }
            public bool IsError { get; set; }
        }

        public sealed class FaceRow
        {
            public string Cara { get; set; }
            public string Zona { get; set; }
            public string Tipo { get; set; }
            public string Orientacion { get; set; }
            public string Bruta { get; set; }
            public string Descuento { get; set; }
            public string Encofra { get; set; }
            public string Contactos { get; set; }
            public string Nota { get; set; }
            public bool IsFormed { get; set; }
        }

        private DataGrid _elements, _faces;
        private ComboBox _wallRule, _slabRule, _footing;
        private CheckBox _onlyConcrete, _generic, _writeParams, _createSchedule, _openSchedule;
        private TextBox _pourLift, _contactTol, _topTilt, _bottomTilt, _widening, _scheduleName;
        private TextBlock _totals, _message;
        private bool _loading = true;

        public FormworkOptionsWindow(AppConfig cfg, List<FormworkAnalysis> items, Func<AppConfig, List<FormworkAnalysis>> analyze)
        {
            _cfg = cfg;
            Items = items;
            _analyze = analyze;

            Title = "Metrado de encofrado de muros de contencion";
            Width = 1380;
            Height = 860;
            MinWidth = 1000;
            MinHeight = 600;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            FontSize = 12;
            Content = Build();
            _loading = false;
            Refresh();
        }

        // ------------------------------------------------------------------
        // Construccion
        // ------------------------------------------------------------------

        private UIElement Build()
        {
            var root = new Grid { Margin = new Thickness(10) };
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(380) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var header = new TextBlock
            {
                Text = "Cada cara del solido de cada elemento (tal y como lo ve Revit, con uniones y cortes) se clasifica por su " +
                       "orientacion y posicion: pantalla (trasdos, intrados, extremos), zapata (laterales), vanos y superficies libres. " +
                       "Sobre cada cara se busca el contacto con muros, losas, cimentaciones, columnas y vigas, y se descuenta " +
                       "segun las reglas de la derecha. Haz clic en un elemento para ver sus caras.",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 8)
            };
            Grid.SetColumnSpan(header, 2);
            root.Children.Add(header);

            // --- izquierda: elementos y caras ---
            var left = new Grid();
            left.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            left.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            left.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            Grid.SetRow(left, 1);
            Grid.SetColumn(left, 0);
            root.Children.Add(left);

            _elements = MakeGrid();
            AddCol(_elements, "Elemento", "Elemento", 150);
            AddCol(_elements, "Marca", "Marca", 60);
            AddCol(_elements, "Tipo", "Tipo", 120);
            AddCol(_elements, "Trasdos", "Trasdos", 62, true);
            AddCol(_elements, "Intrados", "Intrados", 62, true);
            AddCol(_elements, "Extremos", "Extremos", 62, true);
            AddCol(_elements, "Vanos", "Vanos", 55, true);
            AddCol(_elements, "Zapata", "Zapata", 60, true);
            AddCol(_elements, "Desc. muros", "DescMuros", 70, true);
            AddCol(_elements, "Desc. losas", "DescLosas", 68, true);
            AddCol(_elements, "Total m2", "Total", 65, true);
            AddCol(_elements, "Etapas", "Etapas", 50, true);
            AddCol(_elements, "Diagnostico", "Diagnostico", 0);
            _elements.RowStyle = ErrorRowStyle("IsError", Brushes.Firebrick);
            _elements.SelectionChanged += (s, e) => ShowFaces();
            left.Children.Add(Boxed("Elementos seleccionados (m2 encofrados con las reglas actuales)", _elements, 0));

            var lbl = new TextBlock { Text = "Caras del elemento marcado", FontWeight = FontWeights.Bold, Margin = new Thickness(0, 8, 0, 2) };
            Grid.SetRow(lbl, 1);
            left.Children.Add(lbl);

            _faces = MakeGrid();
            AddCol(_faces, "Cara", "Cara", 42);
            AddCol(_faces, "Zona", "Zona", 60);
            AddCol(_faces, "Tipo", "Tipo", 120);
            AddCol(_faces, "Orientacion", "Orientacion", 150);
            AddCol(_faces, "Bruta m2", "Bruta", 62, true);
            AddCol(_faces, "Descuento", "Descuento", 68, true);
            AddCol(_faces, "Se encofra", "Encofra", 68, true);
            AddCol(_faces, "Contactos (que toca y que regla se aplica)", "Contactos", 0);
            AddCol(_faces, "Nota", "Nota", 260);
            _faces.RowStyle = ErrorRowStyle("IsFormed", Brushes.Gray, invert: true);
            Grid.SetRow(_faces, 2);
            left.Children.Add(_faces);

            // --- derecha: reglas ---
            var right = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(10, 0, 0, 0) };
            Grid.SetRow(right, 1);
            Grid.SetColumn(right, 1);
            root.Children.Add(right);
            var panel = new StackPanel();
            right.Content = panel;

            // reglas constructivas
            var rules = new StackPanel();
            rules.Children.Add(Label("Contacto con muros, cimentaciones, columnas y vigas de concreto:"));
            _wallRule = Combo(new[]
            {
                "Auto: segun union de geometria (recomendado)",
                "Siempre se descuenta (vaciado monolitico)",
                "Nunca se descuenta (el muro se encofra completo)"
            }, _cfg.WallRuleIndex);
            rules.Children.Add(_wallRule);
            rules.Children.Add(Help("Auto: elementos UNIDOS (Unir geometria) se vacian juntos y el contacto no se encofra en " +
                                    "ninguno. Elementos SIN unir: el que se detiene contra el otro (cara de extremo, jamba o " +
                                    "fondo) se vacia despues y no encofra ese contacto; la cara principal del otro se encofra " +
                                    "completa, como en obra."));
            rules.Children.Add(Label("Contacto con losas:", 8));
            _slabRule = Combo(new[]
            {
                "Auto: losa unida = se descuenta la franja; sin unir = no",
                "Siempre se descuenta la franja de contacto",
                "Nunca se descuenta (la cara se encofra completa)"
            }, _cfg.SlabRuleIndex);
            rules.Children.Add(_slabRule);
            rules.Children.Add(Help("Una losa UNIDA al muro se vacia antes o monolitica (el muro se vacia contra ella): la " +
                                    "franja de contacto no lleva encofrado. Una losa SIN unir se vacia despues del muro: la " +
                                    "cara del muro se encofro completa. El contacto se informa siempre en 'Desc. losas'."));
            _onlyConcrete = new CheckBox { Content = "Solo cuentan los vecinos de concreto (albanileria, acero, etc. se encofran igual)", IsChecked = _cfg.OnlyConcreteNeighbors, Margin = new Thickness(0, 8, 0, 0) };
            rules.Children.Add(_onlyConcrete);
            _generic = new CheckBox { Content = "Incluir modelos genericos como vecinos (requiere recalcular)", IsChecked = _cfg.NeighborGenericModels, Margin = new Thickness(0, 4, 0, 0) };
            rules.Children.Add(_generic);
            rules.Children.Add(Label("Zapata:", 8));
            _footing = Combo(new[]
            {
                "Encofrar las caras laterales de la zapata",
                "Zapata vaciada contra el terreno (sin encofrado lateral)"
            }, _cfg.FootingSidesFormed ? 0 : 1);
            rules.Children.Add(_footing);
            rules.Children.Add(Help("La cara superior de la zapata y la coronacion nunca se encofran; el fondo tampoco. " +
                                    "La pantalla se mide desde la cara superior de zapata (junta de construccion)."));
            var liftRow = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
            _pourLift = Num(_cfg.PourLiftMm);
            DockPanel.SetDock(_pourLift, Dock.Right);
            liftRow.Children.Add(_pourLift);
            liftRow.Children.Add(new TextBlock { Text = "Altura maxima de pantalla por vaciado (mm, 0 = un vaciado):", VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap });
            rules.Children.Add(liftRow);
            rules.Children.Add(Help("Solo informa el numero de etapas (columna 'Etapas'); el area de encofrado no cambia."));
            panel.Children.Add(Group("Reglas constructivas", rules));

            // geometria
            var geo = new StackPanel();
            _contactTol = NumRow(geo, "Holgura de contacto (mm):", _cfg.ContactToleranceMm);
            _topTilt = NumRow(geo, "Cara superior libre hasta (grados sobre la horizontal):", _cfg.TopFaceMaxTiltDeg);
            _bottomTilt = NumRow(geo, "Fondo apoyado hasta (grados):", _cfg.BottomFaceMaxTiltDeg);
            _widening = NumRow(geo, "Ensanche minimo para reconocer la zapata (mm):", _cfg.MinFootingWideningMm);
            var recalc = new Button { Content = "Recalcular geometria", Margin = new Thickness(0, 8, 0, 0), Padding = new Thickness(8, 3, 8, 3), HorizontalAlignment = HorizontalAlignment.Left };
            recalc.Click += (s, e) => Recalculate();
            geo.Children.Add(recalc);
            geo.Children.Add(Help("Estos valores se aplican al volver a leer la geometria. Las reglas de arriba se aplican al momento."));
            panel.Children.Add(Group("Geometria", geo));

            // salida
            var outp = new StackPanel();
            _writeParams = new CheckBox { Content = "Escribir los parametros 'ENC ...' en cada elemento", IsChecked = _cfg.WriteParameters };
            outp.Children.Add(_writeParams);
            _createSchedule = new CheckBox { Content = "Crear o actualizar la tabla de planificacion", IsChecked = _cfg.CreateSchedule, Margin = new Thickness(0, 4, 0, 0) };
            outp.Children.Add(_createSchedule);
            outp.Children.Add(Label("Nombre de la tabla:", 6));
            _scheduleName = new TextBox { Text = _cfg.ScheduleName };
            outp.Children.Add(_scheduleName);
            _openSchedule = new CheckBox { Content = "Abrir la tabla al terminar", IsChecked = _cfg.OpenSchedule, Margin = new Thickness(0, 6, 0, 0) };
            outp.Children.Add(_openSchedule);
            outp.Children.Add(Help("La tabla es multicategoria (sirve para cimentaciones y muros a la vez), con una fila por " +
                                   "elemento metrado, ordenada por Marca y con totales al pie. Los parametros compartidos se " +
                                   "crean la primera vez con GUID fijos, asi la tabla sigue valiendo en las siguientes ejecuciones."));
            panel.Children.Add(Group("Salida", outp));

            _message = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = Brushes.Firebrick, Margin = new Thickness(0, 6, 0, 0) };
            panel.Children.Add(_message);

            // --- pie: totales y botones ---
            var foot = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
            Grid.SetRow(foot, 2);
            Grid.SetColumnSpan(foot, 2);
            root.Children.Add(foot);

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            DockPanel.SetDock(buttons, Dock.Right);
            var save = new Button { Content = "Guardar como valores por defecto", Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(6, 0, 0, 0) };
            save.Click += (s, e) => SaveDefaults();
            var copy = new Button { Content = "Copiar resumen", Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(6, 0, 0, 0) };
            copy.Click += (s, e) => CopySummary();
            var ok = new Button { Content = "Metrar y crear tabla", Padding = new Thickness(14, 4, 14, 4), Margin = new Thickness(6, 0, 0, 0), IsDefault = true, FontWeight = FontWeights.Bold };
            ok.Click += (s, e) => Accept();
            var cancel = new Button { Content = "Cancelar", Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(6, 0, 0, 0), IsCancel = true };
            buttons.Children.Add(save);
            buttons.Children.Add(copy);
            buttons.Children.Add(ok);
            buttons.Children.Add(cancel);
            foot.Children.Add(buttons);

            _totals = new TextBlock { VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeights.Bold, TextWrapping = TextWrapping.Wrap };
            foot.Children.Add(_totals);

            // cambios de reglas: recalculo inmediato de totales
            _wallRule.SelectionChanged += (s, e) => Refresh();
            _slabRule.SelectionChanged += (s, e) => Refresh();
            _footing.SelectionChanged += (s, e) => Refresh();
            _onlyConcrete.Checked += (s, e) => Refresh();
            _onlyConcrete.Unchecked += (s, e) => Refresh();
            _pourLift.TextChanged += (s, e) => Refresh();
            return root;
        }

        private static DataGrid MakeGrid() => new DataGrid
        {
            AutoGenerateColumns = false,
            IsReadOnly = true,
            CanUserAddRows = false,
            CanUserDeleteRows = false,
            HeadersVisibility = DataGridHeadersVisibility.Column,
            SelectionMode = DataGridSelectionMode.Single,
            GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
            AlternatingRowBackground = new SolidColorBrush(Color.FromRgb(0xF4, 0xF4, 0xF4)),
            FontSize = 11.5
        };

        private static void AddCol(DataGrid g, string header, string path, double width, bool right = false)
        {
            var col = new DataGridTextColumn
            {
                Header = header,
                Binding = new Binding(path),
                Width = width <= 0 ? new DataGridLength(1, DataGridLengthUnitType.Star) : new DataGridLength(width)
            };
            var style = new Style(typeof(TextBlock));
            style.Setters.Add(new Setter(TextBlock.TextWrappingProperty, TextWrapping.Wrap));
            if (right) style.Setters.Add(new Setter(TextBlock.TextAlignmentProperty, TextAlignment.Right));
            col.ElementStyle = style;
            g.Columns.Add(col);
        }

        private static Style ErrorRowStyle(string boolPath, Brush brush, bool invert = false)
        {
            var style = new Style(typeof(DataGridRow));
            var trig = new DataTrigger { Binding = new Binding(boolPath), Value = !invert };
            trig.Setters.Add(new Setter(Control.ForegroundProperty, brush));
            style.Triggers.Add(trig);
            return style;
        }

        private static UIElement Boxed(string title, UIElement content, int row)
        {
            var dp = new DockPanel();
            var t = new TextBlock { Text = title, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 2) };
            DockPanel.SetDock(t, Dock.Top);
            dp.Children.Add(t);
            dp.Children.Add(content);
            Grid.SetRow(dp, row);
            return dp;
        }

        private static GroupBox Group(string title, UIElement content) =>
            new GroupBox { Header = title, Content = content, Padding = new Thickness(6), Margin = new Thickness(0, 0, 0, 8) };

        private static TextBlock Label(string text, double top = 0) =>
            new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, top, 0, 2) };

        private static TextBlock Help(string text) =>
            new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Foreground = Brushes.DimGray, FontSize = 11, Margin = new Thickness(0, 2, 0, 0) };

        private static ComboBox Combo(string[] items, int selected)
        {
            var cb = new ComboBox();
            foreach (string s in items) cb.Items.Add(s);
            cb.SelectedIndex = Math.Max(0, Math.Min(items.Length - 1, selected));
            return cb;
        }

        private static TextBox Num(double value) =>
            new TextBox { Text = value.ToString("0.##", CultureInfo.InvariantCulture), Width = 70, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center };

        private static TextBox NumRow(StackPanel panel, string label, double value)
        {
            var row = new DockPanel { Margin = new Thickness(0, 3, 0, 0) };
            TextBox tb = Num(value);
            DockPanel.SetDock(tb, Dock.Right);
            row.Children.Add(tb);
            row.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap });
            panel.Children.Add(row);
            return tb;
        }

        // ------------------------------------------------------------------
        // Lectura de la configuracion desde los controles
        // ------------------------------------------------------------------

        private static bool TryNum(TextBox tb, double min, out double v)
        {
            string t = (tb.Text ?? "").Trim().Replace(',', '.');
            bool ok = double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out v) && v >= min;
            tb.Background = ok ? Brushes.White : new SolidColorBrush(Color.FromRgb(0xFF, 0xD6, 0xD6));
            return ok;
        }

        /// <summary>Vuelca los controles en la configuracion. Devuelve el primer error, o null.</summary>
        private string ReadInto(AppConfig c)
        {
            c.WallContactRule = AppConfig.RuleName(_wallRule.SelectedIndex);
            c.SlabContactRule = AppConfig.RuleName(_slabRule.SelectedIndex);
            c.OnlyConcreteNeighbors = _onlyConcrete.IsChecked == true;
            c.NeighborGenericModels = _generic.IsChecked == true;
            c.FootingSides = _footing.SelectedIndex == 1 ? "ground" : "form";
            c.WriteParameters = _writeParams.IsChecked == true;
            c.CreateSchedule = _createSchedule.IsChecked == true;
            c.OpenSchedule = _openSchedule.IsChecked == true;
            c.ScheduleName = (_scheduleName.Text ?? "").Trim();

            string err = null;
            if (TryNum(_pourLift, 0, out double v)) c.PourLiftMm = v; else err = err ?? "altura por vaciado no valida";
            if (TryNum(_contactTol, 1, out v)) c.ContactToleranceMm = v; else err = err ?? "holgura de contacto no valida";
            if (TryNum(_topTilt, 0, out v) && v <= 89) c.TopFaceMaxTiltDeg = v; else err = err ?? "inclinacion de cara superior no valida (0 a 89)";
            if (TryNum(_bottomTilt, 0, out v) && v <= 89) c.BottomFaceMaxTiltDeg = v; else err = err ?? "inclinacion de fondo no valida (0 a 89)";
            if (TryNum(_widening, 10, out v)) c.MinFootingWideningMm = v; else err = err ?? "ensanche minimo no valido";
            if (c.CreateSchedule && c.ScheduleName.Length == 0) err = err ?? "la tabla necesita un nombre";
            c.Normalize();
            return err;
        }

        // ------------------------------------------------------------------
        // Acciones
        // ------------------------------------------------------------------

        private void Refresh()
        {
            if (_loading) return;
            string err = ReadInto(_cfg);
            _message.Text = err ?? "";

            int selected = _elements.SelectedIndex;
            var rows = new List<ElementRow>();
            double grand = 0, back = 0, front = 0, ends = 0, open = 0, foot = 0;
            int ok = 0;
            foreach (FormworkAnalysis a in Items)
            {
                var r = new ElementRow { Analysis = a, Elemento = a.Tag.Trim(), Marca = a.Mark, Tipo = a.TypeName };
                if (!a.CanBuild)
                {
                    r.IsError = true;
                    r.Diagnostico = "SIN METRAR -> " + a.Error;
                    rows.Add(r);
                    continue;
                }
                Totals t = a.Compute(_cfg);
                ok++;
                grand += t.Total; back += t.StemBack; front += t.StemFront; ends += t.StemEnds; open += t.Openings; foot += t.FootingSides;
                r.Trasdos = Geo.M2(t.StemBack);
                r.Intrados = Geo.M2(t.StemFront);
                r.Extremos = Geo.M2(t.StemEnds);
                r.Vanos = Geo.M2(t.Openings);
                r.Zapata = Geo.M2(t.FootingSides);
                r.DescMuros = Geo.M2(t.DiscountOthers);
                r.DescLosas = Geo.M2(t.DiscountSlabs);
                r.Total = Geo.M2(t.Total);
                r.Etapas = t.Lifts.ToString();
                r.Diagnostico = a.Kind + ": " + a.Detail(_cfg);
                rows.Add(r);
            }
            _elements.ItemsSource = rows;
            if (rows.Count > 0) _elements.SelectedIndex = selected >= 0 && selected < rows.Count ? selected : 0;
            _totals.Text = "TOTAL " + Geo.M2(grand) + " m2 en " + ok + " de " + Items.Count + " elemento(s): trasdos " + Geo.M2(back) +
                           ", intrados " + Geo.M2(front) + ", extremos " + Geo.M2(ends) + ", vanos " + Geo.M2(open) + ", zapata " + Geo.M2(foot) + " m2";
            ShowFaces();
        }

        private void ShowFaces()
        {
            var rows = new List<FaceRow>();
            if (_elements.SelectedItem is ElementRow er && er.Analysis != null && er.Analysis.CanBuild)
            {
                foreach (FacePart p in er.Analysis.Parts.OrderBy(x => x.Zone == Zone.Footing).ThenBy(x => !x.Lateral).ThenBy(x => x.Index))
                {
                    bool formed = p.Formed(_cfg);
                    rows.Add(new FaceRow
                    {
                        Cara = p.Index.ToString(),
                        Zona = p.ZoneText,
                        Tipo = p.RoleText,
                        Orientacion = p.OrientationText,
                        Bruta = Geo.M2(p.GrossArea),
                        Descuento = formed ? Geo.M2(p.Discount(_cfg)) : "-",
                        Encofra = formed ? Geo.M2(p.FormedArea(_cfg)) : "no",
                        Contactos = p.ContactsText(_cfg),
                        Nota = formed ? p.Note : (p.Lateral ? p.Note + " (zapata contra terreno: no se encofra)" : p.Note),
                        IsFormed = formed
                    });
                }
            }
            _faces.ItemsSource = rows;
        }

        private void Recalculate()
        {
            string err = ReadInto(_cfg);
            if (err != null) { _message.Text = err; return; }
            try
            {
                System.Windows.Input.Mouse.OverrideCursor = System.Windows.Input.Cursors.Wait;
                Items = _analyze(_cfg);
            }
            catch (Exception ex)
            {
                _message.Text = "No se pudo recalcular: " + ex.Message;
            }
            finally { System.Windows.Input.Mouse.OverrideCursor = null; }
            Refresh();
        }

        private void Accept()
        {
            var c = _cfg.Clone();
            string err = ReadInto(c);
            if (err != null) { _message.Text = err; return; }
            Result = c;
            DialogResult = true;
            Close();
        }

        private void SaveDefaults()
        {
            var c = _cfg.Clone();
            string err = ReadInto(c);
            if (err != null) { _message.Text = err; return; }
            try
            {
                c.Save();
                _message.Foreground = Brushes.DarkGreen;
                _message.Text = "Guardado en " + AppConfig.ConfigPath();
            }
            catch (Exception ex)
            {
                _message.Foreground = Brushes.Firebrick;
                _message.Text = "No se pudo guardar: " + ex.Message;
            }
        }

        private void CopySummary()
        {
            try
            {
                Clipboard.SetText(MetrarEncofradoCommand.Summary(Items, _cfg));
                _message.Foreground = Brushes.DarkGreen;
                _message.Text = "Resumen copiado al portapapeles.";
            }
            catch (Exception ex)
            {
                _message.Foreground = Brushes.Firebrick;
                _message.Text = "No se pudo copiar: " + ex.Message;
            }
        }
    }
}
