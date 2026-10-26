# El árbol, las áreas y los diagramas

Cómo recorrer una base conectada desde el árbol, abrir y compilar sus objetos, buscar en los metadatos, ordenar una
base grande con áreas y dibujar sus relaciones.

## El árbol de una base

El árbol de la izquierda muestra las bases registradas, agrupadas por entorno o en tus carpetas. Registrar, conectar y
ordenar bases está en [Primeros pasos](01-start.md). Acá se ve qué hay adentro de una base conectada.

### Categorías

Al conectar una base, se abre en categorías con la cantidad de objetos al lado (por ejemplo, **Tablas** 245):

- **Tablas**, **Vistas** y **Procedimientos** están siempre, aunque no tengan nada.
- **Funciones**, **Paquetes**, **Triggers**, **Generadores**, **Excepciones**, **Dominios**, **Índices** y **Roles**
  aparecen solo si la base tiene alguno.
- **Áreas** va al final, solo si la base tiene áreas (ver [Las áreas en el árbol](05-tree.md#las-áreas-en-el-árbol)).

Los objetos de una categoría se leen la primera vez que la abrís. Al lado de cada objeto va un dato más: la tabla de un
trigger o de un índice, el tipo de un dominio. Los triggers y los índices desactivados se ven atenuados.

Las tablas y las vistas se abren a su vez en sus columnas, con el tipo y las marcas PK y NOT NULL; las de la clave
primaria llevan el ícono de una llave. Al pasar el mouse por una columna se ve su comentario.

### Filtrar el árbol

El cuadro de arriba del árbol filtra los objetos de las bases conectadas:

- Sin comodines, muestra los objetos cuyo nombre contiene el texto, sin distinguir mayúsculas: `turno` encuentra
  TURNOS, TURNOS_DET y P_TURNO_ALTA.
- Con `*` o `%`, el patrón tiene que cubrir el nombre entero: `LAB_*` (empiezan con LAB_), `*_DET` (terminan en _DET).
- Filtra objetos: las bases, las carpetas y las columnas no se filtran.
- Cada categoría dice cuántos coinciden («3 de 245»), se abre sola si tiene alguno y queda atenuada si no tiene
  ninguno. También filtra adentro de las áreas.
- Las categorías que todavía no se habían abierto se leen solas (solo los nombres). Mientras tanto dicen «… de 245» y
  la barra de estado avisa que está buscando en todas las categorías; al terminar, dice cuántos objetos coinciden.

Con un filtro puesto, el cuadro queda resaltado. Para quitarlo, borrá el texto o usá la cruz del cuadro (**Quitar el
filtro**).

### Abrir, cerrar y doble clic

- Un clic en el triángulo abre o cierra la fila, sin hacer nada más.
- `→` abre la fila elegida y `←` la cierra.
- Doble clic (o `Enter`):
  - en una base, la conecta y la abre, y la pestaña de consulta pasa a usarla (o se abre una consulta nueva en ella);
  - en un objeto, abre su pestaña (ver [La pestaña de un objeto](05-tree.md#la-pestaña-de-un-objeto));
  - en una columna, escribe su nombre en el editor, donde está el cursor;
  - en una categoría, una carpeta o un área, la abre o la cierra.
- Clic derecho: el menú de esa fila (ver [El menú de cada objeto](05-tree.md#el-menú-de-cada-objeto)).

## La pestaña de un objeto

Abrí un objeto con doble clic en el árbol, con **Abrir (DDL y dependencias)** o **Abrir (editar, compilar y
dependencias)** de su menú, o con `Ctrl+clic` sobre su nombre en el editor (ver [El editor de SQL](02-editor.md)).
Cada objeto tiene una sola pestaña por base: si ya está abierta, se elige.

La pestaña tiene tres partes:

- Arriba, una barra con el ícono y el nombre del objeto, su comentario (si tiene) y los botones **Compilar** y
  **Recargar**.
- En el medio, el DDL en el editor.
- Abajo, las pestañas de siempre (**Resultados**, **Mensajes**…) y además **Dependencias**, que es la que se ve al
  abrir (`Ctrl+6`). Las tablas y las vistas tienen también **Datos** (`Ctrl+7`), que se explica en
  [La pestaña Datos](04-data.md).

### El DDL

Los procedimientos, triggers, vistas y funciones se muestran como una sola sentencia `CREATE OR ALTER`, lista para
editar y compilar. Los demás objetos (tablas, dominios, generadores, excepciones, índices, paquetes y roles) muestran
el DDL armado desde los metadatos, para ver o copiar: en esas pestañas el editor es de solo lectura y no se ejecuta
nada (ni `F9`, `Ctrl+Enter`, `Ctrl+F9` ni el plan), porque correr el DDL de una tabla intentaría crearla de nuevo con
sus claves, índices, triggers y permisos. Para usar parte de ese DDL, copialo a una consulta nueva (`Ctrl+T`).

- **Recargar** vuelve a leer el DDL desde la base.
- `Ctrl+S` guarda una copia del texto en un archivo .sql. Siempre pregunta dónde y no compila nada.

### Dependencias

La pestaña **Dependencias** tiene dos listas:

- **Usa**: lo que el objeto necesita (las tablas que lee un procedimiento, la tabla a la que apunta una FK…).
- **Es usado por**: lo que se rompe si lo cambiás o lo borrás.

Cada lista va agrupada por tipo, con la cantidad, y al lado de cada objeto el detalle: las columnas que usa, el nombre
de la FK, una columna calculada o un CHECK. El cuadro **Filtrar por nombre o columna** achica las dos listas (los
títulos pasan a decir, por ejemplo, «Usa (3 de 12)»). Doble clic abre cada objeto.

Las dependencias salen de RDB$DEPENDENCIES, más las FK entre tablas y, en un dominio, las columnas y los parámetros que
lo usan.

> **Ojo:** el SQL dinámico (`EXECUTE STATEMENT`) y las consultas de las aplicaciones no figuran. Que **Es usado por**
> esté vacía no garantiza que nadie use el objeto.

### Compilar

Los procedimientos, triggers, vistas y funciones se editan y se compilan en su pestaña:

1. Cambiá el texto. Al lado del nombre aparece **● sin compilar**.
2. Apretá **Compilar**, o `F9` (o `Ctrl+Enter`) sin nada seleccionado.
3. Si compila, el cambio se confirma enseguida (no queda en una transacción abierta), el aviso desaparece, la barra de
   estado dice cuánto tardó y las dependencias se vuelven a leer.
4. Si no compila, la sentencia se deshace: el cursor va a la línea y la columna del error, la barra de estado muestra un
   resumen y **Mensajes**, el error completo.

Con texto seleccionado, `F9` ejecuta lo seleccionado, como en una consulta.

Para poder compilar:

- El texto tiene que ser una sola sentencia `CREATE OR ALTER` (o `ALTER` o `RECREATE`) del mismo objeto. Para crear
  otro objeto, usá una pestaña de consulta.
- No puede haber una transacción abierta en esa pestaña: hacé Commit o Rollback antes.
- En un registro de solo lectura, **Compilar** queda deshabilitado.
- La sentencia pasa por la misma revisión de seguridad que cualquier otra (ver [El editor de SQL](02-editor.md)).

### Cambios sin compilar

Mientras el texto no coincide con lo que está en la base, la barra dice **● sin compilar**. Los cambios no se aplican
hasta compilar, y FbStudio pregunta antes de perderlos:

- al cerrar la pestaña: **Descartar y cerrar** o **Seguir editando**;
- al apretar **Recargar**: **Descartar y recargar** o **Seguir editando**;
- al cerrar varias pestañas o al salir de FbStudio: un solo aviso con los nombres de las pestañas.

## El menú de cada objeto

Clic derecho sobre un objeto o una columna del árbol. Lo que no corresponde a esa clase de objeto aparece
deshabilitado o no aparece.

### Tablas

- **Abrir (DDL y dependencias)**: la pestaña del objeto.
- **Ver datos (editar sin bloquear)**: la misma pestaña, en **Datos** (ver [La pestaña Datos](04-data.md)).
- `SELECT FIRST 100 *`: ejecuta esa consulta en una pestaña con el nombre de la tabla (si ya hay una libre, la reusa).
- **Generar SQL**: ver [Generar SQL](05-tree.md#generar-sql).
- **Diagrama de relaciones**: ver [Diagramas](05-tree.md#diagramas).
- **Importar datos (CSV o Excel)…**: ver [Scripts, comparaciones e importación](08-scripts.md). Deshabilitado en un
  registro de solo lectura.
- **Comparar datos con otra base…**: compara las filas de la tabla con las de otra base y arma el script para
  igualarlas. Aparece solo con más de una base registrada. Se explica en
  [Scripts, comparaciones e importación](08-scripts.md).
- **Generar datos de prueba…**: solo en bases de Desarrollo y Prueba que no sean de solo lectura (ver
  [Scripts, comparaciones e importación](08-scripts.md)).
- **Permisos…**, **Escribir el nombre en la consulta** y **Copiar el nombre**: ver más abajo.

El menú de la categoría **Tablas** tiene **Importar a una tabla nueva (CSV o Excel)…** (salvo en un registro de solo
lectura).

### Vistas

Como las tablas, pero con **Abrir (editar, compilar y dependencias)**, porque una vista se compila, y **Generar SQL**
solo con el SELECT. No tienen importar, comparar datos ni datos de prueba.

### Procedimientos y funciones

- **Abrir (editar, compilar y dependencias)**.
- **Generar SQL**: la llamada con sus parámetros (ver [Generar SQL](05-tree.md#generar-sql)).
- **Permisos…**, **Escribir el nombre en la consulta** y **Copiar el nombre**.

### Otros objetos

- Triggers: **Abrir (editar, compilar y dependencias)** y el nombre.
- Paquetes, generadores y excepciones: **Abrir (DDL y dependencias)**, **Permisos…** y el nombre.
- Dominios, índices y roles: **Abrir (DDL y dependencias)** y el nombre.

### Columnas

Las columnas de las tablas y las vistas tienen **¿Dónde se usa?…**, **Escribir el nombre en la consulta** y **Copiar el
nombre**.

### Generar SQL

**Generar SQL** arma la sentencia y la deja en una consulta nueva de esa base, sin ejecutarla, con el cursor en el
editor.

| Objeto | Opciones |
|---|---|
| Tabla | **SELECT con todas las columnas**, **INSERT**, **UPDATE por la clave primaria**, **DELETE por la clave primaria**, **UPDATE OR INSERT** |
| Vista | **SELECT con todas las columnas** |
| Procedimiento | **SELECT * FROM …**, **EXECUTE PROCEDURE …** |
| Función | **SELECT … FROM RDB$DATABASE** |

En las tablas:

- Los valores van como parámetros `:COLUMNA`, que FbStudio pide al ejecutar (ver [El editor de SQL](02-editor.md)).
  Si el nombre de una columna tiene espacios u otros signos, esos caracteres pasan a `_` en el parámetro; si empieza
  con un número, lleva `P_` adelante.
- INSERT, UPDATE y UPDATE OR INSERT no escriben las columnas calculadas (COMPUTED BY).
- UPDATE cambia todas las columnas que no son de la clave. UPDATE y DELETE buscan la fila por la clave primaria, y
  UPDATE OR INSERT la usa en MATCHING.
- Las listas largas de columnas se parten en varios renglones.

Por ejemplo, **UPDATE por la clave primaria** de TURNOS:

```
UPDATE TURNOS
SET FECHA = :FECHA,
    PACIENTE = :PACIENTE,
    ESTADO = :ESTADO
WHERE ID = :ID
```

Si la tabla no tiene clave primaria, la condición queda para completar, así la sentencia no corre por error:

```
DELETE FROM TURNOS_LOG
WHERE /* sin clave primaria: completar la condición */
```

En los procedimientos y las funciones, cada parámetro de entrada va como `:NOMBRE`; los de una función externa (UDF),
que no tienen nombre, van como `?`. **SELECT * FROM …** es para los procedimientos que devuelven filas: si el
procedimiento no devuelve nada, arma un `EXECUTE PROCEDURE` igual.

```
SELECT *
FROM TURNOS_LIBRES(:DESDE, :HASTA)
```

Si el objeto se borró después de cargar el árbol, FbStudio avisa: recargá los metadatos.

### ¿Dónde se usa?

Sobre una columna, **¿Dónde se usa?…** abre una ventana con lo que hay que mirar antes de cambiarla o borrarla:

- **La usa**: las vistas, procedimientos, funciones y triggers que la nombran; las columnas calculadas y los CHECK que
  la usan; sus índices y la clave que respaldan; las FK de otras tablas que la apuntan, también las lógicas (ver
  [FK lógicas](05-tree.md#fk-lógicas)).
- **Apunta a**: la tabla a la que apunta la FK de la columna misma.

Doble clic abre cada objeto (los índices no). La ventana no bloquea: podés seguir trabajando con ella abierta.

> **Ojo:** el SQL dinámico (`EXECUTE STATEMENT`) y las consultas de las aplicaciones no figuran. Que la lista esté
> vacía no garantiza que nadie use la columna.

### Permisos

**Permisos…** está en las tablas, vistas, procedimientos, funciones, paquetes, generadores y excepciones. Abre
**Usuarios y permisos** en **Por objeto: quién tiene permisos**, con ese objeto elegido: se ve quién tiene qué permiso
sobre él y se cambia con las casillas y **Aplicar**. Se explica en [Mantenimiento y permisos](07-maintenance.md).

### Escribir o copiar el nombre

- **Escribir el nombre en la consulta**: lo escribe donde está el cursor del editor de la pestaña elegida, entre
  comillas dobles si hace falta (si tiene minúsculas, espacios u otros signos, o es una palabra reservada).
- **Copiar el nombre**: lo copia tal cual, sin comillas.

El doble clic en una columna hace lo mismo que **Escribir el nombre en la consulta**.

## Recargar metadatos

El árbol lee los objetos al conectar y no se entera solo de lo que cambia después. Si creás, cambiás o borrás un objeto
desde una consulta (o lo hace otra persona), usá **Recargar metadatos** en el menú de la base. Aparece solo con la
base conectada.

Vuelve a leer las categorías, las áreas y los datos del servidor (por ejemplo, si la base salió del modo backup), y
también lo que usan el autocompletado y los diagramas. Las categorías que estaban abiertas quedan cerradas.

## Buscar en los metadatos

Busca un texto en las fuentes, los nombres, las columnas y las descripciones de una base. Abrila con `Ctrl+Shift+F`,
con **Consulta › Buscar en metadatos…**, con el botón **Buscar** de la barra o con **Buscar en metadatos…** en el menú
de la base. Se abre en una pestaña, en la base de lo último que tocaste: la elegida en el árbol, o la de la pestaña si
volviste a ella.

### Cómo buscar

1. Escribí el texto (al menos 2 caracteres) y apretá `Enter` o **Buscar**. No distingue mayúsculas y busca el texto
   tal cual, sin comodines.
2. Elegí dónde buscar (vienen todas tildadas): **Fuentes (procedimientos, triggers, vistas, funciones, paquetes)**,
   **Nombres de objetos**, **Columnas** y **Descripciones**.
3. Si la base tiene áreas, **Área:** muestra solo lo que está en un área (o en **Sin área**). Se puede cambiar
   después de buscar, sin volver a buscar; los triggers y los índices van con su tabla.

La búsqueda la hace el servidor y solo trae lo que coincide. `Esc` cancela una búsqueda larga.

### Los resultados

- Van agrupados por tipo de objeto, con la cuenta: «Procedimientos · 148 objetos · 160 coincidencias».
- Cada objeto muestra su primera coincidencia: dónde está (**línea 12**, **nombre**, **columna** o **descripción**) y
  el fragmento con el texto resaltado.
- Un objeto con varias coincidencias se despliega con el triángulo para ver cada una.
- Doble clic o `Enter` abre el objeto con el cursor en esa línea. `→` despliega y `←` pliega.
- Arriba, el resumen: cuántas coincidencias, en cuántos objetos y cuánto tardó. La pestaña toma el nombre de lo que
  buscaste.

## Arrastrar al editor

Arrastrá una tabla, una vista u otro objeto, o una columna, desde el árbol hasta el editor de SQL: se escribe su nombre
donde lo soltás, entre comillas si hace falta. Las bases y las carpetas, en cambio, se arrastran dentro del árbol para
ordenarlas (ver [Primeros pasos](01-start.md)).

## Áreas

### Qué es un área

Un área es un grupo de objetos con nombre propio, para no ver cientos de tablas juntas: por ejemplo, LAB con todo lo
del laboratorio y TURNOS con lo de turnos. Un área junta objetos por reglas: patrones de prefijo como `LAB_*` y
nombres de objetos sueltos.

- Un objeto puede estar en varias áreas.
- Los triggers y los índices van con su tabla.
- Lo que no está en ninguna área queda en **Sin área**.
- Las áreas no tocan la base: se guardan en un archivo aparte, que se puede compartir.

### La pestaña Áreas

Abrila con **Herramientas › Áreas de la base…**, con **Herramientas › Áreas…** en el menú de la base del árbol, o con
**Editar áreas…** sobre el nodo **Áreas** del árbol. Hay una por base.

- A la izquierda, la lista de áreas: el nombre, las reglas y cuántos objetos junta cada una. Al final, **Sin área**.
- A la derecha, el área elegida: **Nombre**, **Descripción**, las reglas, las excepciones, **Sumar lo que usa solo sus
  tablas (procedimientos, vistas y triggers)** y **Color:**.
- Debajo, la vista previa: lo que junta el área, con un resumen por tipo («35 objetos: 12 tablas · 3 vistas · 20
  procedimientos»). Se filtra por nombre (**Filtrar por nombre**, con `*`, `?` y `%`) y por tipo (**Todos los tipos**
  o uno). Cada objeto dice si entró **por lo que usa** y en qué otras áreas está («también en TURNOS»). Doble clic o
  `Enter` abre el objeto.
- Arriba, los botones **Nueva área**, **Quitar área**, **Proponer por prefijos**, **Diagrama**, **Recargar** y
  **Guardar**.
- Abajo de todo, qué archivo se está usando (ver [El archivo de áreas](05-tree.md#el-archivo-de-áreas)).

La vista previa cambia mientras escribís, pero nada se guarda hasta **Guardar**. No se puede guardar un área sin nombre
ni dos con el mismo nombre. Si cerrás la pestaña con cambios sin guardar, pregunta antes.

### Reglas y excepciones

- **Reglas: una por renglón (nombres o patrones con * y ?)**: entra el objeto cuyo nombre cumpla alguna. También se
  pueden separar con coma o punto y coma.
- `*` es cualquier cosa (también nada), `?` es un carácter y `%` vale como `*`. No distingue mayúsculas. Un nombre sin
  comodines toma solo ese objeto.
- Cada regla se compara con el nombre del objeto, sea del tipo que sea, y en los triggers y los índices también con el
  de su tabla.
- **Excepciones: quedan afuera aunque cumplan una regla**: se escriben igual y ganan siempre.

Por ejemplo:

| Reglas | Excepciones |
|---|---|
| `LAB_*`, `LABORATORIO_*`, `PACIENTES` | `LAB_TMP_*` |

junta todo lo que empieza con LAB_ o LABORATORIO_ (tablas, vistas, procedimientos… y los triggers e índices de esas
tablas) y la tabla PACIENTES, menos lo que empieza con LAB_TMP_.

### Sumar lo que usa

Con **Sumar lo que usa solo sus tablas (procedimientos, vistas y triggers)** tildado, entra también un procedimiento,
una vista o un trigger si todas las tablas que usa están en el área, según las dependencias de la base (las tablas del
sistema no cuentan). Sirve cuando los procedimientos no siguen el prefijo de las tablas.

- Una vista que entra así cuenta como tabla del área para los demás.
- Un trigger necesita, además, que su tabla esté en el área.
- Las excepciones igual ganan.
- En la vista previa, lo que entró así dice **por lo que usa**.

Las áreas nuevas vienen con esta casilla tildada.

### Proponer por prefijos

**Proponer por prefijos** arma un área por cada familia de prefijos de las tablas que todavía no tienen área:

- El prefijo es lo que va antes del primer `_`. Los prefijos de una sola letra no cuentan.
- Los prefijos que empiezan igual van juntos, con el nombre del más corto: LAB_ y LABORATORIO_ forman el área LAB, con
  las reglas `LAB_*` y `LABORATORIO_*`.
- Se propone la familia que junta 3 tablas o más, si no hay ya un área con ese nombre. Las que juntan más van primero.
- Cada propuesta trae una descripción y la casilla de [Sumar lo que usa](05-tree.md#sumar-lo-que-usa) tildada.

Las propuestas quedan para revisar: no se guarda nada hasta **Guardar**. Si no hay ninguna, la barra de estado lo dice.

### Agregar o quitar a mano

En la vista previa, elegí uno o varios objetos y usá el clic derecho:

- **Abrir** (con un solo objeto elegido).
- **Agregar a** › un área: suma el nombre como regla de esa área (o saca la excepción que lo dejaba afuera, si era su
  nombre exacto).
- **Quitar de «LAB»**: saca la regla con su nombre o, si lo toma un patrón o entró por lo que usa, agrega una
  excepción con su nombre.

Para repartir lo suelto, elegí **Sin área** a la izquierda, elegí los objetos y usá **Agregar a**. Si una excepción con
patrón lo sigue dejando afuera, la barra de estado lo avisa: cambiá las excepciones.

### Color

**Color:** deja el área **Sin color** o le pone uno de la paleta de las carpetas, sin rojos (quedan para producción).
El color se ve en el ícono del área en el árbol y en la pestaña Áreas, y viaja con el archivo de áreas y con el backup
de los registros.

### El archivo de áreas

Las áreas de cada base se guardan en un archivo JSON propio, sin datos de conexión: se puede pasar a otras personas.
Abajo de la pestaña se ve cuál se usa:

- **Archivo de esta PC:** el de siempre, en la carpeta de datos de FbStudio de tu usuario (ver
  [Opciones, atajos y privacidad](09-options.md)).
- **Archivo compartido:** uno que elegiste, por ejemplo en una carpeta de red que usa todo el equipo.

Los botones de abajo:

- **Usar otro archivo…**: la base pasa a usar ese archivo (el registro de la base lo recuerda).
- **Guardar como…**: guarda las áreas en otro archivo y la base pasa a usarlo. Sirve para compartirlas.
- **Volver al archivo de esta PC**: aparece solo cuando se usa un archivo compartido.

Si el archivo cambió desde que lo abriste (lo guardó otra persona), **Guardar** avisa antes de pisarlo: **Reemplazar**
pierde esos cambios; para verlos primero, cancelá y usá **Recargar**. **Recargar** vuelve a leer el archivo y los
objetos de la base; con cambios sin guardar, pregunta antes (**Descartar y recargar**). **Usar otro archivo…** también
pregunta (**Descartar y cambiar**).

El árbol y la búsqueda usan lo último que se leyó o se guardó; la pestaña Áreas vuelve a leer el archivo cada vez que
se abre.

El backup de los registros (**Exportar registros…**) puede llevar las áreas de cada base: al importarlo, las reciben
las bases que todavía no tienen y las demás conservan las suyas (ver [Primeros pasos](01-start.md)).

### Las áreas en el árbol

Cuando una base tiene áreas, su último nodo es **Áreas**, con la cantidad. Adentro va cada área con su color y cuántos
objetos junta, y en cada una sus objetos por tipo (un tipo sin objetos en el área no aparece). Al final, **Sin área**.

Los objetos de un área se usan igual que en el resto del árbol: doble clic, menú y arrastrar. Al guardar las áreas, el
árbol se rearma solo.

Clic derecho sobre un área: **Diagrama del área** y **Editar áreas…**. Sobre **Áreas** o **Sin área**: **Editar
áreas…**.

### Dónde se usan

- **Buscar en metadatos**: el filtro **Área:** (ver [Buscar en los metadatos](05-tree.md#buscar-en-los-metadatos)).
- **Estadísticas**: **Tildar las tablas de un área…** (ver [Monitoreo y rendimiento](06-monitoring.md)).
- **Script de la base…** y **Documentación en HTML…**: se pueden limitar a un área. El script lleva sus objetos, los
  dominios que usan y sus permisos; las FK a tablas de afuera no se escriben, así corre en una base vacía (ver
  [Scripts, comparaciones e importación](08-scripts.md)).
- **Comparar bases…**: las reglas del área del origen se aplican a las dos bases y se compara solo lo que cae en ella
  (ver [Scripts, comparaciones e importación](08-scripts.md)).
- **Diagramas**: el diagrama de un área (ver [Diagramas](05-tree.md#diagramas)).

## Diagramas

Un diagrama dibuja tablas y vistas con sus relaciones: las FOREIGN KEY y, si querés, las FK lógicas de los
comentarios. Solo lee la base.

### Abrir un diagrama

- De una tabla o una vista: **Diagrama de relaciones** en su menú del árbol. Dibuja la tabla y sus vecinas.
- De un área: **Diagrama del área** en el árbol, o **Diagrama** en la pestaña Áreas (con el área tal como está
  escrita, aunque no la hayas guardado). Dibuja las tablas y las vistas del área.
- Uno guardado: ver [Guardar y abrir](05-tree.md#guardar-y-abrir).

Cada diagrama se abre en su pestaña («Diagrama · TURNOS», «Diagrama · área LAB»); si ya estaba abierto, se elige.

### Qué muestra

- Cada tabla es una caja con el nombre arriba. Las vistas llevan el encabezado de otro color.
- Las columnas llevan su tipo y la marca PK, FK o PK FK. Las que no se muestran se resumen («+ 12 columnas»).
- La flecha apunta a la tabla referenciada. Una línea punteada es una FK lógica. Una FK a la misma tabla es un lazo.
- A la derecha: el resumen («12 tablas · 15 relaciones · 2 lógicas · 3 escondidas»), la tabla elegida y las tablas
  escondidas.

### Opciones

| Opción | Qué hace |
|---|---|
| **Vecinas:** | Solo en el diagrama de una tabla: **1 paso (las vecinas)** o **2 pasos** (también las vecinas de las vecinas). |
| **Mostrar las vecinas de afuera** | Solo en el de un área: suma las tablas de afuera que se relacionan con las del área, como cajitas sin columnas. |
| **Esconder las muy referenciadas** | Ver [Tablas muy referenciadas](05-tree.md#tablas-muy-referenciadas). |
| **FK lógicas (punteadas)** | Dibuja también las FK de los comentarios (ver [FK lógicas](05-tree.md#fk-lógicas)). |
| **Columnas:** | **Solo claves** (las de la clave primaria y las que apuntan a otra tabla), **Todas** o **Ninguna**. |
| **Acomodo:** | **Automático**, **En capas**, **Por fuerzas** o **Por distancias** (ver abajo). |
| **Reacomodar** | Vuelve a acomodar todo. Lo movido a mano se pierde. |
| **Ver todo** | Encuadra el diagrama entero. |
| **Releer la base** | Vuelve a leer las tablas y relaciones, después de cambiar la estructura de la base. |
| **Agregar tabla…** | Suma una tabla o vista por su nombre, con sus relaciones con lo que ya está. |
| **Camino hasta…** | Ver [Camino hasta otra tabla](05-tree.md#camino-hasta-otra-tabla). |
| **Guardar diagrama…** | Ver [Guardar y abrir](05-tree.md#guardar-y-abrir). |
| **Copiar como Mermaid** y **Exportar…** | Ver [Exportar](05-tree.md#exportar). |

Los acomodos:

- **En capas**: las tablas referenciadas arriba y las que las usan abajo.
- **Por fuerzas**: las relacionadas se atraen y las demás se separan.
- **Por distancias**: cada tabla cerca de las que tiene a pocos pasos.
- **Automático**: en capas, salvo que con más de 8 tablas quede muy ancho o muy alto; ahí, por distancias.

### Tablas muy referenciadas

Algunas tablas se relacionan con casi todo (usuarios, pacientes…) y harían que todo quede conectado con todo. Con
**Esconder las muy referenciadas** tildado (viene así), las tablas que se relacionan con más de 30 tablas no se dibujan
y la búsqueda de vecinas no pasa a través de ellas.

- Se listan a la derecha, en **Escondidas por muy referenciadas**, con cuántas relaciones tienen con lo dibujado y
  cuántas vecinas en total.
- **Mostrar** dibuja una.
- Si el diagrama sale de una de ellas, se dibuja igual. **Agregar tabla…** y **Camino hasta…** también la muestran
  cuando la necesitan.

### FK lógicas

Algunas bases no declaran todas sus FOREIGN KEY y anotan la relación en el comentario de la columna, con la marca
`#FK_%TABLA,%COLUMNA` (lo que sigue, como más campos o un `#` de cierre, no importa). Por ejemplo, el comentario
`#FK_%PACIENTES,%ID#` en la columna TURNOS.PACIENTE dice que esa columna apunta a PACIENTES.ID. La marca puede ir junto
a otras en el mismo comentario.

FbStudio las usa en:

- los diagramas, como líneas punteadas, con **FK lógicas (punteadas)** tildado;
- **¿Dónde se usa?…** de una columna;
- el autocompletado después de JOIN (ver [El editor de SQL](02-editor.md));
- la documentación en HTML (ver [Scripts, comparaciones e importación](08-scripts.md)).

### Mover, acercar y elegir

- Arrastrá una tabla para moverla; arrastrá el fondo para recorrer el diagrama.
- La rueda del mouse acerca y aleja en el lugar del puntero (de 10 % a 300 %).
- Un clic elige una tabla y resalta sus líneas. A la derecha se ven sus columnas, qué usa y quién la usa, con los
  botones **Abrir**, **Agregar sus vecinas**, **Quitar del diagrama** y **Camino hasta…**. Un clic en el fondo la deja
  de elegir.
- Doble clic en una tabla la abre en su pestaña.

Lo que movés a mano queda en su lugar cuando sumás o sacás tablas, hasta que apretás **Reacomodar**.

### Camino hasta otra tabla

**Camino hasta…** busca la cadena de FK más corta entre dos tablas, sin importar hacia dónde apunta cada FK:

1. Elegí en el diagrama la tabla de partida. En el diagrama de una tabla, si no elegís ninguna, parte de esa tabla.
2. Apretá **Camino hasta…** y escribí la tabla de llegada. No hace falta que esté dibujada.
3. FbStudio suma las tablas del medio, resalta el camino y elige la tabla de llegada. La barra de estado lo dice:
   «Camino: TURNOS_DET → TURNOS → PACIENTES (2 relaciones)».

Si hay otro camino, evita las tablas muy referenciadas; si no, pasa por donde haga falta y las muestra. Usa las FK
lógicas solo con **FK lógicas (punteadas)** tildado. Si las tablas no están unidas por ninguna cadena, lo avisa. Entre
caminos igual de cortos, elige siempre el mismo.

### Guardar y abrir

**Guardar diagrama…** deja un archivo .fbsdiagram con las opciones, la tabla o el área de partida, las tablas sumadas y
sacadas, las muy referenciadas que mostraste, el camino resaltado y dónde quedó cada tabla. El de un área guarda
también la definición del área como estaba.

No guarda la estructura de la base: al abrirlo se vuelve a leer, así que una tabla nueva o una FK que cambió aparecen
como están ahora.

Para abrirlo, usá **Herramientas › Abrir diagrama guardado…**: en la barra de menú, se abre en la base elegida en el
árbol (o en la de la pestaña); en el menú de una base del árbol, en esa base. Se puede abrir en cualquier base: si se
guardó en otra, la barra de estado lo dice. Abrir dos veces el mismo archivo abre dos pestañas.

### Exportar

**Exportar…** guarda el diagrama entero en el formato que elijas:

| Formato | Para qué sirve |
|---|---|
| **Imagen PNG** | Documentos y mails. Sale siempre con fondo blanco y colores claros, al doble de resolución (menos si el diagrama es enorme). |
| **Dibujo SVG** | Una imagen que se agranda sin perder calidad; se abre en el navegador, en draw.io o en Inkscape. |
| **Diagrama de draw.io (editable)** | Un .drawio para seguir editándolo en draw.io: cada tabla con sus columnas (marca, nombre y tipo), las relaciones pegadas a las tablas (las lógicas punteadas) y los lugares que tenían. Al mover una tabla, sus líneas la siguen. |
| **Mermaid (texto para GitHub y wikis)** | Texto `erDiagram` en un .mmd, o en un .md ya dentro de un bloque de código mermaid. GitHub, GitLab y muchas wikis lo dibujan solos. No lleva los lugares: Mermaid acomoda solo. |
| **Diagrama de FbStudio** | El mismo .fbsdiagram de **Guardar diagrama…**. |

**Copiar como Mermaid** pone ese texto en el portapapeles: pegalo en un bloque de código marcado como `mermaid` en un
README, un issue o una wiki.
