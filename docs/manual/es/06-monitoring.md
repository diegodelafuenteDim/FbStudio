# Monitoreo y rendimiento

Cómo mirar lo que pasa en el servidor: las conexiones, transacciones y bloqueos en vivo (MON$), el trace de sentencias
con su análisis por consulta, las estadísticas de la base (como gstat) y los datos del servidor.

## Cómo se abren

Todas estas herramientas trabajan sobre una base registrada y abren su propia conexión: no hace falta conectarla antes
en el árbol, pero sí que el registro tenga la contraseña guardada. Cada herramienta abre una pestaña por base; si ya
está abierta, la elige.

| Herramienta | Menú principal | Clic derecho en la base, en el árbol |
|---|---|---|
| Monitoreo | **Herramientas › Monitoreo (conexiones y transacciones)** | **Herramientas › Monitoreo (conexiones y transacciones)** |
| Trace | **Herramientas › Trace (sentencias en vivo)…** | **Herramientas › Trace (sentencias en vivo)** |
| Estadísticas | **Herramientas › Estadísticas de la base** | **Herramientas › Estadísticas** |
| Datos del servidor | — | **Herramientas › Datos del servidor…** |

El menú principal usa la base elegida en el árbol o, si no hay ninguna, la de la pestaña actual. En estas pestañas,
`F9` hace lo más habitual: en el monitoreo refresca y en las estadísticas vuelve a leer el encabezado.

> **Ojo:** FbStudio está probado con Firebird 3.x. Si te conectás a otra versión, lo avisa al conectar: algunas
> lecturas de metadatos pueden fallar.

## Monitoreo

La pestaña **Monitoreo** muestra arriba los contadores de transacciones de la base y abajo una consulta sobre las
tablas MON$: conexiones, transacciones, sentencias o bloqueos. Abre siempre en **Transacciones**.

Cada refresco es una transacción nueva que se confirma enseguida. MON$ es una foto que se toma al empezar la
transacción: así se ve siempre lo último y el monitoreo nunca frena la OAT que está mirando.

### Los contadores

