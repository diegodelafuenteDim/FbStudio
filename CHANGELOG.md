# Changelog · Registro de cambios

What changed in each version of FbStudio, in English and in Spanish. The section of a version is also shown on its
[release page](https://github.com/diegodelafuenteDim/FbStudio/releases) and in FbStudio under Help → What's new.

Qué cambió en cada versión de FbStudio, en inglés y en español. La sección de cada versión también aparece en su
[página de descarga](https://github.com/diegodelafuenteDim/FbStudio/releases) y en FbStudio, en Ayuda → Novedades.

## [Unreleased]

### English

### Español

## [0.3.0] - 2026-10-26

### English

- Full user manual in Spanish and English: Help › Manual opens it in the browser, without internet, and F1 opens the
  chapter for what you are using (editor, grid, Data tab, tree, monitoring, maintenance, scripts). It is also in
  docs/manual on GitHub.
- Filter panel: every column in a list with its filter (the same text as the filter row), to find a column by name and
  see all the filters at once; open it from the grid's top bar or the column header menu.
- Data tab: "Count when filtering" counts the matching rows after each read with a filter or a WHERE (remembered for
  every table).
- Import can update the rows already in the table by their key and add the rest (UPDATE OR INSERT; the key is the
  primary key or the columns you tick).
- Import into a new table (tree menu of the database or of Tables): a table with one column per column of the file,
  typed by what it holds, with names and types you can change first; if the import fails, the new table is dropped
  again.
- Users and permissions "By object": pick a table, view, procedure or other object and see who has which permission on
  it, with the same checkboxes and "Apply"; also from the tree with "Permissions…" on an object.
- Query → "Run script from file…": runs a .sql file of any size without loading it into the editor, like Ctrl+F9 (same
  safety checks, stop or continue on errors, the log with the file's line numbers); the history keeps the file name.
- Format SQL now also formats procedures, functions, triggers and EXECUTE BLOCK: one statement per line, indented by
  BEGIN … END, IF … ELSE, WHILE and FOR SELECT … DO, with the queries inside formatted; a long CASE goes one WHEN per
  line.
- Diagrams: "Path to…" finds the shortest chain of foreign keys between two tables (avoiding the most referenced ones
  when possible), adds the tables in between and highlights it.
- Trace: "Compare with a log…" sets a saved trace (an earlier period) against the current one, query by query: slower,
  faster, new or gone, by the average per run, ordered by how much the total time changed.
- Open and save .sql files in the editor (Query → Open .sql file…, Save, Save as…, Open recent; Ctrl+O, Ctrl+S,
  Ctrl+Shift+S): the tab takes the file name, marks unsaved changes with "*" and asks before closing or quitting; the
  encoding is kept (UTF-8 or Windows-1252).
- Diagrams: "Save diagram…" keeps a .fbsdiagram with the options, the tables and where each one is, and Tools → "Open
  saved diagram…" opens it on any database as it was; tables moved by hand stay put when others are added.
- Areas in more tools: the database script and the database comparison can be limited to an area (its objects, the
  domains they use and their permissions, without foreign keys to tables outside), and table statistics can tick the
  tables of an area.
- Diagrams: "Copy as Mermaid" and export to Mermaid (.mmd or .md), the text format GitHub, GitLab and many wikis draw by
  themselves.
- Users and permissions: structure permissions of Firebird 3 (create tables, alter any procedure, drop any domain, alter
  the database…) for users and roles, with the same checkboxes and "Apply".
- Generate test data for a table (table menu in the tree): made-up rows with what suits each column's type and name,
  foreign keys with existing values and the key numbered from the highest, with a preview first; only in Development and
  Test.
- Find and replace in the SQL editor (Ctrl+F, Ctrl+H, F3 and Shift+F3): every match highlighted, "3 of 12", match case,
  whole word and regular expressions (with $1… in the replacement); "Replace all" undoes with a single Ctrl+Z.
- Query tabs are remembered on close and reopened on the next start, each with its database and text (encrypted like the
  history; a .sql file is read again from disk). Can be turned off in Options › Editor.
- After JOIN, autocomplete first suggests the tables related by a foreign key (or a logical "#FK_" comment) to the ones
  already in the FROM, with a free alias and the ON clause built: "TURNOS t ON t.ID = td.TURNO".
- Tables and views in the tree have "Generate SQL": SELECT with all columns and, for tables, INSERT, UPDATE and DELETE
  by primary key and UPDATE OR INSERT, with ":COLUMN" parameters, in a new query (not run). Without a primary key the
  condition is left to fill in, so it cannot run by mistake.
- Editor commands in Query › Edit text: comment or uncomment lines (Ctrl+/), duplicate line or selection (Ctrl+D), move
  lines (Alt+Up/Down), upper and lower case (Ctrl+Shift+U, Ctrl+U) and go to line (Ctrl+G); each one undoes with a
  single Ctrl+Z.
- "View value…" in the grid (right click, or double click where the cell cannot be edited) shows the whole cell in its
  own window: long text with wrapping, JSON and XML indented, the text of an RTF, images, and binaries in hexadecimal;
  copy it or save it to a file as is.
- "Compare data with another database…" on a table in the tree: reads both databases (read only), lists by primary key
  the rows missing in the target, the different ones (with each changed column) and the extra ones, and builds the
  script to make the target equal (INSERT, UPDATE and, if asked, DELETE; no COMMIT), which opens in a query of the
  target without running. For small tables (up to 100,000 rows).
- "Generate SQL" also for procedures and functions in the tree: SELECT * FROM or EXECUTE PROCEDURE, and SELECT … FROM
  RDB$DATABASE for a function, with a ":NAME" parameter for each input.
- Results can be exported to JSON and Markdown, and the grid copies the chosen rows as a Markdown table (to paste in a
  ticket or a chat), with the columns in the order shown.
- "HTML documentation…" (in the database menu › Script, or the button in "Database script"): a single HTML file with an
  index and search, each table with its columns, keys, linked FKs (also the logical "#FK_" ones), who references it,
  indexes and triggers, plus views, procedures, functions, domains and exceptions; the PSQL source only if asked. It
  respects the chosen area and opens in the browser.
- The editor folds blocks that span several lines (BEGIN…END, CASE…END, parentheses and /* */ comments) with markers in
  the margin; it can be turned off in Options › Editor.
- "View row vertically…" in the grid: one line per column with its type and value (for wide tables), filterable like any
  grid, with ◀ ▶ to go through the rows shown.
- Ctrl+F with the grid selected searches the results (ignoring case and accents): it goes to each matching cell with
  Enter and Shift+Enter and shows "3 of 27"; in the editor, Ctrl+F still searches the text.
- "Show plan without running" (Ctrl+Shift+E): prepares the statement at the cursor (or the selection) and shows its
  plan, warning if it reads a whole table, without fetching or changing anything; useful before running something heavy
  in production.
- Query tabs can be renamed (remembered on close) and duplicated from their menu, and Ctrl+Shift+T reopens the last
  closed query with its database and text.
- The grid copies a column's values as "COLUMN IN (…)" (from the header, or only from the selected rows): no duplicates
  or NULLs, with Firebird literals, and several INs joined with OR beyond 1,500 values.
- "Where is it used?…" on a column in the tree: the views, procedures, functions and triggers that name it, its indexes
  and keys, the FKs of other tables that point to it (also logical "#FK_" ones) and its own FK; double click opens each
  object.
- "Compare databases" marks line by line what changes in the DDL of an object that is in both: green what the source
  adds, red what the target loses.
- "Chart…" in the grid: bars or a line of what is shown, with the labels column (X) and the value column (Y, summed per
  X) or the row count; numbers and dates are sorted, hovering shows each value, and it can be saved as PNG.
- Fixed: options, connection registrations, saved diagrams and areas written by an earlier version lost the factory
  value of anything they did not have yet (it was read as off or zero): new options such as folding or remembering tabs
  came in turned off, and the OAT and transaction warnings could end up at 1 minute. Now what is missing takes its
  factory value; options already saved that way have to be put back in Options.
- The "What's new" window was redesigned: versions on the left (the installed one marked) and, on the right, each change
  in its own card with its title highlighted, the names in "…" in bold and fixes with their own icon.
- Fixed: accepting Options erased what that window does not show (recent files, "Count on filter", "End with COMMIT" and
  the last version seen, so What's new opened again).
- Help › Keyboard shortcuts lists all the new shortcuts, deleting a connection registration asks first, and several
  texts were corrected (what F9 runs without a selection, the Compile tooltip, where a new tab opens, closing the Areas
  tab with unsaved changes).
- The tab of a table (or of another object that is not compiled) is read-only and runs nothing: F9, F5, Ctrl+Enter,
  Ctrl+F9 and the plan used to try to run its DDL, which would create the table again with its keys, indexes, triggers
  and permissions.

### Español

- Manual de usuario completo en español e inglés: Ayuda › Manual lo abre en el navegador, sin internet, y F1 abre el
  capítulo de lo que estás usando (editor, grilla, pestaña Datos, árbol, monitoreo, mantenimiento, scripts). También
  está en docs/manual en GitHub.
- Panel de filtros: todas las columnas en una lista con su filtro (el mismo texto que la fila de filtros), para buscar
  una columna por nombre y ver juntos todos los filtros; se abre desde la barra de arriba de la grilla o desde el menú
  del encabezado.
- Pestaña Datos: «Contar al filtrar» cuenta las filas que coinciden después de cada lectura con un filtro o un WHERE (se
  recuerda para todas las tablas).
- Importar puede actualizar las filas que ya están en la tabla por su clave y agregar las demás (UPDATE OR INSERT; la
  clave es la primaria o las columnas que tildes).
- Importar a una tabla nueva (menú de la base o de Tablas en el árbol): una tabla con una columna por cada una del
  archivo, con el tipo según lo que trae y nombres y tipos que se pueden cambiar antes; si la importación falla, la
  tabla nueva se vuelve a borrar.
- Usuarios y permisos «Por objeto»: se elige una tabla, vista, procedimiento u otro objeto y se ve quién tiene qué
  permiso sobre él, con las mismas casillas y «Aplicar»; también desde el árbol con «Permisos…» en un objeto.
- Consulta → «Ejecutar script desde archivo…»: corre un .sql de cualquier tamaño sin cargarlo en el editor, como Ctrl+F9
  (la misma revisión de seguridad, parar o seguir ante errores, el registro con los renglones del archivo); el historial
  guarda el nombre del archivo.
- Formatear SQL ahora también formatea procedimientos, funciones, triggers y EXECUTE BLOCK: una sentencia por renglón,
  sangrada por BEGIN … END, IF … ELSE, WHILE y FOR SELECT … DO, con las consultas de adentro formateadas; un CASE largo
  va con un WHEN por renglón.
- Diagramas: «Camino hasta…» encuentra la cadena de FK más corta entre dos tablas (evitando las muy referenciadas si se
  puede), suma las tablas del medio y la resalta.
- Trace: «Comparar con un log…» pone un trace guardado (un período anterior) contra el de ahora, consulta por consulta:
  más lenta, más rápida, nueva o ya no aparece, por el promedio por ejecución y ordenado por cuánto cambió el tiempo
  total.
- Abrir y guardar archivos .sql en el editor (Consulta → Abrir archivo .sql…, Guardar, Guardar como…, Abrir reciente;
  Ctrl+O, Ctrl+S, Ctrl+Shift+S): la pestaña toma el nombre del archivo, marca con «*» los cambios sin guardar y pregunta
  antes de cerrar o salir; se conserva la codificación (UTF-8 o Windows-1252).
- Diagramas: «Guardar diagrama…» deja un .fbsdiagram con las opciones, las tablas y dónde quedó cada una, y Herramientas
  → «Abrir diagrama guardado…» lo abre como estaba en cualquier base; las tablas movidas a mano no se mueven al sumar
  otras.
- Áreas en más herramientas: el script de la base y la comparación de bases se pueden limitar a un área (sus objetos,
  los dominios que usan y sus permisos, sin las FK a tablas de afuera), y las estadísticas de tablas pueden tildar las
  de un área.
- Diagramas: «Copiar como Mermaid» y exportar a Mermaid (.mmd o .md), el formato de texto que GitHub, GitLab y muchas
  wikis dibujan solos.
- Usuarios y permisos: los permisos de estructura de Firebird 3 (crear tablas, modificar cualquier procedimiento, borrar
  cualquier dominio, modificar la base…) para usuarios y roles, con las mismas casillas y «Aplicar».
- Generar datos de prueba para una tabla (menú de la tabla en el árbol): filas inventadas con lo que conviene por el
  tipo y el nombre de cada columna, las FK con valores que ya existen y la clave numerada desde la mayor, con una
  muestra antes; solo en Desarrollo y Prueba.
- Buscar y reemplazar en el editor de SQL (Ctrl+F, Ctrl+H, F3 y Mayús+F3): todas las coincidencias marcadas, «3 de 12»,
  mayúsculas, palabra entera y expresiones regulares (con $1… en el reemplazo); «Reemplazar todo» se deshace con un solo
  Ctrl+Z.
- Las pestañas de consulta se recuerdan al cerrar y se vuelven a abrir la próxima vez, cada una con su base y su texto
  (cifrado como el historial; un archivo .sql se vuelve a leer del disco). Se puede apagar en Opciones › Editor.
- Después de JOIN, el autocompletado sugiere primero las tablas relacionadas por una FK (o por un comentario «#FK_»
  lógico) con las que ya están en el FROM, con un alias libre y el ON armado: «TURNOS t ON t.ID = td.TURNO».
- Las tablas y vistas del árbol tienen «Generar SQL»: SELECT con todas las columnas y, de una tabla, INSERT, UPDATE y
  DELETE por la clave primaria y UPDATE OR INSERT, con parámetros «:COLUMNA», en una consulta nueva (sin ejecutar). Sin
  clave primaria la condición queda para completar, así no corre por error.
- Comandos del editor en Consulta › Editar el texto: comentar o descomentar renglones (Ctrl+/), duplicar el renglón o la
  selección (Ctrl+D), mover renglones (Alt+↑/↓), mayúsculas y minúsculas (Ctrl+Shift+U, Ctrl+U) e ir al renglón
  (Ctrl+G); cada uno se deshace con un solo Ctrl+Z.
- «Ver el valor…» en la grilla (clic derecho, o doble clic donde la celda no se edita) muestra la celda entera en su
  ventana: texto largo con ajuste de renglones, JSON y XML con sangría, el texto de un RTF, imágenes y binarios en
  hexadecimal; copiarlo o guardarlo tal cual en un archivo.
- «Comparar datos con otra base…» en una tabla del árbol: lee las dos bases (solo lectura), lista por la clave primaria
  las filas que faltan en el destino, las distintas (con cada columna que cambia) y las que sobran, y arma el script
  para igualar el destino (INSERT, UPDATE y, si se pide, DELETE; sin COMMIT), que se abre en una consulta del destino
  sin ejecutarse. Para tablas chicas (hasta 100.000 filas).
- «Generar SQL» también para procedimientos y funciones del árbol: SELECT * FROM o EXECUTE PROCEDURE, y SELECT … FROM
  RDB$DATABASE para una función, con un parámetro «:NOMBRE» por cada entrada.
- Los resultados se exportan también a JSON y a Markdown, y la grilla copia las filas elegidas como tabla de Markdown
  (para pegar en un ticket o un chat), con las columnas en el orden en que se ven.
- «Documentación en HTML…» (en el menú de la base › Script, o el botón de «Script de la base»): un solo archivo HTML con
  índice y buscador, cada tabla con sus columnas, claves, FK enlazadas (también las lógicas «#FK_»), quién la
  referencia, índices y triggers, y las vistas, procedimientos, funciones, dominios y excepciones; el código PSQL solo
  si se pide. Respeta el área elegida y se abre en el navegador.
- El editor pliega los bloques de varios renglones (BEGIN…END, CASE…END, paréntesis y comentarios /* */) con marcas en
  el margen; se puede apagar en Opciones › Editor.
- «Ver la fila en vertical…» en la grilla: un renglón por columna con su tipo y su valor (para las tablas anchas),
  filtrable como cualquier grilla, con ◀ ▶ para recorrer las filas que se ven.
- Ctrl+F con la grilla elegida busca en los resultados (sin mayúsculas ni acentos): va a cada celda que coincide con
  Enter y Shift+Enter y muestra «3 de 27»; en el editor, Ctrl+F sigue buscando en el texto.
- «Ver el plan sin ejecutar» (Ctrl+Shift+E): prepara la sentencia del cursor (o la selección) y muestra su plan, con el
  aviso si recorre una tabla entera, sin traer ni cambiar nada; sirve antes de correr algo pesado en producción.
- Las pestañas de consulta se pueden renombrar (se recuerda al cerrar) y duplicar desde su menú, y Ctrl+Shift+T vuelve a
  abrir la última consulta cerrada con su base y su texto.
- La grilla copia los valores de una columna como «COLUMNA IN (…)» (desde el encabezado, o solo de las filas elegidas):
  sin repetidos ni NULL, con los literales de Firebird, y varios IN unidos con OR pasados los 1.500 valores.
- «¿Dónde se usa?…» en una columna del árbol: las vistas, procedimientos, funciones y triggers que la nombran, sus
  índices y claves, las FK de otras tablas que la apuntan (también las lógicas «#FK_») y su propia FK; doble clic abre
  cada objeto.
- «Comparar bases» marca renglón por renglón lo que cambia en el DDL de un objeto que está en las dos: en verde lo que
  el origen agrega y en rojo lo que el destino pierde.
- «Gráfico…» en la grilla: barras o línea de lo que se ve, con la columna de las etiquetas (X) y la del valor (Y, sumado
  por cada X) o la cantidad de filas; los números y las fechas se ordenan, al pasar el mouse se ve cada valor y se
  guarda como PNG.
- Corregido: las opciones, los registros de conexión, los diagramas guardados y las áreas escritos por una versión
  anterior perdían el valor de fábrica de lo que todavía no tenían (se leía apagado o en cero): las opciones nuevas,
  como plegar bloques o recordar las pestañas, llegaban apagadas y los avisos de la OAT y de las transacciones podían
  quedar en 1 minuto. Ahora lo que falta toma su valor de fábrica; las opciones que ya quedaron guardadas así hay que
  volver a ponerlas en Opciones.
- La ventana de «Novedades» se rediseñó: las versiones a la izquierda (con la instalada marcada) y, a la derecha, cada
  cambio en su tarjeta con el título resaltado, los nombres entre «…» en negrita y las correcciones con su propio ícono.
- Corregido: aceptar Opciones borraba lo que esa ventana no muestra (los archivos recientes, «Contar al filtrar»,
  «Terminar con COMMIT» y la última versión vista, por lo que Novedades volvía a abrirse).
- Ayuda › Atajos de teclado tiene todos los atajos nuevos, borrar un registro de conexión pregunta antes, y se
  corrigieron varios textos (qué ejecuta F9 sin selección, el tooltip de Compilar, en qué base se abre una pestaña
  nueva, cerrar Áreas con cambios sin guardar).
- La pestaña de una tabla (o de otro objeto que no se compila) es de solo lectura y no ejecuta nada: F9, F5, Ctrl+Enter,
  Ctrl+F9 y el plan intentaban correr su DDL, que crearía de nuevo la tabla con sus claves, índices, triggers y
  permisos.

## [0.2.0] - 2026-10-24

### English

- The registrations backup can carry each database's areas: on import, databases without areas get them and the others
  keep theirs.
- Hide columns from the column header menu; the Data tab remembers them for each table. Hidden columns still go into
  "Copy as INSERT / UPDATE" and into exports.
- Trace: export the analysis by query to Excel, as shown in the grid.
- Choose the color of each environment (Options → General) and of your own folders (tree menu), from a palette that
  reads well with white text and keeps reds for production.
- Help → What's new shows the changes of the installed version.
- Each release page lists what changed in that version.
- Safer scripts and imports: character sets and collations with unusual names are quoted, a huge Excel file is refused
  with a message instead of filling the memory, and --import-mcp warns when the computer has no keyring.
- The database bar shows how long the tab's write transaction has been open, and turns it amber and then red after some
  minutes (set in Options → Queries), with a notice: a forgotten transaction holds back the server's garbage collection.
- With no registered databases, the welcome panel also offers "Import registrations…": the file shared by your team
  brings all the databases, with their areas.
- Autocompletion: the columns of a package procedure in the FROM (PKG.PROC(…) alias), and parameter help for EXECUTE
  PROCEDURE written without parentheses.
- The database bar keeps showing the tab's database (where your statements run) and, next to it, the other connected
  databases as chips with their environment color: a click takes you to their tab, or opens a query on them.
- In Development and Test, each tab can turn on autocommit (every writing statement is committed when it ends well) and
  choose the transaction isolation (read committed, snapshot, snapshot table stability); production always keeps
  explicit Commit and read committed.
- The first time a new version is opened, What's new opens by itself (once; not on a fresh install).
- Each area can have a color (Areas tab), from the folder palette without reds, shown on its icon in the tree and in the
  Areas tab; it travels with the areas file and the registrations backup.
- About: links with icons in two columns, separate "Report a bug" and "Request a feature" forms, What's new, and "Copy
  version and system" to paste in a report.
- Data tab: with unapplied changes, a bar says where they are applied, "Apply" shows how many there are, and "Generate
  script…" shows the commands with an "End with COMMIT" option for copying them or taking them to a query (remembered
  for next time).
- New query (Ctrl+T or "+") and metadata search open on the database you touched last: the one picked in the tree, or
  the current tab's if you went back to it. Before, the current tab always won.
- Drag a table, view or other object, or a column, from the tree into the SQL editor to write its name where you drop
  it.

### Español

- El backup de los registros puede llevar las áreas de cada base: al importar, las reciben las bases que no tienen y
  las demás conservan las suyas.
- Ocultar columnas desde el menú del encabezado; la pestaña Datos las recuerda para cada tabla. Las ocultas siguen en
  «Copiar como INSERT / UPDATE» y en las exportaciones.
- Trace: exportar el análisis por consulta a Excel, como se ve en la grilla.
- Elegir el color de cada entorno (Opciones → General) y de tus carpetas (menú del árbol), de una paleta que se lee con
  letra blanca y deja los rojos para producción.
- Ayuda → Novedades muestra los cambios de la versión instalada.
- La página de cada versión dice qué cambió en ella.
- Scripts e importaciones más seguros: los juegos de caracteres e intercalaciones con nombres raros van entre comillas,
  un Excel enorme se rechaza con un aviso en vez de llenar la memoria y --import-mcp avisa cuando el equipo no tiene
  llavero.
- La franja de la base muestra hace cuánto está abierta la transacción de escritura de la pestaña, y la pasa a ámbar y
  después a rojo a los minutos que se elijan (Opciones → Consultas), con un aviso: una transacción olvidada traba la
  recolección de basura del servidor.
- Sin bases registradas, la bienvenida ofrece también «Importar registros…»: el archivo que comparte el equipo trae
  todas las bases, con sus áreas.
- Autocompletado: las columnas de un procedimiento de paquete en el FROM (PKG.PROC(…) alias), y la ayuda de parámetros
  en un EXECUTE PROCEDURE escrito sin paréntesis.
- La franja de la base sigue mostrando la de la pestaña (donde se ejecuta lo que escribís) y, al lado, las otras bases
  conectadas como chips con el color de su entorno: un clic lleva a su pestaña, o abre una consulta en ella.
- En Desarrollo y Prueba, cada pestaña puede activar autocommit (cada sentencia que escribe se confirma al terminar
  bien) y elegir el aislamiento de la transacción (read committed, snapshot, snapshot table stability); en producción
  sigue siempre con Commit explícito y read committed.
- La primera vez que se abre una versión nueva, Novedades se abre sola (una vez; en una instalación nueva, no).
- Cada área puede tener un color (pestaña Áreas), de la paleta de las carpetas y sin rojos, que se ve en su ícono en el
  árbol y en la pestaña Áreas; viaja con el archivo de áreas y con el backup de los registros.
- Acerca de: enlaces con ícono en dos columnas, «Reportar un error» y «Pedir una función» por separado, Novedades y
  «Copiar versión y sistema» para pegar en un reporte.
- Pestaña Datos: con cambios sin aplicar, una franja dice dónde se aplican, «Aplicar» muestra cuántos son y «Generar
  script…» muestra los comandos con la opción «Terminar con COMMIT» para copiarlos o llevarlos a una consulta (se
  recuerda para la próxima).
- Consulta nueva (Ctrl+T o «+») y búsqueda en metadatos se abren en la base de lo último que tocaste: la elegida en el
  árbol, o la de la pestaña si volviste a ella. Antes ganaba siempre la pestaña.
- Arrastrá una tabla, una vista u otro objeto, o una columna, del árbol al editor de SQL para escribir su nombre donde
  lo soltás.

## [0.1.0] - 2026-10-24

### English

- First public version.
- SQL editor with highlighting, autocompletion (also of subqueries and WITH, procedures, functions and packages), a
  formatter, Ctrl+click to open an object, templates, parameters and explicit transactions.
- Production safety: environments with their own color, read-only registrations, confirmations before risky statements
  (an UPDATE or DELETE without WHERE, a MERGE or EXECUTE BLOCK that deletes everything, a procedure inside an open write
  transaction) and whether the connection is encrypted.
- A fast grid with typed filters, grouping, totals and columns you reorder, and table data you edit without holding
  locks.
- Database tree with your own folders, DDL, compiling, dependencies and metadata search; areas to organize a big
  database; relationship diagrams exported to PNG, SVG and draw.io.
- Monitoring (OAT, locks, kill a connection), trace with analysis by query, statistics (also compared between two
  databases) and server details.
- Full database script and database compare; backup, restore, validation, sweep, nbackup history and database
  properties; users, roles and permissions.
- Export to CSV, Excel and INSERT scripts, import from CSV and Excel, and a backup of the registrations with encrypted
  passwords.
- Packages for Windows, Linux (also .deb) and macOS; English and Spanish; light and dark mode.

### Español

- Primera versión pública.
- Editor de SQL con resaltado, autocompletado (también de subconsultas y WITH, procedimientos, funciones y paquetes),
  formateador, Ctrl+clic para abrir un objeto, plantillas, parámetros y transacciones explícitas.
- Seguridad para producción: entornos con su color, registros de solo lectura, confirmaciones antes de lo riesgoso (un
  UPDATE o DELETE sin WHERE, un MERGE o EXECUTE BLOCK que borra todo, un procedimiento dentro de una transacción de
  escritura abierta) y si la conexión va cifrada.
- Una grilla rápida con filtros según el tipo, agrupado, totales y columnas que se reordenan, y edición de datos sin
  dejar registros bloqueados.
- Árbol de bases con carpetas propias, DDL, compilar, dependencias y búsqueda en los metadatos; áreas para ordenar una
  base grande; diagramas de relaciones exportables a PNG, SVG y draw.io.
- Monitoreo (OAT, bloqueos, cortar una conexión), trace con análisis por consulta, estadísticas (también comparadas
  entre dos bases) y datos del servidor.
- Script de la base entera y comparación de bases; backup, restore, validación, sweep, historial de nbackup y
  propiedades de la base; usuarios, roles y permisos.
- Exportación a CSV, Excel y script INSERT, importación desde CSV y Excel, y backup de los registros con las contraseñas
  cifradas.
- Paquetes para Windows, Linux (también .deb) y macOS; español e inglés; modo claro y oscuro.
