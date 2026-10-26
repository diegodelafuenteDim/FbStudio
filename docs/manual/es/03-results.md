# Resultados

La grilla donde aparece lo que devuelve una consulta: moverse, filtrar, ordenar, agrupar, sacar totales, copiar, ver un
valor entero, graficar y exportar.

## La grilla

Al ejecutar un SELECT, las filas aparecen en la pestaña **Resultados**, debajo del editor (`Ctrl+1` la elige). La misma
grilla se usa en la pestaña **Datos** de una tabla, con algunas diferencias que se cuentan en [La pestaña Datos](04-data.md).

- Arriba está la barra de la grilla: el panel para agrupar y el botón **Panel de filtros**. Con filtros puestos aparece
  **Quitar filtros**, y con grupos, **Expandir todo** y **Contraer todo**.
- Debajo de los encabezados está la fila **Filtro**.
- A la izquierda, la columna `#` numera las filas.
- Abajo, el pie: a la izquierda cuántas filas se ven y, debajo de cada columna, su total si elegiste uno.

La grilla trabaja sobre las filas que ya se trajeron, hasta el **Límite** de la barra de herramientas (ver
[El editor de SQL](02-editor.md)). Filtrar, ordenar o agrupar no vuelve a consultar la base: si se alcanzó el límite,
lo que no se trajo no entra. Para filtrar toda una tabla, poné la condición en el WHERE de la consulta o usá la pestaña
**Datos**, que filtra en el servidor.

## Moverse y elegir

Un clic en una celda la elige, junto con su fila. Con el teclado:

| Tecla | Qué hace |
| --- | --- |
| `↑` `↓` `←` `→` | Pasa a la celda de al lado |
| `RePág` / `AvPág` | Sube o baja una pantalla |
| `Inicio` / `Fin` | Primera o última columna de la fila |
| `Ctrl+Inicio` / `Ctrl+Fin` | Primera celda o última celda de la grilla |
| `Shift+↑` / `Shift+↓` (también con `RePág` y `AvPág`) | Elige un rango de filas |
| `Shift+clic` | Elige las filas entre la actual y la del clic |
| `Ctrl+clic` | Suma o saca una fila de las elegidas |
| `Ctrl+A` | Elige todas las filas |

La rueda del mouse desplaza hacia arriba y abajo; con `Shift`, hacia los costados. Izquierda y derecha siguen el orden
en que se ven las columnas, aunque las hayas movido, y saltean las ocultas.

## La fila de filtros

Cada columna tiene su cuadro en la fila **Filtro**, debajo del encabezado:

1. Hacé clic en el cuadro de la columna.
2. Escribí el filtro (ver [Sintaxis de los filtros](#sintaxis-de-los-filtros)). Se aplica solo cuando dejás de
   escribir.
3. `Enter` lo confirma y vuelve a la grilla.

El cuadro vacío muestra un ejemplo según el tipo: `>100, <=5, =3` en los números, `2026-07-01` en las fechas y
`texto, PÉREZ*, null` en el resto. Los filtros de varias columnas se suman: una fila tiene que cumplir todos.

Con algún filtro puesto, la barra de arriba dice **Filtrado: N de M filas** y aparece **Quitar filtros**, que los saca
todos. Para sacar uno solo, borrá su texto o usá clic derecho en el encabezado › **Quitar el filtro de esta columna**.

Si el texto no se entiende para el tipo de la columna (por ejemplo `>abc` en una columna numérica), el filtro se ve en
rojo, no filtra y la barra de arriba explica por qué.

## Sintaxis de los filtros

Lo que se escribe en la fila de filtros, en el panel de filtros y lo que arma la ventanita de cada columna es el mismo
texto. Cambia según el tipo de la columna.

### Texto

| Escribís | Muestra las filas que… |
| --- | --- |
| `perez` | contienen «perez», sin distinguir mayúsculas ni acentos (encuentra «PÉREZ») |
| `GAR*` | empiezan con «GAR» |
| `*EZ` | terminan con «EZ» |
| `*AR*` | contienen «AR» (`*` y `%` reemplazan cualquier cantidad de caracteres) |
| `!perez` | no contienen «perez» (las que tienen NULL también entran) |
| `=GARCIA` | son iguales a «GARCIA», sin distinguir mayúsculas ni acentos |
| `<>GARCIA` o `!=GARCIA` | son distintas de «GARCIA» |
| `>M`, `<F` | van después de «M» o antes de «F» en orden alfabético |

### Números

| Escribís | Muestra las filas que… |
| --- | --- |
| `1500` | valen 1500 |
| `>100`, `>=100`, `<5`, `<=5` | son mayores, mayores o iguales, menores, menores o iguales |
| `=3`, `<>0` | son iguales o distintas |
| `10..20` | están entre 10 y 20, los dos incluidos |

El decimal puede ir con coma o con punto. Si escribís una coma, la coma es el decimal y el punto separa los miles:
`1.234,5` es mil doscientos treinta y cuatro con cinco.

### Fechas

Valen para las columnas DATE y TIMESTAMP.

| Escribís | Muestra las filas que… |
| --- | --- |
| `2026-07-01` o `01/07/2026` | son de ese día (en una columna con hora, todo el día) |
| `2026-07-01 08:30` | son de ese momento exacto |
| `>=2026-07-01`, `<2026-08-01` | son desde o antes de esa fecha |
| `<=2026-07-05`, `>2026-07-05` | son hasta el 5 (incluye todo ese día) o desde el 6 |
| `2026-07-01..2026-07-31` | están entre los dos días, los dos enteros |
| `hoy`, `ayer`, `mañana` | son de ese día |
| `esta semana`, `semana pasada` | son de esa semana (las semanas empiezan el lunes) |
| `este mes`, `mes pasado` | son de ese mes |
| `este año`, `año pasado` | son de ese año |
| `últimos 7 días` | son de los últimos 7 días, con hoy incluido (sirve cualquier número) |

Los períodos se pueden escribir con o sin acentos, y también en inglés: `today`, `this month`, `last 7 days`.

### Horas

| Escribís | Muestra las filas que… |
| --- | --- |
| `08:30` | tienen esa hora |
| `>=08:00`, `<12:00` | son desde o antes de esa hora |
| `08:00..12:00` | están entre las dos horas, las dos incluidas |

### BOOLEAN

`sí` o `no`. También valen `s`, `n`, `true`, `false`, `1` y `0`.

### Varios valores y NULL

Sirven en cualquier tipo de columna:

| Escribís | Muestra las filas que… |
| --- | --- |
| `1;5;9` | en números, valen 1, 5 o 9 (los valores se separan con `;`) |
| `perez;gomez` | en texto, contienen cualquiera de los dos |
| `=PENDIENTE;CONFIRMADO` | son iguales a cualquiera (el operador de adelante vale para los que no traen el suyo) |
| `null` o `vacío` | tienen NULL |
| `!null` o `con valor` | tienen algún valor |

## La ventanita de filtro

Cada cuadro de la fila de filtros tiene un `▾` a la derecha. Abre una ventanita para armar el filtro sin escribirlo,
con opciones según el tipo de la columna. También se abre con clic derecho en el encabezado › **Filtrar…**.

| Tipo | Opciones |
| --- | --- |
| Texto | contiene, no contiene, empieza con, termina con, es igual a, es distinto de, vacío (NULL), con valor |
| Números | es igual a, es distinto de, mayor que, mayor o igual que, menor que, menor o igual que, entre, vacío (NULL), con valor |
| Fechas | es el día, en el período, entre, desde, hasta (incluido), antes de, después de, otro día que, vacío (NULL), con valor |
| Horas | es igual a, entre, desde, hasta (incluido), vacío (NULL), con valor |
| BOOLEAN | sí, no, vacío (NULL), con valor |

- **En el período** ofrece hoy, ayer, esta semana, semana pasada, este mes, mes pasado, este año, últimos 7 días y
  últimos 30 días.
- En las fechas, el botón del calendario (**Elegir el día en un calendario**) escribe el día que elegís.
- Debajo, **Valores (tildá uno o varios)** lista los valores de la columna con cuántas filas tiene cada uno, teniendo
  en cuenta los filtros de las demás columnas. En una columna con fecha y hora van por día (**Días (tildá uno o
  varios)**). Se muestran los 500 más repetidos, NULL figura como **(vacío)** y con más de diez valores aparece
  **Buscar un valor**. Si tildás valores, mandan sobre la opción de arriba.
- Abajo se ve cómo va a quedar en la fila de filtros (**En la fila de filtros: …**), con el error si lo hay.

**Aplicar** (o `Enter` en un cuadro) pone el filtro, **Quitar filtro** lo saca y **Cancelar** (o `Esc`) cierra sin
cambiar nada. Como el resultado es el mismo texto de la fila de filtros, después lo podés corregir a mano.

Las columnas de tipo BLOB no listan valores.

## El panel de filtros

Con muchas columnas, el panel de filtros las muestra todas en una lista, cada una con su filtro. Sirve para encontrar
una columna por nombre y para ver juntos los filtros puestos, aunque queden fuera de la pantalla.

- Se abre con el botón **Panel de filtros** de la barra de arriba, o con clic derecho en un encabezado › **Filtrar en
  el panel de filtros** (que lleva el cursor a esa columna).
- Aparece a la derecha de la grilla. El título dice cuántas columnas tienen filtro.
- **Buscar columna** achica la lista por nombre y **Solo las que tienen filtro** deja solo las filtradas.
- Cada columna muestra su tipo (y **oculta** si la ocultaste) y el nombre va en negrita si tiene filtro.
- El cuadro de cada una es igual a la fila de filtros: `Enter` (o salir del cuadro) aplica y `Esc` vuelve a lo que
  había. El `▾` abre la ventanita de esa columna.
- **Quitar todos** saca todos los filtros.

Se cierra con la cruz de arriba (**Cerrar el panel de filtros**) o con el mismo botón **Panel de filtros**.

## Ordenar

- Clic en un encabezado: orden ascendente. Otro clic: descendente. `Ctrl+clic`: quita el orden.
- También con clic derecho en el encabezado › **Orden ascendente**, **Orden descendente** o **Quitar orden**.

La flecha `▲` o `▼` en el encabezado marca la columna ordenada. Se ordena según el tipo (los números como números y las
fechas como fechas), en segundo plano: con muchas filas la barra de arriba dice **Procesando…** mientras tanto.

## Agrupar

Agrupar junta las filas que tienen el mismo valor en una columna. Solo se puede en **Resultados**.

1. Arrastrá un encabezado hasta el panel de arriba (dice «Arrastrá un encabezado acá…»), o usá clic derecho en el
   encabezado › **Agrupar por esta columna**.
2. Cada grupo muestra la columna, el valor y cuántas filas tiene. Arrancan cerrados: un clic en el grupo (o `Enter` o
   `Espacio`) lo abre o lo cierra.
3. **Expandir todo** y **Contraer todo** abren o cierran todos.

Se puede agrupar por varias columnas: cada una agrega un nivel adentro del anterior. Cada columna agrupada aparece en el
panel de arriba con una cruz: un clic la quita (también **Desagrupar esta columna** en el encabezado). Los grupos
siguen el orden: si ordenás una columna agrupada en forma descendente, sus grupos se invierten.

## Totales

El pie de la grilla muestra un total por columna:

1. Hacé clic derecho en el pie, debajo de la columna.
2. Elegí **Cantidad**, **Suma**, **Promedio**, **Mínimo** o **Máximo** (**Sin total** lo saca).

**Suma** y **Promedio** solo se ofrecen en las columnas numéricas. Los totales se calculan sobre las filas que se ven
(con los filtros puestos) y no cuentan los NULL. Se muestran con un prefijo: `n =` la cantidad, `Σ` la suma, `prom.`,
`mín.` y `máx.`.

Los totales están solo en **Resultados**: en la pestaña **Datos** el pie dice cuántas filas se cargaron.

## Columnas

Todo lo de columnas es solo la vista: no cambia la consulta, y el orden, los filtros y los totales siguen con su columna.

### Reordenar

Arrastrá un encabezado a otro lugar: una línea marca dónde va a quedar y, cerca de los bordes, la grilla se corre sola.

### Ancho

- Arrastrá el borde derecho de un encabezado.
- Doble clic en el borde, o clic derecho › **Ajustar ancho al contenido**, lo ajusta a lo que se ve.

Al llegar un resultado, cada ancho se ajusta solo al encabezado y a las primeras filas.

### Ocultar y mostrar

- Clic derecho en el encabezado › **Ocultar esta columna**. La última que queda a la vista no se puede ocultar.
- **Mostrar columnas ocultas (N)** lista las ocultas para mostrarlas de a una, o todas con **Mostrar todas**.

Una columna oculta sigue filtrando, ordenando y con su total, y va igual en **Copiar como INSERT**, **Copiar como
UPDATE** y en las exportaciones. No se copia como texto y el teclado la saltea.

### Restaurar

Clic derecho en un encabezado › **Restaurar el orden y el ancho de las columnas**: vuelve al orden de la consulta, a los
anchos según el contenido y con todas las columnas a la vista.

En **Resultados**, cada ejecución arranca con las columnas como vienen. En la pestaña **Datos**, el orden, el ancho y
las ocultas se recuerdan para cada tabla (ver [Columnas recordadas](04-data.md#columnas-recordadas)).

## Copiar

Desde el teclado o con clic derecho en una celda o en un encabezado:

| Qué | Dónde | Copia |
| --- | --- | --- |
| **Copiar celda** | `Ctrl+C`, celda | El valor como se ve |
| **Copiar fila con encabezados** | `Ctrl+Shift+C`, celda | La fila, con los nombres de las columnas |
| **Copiar las N filas seleccionadas con encabezados** | `Ctrl+C` con varias filas elegidas, celda | Las filas elegidas |
| **Copiar todas las filas (N) con encabezados** | Celda | Todas las filas que se ven, en su orden |
| **Copiar columna (N valores)** | Encabezado | Los valores de la columna en las filas que se ven, uno por renglón |
| **Copiar la fila como tabla de Markdown** | Celda | La fila (o las elegidas) como tabla de Markdown |
| **Copiar como «COLUMNA IN (…)»** | Encabezado | Los valores de las filas que se ven como condición IN |
| **Copiar COLUMNA de las filas elegidas como IN (…)** | Celda | Lo mismo, solo con las filas elegidas |
| **Copiar como INSERT** / **Copiar como UPDATE** | Celda | Las filas elegidas como sentencias |

### Como texto

Las filas se copian separadas por tabulaciones, listas para pegar en una planilla: NULL queda vacío, los números van
con el separador decimal del sistema y los valores pierden las tabulaciones y los saltos de línea que tengan adentro.
Las columnas van en el orden en que se ven, sin las ocultas.

Con más de 100.000 filas, FbStudio pregunta antes (**Copiar igual** o **Cancelar**): para tantas conviene exportar.

### Como tabla de Markdown

Para pegar en un ticket, un chat o un documento: encabezado, una fila por renglón, los números alineados a la derecha y
las columnas en el orden en que se ven. Con varias filas elegidas, el menú dice **Copiar las N filas como tabla de
Markdown**.

### Como «COLUMNA IN (…)»

Arma una condición para pegar en otra consulta, por ejemplo `ID IN (1041, 1050, 1063)`:

- sin valores repetidos ni NULL;
- con los literales de Firebird (los textos entre comillas, las fechas como `DATE '2026-07-01'`);
- con más de 1.500 valores, en varios IN unidos con OR.

Desde el encabezado toma las filas que se ven; desde una celda, solo las filas elegidas (el menú dice el nombre de la
columna y cuántas son).

### Como INSERT o UPDATE

Las filas elegidas, una sentencia por fila, sin COMMIT, con los valores como literales de Firebird. Las columnas van en
el orden en que se ven, incluidas las ocultas.

- La tabla es la del FROM de la consulta. Si no se reconoce, el INSERT va a una tabla llamada `TABLA`, que tenés que
  cambiar al pegar.
- Las columnas que no son de esa tabla o que son calculadas quedan afuera, y la barra de estado dice cuáles.
- **Copiar como UPDATE** necesita la clave primaria de la tabla y que el resultado traiga todas sus columnas: arma
  `UPDATE … SET` con lo demás y `WHERE` con la clave.

En la pestaña **Datos** la tabla y su clave se conocen siempre.

## Filtrar por un valor

Clic derecho en una celda:

- **Filtrar por este valor** deja solo las filas con ese valor en la columna (escribe `=valor` en la fila de filtros, o
  `null` si la celda es NULL).
- **Excluir este valor** las saca (escribe `<>valor`, o `!null` si la celda es NULL). Las filas con NULL en esa columna
  también quedan afuera.

## Ver el valor

Una celda muestra un solo renglón y corta lo que no entra. Para verla entera: clic derecho › **Ver el valor…**, o doble
clic en la celda (en **Datos**, el doble clic edita; ahí se usa el menú, salvo en las celdas que no se editan).

Se abre la ventana **Valor de COLUMNA**. Arriba dice el tipo, qué contiene y su tamaño, y deja elegir cómo verlo:

| Contenido | Formas de verlo |
| --- | --- |
| Texto largo | **Texto** |
| JSON o XML | **Con sangría** y **Texto** |
| RTF | **Texto del RTF** (sin formato) y **Código RTF** |
| Imagen PNG, JPEG, GIF o BMP | **Imagen** y **Hexadecimal** |
| Otro binario (PDF, ZIP o desconocido) | **Hexadecimal** |
| Texto guardado en un blob binario | El texto, y además **Hexadecimal** |

- **Ajustar renglones** corta los renglones largos al ancho de la ventana.
- **Copiar** copia el texto que se está viendo.
- **Guardar en archivo…** guarda el valor tal cual: los bytes de un binario o el texto en UTF-8, con la extensión según
  lo que es (`.png`, `.pdf`, `.json`, `.rtf`, `.txt`…).

El hexadecimal muestra hasta 64 KB; **Guardar en archivo…** guarda el valor entero. La ventana solo mira (no edita nada)
y se pueden tener varias abiertas.

## Ver la fila en vertical

Para tablas anchas, de muchas columnas: clic derecho en una celda › **Ver la fila en vertical…**. Se abre una ventana con
un renglón por columna: **Columna**, **Tipo** y **Valor**.

- Es una grilla como esta: se puede filtrar y ordenar, por ejemplo para encontrar una columna por nombre.
- `◀` y `▶` pasan a la fila anterior o a la siguiente de las que se ven en la grilla (con sus filtros y su orden).
- Las columnas van en el orden en que se ven, sin las ocultas.

## Buscar en los resultados

Con la grilla elegida (un clic en ella), `Ctrl+F` abre **Buscar en los resultados…** arriba a la derecha. En el editor,
`Ctrl+F` sigue buscando en el texto.

- Busca en el texto de las celdas como se ve, sin distinguir mayúsculas ni acentos. NULL no coincide con nada.
- Recorre las filas que se ven (con los filtros) y, en cada una, las columnas en su orden, sin las ocultas.
- Va a la primera coincidencia desde la celda elegida y muestra **3 de 27**, o **Sin coincidencias**.
- `Enter` (o `F3`, o `▼`) va a la siguiente y `Shift+Enter` (o `▲`) a la anterior; al final vuelve a empezar.
- `Esc` (o la cruz) cierra la búsqueda.

Con más de 100.000 coincidencias deja de buscar y el contador lo marca con `+`.

## Gráfico

Un gráfico rápido de lo que se ve: clic derecho en una celda › **Gráfico…**, o en un encabezado (esa columna va como
etiqueta o como valor, según su tipo).

- **Etiquetas (X):** la columna que va en el eje horizontal.
- **Valor (Y, suma):** una columna numérica, sumada para cada etiqueta, o **(cantidad de filas)**.
- **Barras** o **Línea**: si las etiquetas son fechas u horas, arranca con línea.

Usa las filas que se ven en la grilla, con sus filtros. Si las etiquetas son números o fechas, se ordenan; si no, quedan
en el orden en que aparecen. Más de 500 etiquetas distintas se cortan (y se avisa). Al pasar el mouse se ve cada valor,
y **Guardar PNG…** lo guarda como imagen.

## Exportar

Exportar guarda en un archivo el resultado de la consulta de la pestaña. Está en el menú **Consulta** y en el botón
**Exportar** de la barra de herramientas:

| Menú **Consulta** | Botón **Exportar** | Archivo |
| --- | --- | --- |
| **Exportar a CSV…** | **CSV para Excel (; y coma decimal)** | `.csv` |
| **Exportar a Excel (.xlsx)…** | **Excel (.xlsx)** | `.xlsx` |
| **Exportar como INSERT (.sql)…** | **Script INSERT (.sql)** | `.sql` |
| **Exportar a JSON…** | **JSON (.json)** | `.json` |
| **Exportar a Markdown…** | **Markdown (.md)** | `.md` |

> **Ojo:** se exportan todas las filas cargadas y todas las columnas, en el orden de la consulta. Los filtros, el orden,
> los grupos y las columnas ocultas de la grilla no se aplican. Para exportar menos, ajustá la consulta.

### Cada formato

- **CSV:** separado por `;` y con coma decimal, como lo abre Excel en español; en UTF-8 con BOM (así se ven bien los
  acentos) y con encabezado. NULL queda vacío, los números van sin separador de miles, las fechas en formato ISO y los
  binarios en hexadecimal. Un texto que empieza con `=`, `+`, `-` o `@` lleva una `'` adelante para que Excel no lo
  tome como fórmula.
- **Excel:** los números como números y las fechas como fechas, el encabezado en negrita, con filtro y fijo al
  desplazarse. Pasado el máximo de filas de una hoja de Excel, sigue en otra hoja.
- **Script INSERT:** primero pide la tabla destino (propone la del FROM). Escribe un INSERT por fila, con COMMIT cada
  1.000 filas y al final; se puede ejecutar en FbStudio o en otra herramienta de Firebird.
- **JSON:** una lista de objetos, uno por fila, con el nombre de cada columna (una repetida lleva `_2`, `_3`…). Los
  números y los BOOLEAN van como tales, las fechas en ISO 8601, los binarios en base64 y NULL como `null`.
- **Markdown:** una tabla, con los números alineados a la derecha.

### Todo o solo lo cargado

Si el SELECT llegó al **Límite** de filas o lo cancelaste, la grilla no tiene todo. Al exportar, FbStudio pregunta:

- **Exportar TODO (re-ejecuta la consulta)**: vuelve a ejecutar el SELECT en el servidor, sin límite, y escribe las
  filas en el archivo a medida que llegan (no las carga en memoria). Mientras tanto cuenta las filas con otra conexión,
  para mostrar el avance y cuánto falta.
- **Solo las N cargadas**: exporta lo que ya está en la grilla.

La barra de estado muestra el avance y, al terminar, cuántas filas se escribieron y dónde. **Cancelar** (o `Esc`) corta
la exportación, y el archivo queda incompleto.
