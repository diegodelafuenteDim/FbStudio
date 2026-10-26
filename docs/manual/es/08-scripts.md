# Scripts, comparaciones e importación

Lo que trabaja con la base entera o con muchas filas a la vez: el script de la base y su documentación, comparar dos
bases (la estructura o los datos de una tabla), importar un CSV o un Excel y generar datos de prueba.

## Script de la base

Arma el script para recrear la base en una base vacía (extraer los metadatos). Se abre de dos maneras:

- **Herramientas › Script de la base…**, para la base elegida en el árbol o la de la pestaña.
- En el árbol, clic derecho en la base › **Script y comparar** › **Script de la base…**.

FbStudio lee la base en una transacción de solo lectura: no bloquea a nadie. En una base grande son miles de consultas
cortas y puede tardar un poco; mientras tanto, la barra de estado muestra cuántos objetos lleva leídos y **Cancelar**
lo corta.

### Qué incluir

La ventana **Script de la base** pregunta qué va en el script. De entrada está todo tildado.

| Casilla | Qué agrega |
|---|---|
| **Dominios** | Los dominios. |
| **Generadores** | Los generadores, con su valor inicial y su incremento. |
| **con su valor actual** | El valor que tiene hoy cada generador (SET GENERATOR … TO) y el de las columnas identity: la copia sigue numerando igual. |
| **Excepciones** | Las excepciones. |
| **Roles** | Los roles. |
| **Permisos (GRANT)** | Los GRANT sobre tablas, vistas, procedimientos, funciones, paquetes, generadores, excepciones, dominios y roles. |
| **Tablas (con claves, CHECK e índices)** | Las tablas con sus columnas calculadas, claves primarias, únicas y foráneas, CHECK e índices. |
| **Vistas** | Las vistas. |
| **Procedimientos** | Los procedimientos. |
| **Funciones (PSQL y UDF)** | Las funciones PSQL y las funciones externas. |
| **Paquetes** | Los paquetes, con su cuerpo. |
| **Triggers** | Los triggers de tablas, de vistas y de la base. |

Si la base tiene áreas (ver [El árbol, las áreas y los diagramas](05-tree.md)), arriba aparece una lista para elegir
**Toda la base** o **Solo el área «…»**. Con un área, el script lleva sus objetos, los dominios que usan y sus
permisos. Las FK a tablas de afuera no se escriben, así corre en una base vacía; un comentario al principio dice
cuántas quedaron afuera.

Apretá **Generar**: se abre la pestaña **Script de la base** con el script, de solo lectura.

### Qué trae el script

El script va en un orden que corre de corrido sobre una base vacía:

1. Roles, dominios, generadores, excepciones y funciones externas (UDF).
2. Tablas con su clave primaria y sus claves únicas.
3. El valor actual de las columnas identity, si se tildó **con su valor actual**. Va con GEN_ID, que da lo mismo en
   Firebird 3, 4 y 5.
4. Procedimientos, funciones y paquetes con solo la firma, así no importa quién usa a quién.
5. Vistas, cada una después de las que usa.
6. Columnas calculadas, puestas en su lugar.
7. Claves foráneas, CHECK e índices.
8. Los cuerpos de procedimientos, funciones y paquetes.
9. Triggers, comentarios y permisos.

Cada sección termina con COMMIT y los bloques PSQL van entre SET TERM. El encabezado dice de qué base salió y cuándo.

> **Ojo:** los permisos de DDL (crear, modificar o borrar un tipo de objeto) no se escriben; un comentario al final dice
> cuántos quedaron afuera, para revisarlos a mano. Tampoco van los permisos dados a procedimientos, triggers, vistas o
> funciones que ya no existen.

### Guardar, copiar o correrlo en otra base

La barra de la pestaña tiene:

- **Guardar como…**: guarda el script en un archivo .sql (UTF-8).
- **Copiar**: lo copia al portapapeles.
- **Abrir en una pestaña sin base**: lo lleva a una pestaña de consulta que no tiene base. Hacé doble clic en el árbol
  en la base **vacía** donde lo querés correr y ejecutalo con `F9`, o con `Ctrl+F9` para correrlo como script sin
  grilla (ver [El editor de SQL](02-editor.md)).
