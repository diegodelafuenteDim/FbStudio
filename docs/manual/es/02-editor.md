# El editor de SQL

Cómo escribir y ejecutar SQL en FbStudio: las pestañas de consulta, ejecutar sentencias y scripts, las transacciones, la
red de seguridad y las ayudas del editor (autocompletado, plantillas, formato, búsqueda). La grilla tiene su propio
capítulo: [Resultados](03-results.md).

## Pestañas de consulta

Cada pestaña de consulta tiene su base, su propia conexión y su propia transacción: lo que hacés en una no se mezcla con
las otras. El título dice el nombre de la consulta y la base («Consulta 3 · Prueba»), y el borde de abajo lleva el color
del entorno de esa base. La franja de arriba muestra siempre la base de la pestaña elegida, que es donde se ejecuta lo
que escribís (ver [Primeros pasos](01-start.md)).

### Pestaña nueva

- `Ctrl+T`, el botón **+** a la derecha de las pestañas o **Consulta › Pestaña nueva**.
- Se abre en la base de lo último que tocaste: si elegiste una base en el árbol después de pasar por la pestaña, en esa;
  si no, en la base de la pestaña elegida. Sin pestañas abiertas, en la del árbol. Si no hay ninguna base elegida, no se
  abre y la barra de estado lo dice.
- Desde el árbol: clic derecho en una base › **Consulta nueva en esta base**.
- Doble clic en una base del árbol la conecta y la usa en la pestaña de consulta elegida, si está libre. Si esa pestaña
  tiene una transacción abierta o está ejecutando, la base se abre en una pestaña nueva y la anterior sigue con su base
  y su transacción.

### Recorrer y cerrar

- `Ctrl+Tab` pasa a la pestaña siguiente y `Ctrl+Shift+Tab` a la anterior, también con el cursor en el editor.
- Para cerrar: `Ctrl+W` (o `Ctrl+F4`), la cruz de la pestaña, el clic del medio sobre ella o **Consulta › Cerrar
  pestaña**.
