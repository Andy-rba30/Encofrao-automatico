# Metrado de encofrado de muros de contención — add-in Revit 2027

Metra el **encofrado (m² de contacto)** de muros de contención a partir de la
**geometría real del elemento**, sin depender de los nombres de parámetros de tus
familias, y escribe el resultado en una **tabla de planificación** de Revit con una
fila por muro, columnas por partida y totales al pie.

Es el hermano del add-in de [armado automático](https://github.com/Andy-rba30/Acero-automatico):
mismo estilo (pestaña **ARBA**, panel **Muros de contención**, botón **Metrar
encofrado**), misma forma de trabajar (seleccionas los muros, se abre una ventana con
lo que se ha detectado en cada uno, eliges las reglas y confirmas) y mismo
`config.json` junto a la DLL con los valores por defecto. Pero admite **cualquier
forma**: tramos rectos, esquineros en L, muros en T o en U, contrafuertes, escalones,
vanos, muros curvos, muros de sistema sin zapata... porque no lee "la sección" sino
**cada cara del sólido**.

## Qué tiene en cuenta

- **Muros a los costados.** Si un muro toca a otro muro, cimentación, columna o viga
  de concreto, esa zona de contacto no lleva encofrado. La regla por defecto (`auto`)
  se apoya en la unión de geometría de Revit, que es como se modela lo que se vacía
  junto:
  - Elementos **unidos** (Unir geometría) se vacían **monolíticos**: el contacto no
    se encofra en ninguno de los dos.
  - Elementos **sin unir** se vacían **por separado**: el que **se detiene contra el
    otro** (cara de extremo, jamba o fondo) se vacía después y no encofra ese
    contacto; la **cara principal** del otro se encofra completa, como en obra.
  - Se puede forzar `always` (todo contacto se descuenta) o `never` (nada).
- **Losas.** Una losa **unida** al muro (vaciada antes o monolítica) descuenta su
  franja de contacto; una losa **sin unir** se vacía después del muro y la cara del
  muro se encofró completa. También `always` / `never`. El contacto con losas se
  informa siempre aparte (columna *Desc. losas*), así se ve cuánto se ha descontado
  y se puede volver a sumar si el criterio de obra es otro.
- **Lo constructivo.**
  - La **cara superior de la zapata** y la **coronación** no se encofran (superficie
    libre); el **fondo** sobre terreno o solado tampoco.
  - La **pantalla** se mide desde la cara superior de la zapata (junta de
    construcción) y se separa en **trasdós** (lado del talón, contra el terreno) e
    **intradós** (lado de la puntera, cara vista, normalmente caravista), porque
    suelen ser partidas distintas.
  - Los **extremos libres** de la pantalla se encofran; los que tocan otro muro no.
  - Las **caras laterales de la zapata** se encofran (columna propia) o, si se
    elige *zapata vaciada contra el terreno*, se informan pero no suman.
  - **Vanos**: las jambas y los dinteles (caras que miran hacia abajo en el aire) se
    encofran; el alféizar no.
  - Caras **inclinadas**: un talud se encofra como cara lateral; una cara que mira
    hacia arriba con menos de 30° (configurable) es superficie libre.
  - **Etapas de vaciado**: con una altura máxima por vaciado se informa el número
    de etapas de la pantalla (para planificar el trepado del encofrado).
- Solo cuentan como vecinos los elementos **de concreto** (por material o por
  material estructural de la familia); albañilería, acero o madera se construyen
  después y no quitan encofrado. Se puede desactivar.

## Cómo funciona

1. **Sólido real.** Se toma el sólido del elemento tal y como lo ve Revit, con las
   uniones y cortes aplicados: es el concreto que se va a vaciar. Si el elemento
   tiene varios sólidos se metran todos (con aviso).
2. **Zapata.** Se buscan las cotas con caras planas horizontales hacia arriba y se
   comprueba, con dos rebanadas finas, que justo debajo la sección es más ancha que
   justo encima (`minFootingWideningMm`). Un escalón de coronación no cumple (la
   sección se acorta, no se ensancha); un contrafuerte cumple pero ensancha menos
   que una zapata, y se elige el candidato con mayor relación de áreas. Si no hay
   zapata (muro de sistema, muro sobre losa) todo se mide como pantalla, con aviso.
   `sectionOverrides` permite forzar la cota o declarar que no hay zapata.
3. **Cada cara** se clasifica por su normal y su posición:
   - hacia arriba (≤ `topFaceMaxTiltDeg`) → superficie libre, no se encofra;
   - hacia abajo en la base → fondo, no se encofra; hacia abajo en el aire → se
     encofra (dintel, voladizo);
   - lateral: **pantalla** o **zapata** según su cota (una cara que cruza el plano
     de zapata, como el frente de un muro en L, se parte en dos con el prisma de la
     cara y un semiespacio); **extremo** si es más estrecha que el concreto que hay
     detrás (largo del muro) y llega al borde de la planta, **jamba de vano** si no
     llega al borde, **cara principal** en otro caso; en las principales, **trasdós
     / intradós** comparando el vuelo de zapata de cada lado (el talón, mayor, es
     el trasdós; con vuelos iguales o sin zapata, según la orientación de la
     familia).
4. **Contactos.** Sobre cada cara plana se levanta un **prisma fino hacia fuera**
   (`contactToleranceMm`, 20 mm) y se interseca con los sólidos de los elementos
   vecinos (muros, cimentaciones, losas, columnas, vigas cuya caja envolvente toca
   la del muro); el área de contacto es el área del resultado vista a lo largo de la
   normal, así un elemento a menos de 20 mm cuenta como en contacto. En caras
   curvas, que no admiten el prisma exacto, se muestrea una malla de puntos a media
   holgura de la cara (`sampleStepMm`).
5. **Reglas.** Los contactos se guardan por vecino y las reglas se aplican al
   calcular los totales, así la ventana puede cambiarlas y ver el efecto al momento
   sin volver a leer la geometría.

## Interfaz gráfica

Al lanzar el comando con uno o varios muros seleccionados se abre una ventana:

1. **Elementos seleccionados**: una fila por elemento con marca, tipo, m² de trasdós,
   intradós, extremos, vanos, zapata, lo descontado por muros y por losas, el total,
   las etapas de vaciado y el diagnóstico (largo, altura de pantalla, canto de
   zapata, número de caras y vecinos, avisos). Los elementos que no se pueden
   metrar salen en rojo con el motivo.
2. **Caras del elemento marcado**: cada cara (o parte) con su zona, tipo,
   orientación (acimut, vertical o inclinada, curva), área bruta, descuento, área
   que se encofra, **qué toca y qué regla se le aplica** (por ejemplo `losa [1234
   Losa 20 cm] 1.35 m2: unido: vaciado monolitico, no se encofra`) y una nota. Las
   caras que no se encofran salen en gris.
3. **Reglas constructivas**: contacto con muros y otros elementos de concreto
   (auto / siempre / nunca), contacto con losas (auto / siempre / nunca), solo
   vecinos de concreto, modelos genéricos como vecinos, zapata encofrada o contra
   el terreno, altura máxima por vaciado. Cualquier cambio recalcula las tablas al
   momento.
4. **Geometría**: holgura de contacto, inclinación máxima de cara superior y de
   fondo, ensanche mínimo de la zapata; **Recalcular geometría** vuelve a leer los
   elementos con esos valores.
5. **Salida**: escribir los parámetros `ENC ...`, crear o actualizar la tabla, su
   nombre y si abrirla al terminar.
6. **Metrar y crear tabla** escribe los parámetros y la tabla con esos valores solo
   para esta ejecución. **Guardar como valores por defecto** los escribe en el
   `config.json` que está junto a la DLL. **Copiar resumen** deja el resumen en el
   portapapeles.

## Tabla de planificación

Se crea una tabla **multicategoría** (sirve a la vez para cimentaciones
estructurales y muros) llamada por defecto `ARBA - Metrado de encofrado (muros de
contencion)`, con columnas *Familia y tipo*, *Marca* y los parámetros compartidos
del metrado, filtrada a los elementos metrados, ordenada por marca y con **totales
al pie** de las columnas de área (formato m², dos decimales). En las siguientes
ejecuciones se reutiliza: solo se añaden los campos que falten, para respetar el
formato que le hayas dado.

Los resultados van en **parámetros compartidos de ejemplar** (grupo *ARBA
Encofrado*, en *Datos*), creados la primera vez con **GUID fijos** en un archivo de
parámetros compartidos propio junto a la DLL (`ARBA_Encofrado_parametros_compartidos.txt`),
así valen en cualquier proyecto y se pueden usar en otras tablas, filtros o
etiquetas:

| Parámetro | Contenido |
|---|---|
| `ENC Pantalla trasdos` | m² de la cara de trasdós de la pantalla |
| `ENC Pantalla intrados` | m² de la cara de intradós (cara vista) |
| `ENC Pantalla extremos` | m² de extremos libres de la pantalla |
| `ENC Vanos` | m² de jambas, dinteles y otras caras inferiores en el aire |
| `ENC Zapata laterales` | m² de caras laterales de la zapata (0 contra el terreno) |
| `ENC Total` | suma de las anteriores |
| `ENC Contacto muros` | m² descontados por muros, cimentaciones, columnas y vigas |
| `ENC Contacto losas` | m² descontados por losas |
| `ENC Longitud` | mayor extensión en planta de la coronación |
| `ENC Altura pantalla` | de la cara superior de zapata a coronación |
| `ENC Canto zapata` | 0 si no se reconoció zapata |
| `ENC Etapas vaciado` | número de vaciados de la pantalla |
| `ENC Observaciones` | avisos (sin zapata, caras curvas, contactos no descontados...) |
| `ENC Metrado` | fecha y reglas del último metrado |

## Montaje

1. `dotnet build -c Debug` — en Windows el `.csproj` copia la DLL, el `config.json`
   y el `.addin` a `%AppData%\Autodesk\Revit\Addins\2027\` (propiedad
   `DeployToRevit`; `-p:DeployToRevit=false` para no hacerlo). Hace falta el SDK de
   .NET 10 (`global.json`); los paquetes `Nice3point.Revit.Api.*` 2027 se descargan
   de NuGet, no hace falta Revit para compilar, y en Linux/CI compila igual gracias a
   `EnableWindowsTargeting`.
2. Abre Revit (si estaba abierto, ciérralo y vuelve a abrirlo). En la pestaña
   **ARBA**, panel **Muros de contención**, aparece **Metrar encofrado** junto al
   botón de armado si también está instalado. El comando sigue también en
   **Complementos → Herramientas externas**.
3. Selecciona uno o varios muros (cimentaciones estructurales o muros; las
   categorías admitidas se configuran en `hostCategories`) y pulsa el botón. Si no
   seleccionas nada antes, el comando te pide que elijas.
4. Revisa las caras, los contactos y las reglas en la ventana y pulsa **Metrar y
   crear tabla**. La tabla se abre al terminar.

Puedes volver a metrar cuando cambie el modelo: los parámetros se sobreescriben y la
tabla se actualiza sola.

## Ajustes en `config.json`

- `wallContactRule`, `slabContactRule`: `"auto"`, `"always"` o `"never"` (ver
  arriba).
- `onlyConcreteNeighbors` (true), `neighborGenericModels` (false).
- `footingSides`: `"form"` (encofrar laterales de zapata) o `"ground"` (contra el
  terreno).
- `pourLiftMm`: altura máxima por vaciado (0 = un solo vaciado).
- `contactToleranceMm` (20): holgura de contacto y profundidad del prisma.
- `topFaceMaxTiltDeg` (30), `bottomFaceMaxTiltDeg` (30): hasta qué inclinación una
  cara hacia arriba es superficie libre y una hacia abajo es fondo apoyado.
- `minFootingWideningMm` (100), `minStemHeightMm` (300), `minFootingThicknessMm`
  (150): reconocimiento de la zapata.
- `minFaceAreaCm2` (1): caras más pequeñas se ignoran.
- `sampleStepMm` (50): paso del muestreo en caras curvas. `probeSliceMm` (10):
  espesor de las rebanadas de sondeo.
- `writeParameters`, `createSchedule`, `scheduleName`, `openSchedule`.
- `hostCategories`: categorías admitidas (`StructuralFoundation`, `Walls`).
- `sectionOverrides`: por nombre de tipo, `footingTopMm` (cota de la cara superior
  de zapata desde la base, si la detección falla) y `noFooting`.

## Limitaciones conocidas

- No lee elementos de **vínculos** (modelos enlazados): solo los del propio
  proyecto cuentan como vecinos.
- En caras **curvas** el contacto se estima por muestreo y el reparto entre zapata y
  pantalla de una cara curva que cruza el plano de zapata es lineal en altura.
- Un vecino que toque dos veces la misma cara (o dos vecinos superpuestos) se
  descuenta acotando al área de la cara, sin unir los contactos entre sí.
- La clasificación trasdós / intradós con vuelos iguales (muro en T simétrico) o sin
  zapata se hace por la orientación de la familia (cara frontal = trasdós): revisa la
  nota de la cara en la ventana.
- La longitud es la mayor extensión en planta de la coronación (en un esquinero, la
  del ala larga), no el desarrollo del eje.
- No hace cálculos de encofrado (paneles, puntales, presiones): solo mide el área de
  contacto y la separa por partidas.
