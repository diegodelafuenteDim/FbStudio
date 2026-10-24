# Changelog · Registro de cambios

What changed in each version of FbStudio, in English and in Spanish. The section of a version is also shown on its
[release page](https://github.com/diegodelafuenteDim/FbStudio/releases) and in FbStudio under Help → What's new.

Qué cambió en cada versión de FbStudio, en inglés y en español. La sección de cada versión también aparece en su
[página de descarga](https://github.com/diegodelafuenteDim/FbStudio/releases) y en FbStudio, en Ayuda → Novedades.

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