- Clic derecho en una pestaña: **Cerrar**, **Cerrar todas menos esta**, **Cerrar las de la derecha** y **Cerrar todas**.
- Una pestaña que está ejecutando no se cierra: cancelá primero con `Esc`. Si tiene una transacción abierta o cambios
  sin guardar, pregunta antes (ver [Cerrar o salir](#cerrar-o-salir)).

### Renombrar, duplicar y reabrir

En el menú de la pestaña (clic derecho):

- **Renombrar…** le pone otro nombre («Consulta 3» → «Turnos del día»). Solo en las consultas que no son de un archivo
  (esas llevan el nombre del archivo). El nombre se recuerda al cerrar FbStudio.
- **Duplicar** abre una consulta nueva en la misma base con el mismo texto (sin el archivo, si tenía).
- **Reabrir la pestaña cerrada** (`Ctrl+Shift+T`) vuelve a abrir la última consulta que cerraste, con su base y su texto
  (o su archivo). Repetilo para ir más atrás: recuerda hasta 20, mientras FbStudio está abierto. Las consultas vacías no
  se recuerdan.

### Las consultas de la última vez

Al cerrar FbStudio, las pestañas de consulta se guardan y vuelven a abrirse la próxima vez, cada una con su base, su
nombre y su texto, y con la misma pestaña elegida. El texto se guarda cifrado, como el historial. Una consulta de un
archivo .sql se vuelve a leer del archivo: lo que no guardaste se pierde (FbStudio avisa al salir). Las consultas vacías
no se guardan.

Se apaga en **Herramientas › Opciones… › Editor**, con **Al cerrar, recordar las consultas abiertas y volver a
abrirlas**.

## Archivos .sql

- **Consulta › Abrir archivo .sql…** (`Ctrl+O`) abre uno o varios archivos .sql o .txt. Si el archivo ya está abierto,
  va a su pestaña. Si no, lo lee en la consulta elegida cuando está vacía, o en una pestaña nueva en la base de lo
  último que tocaste.
- **Consulta › Guardar** (`Ctrl+S`) guarda en su archivo; si la consulta todavía no tiene uno, pregunta dónde. **Guardar
  como…** (`Ctrl+Shift+S`) siempre pregunta.
- **Consulta › Abrir reciente** muestra los últimos 10 archivos abiertos o guardados; al pasar el mouse se ve dónde está
  cada uno. Si un archivo ya no existe, se saca de la lista.
- La pestaña toma el nombre del archivo y marca con «*» los cambios sin guardar. Antes de cerrarla, o de salir de
  FbStudio, con cambios sin guardar, pregunta.
- Se conserva la codificación del archivo (UTF-8 o Windows-1252). Si escribiste letras que Windows-1252 no tiene, se
  guarda en UTF-8 y la barra de estado lo avisa. Un archivo nuevo se guarda en UTF-8.

## Ejecutar

| Atajo | Menú | Qué ejecuta |
|---|---|---|
| `F9` (o `F5`) | **Consulta › Ejecutar (todo o la selección)** | La selección; sin selección, todo el editor. |
| `Ctrl+Enter` | **Consulta › Ejecutar la sentencia del cursor** | Solo la sentencia donde está el cursor (o la selección). |
| `Ctrl+F9` | **Consulta › Ejecutar como script (sin grilla)** | La selección o todo el editor, como script sin grilla. |
| | **Consulta › Ejecutar script desde archivo…** | Un archivo .sql entero, sin abrirlo en el editor. |
| `Ctrl+Shift+E` | **Consulta › Ver el plan sin ejecutar** | Nada: solo muestra el plan. |

El botón **Ejecutar** de la barra de herramientas hace lo mismo que `F9`.

### F9: todo o la selección

- Con texto elegido, ejecuta lo elegido. Sin selección, ejecuta todo el editor.
- Si es una sola sentencia, es una ejecución común: las filas van a **Resultados** y los mensajes a **Mensajes**.
- Si son varias, corren como script: en orden, en la misma transacción, y paran en el primer error (o siguen, ver
  [Scripts](#scripts)). Un `COMMIT` o `ROLLBACK` escrito en el script se ejecuta en su lugar.
- Las sentencias se separan con `;`. FbStudio entiende `SET TERM` (los scripts de isql) y también los bloques PSQL sin
  `SET TERM`: los `;` de adentro de un `CREATE PROCEDURE … BEGIN … END` o de un `EXECUTE BLOCK` no lo cortan.
- Si Firebird dice en qué renglón y columna está el error, el cursor va ahí.
- Cuando no se ejecutó todo el editor, la barra de estado lo aclara: «se ejecutó solo la selección (líneas 4 a 9)».
- En la pestaña de un procedimiento, trigger, vista o función, `F9` sin selección lo compila (ver
  [El árbol, las áreas y los diagramas](05-tree.md)).

### Ctrl+Enter: la sentencia del cursor

`Ctrl+Enter` ejecuta solo la sentencia donde está el cursor, aunque el editor tenga muchas: no hace falta elegirla. Si
hay texto elegido, ejecuta la selección. La barra de estado lo aclara: «se ejecutó solo la sentencia del cursor
(línea 3)».

```sql
SELECT * FROM TURNOS WHERE FECHA = CURRENT_DATE;

UPDATE TURNOS SET ESTADO = 'C' WHERE ID = 1234;
```

Con el cursor en el UPDATE, `Ctrl+Enter` ejecuta solo el UPDATE; `F9` sin selección ejecuta las dos.

### Scripts

Varias sentencias juntas corren como script (con `F9`, con `Ctrl+F9` o desde un archivo):

- Antes de mandar la primera sentencia se revisa el script entero: si algo está bloqueado no se ejecuta nada, y si algo
  pide confirmación se pregunta una sola vez, con el renglón de cada caso (ver
  [La red de seguridad](#la-red-de-seguridad)).
- La transacción de escritura se abre con lo primero que modifica y sigue abierta hasta un `COMMIT` o `ROLLBACK` del
  script, o hasta que apretás **Commit** o **Rollback**.
- Ante un error, el script para. Para que siga con la siguiente sentencia, tildá **Consulta › Seguir ante errores en
  scripts** (es la misma opción que **Ante un error en un script**, en **Herramientas › Opciones… › Consultas**). Una
  sentencia que falla se deshace sola: lo anterior y la transacción siguen en pie. Al terminar, el cursor va al primer
  error.
- El registro queda en **Mensajes**: un resumen arriba y un renglón por sentencia con su número de línea («✓ línea 3 ·
  UPDATE: 12 filas afectadas», «✗ línea 7 · INSERT: …»).
- Con `F9` (hasta 200 sentencias), cada sentencia queda en el historial y la grilla muestra el último SELECT, con su
  plan y sus estadísticas.
- Los parámetros no se piden en un script (ver [Parámetros](#parámetros)).

### Scripts sin grilla

`Ctrl+F9` (**Consulta › Ejecutar como script (sin grilla)**) ejecuta la selección o todo el editor como script, pensado
para scripts largos, como miles de INSERT o UPDATE:

- No pide el plan ni las lecturas por tabla de cada sentencia, así cada una tarda menos.
- La grilla queda como estaba: de un SELECT solo se cuentan las filas.
- El historial guarda el script entero una sola vez, no una entrada por sentencia.
- El avance se ve en la barra de estado: «Script: 1.200 de 5.000 (24 %) · 0 con error · Esc cancela».
- Con más de 500 sentencias, el registro muestra un renglón por tipo de sentencia y lista solo los errores.

`F9` también pasa a este modo cuando el script tiene más de 200 sentencias, y lo avisa: «sin grilla: más de 200
sentencias».

### Ejecutar script desde archivo

**Consulta › Ejecutar script desde archivo…** corre un archivo .sql de cualquier tamaño sin cargarlo en el editor.
Funciona como `Ctrl+F9`: la misma revisión de seguridad antes de empezar, parar o seguir ante errores, y el registro en
**Mensajes** con los renglones del archivo. El cursor del editor no se mueve.

Corre en la pestaña de consulta elegida, si tiene base y no está ejecutando; si no, en una consulta nueva en la base de
lo último que tocaste. El historial guarda el nombre del archivo y cuántas sentencias tenía, no su contenido.

### Cancelar

`Esc`, el botón **Cancelar** de la barra de herramientas o **Consulta › Cancelar** cortan lo que se está ejecutando. En
una sentencia sola, las filas que ya llegaron quedan en la grilla y la barra de estado dice cuántas son y cuánto tardó
la cancelación. En un script, se corta la sentencia en curso y no sigue con las demás.

Mientras espera la primera fila, la barra de estado muestra cuánto lleva («Ejecutando SELECT… 12 s · Esc cancela») y,
si el plan recorre una tabla entera sin índice, lo avisa: «⚠ lectura completa de TURNOS (sin índice): puede tardar».

Si la ejecución había abierto la transacción de escritura y se cancela o falla, esa transacción se deshace. Si ya estaba
abierta de antes, sigue abierta con lo que tenía.

### Límite de filas

Un SELECT trae como mucho las filas que dice el selector **Límite** de la barra de herramientas: 1.000, 10.000,
100.000 o 1.000.000. Cuando se llega, la barra de estado lo dice: «límite de 10.000 alcanzado (subilo para traer más)».

El valor con que arranca FbStudio se elige en **Herramientas › Opciones… › Consultas** (**Filas que trae un SELECT**;
de fábrica, 10.000). El selector de la barra lo cambia solo hasta cerrar la aplicación. Para exportar todas las filas,
sin límite, ver [Resultados](03-results.md).

## Transacciones

Cada pestaña trabaja con su propia transacción:

- Un SELECT, sin transacción abierta, corre en una de solo lectura que se cierra sola.
- Lo que escribe (INSERT, UPDATE, DELETE, MERGE, EXECUTE PROCEDURE, EXECUTE BLOCK y el DDL, como CREATE, ALTER o DROP)
  abre una transacción de escritura y la deja abierta hasta que la confirmás o la deshacés. El DDL también queda
  pendiente hasta el Commit.
- Mientras está abierta, todo lo que ejecutás en esa pestaña corre adentro: un SELECT ve tus cambios sin confirmar. Las
  otras pestañas y las otras conexiones no los ven hasta el Commit.
- Con una transacción abierta, la pestaña no cambia de base: un doble clic en otra base del árbol abre una pestaña
  nueva.

### Commit y Rollback

- **Commit** confirma los cambios: el botón **Commit** de la barra de herramientas, **Consulta › Commit** o
  `Ctrl+Alt+C`.
- **Rollback** los deshace: el botón **Rollback**, **Consulta › Rollback** o `Ctrl+Alt+R`.
- También podés escribir `COMMIT;` o `ROLLBACK;` en el editor y ejecutarlo.
- `SET TRANSACTION`, `SAVEPOINT` y `RELEASE` no están soportados: FbStudio no los ejecuta.

### Autocommit y aislamiento

En la barra de herramientas, al lado de **Rollback**, están la casilla **Autocommit** y la lista del aislamiento. Valen
para la pestaña elegida y solo se cambian en bases de **Desarrollo** y **Prueba**, sin una transacción abierta.

- **Autocommit**: cada sentencia que escribe se confirma sola al terminar bien, y la barra de estado dice «confirmado
  (autocommit)». Si el Commit falla, se deshace y se ve el error.
- El aislamiento vale para la próxima transacción de escritura:
  - **Read committed** (el de fábrica) ve lo que otros confirman.
  - **Snapshot** ve la foto del momento en que empezó.
  - **Snapshot table stability** es como snapshot, y además bloquea las tablas que lee.

> **Ojo:** en producción y en los registros de solo lectura siempre es con Commit explícito y read committed. La casilla
> y la lista quedan deshabilitadas, y la conexión lo respeta igual.

### La transacción abierta

Mientras hay una transacción de escritura abierta:

- la pestaña muestra un punto rojo (●) antes del título;
- a la derecha de la franja de la base se lee hace cuánto está abierta: «Transacción de escritura ABIERTA hace 12 min:
  confirmá (Commit) o deshacé (Rollback)»;
- la barra de estado agrega «transacción ABIERTA (Commit/Rollback)» después de cada ejecución.

De fábrica, a los 5 minutos ese aviso de la franja se pone ámbar y a los 15 rojo, con un aviso aparte (una sola vez).
Los minutos se eligen en **Herramientas › Opciones… › Consultas**: **Transacción de escritura abierta: ámbar a los**
y **…y rojo, con un aviso, a los**.

> **Ojo:** una transacción olvidada traba la recolección de basura del servidor para todos. Confirmala o deshacela
> apenas termines.

### Cerrar o salir

- Al cerrar una pestaña con una transacción abierta, FbStudio pregunta: **Cerrar y deshacer** hace rollback y la cierra;
  **No cerrar** la deja como está.
- Al cerrar varias juntas (con **Cerrar todas**, por ejemplo), pregunta una sola vez y nombra las que tienen trabajo sin
  confirmar.
- Al salir de FbStudio con transacciones abiertas, la ventana **Transacciones abiertas** las lista: **Salir y deshacer**
  hace rollback de todas y sale; **No salir** vuelve.
- Desconectar una base cierra todas sus pestañas, y también pregunta si alguna tiene una transacción abierta.

### Bloqueos

Si otra transacción tiene bloqueado el registro que querés modificar, la escritura espera (de fábrica, hasta 10
segundos) y después da error, en vez de dejar la pestaña colgada. El tiempo se cambia en **Herramientas › Opciones… ›
Consultas** (**Espera ante un registro bloqueado**) y vale para las transacciones que se abran desde ese momento.

Junto con el error, FbStudio dice quién tiene el registro:

- Si es otra pestaña de FbStudio: «el registro lo tiene tomado la pestaña «Consulta 1» (transacción 4521): confirmala o
  deshacela».
- Si es otra conexión: el número de transacción, el usuario, la dirección, el programa y a qué hora se abrió. Si
  FbStudio no puede leer esos datos del servidor, da solo el número de transacción (para ver cuál es, hace falta el rol
  RDB$ADMIN).

Para ver esa conexión y, si hace falta, cortarla: [Monitoreo y rendimiento](06-monitoring.md).

## La red de seguridad

Antes de mandar cada sentencia al servidor, FbStudio la revisa según la base de la pestaña: su entorno y si el registro
es de solo lectura (se eligen al registrar la base, ver [Primeros pasos](01-start.md)). Lo riesgoso pide confirmación y
lo prohibido se bloquea.

| Situación | Qué hace FbStudio |
|---|---|
| Registro de solo lectura y una sentencia que escribe: INSERT, UPDATE, DELETE, MERGE, DDL, EXECUTE PROCEDURE o EXECUTE BLOCK | La bloquea. |
| Registro de solo lectura y `NEXT VALUE FOR` o `GEN_ID` con un paso distinto de 0 | La bloquea: mover un generador no se deshace. Para leer su valor, `GEN_ID(generador, 0)`. |
| `SET TRANSACTION`, `SAVEPOINT` o `RELEASE`, en cualquier base | La bloquea: usá Commit y Rollback. |
| Base de producción y una sentencia que escribe | Pide confirmación. |
| UPDATE o DELETE sin WHERE, en cualquier base | Pide confirmación: afecta todas las filas de la tabla. |
| Un MERGE con `WHEN MATCHED THEN DELETE`, o un EXECUTE BLOCK con un UPDATE o DELETE sin WHERE | Pide confirmación. |
| Base de producción y un SELECT que usa procedimientos o funciones, dentro de una transacción de escritura abierta | Pide confirmación: si modifican datos, esos cambios quedan en la transacción y se graban con el Commit. |

- La confirmación es la ventana **Confirmar ejecución**, con los motivos: **Ejecutar igual** sigue y **No ejecutar** no
  manda nada.
- Lo bloqueado no llega al servidor: la barra de estado dice «Bloqueado: …» y el motivo queda en **Mensajes**.
- En un script se revisa todo antes de empezar: si una sola sentencia está bloqueada no corre ninguna, y las
  confirmaciones se juntan en una sola pregunta con el renglón de cada una.
- En los registros de solo lectura, la conexión vuelve a revisar cada sentencia antes de mandarla.
- La pestaña Datos tiene sus propias confirmaciones: ver [La pestaña Datos](04-data.md).

> **Ojo:** es una red de seguridad de la herramienta: no reemplaza los permisos de la base.

## Parámetros

En un SELECT, INSERT, UPDATE, DELETE, MERGE, UPDATE OR INSERT o EXECUTE PROCEDURE podés usar `?` y `:nombre` en lugar
de los valores:

```sql
SELECT t.ID, t.FECHA, p.APELLIDO
FROM TURNOS t
JOIN PACIENTES p ON p.ID = t.PACIENTE
WHERE t.FECHA = :FECHA
  AND t.MEDICO = :MEDICO
```

- Al ejecutar, la ventana **Parámetros de la consulta** pide un valor por cada nombre (los `?` aparecen como «Parámetro
  1», «Parámetro 2»…). La casilla **NULL** manda un NULL.
- Los valores van aparte, no pegados al SQL. Un mismo nombre repetido recibe el mismo valor.
- Los textos se escriben sin comillas; las fechas como 22/10/2026 o 2026-10-22 (también con hora: 22/10/2026 14:30); los
  números con coma o punto. Lo demás va como texto y Firebird lo convierte al tipo de la columna: «00123» sigue siendo
  «00123» en un VARCHAR.
- FbStudio recuerda el último valor de cada nombre mientras está abierto.
- No se buscan parámetros dentro de textos, nombres entre comillas ni comentarios, ni en el DDL ni en el PSQL
  (procedimientos, triggers, EXECUTE BLOCK), donde `:variable` es una variable.
- Solo se piden al ejecutar una sentencia sola. En un script no se sabría a cuál va cada valor: elegí la sentencia, o
  poné el cursor en ella y usá `Ctrl+Enter`.
- El historial guarda la sentencia como la escribiste, con sus `:nombre`.

## Autocompletado

La lista se abre sola al escribir dos letras o un punto, y con `Ctrl+Espacio` en cualquier momento. `Enter` o `Tab`
insertan lo marcado y `Esc` la cierra. Los nombres se escriben con comillas solo si hacen falta (minúsculas, espacios o
palabras reservadas).

Mira solo la sentencia donde está el cursor. Los nombres salen de los metadatos de la base, que se leen en segundo plano
la primera vez (mientras tanto, la lista trae solo palabras clave). Un CREATE, ALTER o DROP ejecutado desde el editor
los vuelve a leer; si cambiaron desde otro lado, usá clic derecho en la base › **Recargar metadatos**.

### Qué sugiere

| Dónde está el cursor | Qué sugiere |
|---|---|
| Después de `FROM` o `JOIN` | Tablas, vistas, las consultas del WITH, procedimientos y paquetes. |
| Después de `UPDATE`, `INTO` o `TABLE` | Tablas y vistas. |
| Después de `alias.` o `TABLA.` | Las columnas de esa tabla o vista, de una subconsulta con alias (`FROM (SELECT …) x`), de una consulta del WITH o de un procedimiento seleccionable (sus parámetros de salida), también de uno de paquete (`PKG.PROC(…) alias`). |
| Después de `PAQUETE.` | Los procedimientos y funciones del paquete. |
| En el resto (lista del SELECT, WHERE, ORDER BY…) | Primero las columnas de lo que está en el FROM, sin escribir el alias; después palabras clave, funciones, paquetes, tablas y procedimientos. |
| Después de `GEN_ID(`, `NEXT VALUE FOR`, `SEQUENCE` o `GENERATOR` | Generadores. |
| Después de `EXCEPTION` | Excepciones. |
| Después de `PROCEDURE`, `FUNCTION` o `PACKAGE` | Procedimientos, funciones o paquetes. |

Cada elemento dice qué es: «(tabla, 12 columnas)», «(consulta del WITH)», «(procedimiento)», «(función, en PKG)»…
Las columnas de una subconsulta o de un WITH salen de su lista de SELECT:

```sql
WITH ULTIMOS AS (
  SELECT PACIENTE, MAX(FECHA) AS ULTIMA
  FROM TURNOS
  GROUP BY PACIENTE
)
SELECT p.APELLIDO, u.ULTIMA
FROM PACIENTES p
JOIN ULTIMOS u ON u.PACIENTE = p.ID
```

Al escribir `u.` se sugieren PACIENTE y ULTIMA.

### JOIN por las FK

Después de `JOIN`, la lista muestra primero las tablas relacionadas por una FK con las que ya están en el FROM, con un
alias libre y el ON armado. También cuentan las FK lógicas, las escritas en el comentario de una columna con «#FK_».

```sql
SELECT *
FROM TURNOS_DET td
JOIN TURNOS t ON t.ID = td.TURNO
```

Acá, al escribir `JOIN` aparece «TURNOS t ON t.ID = td.TURNO», con el nombre de la FK al lado.

- El alias son las iniciales de la tabla en minúsculas (TURNOS_DET → td), con un número si ya está usado.
- Primero van las de la última tabla del FROM: las tablas a las que apunta y después las que la apuntan.
- Las tablas que ya están en el FROM van al final: puede hacer falta la misma tabla otra vez, por otra FK.

### Ayuda de parámetros

Dentro de los paréntesis de un procedimiento o una función de la base, una ventanita muestra su firma, con el parámetro
en el que estás en negrita, y si es procedimiento o función. Se abre al escribir `(` o `,`, sigue al cursor (cambia de
parámetro con las comas) y se va al salir de los paréntesis. También anda con los de un paquete (`PKG.PROC(`) y con un
`EXECUTE PROCEDURE` escrito sin paréntesis (`EXECUTE PROCEDURE P_CERRAR_TURNO 1234, 'C'`).

## Plantillas

Una plantilla es un texto que se escribe con una abreviatura: escribí `eb`, apretá `Tab` y queda el esqueleto de un
EXECUTE BLOCK con el cursor en el cuerpo. `Ctrl+J` (o **Consulta › Insertar plantilla…**) muestra la lista para elegir;
lo que ya escribiste de la abreviatura la filtra. La plantilla toma la sangría del renglón.

`Tab` pone la plantilla cuando no hay texto elegido y no está abierta la lista del autocompletado. Si la lista está
abierta, `Tab` inserta lo marcado en ella: cerrala con `Esc` y apretá `Tab`, o usá `Ctrl+J`.

| Abreviatura | Plantilla |
|---|---|
| `sel` | SELECT * FROM |
| `self` | SELECT FIRST 100 * FROM |
| `selc` | SELECT COUNT(*) FROM |
| `selw` | SELECT * FROM … WHERE |
| `ins` | INSERT INTO … VALUES |
| `upd` | UPDATE … SET … WHERE |
| `del` | DELETE FROM … WHERE |
| `uoi` | UPDATE OR INSERT … MATCHING |
| `gen` | Valor de un generador |
| `eb` | EXECUTE BLOCK |
| `proc` | CREATE OR ALTER PROCEDURE |
| `func` | CREATE OR ALTER FUNCTION |
| `trg` | CREATE OR ALTER TRIGGER |
| `tab` | CREATE TABLE |

Se cambian en **Herramientas › Opciones… › Plantillas**: **Nueva** agrega una, **Borrar** quita la elegida y **Las de
fábrica** vuelve a las originales. Cada una tiene **Abreviatura**, **Descripción** y **Texto**; en el texto,
`${cursor}` marca dónde queda el cursor. Por ejemplo, la abreviatura `hoy` con el texto
`SELECT * FROM TURNOS WHERE FECHA = CURRENT_DATE AND ${cursor}`.

## Formatear SQL

**Consulta › Formatear SQL** (`Ctrl+Shift+L`) ordena la selección o, sin selección, todo el editor:

- En las consultas (SELECT, WITH, INSERT, UPDATE, DELETE, MERGE y UPDATE OR INSERT), cada cláusula va en su renglón,
  las condiciones del WHERE una por renglón, una lista larga un elemento por renglón, un CASE largo con un WHEN por
  renglón y las subconsultas sangradas.
- En los procedimientos, funciones, triggers y EXECUTE BLOCK, el cuerpo va con una sentencia por renglón, sangrado por
  BEGIN … END, IF … ELSE, WHILE … DO y FOR SELECT … DO, con las consultas de adentro formateadas. El encabezado queda
  como lo escribiste. Si encuentra algo que no reconoce, deja ese bloque tal cual: nunca cambia lo que hace el código.
- El resto del DDL, los `SET TERM`, los textos, los nombres entre comillas y los comentarios quedan como estaban.
- Las palabras clave pasan a mayúsculas; se apaga en **Herramientas › Opciones… › Editor** con **Al formatear, palabras
  clave en mayúsculas**.
- `Ctrl+Z` lo deshace.

Antes:

```sql
select t.ID, p.APELLIDO, count(*) from TURNOS t left outer join PACIENTES p on p.ID=t.PACIENTE where t.FECHA between '2026-01-01' and '2026-01-31' and t.ESTADO<>'X' group by t.ID, p.APELLIDO
```

Después:

```sql
SELECT t.ID, p.APELLIDO, COUNT(*)
FROM TURNOS t
LEFT OUTER JOIN PACIENTES p ON p.ID = t.PACIENTE
WHERE t.FECHA BETWEEN '2026-01-01' AND '2026-01-31'
  AND t.ESTADO <> 'X'
GROUP BY t.ID, p.APELLIDO
```

## Abrir un objeto

`Ctrl+clic` sobre un nombre, o `F12` con el cursor en él (**Consulta › Abrir el objeto (Ctrl+clic)**), abre el objeto en
su pestaña, con su DDL. Con `Ctrl` apretado, el puntero se vuelve una mano sobre los nombres.

- Un alias (`t` o `t.ID`) lleva a su tabla.
- Una columna sin alias lleva a la tabla del FROM que la tiene.
- Lo demás se busca por nombre en la base: tabla, vista, procedimiento, función, paquete, generador, excepción, trigger,
  índice, dominio o rol.

Qué se puede hacer en la pestaña de un objeto: [El árbol, las áreas y los diagramas](05-tree.md).

## Buscar y reemplazar

`Ctrl+F` (**Consulta › Buscar en el editor…**) abre la barra de búsqueda arriba del editor, y `Ctrl+H` (**Consulta ›
Reemplazar…**) la abre con la fila de reemplazo. Si tenías texto elegido en un solo renglón, es lo que se busca.

- Todas las coincidencias quedan marcadas y la barra dice en cuál estás: «3 de 12».
- `Enter` o `F3` van a la siguiente; `Mayús+Enter` o `Mayús+F3`, a la anterior. `F3` también abre la barra si está
  cerrada.
- Los botones `Aa` (**Distinguir mayúsculas**), `ab|` (**Solo la palabra entera**) y `.*` (**Expresión regular**)
  cambian cómo se busca. Con expresión regular, en el reemplazo `$1`, `$2`… son sus grupos.
- **Reemplazar** (o `Enter` en el cuadro de reemplazo) cambia la coincidencia elegida y va a la siguiente. **Reemplazar
  todo** las cambia todas, y un solo `Ctrl+Z` las vuelve atrás.
- `Tab` pasa del cuadro de búsqueda al de reemplazo y vuelve. `Esc` cierra la barra.

Con la grilla elegida, `Ctrl+F` busca en los resultados (ver [Resultados](03-results.md)).

## Editar el texto

En **Consulta › Editar el texto**. Cada comando se deshace con un solo `Ctrl+Z`.

| Comando | Atajo | Qué hace |
|---|---|---|
| **Comentar o descomentar renglones (Ctrl+/)** | `Ctrl+/` | Pone «-- » en los renglones de la selección (o en el del cursor); si ya estaban todos comentados, lo saca. En un teclado español es la tecla «-». |
| **Duplicar el renglón o la selección** | `Ctrl+D` | Duplica lo elegido o, sin selección, el renglón del cursor. |
| **Subir el renglón** / **Bajar el renglón** | `Alt+↑` / `Alt+↓` | Mueve los renglones de la selección (o el del cursor). |
| **Pasar a mayúsculas** / **Pasar a minúsculas** | `Ctrl+Shift+U` / `Ctrl+U` | Lo elegido o, sin selección, la palabra del cursor. |
| **Ir al renglón…** | `Ctrl+G` | Pide el número de renglón y lleva el cursor ahí. |

## Plegar bloques

Los bloques de varios renglones (BEGIN … END, CASE … END, los paréntesis y los comentarios `/* … */`) tienen una marca
en el margen para plegarlos: queda a la vista el principio y el final, y lo de adentro se esconde. Las marcas se
recalculan medio segundo después de dejar de escribir. Se apaga en **Herramientas › Opciones… › Editor**, con
**Plegar bloques (BEGIN…END, CASE, paréntesis y comentarios largos) con marcas en el margen**.

## Zoom de la letra

`Ctrl+rueda` del mouse agranda o achica la letra del editor, y `Ctrl+0` la vuelve al tamaño elegido en **Herramientas ›
Opciones… › Editor** (**Tamaño de la letra**). El zoom de la rueda se puede apagar en esa misma pestaña.

## Ver el plan sin ejecutar

`Ctrl+Shift+E` (**Consulta › Ver el plan sin ejecutar**) prepara la sentencia del cursor, o la selección, y muestra su
plan en la pestaña **Plan**, sin traer filas ni cambiar nada. Si recorre una tabla entera sin índice, la barra de estado
lo avisa. Sirve para mirar una consulta pesada antes de correrla en producción.

- Vale para SELECT, INSERT, UPDATE, DELETE, MERGE y EXECUTE.
- Con parámetros, no pide valores: el plan no depende de ellos.
- Si la pestaña tiene una transacción abierta, se prepara dentro de ella; si no, en una de solo lectura que se descarta.

## Las pestañas de abajo

Debajo del editor hay cinco pestañas. Se eligen con un clic, desde el menú **Ver** o con `Ctrl+1` a `Ctrl+5`. En la
pestaña de un objeto aparecen también **Dependencias** (`Ctrl+6`) y, en tablas y vistas, **Datos** (`Ctrl+7`, ver
[La pestaña Datos](04-data.md)).

### Resultados

La grilla muestra las filas del último SELECT. Las primeras aparecen enseguida y las demás van llegando mientras la
barra de estado cuenta («Recibiendo… 25.000 filas · 1,8 s»); al terminar, dice cuántas filas trajo y en cuánto tiempo.
Ordenar, filtrar, agrupar, totales, copiar, ver valores, gráficos y exportar: [Resultados](03-results.md).

### Plan

El plan de la última sentencia, como lo da Firebird: el detallado (en Firebird 3 o más nuevo) o el clásico. Un script
sin grilla no pide planes.

### Estadísticas

Lo que costó la última sentencia:

- los tiempos: preparar, hasta la primera fila y el total;
- filas leídas y afectadas, fetches, lecturas de página y escrituras;
- las lecturas por tabla, indexadas y no indexadas; una tabla leída sin índice lleva «⚠ lectura secuencial (sin
  índice)».

Las lecturas por tabla salen de las tablas de monitoreo del servidor; si no se pueden leer, la lista dice «(sin datos)».
Un script sin grilla no pide estadísticas.

### Historial

Las sentencias que ejecutaste, de la más nueva a la más vieja (hasta 500): fecha y hora, base, tipo de sentencia,
duración, filas, una ✗ si falló y el primer renglón. Al pasar el mouse se ve el SQL entero.

- El cuadro de arriba busca en el SQL y en los errores.
- **Solo la base de esta pestaña** (tildada de fábrica) muestra solo las de la base de la pestaña; destildada, las de
  todas las bases.
- Doble clic en una sentencia la carga en el editor: reemplaza el texto de la pestaña.
- Se guarda cifrado. Cuánto tiempo se guarda se elige en **Herramientas › Opciones… › Historial** (**Guardar las
  sentencias durante**; de fábrica, 90 días); al achicar el plazo, lo más viejo se borra en el momento.
- En el registro de cada base se puede apagar, con **Guardar historial de sentencias** (ver
  [Primeros pasos](01-start.md)).
- Una contraseña escrita en el editor (`CREATE USER … PASSWORD '…'`) no queda en el historial.
- Sin un llavero del sistema (por ejemplo, en un Linux sin escritorio) el historial no se guarda.

### Mensajes

Los errores completos, lo que hizo una sentencia sin filas («UPDATE: 12 filas afectadas en 35 ms»), el registro de los
scripts y el motivo de lo que se bloqueó. Cuando hay mensajes, **Guardar registro…** los guarda en un archivo de texto
con la fecha y la base (sin la contraseña), y **Copiar** los copia.

## Arrastrar desde el árbol

Arrastrá una tabla, una vista u otro objeto, o una columna, desde el árbol hasta el editor: se escribe su nombre donde
lo soltás, con comillas si hacen falta. También:

- doble clic en una columna del árbol escribe su nombre donde está el cursor;
- clic derecho en un objeto o una columna › **Escribir el nombre en la consulta** o **Copiar el nombre**.

Las bases y las carpetas también se arrastran, pero dentro del árbol, para ordenarlas: ver
[El árbol, las áreas y los diagramas](05-tree.md).