| Tarjeta | Qué dice |
|---|---|
| **Próxima transacción** | El número que va a tomar la próxima transacción que se abra. |
| **OAT (activa más vieja)** | La transacción activa más vieja (Oldest Active Transaction) y, debajo, quién la tiene: usuario, IP, equipo y programa. Un clic abre la consulta **Transacciones**. |
| **Brecha (próxima − OAT)** | Cuántas transacciones hubo desde la OAT. Si crece sin parar, alguien tiene una transacción abierta hace mucho. |
| **OAT abierta hace** | Hace cuánto empezó la OAT, calculado con la hora del servidor. Dice «no se ve» si tu usuario no ve esa transacción. |
| **OIT** | La transacción de interés más vieja: la más vieja que no terminó bien (activa, deshecha o en limbo). |
| **OST** | La foto más vieja (Oldest Snapshot): hasta dónde se puede recolectar basura. |
| **Esperando un bloqueo** | Cuántas sentencias esperan un registro que tiene bloqueado otra transacción, y cuánto lleva la que más espera. En ámbar si hay alguna; en rojo si alguna espera 30 s o más. Un clic abre la consulta **Bloqueos**. |
| **Ahora** | Cuántas conexiones, transacciones y sentencias ejecutando hay. |
| **Servidor** | El tipo de servidor: Classic, SuperClassic o SuperServer (ver [Datos del servidor](#datos-del-servidor)). Se averigua en el primer refresco; el tooltip dice qué significa y cómo se supo. |

### La OAT y sus avisos

Siempre hay una OAT mientras haya transacciones abiertas; lo que preocupa es que dure. Mientras siga abierta, Firebird
no puede limpiar las versiones viejas de los registros (la basura), y la brecha crece.

Cuando la OAT lleva abierta más de lo que dicen las opciones, las tarjetas **OAT (activa más vieja)**,
**Brecha (próxima − OAT)** y **OAT abierta hace** se pintan de ámbar y, pasado el segundo umbral, de rojo. Aparece
además un aviso con el número de la transacción, hace cuánto está abierta, cuántas transacciones hubo después y quién
la tiene. En la consulta **Conexiones** esa conexión lleva la nota «⚠ tiene la OAT», y en **Transacciones** la
transacción lleva «⚠ es la OAT».

Los umbrales se cambian en **Herramientas › Opciones…**, sección **Monitoreo** (ver
[Opciones, atajos y privacidad](09-options.md)):

- **OAT en ámbar si lleva abierta más de**: de fábrica, 2 minutos.
- **y en rojo si lleva más de**: de fábrica, 10 minutos.
- **No alarmar si la OAT es de un backup o un sweep (gbak, gfix, nbackup): se informa sin color**: tildada de fábrica.

Un backup con gbak o un sweep mantienen una transacción abierta mientras trabajan. Con la última opción tildada, si la
OAT es de gbak, gfix, nbackup o de un proceso interno del servidor, no se pinta nada: un renglón aparte dice que es
esperable mientras dure.

### Qué conexiones se ven

Sin el rol RDB$ADMIN, sin ser SYSDBA y sin ser el dueño de la base, MON$ muestra solo las conexiones de tu usuario. La
pestaña lo avisa: «Ves solo tus conexiones…». Para ver las de todos (y poder cortarlas), conectate con el rol
RDB$ADMIN, como SYSDBA o con el usuario dueño de la base.

Si la base quedó en modo backup de nbackup, el monitoreo también lo avisa en cada refresco (ver
[nbackup](07-maintenance.md#nbackup)).

### Refresco automático

- **Refrescar** (o `F9`) vuelve a leer ahora.
- La lista de al lado elige el refresco automático: **A mano**, **Cada 2 s**, **Cada 5 s** (el de fábrica),
  **Cada 10 s** o **Cada 30 s**. Solo refresca mientras la pestaña está a la vista: si pasás a otra, se detiene.
- Al lado se ve la hora del último refresco: «actualizado 10:42:07».
- Con la misma consulta, cada refresco conserva el orden, los filtros, los anchos y la fila elegida de la grilla.

> **Ojo:** en un servidor Classic cada refresco le pide los datos a cada proceso del servidor, uno por conexión. Por
> eso, en Classic el refresco automático arranca cada 10 s (si no elegiste otro), y si lo bajás de 10 s la pestaña
> avisa que con muchas conexiones carga el servidor.

### Cortar una conexión

1. Elegí en la grilla una fila con el número de conexión. Sirven las cuatro consultas predefinidas y las propias que
   traigan esa columna.
2. Apretá **Cortar conexión**.
3. Leé la confirmación y apretá **Cortar conexión** o **No cortar**.

FbStudio corta la conexión con `DELETE FROM MON$ATTACHMENTS`: si tenía una transacción abierta, se deshace, y el
programa recibe un error.

La confirmación muestra el usuario, la dirección, el equipo y el programa de esa conexión leídos del servidor en ese
momento, no de la grilla. Además avisa si es una conexión de FbStudio en esta PC (esa pestaña va a perder la conexión)
y si la base es de producción. Si la conexión ya no estaba, lo dice: «ya no existía (o no se ve sin RDB$ADMIN)».

> **Ojo:** el monitoreo no puede cortar su propia conexión. Con un registro de solo lectura, **Cortar conexión** y
> **Cancelar sentencia** quedan deshabilitados.

### Cancelar una sentencia

Para frenar una consulta pesada sin cortar la conexión, elegí su fila en **Sentencias en curso** (o una fila «espera un
registro» en **Bloqueos**) y apretá **Cancelar sentencia**. La confirmación muestra el usuario y el comienzo del SQL, y
avisa si la base es de producción; se contesta con **Cancelar sentencia** o **No cancelar**.

FbStudio usa `DELETE FROM MON$STATEMENTS`: la conexión sigue y a quien ejecutaba la sentencia le llega un error de
operación cancelada.

## Consultas del monitoreo

A la izquierda de la pestaña está la lista de consultas, en dos grupos: **Predefinidas** (las que trae FbStudio) y
**Propias (N)**. La elegida se ejecuta en cada refresco y se ve en la grilla; al pasar el mouse por una consulta se ve
su SQL. La grilla se ordena y se filtra como cualquier otra (ver [Resultados](03-results.md)).

Cuando la fila elegida trae SQL (en **Sentencias en curso** y **Bloqueos**), el texto completo se ve debajo de la
grilla, para leerlo o copiarlo.

### Consultas predefinidas

| Consulta | Qué muestra |
|---|---|
| **Conexiones** | Una fila por conexión, sin las del sistema: número, **Nota** («tiene la OAT», «este monitoreo»), PID del servidor, estado (activa o inactiva), dirección, equipo, PID remoto, desde cuándo, programa y usuario. |
| **Transacciones** | Una fila por transacción, la más vieja arriba: conexión, **Nota** («es la OAT», «este monitoreo»), número de transacción, desde cuándo y cuántos segundos lleva, dirección, equipo, programa, estado, y la Top, la OIT y la OAT que vio al empezar. |
| **Sentencias en curso** | Las que se están ejecutando o esperan el fetch, sin las del monitoreo: número, conexión, transacción, usuario, IP, equipo, segundos, lecturas sin índice y por índice, y el SQL. |
| **Bloqueos** | Quién espera un registro bloqueado («espera un registro») y quién lo podría tener («posible responsable»), lo que más lleva arriba. |

Firebird 3 no dice qué transacción tiene bloqueado un registro. Por eso, en **Bloqueos**, FbStudio propone como
posibles responsables las transacciones de escritura con cambios que empezaron antes de la espera.

### Consultas propias

Debajo de la lista hay cinco botones; el clic derecho en la lista ofrece lo mismo.

| Botón | Qué hace |
|---|---|
| **+** | Consulta propia nueva: arranca con un SELECT de ejemplo sobre MON$ATTACHMENTS. |
| Copiar | Clona la consulta elegida como propia. Sirve con las predefinidas, para partir de una. |
| Lápiz | Edita la consulta propia elegida (también con doble clic). |
| Tacho | Borra la consulta propia elegida, después de preguntar. |
| Compartir | Exporta o importa un archivo de consultas (ver [Compartir consultas](#compartir-consultas)). |

La ventana de la consulta tiene:

- **Nombre**.
- El SQL: tiene que ser un solo SELECT, porque el monitoreo lo corre cada pocos segundos en una transacción de solo
  lectura.
- **Probar**: la ejecuta con la conexión del monitoreo y dice cuántas filas trae y con qué columnas.
- **N.º de conexión**, **N.º de sentencia** y **Texto SQL**: qué columna trae cada cosa. Habilitan **Cortar conexión**,
  **Cancelar sentencia** y el SQL de abajo. Vacías, «(se detecta sola)»: se reconocen por el nombre (MON$ATTACHMENT_ID,
  ATTACHMENT_ID, "Attachment ID", MON$STATEMENT_ID, MON$SQL_TEXT…), sin importar espacios, guiones bajos, `$` ni
  acentos. Después de **Probar**, las listas ofrecen las columnas de la consulta.
- **Guardar** y **Cancelar**.

Por ejemplo, las conexiones de un programa:

```sql
SELECT a.MON$ATTACHMENT_ID, a.MON$USER, a.MON$REMOTE_ADDRESS,
       a.MON$REMOTE_PROCESS, a.MON$TIMESTAMP
FROM MON$ATTACHMENTS a
WHERE a.MON$SYSTEM_FLAG = 0
  AND a.MON$REMOTE_PROCESS CONTAINING 'facturacion'
ORDER BY a.MON$TIMESTAMP
```

Como trae MON$ATTACHMENT_ID, **Cortar conexión** funciona sin elegir nada.

Las consultas propias valen para todas las bases y se guardan con la configuración de FbStudio en tu equipo.

### Compartir consultas

El botón de compartir (o el clic derecho en la lista) tiene:

- **Exportar las consultas propias…**: todas, a un archivo JSON (propone `fbstudio-monitoreo.json`).
- **Importar consultas…**: suma las del archivo a las tuyas. Si una ya estaba (la misma consulta, exportada antes), se
  reemplaza. Las que no son un solo SELECT se descartan.

> **Ojo:** una consulta importada puede mostrar cualquier cosa al lado del número de conexión. Por eso la confirmación
> de **Cortar conexión** muestra siempre los datos que da el servidor, no los de la grilla.

## Trace

El monitoreo muestra lo que pasa ahora; el trace muestra qué carga el servidor a lo largo del tiempo. Es una sesión de
traza de Firebird (por la API de servicios) que anota las sentencias de cualquier programa que tardan más que un
umbral, con su plan y sus números, y las junta en un análisis por consulta.

### Iniciar y detener

1. Elegí el umbral, el corte automático y qué trazar (ver [Qué se traza](#qué-se-traza)).
2. Apretá **Iniciar**.
3. Cuando tengas suficiente, apretá **Detener**.

Mientras está activo, la línea de estado dice desde cuándo, cuántos eventos llegaron y a qué hora se detiene: «Trace
activo en ventas desde las 10:15:00 · 1.234 eventos · se detiene a las 10:25». Las opciones no se pueden cambiar con
el trace activo. **Limpiar** borra lo que llegó.

El trace se detiene solo:

- a los minutos elegidos: **Se detiene a los 5 min**, **10 min** (el de fábrica), **30 min** o **60 min**, o
  **Sin límite de tiempo**;
- al cerrar la pestaña;
- si FbStudio se cierra o se corta la red: el servidor termina la sesión, que vive con la conexión de servicio.

Si el servidor termina el trace por su cuenta, la pestaña lo dice con el motivo que dio.

> **Ojo:** en una base de producción, **Iniciar** pregunta antes, con el umbral y cuándo se detiene. Con un umbral
> bajo, el servidor anota muchas sentencias y eso lo carga.

### Qué se traza

Siempre se trazan las sentencias que terminaron, con su plan y su rendimiento. Además:

| Opción | Qué hace |
|---|---|
| Umbral | **Todas las sentencias**, o solo las que tardan al menos **Desde 10 ms**, 50, 100 (el de fábrica), 250, 500, 1000 o 5000 ms. En producción conviene no bajar de 100 ms. |
| **Procedimientos y funciones** | También las ejecuciones de procedimientos y funciones. Tildada de fábrica. |
| **Triggers** | También los triggers. En una base con muchos, multiplica los eventos. |
| **Errores** | También los errores. Tildada de fábrica. |
| **Solo el SQL que contenga…** | Solo las sentencias cuyo texto contiene lo escrito, sin distinguir mayúsculas (por ejemplo, `TURNOS`). |

El SQL de cada evento se corta a los 4.000 caracteres. Se guardan hasta 50.000 eventos; pasado eso, se descartan los
más viejos.

### Análisis por consulta

La pestaña **Análisis por consulta** junta las ejecuciones de la misma sentencia y las ordena por el tiempo total:
arriba queda lo que más carga el servidor. Una consulta de 50 ms que corre 10.000 veces pesa más que una de 5 s que
corre una vez.

| Columna | Qué es |
|---|---|
| **Veces** | Cuántas veces se ejecutó. |
| **ms en total** | El tiempo de todas las ejecuciones sumado: el orden de la lista. |
| **ms promedio** y **ms máximo** | El tiempo por ejecución y el de la más lenta. |
| **Consulta** | El SQL, en un renglón. |
| **Lecturas** | Páginas leídas del disco. |
| **Fetches** | Páginas pedidas a la caché. |
| **Escrituras** | Páginas escritas. |
| **Filas** | Las filas que trajo. |
| **Fallas** | Cuántas ejecuciones fallaron. |
| **Aviso** | «recorre TABLA» si alguna ejecución leyó esa tabla entera (lecturas sin índice), o «falló». |

**Juntar las que solo cambian en los valores** (tildada de fábrica) cambia los números y los textos por `?`: así
`WHERE ID = 1` y `WHERE ID = 2` son la misma consulta aunque el programa no use parámetros, y las listas de `IN (…)`
también se juntan.

Al elegir una fila, abajo se ve el resumen («Se ejecutó 120 veces · 9.840 ms en total · 82,0 ms promedio · 410 ms la
más lenta (esta):») y la ejecución más lenta completa: quién la ejecutó, sus números, el SQL, el plan, los parámetros
y lo que hizo en cada tabla (lecturas sin índice y por índice, filas actualizadas, insertadas y borradas).

### Eventos y detalle

La pestaña **Eventos** lista cada evento, lo último arriba: hora, ms, SQL, lecturas, fetches, escrituras, filas,
usuario, IP, programa y aviso. Al elegir uno, abajo se ve entero, con el mensaje si fue un error.

Las rutas del servidor no se muestran: la base aparece con el nombre que tiene en el registro.

### Guardar y abrir un log

- **Guardar log…** guarda tal cual lo que llegó en esta sesión (con el nombre del registro en lugar de la ruta de la
  base), en un `.log` (propone `fbstudio-trace-AAAAMMDD-HHMM.log`). El log se arma mientras el trace está activo: un
  log abierto no se vuelve a guardar.
- **Abrir log…** (con el trace detenido) lee un log guardado, de FbStudio o de fbtracemgr, y lo analiza igual que lo
  que llega en vivo. Reemplaza lo que se estaba viendo.

> **Ojo:** el log tiene SQL con datos. Hasta que lo guardes, lo que llega queda solo en la memoria; si lo guardás,
> hacelo en un lugar seguro.

### Exportar a Excel

**Exportar análisis a Excel…** escribe un `.xlsx` con las consultas tal como se ven en la grilla del análisis (con su
orden y sus filtros) y todas sus columnas.

### Comparar con un log

**Comparar con un log…** pone un log guardado antes (otro período) contra lo de ahora, sea un trace en vivo o un log
abierto, consulta por consulta. Aparece la pestaña **Comparación**:

| Columna | Qué es |
|---|---|
| **Cambio** | «más lenta», «más rápida», «nueva», «ya no aparece» o «igual». |
| **Consulta** | El SQL. |
| **Veces antes** y **Veces ahora** | Cuántas veces se ejecutó en cada período. |
| **Prom. ms antes**, **Prom. ms ahora** y **% prom.** | El tiempo por ejecución en cada período y cuánto cambió. |
| **Total ms antes**, **Total ms ahora** y **Diferencia ms** | El tiempo total en cada período y la diferencia. |
| **Lecturas/vez antes** y **Lecturas/vez ahora** | Las lecturas por ejecución. |

- Se compara el promedio por ejecución, que no depende de cuánto duró cada trace. Una consulta es más lenta o más
  rápida si su promedio cambió al menos un 20 % y al menos 5 ms.
- El orden es por cuánto cambió el tiempo total: lo que más cambió la carga del servidor, arriba.
- La línea de arriba resume: cuántas consultas hay en cada período y cuántas están más lentas, más rápidas, son nuevas
  o ya no aparecen.
- Al elegir una fila, abajo se ven los números de los dos períodos y la ejecución más lenta de ahora (o de antes, si
  ya no aparece).
- La opción **Juntar las que solo cambian en los valores** vale también para la comparación.
- **Quitar la comparación** la cierra.

Para que la comparación sea justa, usá el mismo umbral y las mismas opciones en los dos períodos: con otro umbral
entran otras sentencias.

### Permisos del trace

Como SYSDBA, el trace ve las sentencias de todos. Con otro usuario puede que vea solo las de tus propias conexiones, y
la pestaña lo avisa. El rol RDB$ADMIN no sirve acá: Firebird 3 no acepta el rol al iniciar un trace, así que FbStudio
lo inicia sin rol.

## Estadísticas de la base

La pestaña **Estadísticas** da lo mismo que gstat, sin instalarlo: va por la API de servicios, no abre transacciones
(no afecta la OAT) y no hace falta conectar la base en el árbol.

### El encabezado

Al abrir la pestaña se lee el encabezado de la base (como `gstat -h`): una sola página, instantáneo aunque la base sea
enorme. **Leer encabezado** (o `F9`) lo vuelve a leer, y también se relee al volver a abrir la pestaña desde el menú.

| Tarjeta | Qué dice |
|---|---|
| **OIT** | La transacción de interés más vieja: por debajo de ella todo está confirmado o limpio. |
| **OAT** | La transacción activa más vieja. |
| **OST** | La foto más vieja que alguien todavía puede ver: hasta ahí puede limpiar la recolección de basura. |
| **Próxima transacción** | El número de la próxima transacción. |
| **Próxima − OAT** | Transacciones que empezaron después de la activa más vieja. Si crece sin parar, algo tiene una transacción abierta hace mucho (ver [Monitoreo](#monitoreo)). |
| **Sweep automático** | El intervalo del sweep («apagado» si es 0; «20.000 (de fábrica)» si nunca se cambió) y, debajo, OST − OIT. |
| **Página · ODS** | El tamaño de página y la versión de ODS. |
| **Escrituras forzadas** | «Sí» o «No (riesgo)». |

El sweep automático corre cuando OST − OIT pasa el intervalo. Si ya lo pasó, la tarjeta se pinta de ámbar y un aviso
dice que el sweep automático ya debería haber corrido y que, si la brecha sigue creciendo, una transacción vieja lo
está frenando. El intervalo se cambia en [Propiedades de la base](07-maintenance.md#propiedades-de-la-base).

**Escrituras forzadas** en «No (riesgo)» también se pinta de ámbar: sin ellas, un corte de luz puede dañar la base. Si
la base quedó en modo backup de nbackup (gstat lo dice como «backup lock»), se avisa arriba.

A la izquierda está la lista con todos los datos del encabezado, con el nombre traducido (el tooltip muestra el nombre
que usa gstat). A la derecha, la pestaña **Texto de gstat** tiene la salida tal cual, con el nombre del registro en
lugar de la ruta de la base; **Copiar texto** la copia para pegarla en un mail o un ticket.

### La estadística completa

**Estadística completa…** lee todas las páginas de datos e índices de cada tabla, con las versiones de los registros
(como `gstat -a -r`). Antes pregunta: en una base grande puede tardar minutos y carga el disco del servidor, aunque no
bloquea a nadie. En una base de producción agrega que conviene hacerla fuera del horario de atención. Mientras lee, la
línea de estado cuenta los renglones que van llegando, y **Cancelar** la corta.

El resultado queda en dos grillas, que se ordenan y se filtran como cualquier otra:

- **Tablas (N)**: registros, largo promedio del registro, versiones, máximo de versiones de un registro, largo de las
  versiones, fragmentos, páginas de datos, llenado %, páginas vacías y llenas, compresión, formatos y **Aviso**.
- **Índices (N)**: tabla, índice, profundidad, páginas hoja, nodos, duplicados, % repetidos, máximo de repetidos, largo
  de la clave, clustering y **Aviso**.

Las **Versiones** son las versiones viejas de los registros que la recolección de basura todavía no limpió. La línea
de estado dice cuántas tablas e índices hay y cuántos tienen aviso.

### La columna Aviso

| Grilla | Aviso | Cuándo aparece |
|---|---|---|
| Tablas | «muchas versiones viejas: la basura no se está limpiando (¿una transacción abierta hace mucho?)» | Las versiones son más de la mitad de los registros, o algún registro tiene 10 versiones o más. |
| Índices | «profundidad N: conviene recrearlo» | El índice tiene 4 niveles o más: cada búsqueda lee más páginas. Se arregla recreándolo (o con páginas más grandes). |
| Índices | «poco selectivo (N % repetidos)» | Tiene 1.000 nodos o más y el 95 % o más de los valores repetidos: el optimizador casi no lo aprovecha. |

Con muchas versiones viejas, mirá primero la OAT en el [monitoreo](#la-oat-y-sus-avisos); después, un
[sweep](07-maintenance.md#sweep) las limpia.

## Estadísticas de tablas elegidas

En una base grande, leer solo algunas tablas es mucho más rápido (como `gstat -t`).

1. Apretá **Tablas: todas**. La primera vez se leen las tablas de la base.
2. Tildá las que quieras. **Filtrar tablas** busca por nombre, **Todas** quita la elección y, si la base tiene áreas,
   **Tildar las tablas de un área…** suma las tablas de un área (ver [El árbol, las áreas y los diagramas](05-tree.md)).
3. El botón pasa a decir «Tablas: 3 elegidas» y la estadística completa pasa a **Estadística de 3 tablas…**: apretalo.
4. La confirmación dice qué tablas se van a leer; seguí con **Leer**.

Se leen las páginas de datos e índices de esas tablas y de sus índices, y el resultado queda en las mismas grillas.
Sin ninguna tildada, se leen todas.

## Comparar estadísticas

Para poner dos bases lado a lado (por ejemplo, la de prueba contra la de producción):

1. En **Comparar con**, elegí otra base registrada (de fábrica dice «(no comparar)»).
2. Apretá **Estadística completa…** (o la de las tablas elegidas). La confirmación dice que después se lee lo mismo en
   la otra base, y avisa si es de producción.
3. Al terminar con esta base, se lee la otra (se puede cancelar).

Aparecen dos pestañas más. En las dos, A es esta base y B la otra, y la diferencia es B − A.

- **Comparar tablas (N)**: registros de A y de B y su diferencia, páginas de A y de B y su diferencia, versiones,
  llenado % y una **Nota** con «Solo en …» si la tabla está en una sola.
- **Comparar índices (N)**: profundidad, nodos y % de duplicados de A y de B, y una **Nota**: «Más profundo en …» si en
  B tiene más niveles, o «Solo en …».

El texto de gstat de la otra base queda debajo del de esta, en **Texto de gstat**, con su nombre como título. Si la
otra base no se puede leer (no tiene la contraseña guardada, le falta una de las tablas…), queda lo de esta base y la
línea de estado dice por qué no se comparó.

## Datos del servidor

Clic derecho en la base › **Herramientas › Datos del servidor…** abre una ventana que se llena cuando contesta el
servidor:

| Dato | Qué muestra |
|---|---|
| **Servidor** | El servidor y el puerto del registro, por ejemplo `servidor:3050`. |
| **Versión** | «Firebird 3.0.x» y la versión completa que informa el servidor. |
| **Tipo** | Classic, SuperClassic o SuperServer. |
| **Qué significa** | Cómo atiende ese tipo las conexiones. |
| **Cómo se supo** | De dónde salió el tipo. |
| **Sistema** | El sistema y la arquitectura del servidor: «Windows x64», «Linux x64». |
| **Base** | ODS, dialecto, tamaño de página y charset. |
| **Caché** | Las páginas de caché y cuánto ocupan: compartida por todas las conexiones (SuperServer) o una por conexión (Classic y SuperClassic). |

**Copiar** copia todo como texto, para pegarlo en un reporte. La ventana no muestra rutas del servidor (ni el archivo
de la base ni la carpeta de Firebird), aunque te conectes como SYSDBA: para eso están los alias.

El tipo y la versión también se ven en el árbol al conectar («conectada · Firebird 3.0.11 · Classic») y en la tarjeta
**Servidor** del monitoreo.

### Cómo se sabe el tipo

| Tipo | Qué significa |
|---|---|
| Classic | Un proceso del servidor por cada conexión, cada uno con su propia caché. Si un proceso se cae, las demás conexiones siguen. El monitoreo es más caro: cada consulta a MON$ le pide los datos a todos los procesos. |
| SuperClassic | Un solo proceso del servidor, con una caché propia para cada conexión. |
| SuperServer | Un solo proceso del servidor, con una caché compartida por todas las conexiones. |

FbStudio lo deduce de MON$ATTACHMENTS:

- Si las conexiones remotas corren en procesos distintos, es Classic. Esto lo ve cualquier usuario.
- Si comparten un proceso y está la conexión interna «Cache Writer», que solo tiene SuperServer, es SuperServer; si no
  está, es SuperClassic.
- Las conexiones internas solo las ven SYSDBA, quien use el rol RDB$ADMIN y el dueño de la base. Sin eso, el tipo
  queda como «SuperServer/SuperClassic».
- Si se ven menos de dos conexiones remotas, FbStudio abre una segunda conexión, corta, para comparar. Si aun así no
  puede, dice «tipo sin detectar».