- **Documentación HTML…**: la documentación de lo leído (ver [Documentación en HTML](08-scripts.md#documentación-en-html)).
- **Volver a generar…**: elegir otra vez qué incluir y volver a leer la base.

Al lado, «Generado a las 16:50 · 4.123 objetos» dice de cuándo es lo que se ve.

> **Ojo:** el script es para una base vacía. Sobre la misma base fallaría o pisaría objetos.

Para crear una base vacía directamente en el servidor, mirá **Base vacía** en
[Mantenimiento y permisos](07-maintenance.md).

## Documentación en HTML

Genera un solo archivo HTML con la documentación de la base, para leer en cualquier navegador y sin internet. Se abre de
dos maneras:

- En el árbol, clic derecho en la base › **Script y comparar** › **Documentación en HTML…**. Aparece la misma ventana
  del script (qué incluir y el área), se lee la base y después sigue con la documentación.
- Con el botón **Documentación HTML…** de la pestaña **Script de la base**. Sale de lo que ya se leyó, sin volver a
  leer la base.

FbStudio pregunta si incluir el código:

- **Sin el código**: la estructura y las descripciones.
- **Con el código**: suma, plegado debajo de cada uno, el código PSQL de los procedimientos, las funciones y los
  triggers. En una base grande el archivo crece mucho.

Después elegís dónde guardarlo (propone un nombre con el de la base y la fecha) y se abre en el navegador.

Qué trae el HTML:

- A la izquierda, un índice por tipo de objeto con un buscador que filtra por nombre.
- Arriba, cuándo se generó y, si se eligió, el área.
- Cada tabla con sus columnas (tipo, si acepta NULL, valor por defecto y descripción), la clave primaria marcada, cada
  FK como enlace a la tabla referenciada (también las lógicas de los comentarios «#FK_»), sus claves y restricciones,
  las tablas que la referencian, sus índices y sus triggers.
- Las vistas con sus columnas y su SELECT, los procedimientos con sus parámetros de entrada y de salida, las funciones
  con sus parámetros y lo que devuelven, los paquetes con su encabezado, y los dominios, generadores y excepciones (con
  su mensaje).

La documentación respeta lo elegido en la ventana del script: el área y las casillas.

## Comparar bases

Compara la estructura de dos bases y arma el script para que el destino quede igual al origen. Se abre de dos maneras:

- **Herramientas › Comparar bases…**.
- En el árbol, clic derecho en la base › **Script y comparar** › **Comparar con otra base…**: esa base queda como
  origen.

Hacen falta dos bases registradas.

### Origen, destino y opciones

En la ventana **Comparar bases**:

- **Origen (el modelo, por ejemplo desarrollo):** la base que tiene la estructura buena.
- **Destino (el que se quiere igualar, por ejemplo producción):** la que se va a cambiar. FbStudio propone una de
  producción, si hay.
- Si el origen tiene áreas, se puede elegir **Solo el área «…»**. Las reglas del área del origen se aplican a las dos
  bases y se compara solo lo que cae en ella, sin las FK a tablas de afuera.

Las opciones:

- **Comparar también los permisos (GRANT)**: tildada de entrada.
- **Dejar las columnas en el mismo orden que en el origen**: tildada de entrada. Agrega ALTER … POSITION donde el orden
  es distinto.
- **Borrar en el destino lo que no está en el origen (objetos, columnas, índices)**: destildada de entrada. Sin ella,
  lo que sobra en el destino se ve en la lista pero no se borra. Con ella, también se quitan los permisos que sobran
  (REVOKE).

Apretá **Comparar**. FbStudio lee las dos bases a la vez en transacciones de solo lectura: no cambia nada. El script
se arma después, con lo que tildes, y se ejecuta a mano.

### La lista de diferencias

La pestaña **Comparar** muestra arriba «desarrollo → producción» y un resumen como «12 diferencias: 3 nuevas, 8
distintas, 1 sobran». A la izquierda va una diferencia por renglón: la casilla, el tipo (tabla, índice, vista,
procedimiento, permiso…), el nombre con el primer detalle y una marca:

| Marca | Qué quiere decir | Qué hace el script |
|---|---|---|
| **nuevo** (verde) | Está en el origen y no en el destino. | Lo crea. |
| **distinto** (ámbar) | Está en las dos con otra definición. | Lo modifica. |
| **sobra** (rojo) | Está solo en el destino. | Lo borra, solo con la opción de borrar. |

Lo que sobra sin la opción de borrar no tiene nada que ejecutar: se ve, pero no se puede tildar. **Tildar todo** y
**Destildar todo** cambian todas las casillas.

Al elegir una diferencia, la pestaña **Diferencia** de la derecha muestra:

- El detalle de lo que cambió, de lo que tiene el destino a lo que tiene el origen: «columna OBSERVACIONES:
  VARCHAR(100) → VARCHAR(200)», «columna FECHA: pasa a NOT NULL», «orden de las columnas», «comentario distinto».
- El DDL de los dos lados, **En el origen** y **En el destino**, uno al lado del otro. Si el objeto está en las dos,
  los renglones que cambian van marcados: en verde lo que el origen agrega y en rojo lo que el destino pierde. Del lado
  donde no está dice «(no está)».

### El script para igualar

1. Tildá lo que querés llevar al destino.
2. Apretá **Script para igualar**: el script aparece en la pestaña **Script para igualar** de la derecha.
3. Revisalo y apretá **Abrir en una pestaña del destino**: va a una pestaña de consulta del destino, sin ejecutarse.
4. Ejecutalo con `F9`. Cada etapa termina con COMMIT. En producción, FbStudio pide confirmación antes de ejecutar.

**Guardar como…** guarda el script en un archivo .sql.

Cómo es el script:

- Va por etapas, en el mismo orden que el script de la base: primero lo que se borra y después dominios, tablas,
  firmas, vistas, restricciones, índices, cuerpos, triggers, comentarios y permisos, con COMMIT entre etapas.
- Las tablas se modifican columna por columna: nunca se recrean y los datos no se tocan. Procedimientos, funciones,
  vistas y triggers van con CREATE OR ALTER.
- Las restricciones se comparan por su definición y no por su nombre: los nombres INTEG_n que pone el servidor cambian
  de una base a otra.
- Lo que Firebird no cambia con un ALTER queda como comentario, para hacerlo a mano: la collation de un dominio, una
  columna que pasa de calculada a común (o al revés) y una identity distinta.

> **Ojo:** si cambiás las casillas después de armar el script, el script se borra: apretá **Script para igualar** otra
> vez. **Volver a comparar** lee las dos bases de nuevo con las mismas opciones.

> **Ojo:** si el destino es un registro de solo lectura, el script se puede ver y guardar, pero no ejecutar ahí: el
> registro bloquea el DDL.

## Comparar datos con otra base

Compara las filas de una tabla en dos bases, por la clave primaria, y arma el script para igualar los datos del
destino. Es para tablas chicas: configuración, tipos, nomencladores (por ejemplo, PRECIOS).

En el árbol, clic derecho en la tabla › **Comparar datos con otra base…**. Aparece si hay más de una base registrada.
La base de la tabla es el origen (el modelo). Se abre la pestaña **Datos de PRECIOS**:

1. En **Destino (el que se iguala):** elegí la otra base. FbStudio propone una de producción si el origen no lo es.
2. Apretá **Comparar**. Se leen las dos tablas a la vez, cada una con su conexión de solo lectura: no se escribe nada
   en ninguna de las dos.

### Las diferencias

Arriba, el resumen: cuántas filas faltan en el destino, cuántas son distintas, cuántas sobran y cuántas son iguales.
Si no hay diferencias, dice «Son iguales». La lista muestra la clave de cada fila («12», o «12 · 3» con una clave de
varias columnas) y su marca:

| Marca | Qué hace el script |
|---|---|
| **Falta en el destino** (verde) | La inserta (INSERT). |
| **Distinta** (ámbar) | Actualiza solo las columnas que cambian (UPDATE … WHERE por la clave). Debajo de la fila se ve cada una, de lo que tiene el destino a lo que tiene el origen: «IMPORTE: 100.00 → 120.00». |
| **Sobra en el destino** (rojo) | La borra (DELETE) solo si tildás **Borrar las que sobran**; si no, el script deja un comentario con cuántas son. |

Se comparan las columnas que están en las dos bases, salvo las calculadas. Una columna que está solo en el origen no se
compara y una que está solo en el destino no se toca; el resumen las nombra. Los textos se comparan sin los espacios del
final y los números por su valor, aunque los tipos sean distintos (INTEGER y BIGINT).

### El script

El script aparece a la derecha y se vuelve a armar solo al tildar o destildar **Borrar las que sobran**. No lleva
COMMIT.

1. Apretá **Abrir en una pestaña del destino**: el script va a una consulta del destino, sin ejecutarse.
2. Revisalo y ejecutalo con `F9`.
3. Confirmá a mano con Commit (`Ctrl+Alt+C`), o deshacé con Rollback (`Ctrl+Alt+R`).

Cambiar el destino no cambia un script ya armado: volvé a apretar **Comparar**.

### Límites

- La tabla tiene que tener clave primaria: sin ella no se puede saber qué fila es cuál.
- En el destino, la tabla tiene que existir y tener todas las columnas de la clave primaria.
- Con más de 100.000 filas en alguna de las dos bases no se compara: las filas se comparan en memoria.

## Importar CSV o Excel

Carga un archivo CSV o Excel (.xlsx) en una tabla que ya existe, o en una tabla nueva que se arma desde el archivo. Se
abre desde el árbol:

- Clic derecho en una tabla › **Importar datos (CSV o Excel)…**: a esa tabla.
- Clic derecho en la base o en **Tablas** › **Importar a una tabla nueva (CSV o Excel)…**.

### El archivo

Apretá **Elegir archivo…** y elegí un .csv, .txt o .xlsx.

- En un CSV: **Separador** (**Automático**, `;`, `,`, **Tabulador** o `|`) y **Codificación** (**Automático**,
  **UTF-8** o **Windows-1252 (Excel en castellano)**). En automático, el separador es el que más aparece en la primera
  línea, y la codificación es UTF-8 si el archivo lo es y Windows-1252 si no. Un valor entre comillas puede tener el
  separador y saltos de línea adentro.
- En un Excel: **Hoja**, para elegir cuál.
- **La primera fila tiene los nombres de las columnas**: destildala si el archivo no tiene encabezado; las columnas se
  llaman «Columna 1», «Columna 2»…

A la derecha se ve la **Vista previa (primeras 100 filas del archivo)**.

> **Ojo:** un Excel con muchísimo texto (más de 50 millones de caracteres) se rechaza con un aviso: partilo en archivos
> más chicos o guardalo como CSV.

### A una tabla existente

A la izquierda, **Qué columna del archivo va a cada columna de la tabla**: cada columna de la tabla (con su tipo, NOT
NULL y PK) tiene una lista con las columnas del archivo y **(no importar)**. Las que se llaman igual se relacionan
solas, sin distinguir mayúsculas, acentos, espacios ni guiones bajos: «Fecha alta» va a FECHA_ALTA. Las columnas
calculadas no aparecen, porque no se escriben.

Cómo se convierte lo leído:

- En una columna de fecha, una fecha como se escribe en la PC («22/10/2026») o un número de Excel se convierten.
- En una columna numérica, la coma decimal de la PC pasa a punto.
- En una columna BOOLEAN se aceptan sí/no, true/false y 1/0.
- Lo demás va como texto y Firebird lo convierte, o avisa en qué fila no pudo.

Las opciones:

- **Celdas vacías como NULL**: tildada de entrada.
- **Si una fila falla, detener y deshacer todo**, o **Si una fila falla, saltearla y seguir**.
- **Vaciar la tabla antes de importar**: borra todas las filas antes, en la misma transacción. Para confirmar hay que
  escribir el nombre de la tabla. Si la importación falla o se cancela, las filas vuelven.

Apretá **Importar N filas**. Todo va en una sola transacción, sin confirmaciones parciales:

- Con «detener», la primera fila que falla deshace todo y no se guarda nada.
- Con «saltearla», el resto se guarda al final y las filas con error se listan abajo, con el renglón del archivo
  («Renglón 15: …»).
- **Cancelar** deshace todo.

### Actualizar por la clave

Elegí **Actualizar las que ya están (por la clave) y agregar las demás** en lugar de **Agregar las filas**. Cada fila se
escribe con UPDATE OR INSERT … MATCHING: la que ya tiene esa clave se actualiza con lo del archivo y la que no, se
agrega.

La clave son las columnas tildadas a la izquierda de cada columna de la tabla; de entrada, la clave primaria. Tiene que
haber al menos una columna de la clave relacionada con el archivo. Este modo no se combina con **Vaciar la tabla antes
de importar**.

### A una tabla nueva

FbStudio lee el archivo entero y arma una tabla con una columna por cada una del archivo.

- **Tabla nueva:** el nombre sale del archivo, en mayúsculas, sin acentos, con «_» en lugar de espacios y signos, y de
  hasta 31 caracteres: «Precios 2026.xlsx» da PRECIOS_2026.
- **Sus columnas: el nombre y el tipo de cada una (destildá las que no van)**: el nombre sale del encabezado de la
  misma manera, sin repetir, y el tipo sale de lo que trae la columna.

| Lo que trae la columna | Tipo propuesto |
|---|---|
| Celdas de sí o no de un Excel | BOOLEAN |
| Solo números enteros | INTEGER, o BIGINT si no entran |
| Números con decimales | NUMERIC(18,n) con al menos 2 decimales, o DOUBLE PRECISION si no entran |
| Fechas (2026-10-22, o como se escriben en la PC) | DATE, o TIMESTAMP si alguna trae hora |
| Texto | VARCHAR con el largo redondeado hacia arriba (10, 20, 50, 100, 255, o de a 500 hasta 8.000), o BLOB SUB_TYPE TEXT si es más largo |
| Nada (columna vacía) | VARCHAR(100) |

Un número con ceros adelante («00123») queda como texto, así no pierde los ceros.

Antes de crearla podés cambiar el nombre de la tabla y el nombre y el tipo de cada columna (se escriben a mano), o
destildar las que no van. Debajo de cada una se ve de qué columna del archivo sale. FbStudio avisa si falta el nombre de
la tabla, si una columna quedó sin nombre o sin tipo, si un nombre se repite o si ya hay un objeto con ese nombre.

Apretá **Crear la tabla e importar N filas**:

- La tabla se crea sin clave primaria (se puede agregar después) y queda confirmada antes de importar.
- Si la importación no se guarda (una fila falla con «detener», o cancelás), la tabla nueva se vuelve a borrar: no queda
  una tabla vacía a medias.
- Si sale bien, el árbol la muestra y la pestaña pasa a ser la de importar a esa tabla.

### Producción y solo lectura

- En una base de producción, antes de importar FbStudio pregunta, con la cantidad de filas, la tabla y la base: «Vas a
  insertar 1.250 filas en PACIENTES de «producción», que es de PRODUCCIÓN» (o «Vas a crear la tabla…»).
- **Vaciar la tabla antes de importar** borra todas las filas de la tabla: por eso pide escribir su nombre, también
  fuera de producción.
- Con un registro de solo lectura no se puede importar: **Importar datos (CSV o Excel)…** aparece deshabilitado,
  **Importar a una tabla nueva (CSV o Excel)…** no aparece y la pestaña lo avisa.

## Generar datos de prueba

Llena una tabla con filas inventadas para probar pantallas, consultas o índices. En el árbol, clic derecho en la tabla
› **Generar datos de prueba…**.

> **Ojo:** solo aparece en bases de **Desarrollo** y **Prueba**, con un registro que no sea de solo lectura. En
> producción no se generan datos.

Se abre la pestaña **Datos de prueba · TURNOS**. Arriba:

- **Filas:** cuántas agregar, de 1 a 100.000.
- **Vacías en las opcionales:** qué porcentaje de filas (0, 10, 25 o 50 %) va con NULL en las columnas que lo aceptan.
- **Semilla:** con la misma semilla salen los mismos datos, para repetir una prueba. Después de agregar, sube sola.

### Qué va en cada columna

En **Qué va en cada columna**, cada columna (con su tipo, NOT NULL, PK y FK) tiene una lista con lo que se pone en ella.
FbStudio propone lo que conviene por el tipo y el nombre de la columna, sin distinguir mayúsculas ni acentos, y se puede
cambiar.

| Lo que se pone | Qué genera | Se propone para |
|---|---|---|
| **Numerada (desde la mayor)** | Números seguidos desde el mayor que ya hay en la tabla, más uno. | La clave primaria entera. |
| **Un valor de la tabla referenciada** | Uno de los valores que ya existen en la tabla a la que apunta la FK. | Las columnas con FK de una sola columna. |
| **Nombre**, **Apellido**, **Nombre completo** | Nombres y apellidos inventados; el completo, «Apellido, Nombre». | Nombres con NOMBRE o APELLIDO; el completo, con NOMBRE_COMPLETO, APELLIDO_Y_NOMBRE, RAZON_SOCIAL, PACIENTE, CLIENTE, PERSONA o CONTACTO. |
| **Mail** | Un mail inventado con el dominio example.com. | Nombres con MAIL o EMAIL. |
| **Teléfono** | Un teléfono inventado. | Nombres con TELEFONO, CELULAR, MOVIL o TEL. |
| **Documento** | Un número de 8 cifras. | Nombres con DNI o DOCUMENTO; en una columna de texto, también CUIT o CUIL. |
| **Dirección** | Una calle con su número. | Nombres con DIRECCION, DOMICILIO o CALLE. |
| **Ciudad** | Una ciudad. | Nombres con CIUDAD, LOCALIDAD o PROVINCIA. |
| **Código** | Letras y números al azar, de hasta 8 caracteres. | Nombres con CODIGO o que empiezan con COD, y textos de hasta 2 caracteres. |
| **Texto** | Una frase corta. | Cualquier otro texto. |
| **Número entero**, **Número con decimales** | Un número al azar que entra en el tipo. | Los tipos enteros y con decimales. |
| **Fecha (último año)**, **Fecha y hora (último año)**, **Hora** | Fechas del último año; las horas, entre las 7 y las 21. | DATE, TIMESTAMP y TIME. |
| **Sí o no** | Verdadero o falso. | BOOLEAN. |
| **(no se llena)** | Nada: la columna queda en NULL o con su DEFAULT. | Las columnas calculadas y los tipos que no se generan, como un BLOB binario. |

Los nombres, las calles y los textos salen en castellano si la interfaz está en castellano, y en inglés si no. Un texto
nunca pasa del largo de la columna.

- **Numerada (desde la mayor)** busca el mayor valor que ya hay, así no choca con las filas que existen.
- **Un valor de la tabla referenciada** elige al azar entre hasta 1.000 valores distintos de la tabla referenciada. Si
  la columna es obligatoria y esa tabla está vacía, FbStudio avisa y no genera nada.

### La muestra y agregar

1. Apretá **Ver una muestra**: la grilla de la derecha muestra las primeras 20 filas. Nada se escribe todavía.
2. Apretá **Agregar N filas** y confirmá.

Todo va en una transacción. Una fila que no entra (por ejemplo, por un índice único) se saltea y se sigue; abajo se ven
las primeras con error. **Cancelar** deshace todo.
