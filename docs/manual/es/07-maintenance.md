# Mantenimiento y permisos

Las tareas de mantenimiento de una base (backup, restore, nbackup, validación, sweep, base vacía y propiedades) y la
administración de usuarios, roles y permisos, con los cuidados para producción y para los registros de solo lectura.

## La pestaña Mantenimiento

Se abre desde **Herramientas › Mantenimiento (backup, restore…)** o con clic derecho en la base › **Mantenimiento**,
que lleva directo a una tarea: **Backup**, **Restore**, **nbackup**, **Validación**, **Sweep**, **Base vacía** o
**Propiedades**. Hay una pestaña por base; abre su propia conexión, así que no hace falta conectar la base antes, pero
el registro tiene que tener la contraseña guardada.

- A la izquierda, la lista de tareas.
- Arriba, la base como la nombra el registro, con su página y su charset: «Base ventas · página de 8.192 · WIN1252».
  La ruta real en el servidor no se muestra: si el registro usa un alias, se ve el alias, también en lo que dicen gbak,
  la validación y los errores.
- En el medio, las opciones de la tarea y sus botones: el de ejecutar (cambia según la tarea: **Hacer backup**,
  **Restaurar**…), **Detener**, **Comprobar estado**, **Registrar la base nueva** y **Copiar registro**.
- Abajo, el registro: lo que se hizo, con la hora, las opciones usadas y lo que dice el servidor (la salida de gbak, de
  la validación o el error).

Mientras corre una tarea, la lista y las opciones quedan deshabilitadas.

> **Ojo:** las rutas son del servidor, como las ve Firebird, no de esta PC. Escribí la ruta completa
> (`D:\Backups\ventas.fbk` o `/backups/ventas.fbk`) o, para una base nueva, un alias. FbStudio no propone carpetas,
> porque la carpeta de la base en el servidor no se muestra. Los archivos los escribe el propio servidor de Firebird,
> así que la carpeta tiene que existir allá y Firebird tiene que poder escribir en ella.

## Backup

La tarea **Backup** hace un backup con gbak, ejecutado por el servidor (por la API de servicios).

1. En **Archivo de backup (ruta en el servidor)**, escribí el archivo con la ruta completa.
2. Elegí las opciones que hagan falta.
3. Apretá **Hacer backup**.

Lo hace el servidor: si cerrás FbStudio o se corta la red, el backup sigue hasta el final. Si el archivo ya existe, se
reemplaza. FbStudio espera a que el servidor termine de verdad y lo anota: «Backup terminado: … (N s)». El archivo
queda además como origen de la tarea **Restore**.

> **Ojo:** el destino tiene que ser un archivo de backup (`.fbk` o `.gbk`). FbStudio rechaza un nombre con extensión
> de base (`.fdb`, `.gdb`, `.ib`…) o la misma base del registro, porque gbak la pisaría.

### Opciones del backup

| Opción | Para qué sirve |
|---|---|
| **Solo la estructura (sin datos)** | `gbak -m`. Al restaurarlo, los generadores vuelven a 0 y las identity a 1. Para una base sin datos que siga numerando igual, usá [Base vacía](#base-vacía). |
| **Sin recolectar basura (-g): más rápido** | No limpia las versiones viejas mientras lee. La base queda igual. |
| **Ignorar errores de checksum (para rescatar una base dañada)** | Sigue aunque haya páginas dañadas; el backup puede quedar incompleto. Es para sacar lo que se pueda de una base que el backup normal no termina. |
| **Formato no transportable (más rápido; se restaura solo en el mismo tipo de sistema)** | El formato propio de la plataforma del servidor. |
| **Sin los datos de estas tablas (la estructura sí):** | Las tablas van vacías en el backup, por ejemplo `AUDITORIA, LOG_TURNOS`. |

Las listas de tablas se escriben como en SQL: separadas por coma, punto y coma o espacios; sin comillas se toman en
mayúsculas y entre comillas dobles, tal cual. Cada nombre vale exacto: `TURNOS` no incluye `TURNOS_HIST`.

## Restore

La tarea **Restore** restaura un backup en el servidor, siempre en una base nueva.

1. En **Backup a restaurar (ruta en el servidor)**, el archivo `.fbk`.
2. En **Base nueva (ruta en el servidor)**, la ruta completa (`/datos/ventas_prueba.fdb`) o un alias.
3. Elegí las opciones y apretá **Restaurar**.

Lo hace el servidor aunque cierres FbStudio; en una base grande puede tardar horas. FbStudio mira el encabezado de la
base nueva hasta que queda lista y cada minuto anota «Sigue restaurando…». Al final dice «Restore terminado: … quedó
lista para usar».

La base del registro no se pisa nunca: FbStudio compara la ruta, el alias y la ruta real del destino (se conecta a él
para saberla). Si en el destino ya hay una base y no tildaste **Reemplazar si existe**, lo dice antes de empezar.
Para crear la base nueva, el usuario del registro tiene que poder crear bases en el servidor.

### Opciones del restore

| Opción | Para qué sirve |
|---|---|
| **Reemplazar si existe (pide escribir el nombre del archivo)** | Pisa una base que ya esté en el destino (ver [Reemplazar una base](#reemplazar-una-base)). |
| **Solo la estructura** | Restaura sin los datos. |
| **Sin activar los índices (-i): más rápido; activalos después** | Los índices quedan inactivos. |
| **Confirmar tabla por tabla (si los datos de una fallan, las demás quedan)** | Cada tabla se confirma por separado. |
| **Sin los archivos espejo (shadow)** | No recrea los shadow que tuviera la base. |
| **Tamaño de página** | **La misma** que tenía la base, o 4.096, 8.192, 16.384 o 32.768. |
| **Sin las reglas NOT NULL de las columnas (para rescatar datos viejos)** | `gbak -n`, sin chequeo de validez: restaura aunque haya filas con NULL en una columna NOT NULL, que hacen fallar el restore normal. |
| **Sin los datos de estas tablas (la estructura sí):** | Esas tablas se restauran vacías. Los nombres se escriben como en el backup. |

> **Ojo:** con **Sin las reglas NOT NULL de las columnas**, la base queda SIN esos NOT NULL (los CHECK sí quedan).
> Después hay que corregir los datos y volver a ponerlos.

### Reemplazar una base

Con **Reemplazar si existe**, antes de empezar FbStudio pide escribir el nombre del archivo de destino, sin la carpeta
ni la extensión: para `/datos/ventas_prueba.fdb`, escribís `ventas_prueba`. Todo lo que tenga ese archivo se pierde.
La base del registro no se reemplaza nunca, aunque la escribas con otro alias.

### Registrar la base nueva

Cuando termina un restore o una base vacía, aparece **Registrar la base nueva**: la registra igual que la de esta
pestaña (mismo servidor, usuario, contraseña y carpeta), con el nombre «registro · archivo», por ejemplo «Ventas ·
ventas_prueba». Una copia de una base de producción queda registrada como Prueba. Después se edita desde el árbol,
como cualquier otra (ver [Primeros pasos](01-start.md)).

## Base vacía

La tarea **Base vacía** crea una base nueva con toda la estructura de esta, armada desde el script de la base: mismo
charset, tamaño de página y dialecto, con tablas, vistas, procedimientos, triggers y permisos. Sirve para tener una
base en cero que siga numerando igual, algo que un backup de solo estructura no da.

1. En **Base nueva (ruta en el servidor)**, la ruta completa o un alias. Nunca se pisa un archivo que ya exista.
2. Dejá tildado **Generadores e identity con su valor actual** para que la base nueva siga numerando como esta. Sin
   eso empiezan de cero, y si copiás tablas con datos sus claves pueden chocar.
3. En **Tablas a copiar con sus datos**, tildá las que quieras (por ejemplo, las tablas de referencia).
   **Filtrar tablas** busca por nombre y **Ninguna** quita la elección. Sin ninguna, va solo la estructura.
4. Apretá **Crear la base vacía**.

Cómo la arma:

1. Crea la base y corre la parte de estructura del script: tablas, claves, índices, vistas, procedimientos…
2. Copia los datos de las tablas elegidas y de las que referencian sus FK (se suman solas y el registro dice cuáles),
   leídos en una sola foto de la base de origen.
3. Corre la parte diferida: FK, CHECK, triggers y permisos. Así los datos entran antes de las FK y los triggers.

Cada sentencia se confirma sola: la que falla se anota en el registro y se sigue con la próxima. El mensaje final dice
cuántas fallaron y qué tablas no se copiaron.

> **Ojo:** la base vacía la arma FbStudio, no el servidor: si cerrás FbStudio, se corta y la base queda a medio armar
> (borrala o armala de nuevo con otro nombre). La copia de datos va fila por fila: sirve para tablas chicas o de
> referencia; para llevar la base entera, usá backup y restore.

## Validación

La tarea **Validación** hace una validación en línea (Firebird 3 en adelante): con la base en uso, sin conexión
exclusiva. Revisa las páginas y los índices de cada tabla, bloqueando cada una unos instantes mientras la revisa. No
corrige nada: solo informa.

1. Para validar solo algunas tablas, escribilas en **Solo estas tablas, con sus índices (vacío: toda la base):**, por
   ejemplo `TURNOS, PACIENTES`.
2. Apretá **Validar**.

El resultado queda en la línea de estado y en el registro: «Validación terminada sin errores», «La validación encontró
N error(es) y M aviso(s): mirá el registro» o «La validación no llegó al final». La validación clásica, con la base en
exclusiva (`gfix -v`), no se ofrece.

En una base de producción pregunta antes: cada tabla queda bloqueada un rato mientras se revisa, y conviene hacerla
fuera de hora.

## Sweep

El sweep recorre toda la base y limpia las versiones viejas de los registros (lo mismo que `gfix -sweep`). En una base
grande carga el servidor un buen rato.

Elegí la tarea **Sweep** y apretá **Hacer sweep**. Siempre pregunta; en una base de producción hay que escribir el
nombre de la base (el del registro). Al terminar dice cuánto tardó; en [Estadísticas](06-monitoring.md#el-encabezado)
se ve cómo quedaron la OIT y la OAT.

Si hay una transacción vieja abierta, el sweep no puede limpiar lo que esa transacción todavía puede ver: antes,
mirá la OAT en el [monitoreo](06-monitoring.md#la-oat-y-sus-avisos). El sweep automático se configura en
[Propiedades de la base](#propiedades-de-la-base).

## nbackup

nbackup hace backups físicos por niveles: el nivel 0 es una copia completa y cada backup de nivel N toma como base el
último de nivel N − 1, así que los incrementales dependen del último nivel 0. Mientras dura, la base está en modo
backup: lo que se escribe va al archivo delta y al terminar se fusiona con la base.

Por ahora FbStudio solo mira: el backup con nbackup se sigue haciendo en el servidor. La tarea **nbackup** muestra:

- si la base está en modo backup (con un aviso) o no («La base no está en modo backup»);
- dónde va el archivo delta: junto a la base (lo de fábrica) o a otro archivo definido en la base con
  `ALTER DATABASE ADD DIFFERENCE FILE`;
- el historial de backups registrados en la base (RDB$BACKUP_HISTORY), lo último primero: **Fecha**, **Nivel** («Nivel
  0 (completo)», «Nivel 1 (incremental)») y **Archivo** (solo el nombre, sin la carpeta del servidor).

**Comprobar estado** lo vuelve a leer. Lo puede ver cualquier usuario: es una lectura, sin cambiar nada.

> **Ojo:** si la base está en modo backup y no hay ningún nbackup corriendo, quedó trabada: el delta crece sin parar
> hasta terminarlo (`nbackup -N` o `ALTER DATABASE END BACKUP`), y conviene mirar el disco del servidor.

El modo backup se avisa también al conectar (la base lleva «⚠ modo backup» en el árbol y el detalle en la línea de
estado), en el [monitoreo](06-monitoring.md#qué-conexiones-se-ven) y en las [estadísticas](06-monitoring.md#el-encabezado).

## Propiedades de la base

La tarea **Propiedades** lee las propiedades de la base al elegirla:

| Propiedad | Qué es |
|---|---|
| **Sweep automático (0 = apagado)** | El servidor hace un sweep cuando OST − OIT pasa este número. 20.000 es el de fábrica. |
| **Páginas de caché (0 = las del servidor)** | La caché propia de la base; con 0 usa la de la configuración del servidor. |
| **Escrituras forzadas (cada cambio va al disco al confirmar)** | Sin ellas, un corte de luz o un cuelgue del servidor puede dañar la base. |
| **Solo lectura (nadie puede escribir)** | La base no acepta cambios hasta que se vuelva a cambiar. |

Cambiá lo que haga falta y apretá **Aplicar cambios**. Se aplica solo lo que cambió, y la confirmación lo lista
(«Sweep automático: 20.000 → 0»), con un aviso si apagás las escrituras forzadas o pasás a solo lectura.
**Comprobar estado** vuelve a leerlas.

- Las cambia el servidor, como gfix: hace falta conectarse como SYSDBA, como dueño de la base o con el rol RDB$ADMIN.
- Para pasar a solo lectura no tiene que haber nadie más conectado, tampoco FbStudio en otra pestaña.
- Salir de solo lectura se aplica primero y entrar, al final: con la base en solo lectura no se puede cambiar lo demás.

> **Ojo:** en una base de producción, apagar las escrituras forzadas o pasar a solo lectura pide escribir el nombre de
> la base (el del registro). Los otros cambios piden la confirmación de siempre.

## Comprobar estado y detener

**Detener** aparece mientras corre una tarea:

- En backup y restore solo deja de mirar: el servidor sigue hasta terminar, y el registro lo dice.
- En la base vacía corta el trabajo, y la base nueva queda a medio armar.

**Comprobar estado** (en backup, restore, nbackup y propiedades) dice en qué quedó lo que dejaste de mirar:

| Tarea | Qué comprueba |
|---|---|
| Backup | Si el servidor sigue con un backup de tu usuario. Lo reconoce por la conexión que abre el servicio de backup: de tu usuario y sin programa cliente. |
| Restore | En qué quedó la base nueva: «está lista para usar», «todavía se está restaurando (o quedó a medio restaurar si el restore falló)» o «No hay ninguna base…». Si quedó lista, ofrece **Registrar la base nueva**. |
| nbackup y Propiedades | Las vuelve a leer. |

**Copiar registro** copia todo el registro de la pestaña, para pegarlo en un ticket.

## Usuarios y permisos

La pestaña **Usuarios y permisos** administra los usuarios del servidor y los roles y permisos de la base, con el SQL
de Firebird 3: lee SEC$USERS, RDB$ROLES y RDB$USER_PRIVILEGES, y cambia con CREATE, ALTER y DROP USER, CREATE y DROP
ROLE, GRANT y REVOKE. Se abre:

- desde **Herramientas › Usuarios y permisos**;
- con clic derecho en la base › **Herramientas › Usuarios y permisos…**;
- con clic derecho en una tabla, vista, procedimiento, función, paquete, generador o excepción del árbol ›
  **Permisos…**, que abre la vista [Por objeto](#permisos-por-objeto) con ese objeto elegido.

> **Ojo:** los usuarios son del servidor y sirven para todas sus bases; los roles y los permisos son de esta base.

Arriba están **Actualizar** (`F9`), **Nuevo usuario…** y **Nuevo rol…**. A la izquierda, la lista que elige la
primera opción:

- **Usuarios y roles**: los usuarios del servidor (con su nombre, y «inactivo» o «administra usuarios» si
  corresponde), los que tienen permisos pero no están en el servidor («no está en el servidor»), **PUBLIC** («todos los
  usuarios») y los roles de la base, con su descripción.
- **Procedimientos, funciones, paquetes, triggers y vistas**: estos objetos también reciben permisos (por ejemplo, un
  procedimiento que escribe en TURNOS).
- **Por objeto: quién tiene permisos**: ver [Permisos por objeto](#permisos-por-objeto).

**Filtrar** busca en la lista por nombre. Abajo de todo, un panel muestra el SQL que se va a ejecutar o que se ejecutó.

Para ver todos los usuarios y para crearlos, cambiarlos o borrarlos hay que conectarse como SYSDBA o con el rol
RDB$ADMIN; si no, la pestaña lo avisa y la lista trae solo tu usuario.

## Usuarios

### Crear un usuario

**Nuevo usuario…** abre una ventana con:

- **Usuario**, **Contraseña** y **Repetir la contraseña**;
- **Nombre**, **Segundo nombre**, **Apellido** y **Descripción**;
- **Administrador de usuarios**: aparece solo si el servidor usa más de uno; de fábrica, «(el del servidor)»;
- **Activo (puede conectarse)** y **Administra usuarios (GRANT ADMIN ROLE)**.

Con **Guardar** el usuario se crea en el momento, sin esperar a **Aplicar**. Si tenías cambios de permisos sin
aplicar, antes pregunta si los descartás. Un nombre común se guarda en mayúsculas, como si se escribiera sin comillas
(«pepe» queda PEPE); con espacios u otros signos se respeta tal cual.

Firebird graba el usuario recién al confirmar: si ya existía, falla en ese momento y no se aplica nada.

### Cambiar un usuario

Elegí el usuario y apretá **Cambiar usuario…**. Es la misma ventana, sin poder cambiar el nombre; la contraseña vacía
no se cambia. Se ejecuta solo lo que cambió.

### Borrar un usuario

**Borrar usuario…** pide escribir el nombre del usuario. Ya no va a poder conectarse a ninguna base del servidor; sus
permisos en las bases quedan, y se ven como «no está en el servidor». SYSDBA y el usuario con el que estás conectado
no se pueden borrar desde acá.

> **Ojo:** las contraseñas nunca se muestran ni se anotan: en el SQL de la pantalla van como `'********'`, y también se
> tapan si aparecen en un error.

## Roles

- **Nuevo rol…** pide el nombre (con la misma regla de mayúsculas que los usuarios) y lo crea en el momento.
- Para darle un rol a un usuario, elegí el usuario: en **Roles** hay una casilla por rol, incluido
  **RDB$ADMIN (administrador de la base)**. Un rol dado con permiso para darlo a otros dice «(puede darlo a otros)».
  Estos cambios quedan pendientes hasta **Aplicar**, como los permisos.
- Los permisos de un rol se ven y se cambian eligiéndolo en la lista, igual que los de un usuario.
- **Borrar rol…** pide escribir el nombre del rol. Se borra con sus permisos: los usuarios que lo tienen los pierden.

## Permisos por usuario o rol

Elegí un usuario, PUBLIC, un rol o un objeto PSQL en la lista. A la derecha, en **Permisos**, hay un renglón por
objeto de la base, con casillas. Para encontrar lo que buscás:

- la lista de tipos: **Todos los objetos**, **Tablas**, **Vistas**, **Procedimientos**, **Funciones**, **Paquetes**,
  **Generadores** o **Excepciones**;
- **Filtrar**, por nombre;
- **Solo los que tiene**: solo los objetos sobre los que ya tiene algún permiso (o que cambiaste).

Cada renglón muestra solo las casillas que tienen sentido para ese tipo de objeto:

| Objeto | Permisos |
|---|---|
| Tabla | SELECT, INSERT, UPDATE, DELETE, REFERENCES |
| Vista | SELECT, INSERT, UPDATE, DELETE |
| Procedimiento, función o paquete | EXECUTE |
| Generador o excepción | USAGE |

- **Puede darlos** es WITH GRANT OPTION: puede dar esos permisos a otros. Vale para todos los permisos del renglón.
- Un renglón con cambios sin aplicar lleva un punto (●) a la izquierda.
- Los permisos sobre algunas columnas se muestran («Solo algunas columnas: UPDATE (OBSERVACIONES)») pero no se cambian
  desde acá.
- Un permiso que dio otro usuario se quita con GRANTED BY, y eso lo puede hacer un administrador.

## Permisos por objeto

Con **Por objeto: quién tiene permisos**, la pestaña se da vuelta: a la izquierda los objetos (con la misma lista de
tipos y **Filtrar**), cada uno con cuántos tienen permiso sobre él («Tabla · 3 con permiso»). Al elegir uno, a la
derecha hay un renglón por usuario, PUBLIC, rol y objeto PSQL con permisos sobre él, con las mismas casillas. **Filtrar**
y **Solo los que tiene** buscan entre los usuarios y roles.

Cada renglón es el mismo en las dos vistas: un cambio hecho en una se ve en la otra, y **Aplicar** lo ejecuta una sola
vez.

## Permisos de estructura

Al elegir un usuario o un rol, el desplegable **Permisos de estructura (crear, modificar y borrar objetos)** tiene los
permisos de DDL de Firebird 3: crear tablas, modificar cualquier procedimiento, borrar cualquier dominio… Hay una fila
por clase de objetos (**Tablas**, **Vistas**, **Procedimientos**, **Funciones**, **Paquetes**, **Generadores**,
**Dominios**, **Excepciones**, **Roles**, **Juegos de caracteres**, **Intercalaciones**, **Filtros de BLOB** y
**La base (ALTER DATABASE)**) con las casillas CREATE, ALTER ANY, DROP ANY y **Puede darlos**. La base solo tiene ALTER.

Por ejemplo, tildar CREATE en **Tablas** y ALTER ANY en **Procedimientos** para un rol genera:

```sql
GRANT CREATE TABLE TO ROLE DESARROLLO;
GRANT ALTER ANY PROCEDURE TO ROLE DESARROLLO;
```

Va una sentencia por permiso, junto con los demás cambios, cuando apretás **Aplicar**. El dueño de un objeto siempre lo
puede cambiar, tenga o no estos permisos. No hay fila para triggers: en Firebird 3 crear triggers va con ALTER ANY
TABLE (la fila **Tablas**).

## Aplicar y ver el SQL

Los cambios de permisos y de roles se juntan, aunque pases de un usuario a otro o de una vista a la otra; abajo dice
cuántos hay sin aplicar.

- **Ver SQL** muestra en el panel de abajo los GRANT y REVOKE, sin ejecutarlos.
- **Aplicar** muestra el SQL, pregunta («Se van a ejecutar N sentencias GRANT/REVOKE en ventas, todas juntas (si una
  falla, no se aplica ninguna)») y las ejecuta en una sola transacción. Después vuelve a leer todo, y el panel queda con
  lo que se ejecutó.
- **Descartar** deja todo como estaba.
- **Actualizar** con cambios sin aplicar pregunta antes si los descartás.

Si una sentencia falla, no se aplica ninguna y la línea de estado dice cuál fue y qué contestó Firebird.

```sql
-- 3 sentencias (no se ejecutaron todavía)
GRANT SELECT, INSERT, UPDATE ON PACIENTES TO VENTAS_APP;
REVOKE DELETE ON TURNOS FROM VENTAS_APP;
GRANT EXECUTE ON PROCEDURE ALTA_TURNO TO ROLE RECEPCION;
```

## Producción y solo lectura

Un registro de solo lectura (la opción **Solo lectura (bloquea DML, DDL y EXECUTE)** del registro, ver
[Primeros pasos](01-start.md)) deja mirar pero no cambiar. Con una base de producción, FbStudio pregunta más.

| Acción | Registro de solo lectura | Base de producción |
|---|---|---|
| Backup | Se puede | Sin pregunta extra |
| Restore | No se ofrece | Sin pregunta extra: nunca toca la base del registro (reemplazar otra pide siempre su nombre) |
| Base vacía | No se ofrece | Sin pregunta extra: nunca pisa un archivo |
| Validación | Se puede | Pregunta antes |
| Sweep | No se ofrece | Hay que escribir el nombre de la base |
| Propiedades | Se ven, no se aplican | Pregunta; apagar las escrituras forzadas o pasar a solo lectura pide escribir el nombre |
| nbackup | Se ve | Solo se mira |
| Permisos y roles de un usuario (**Aplicar**) | Solo mirar | La confirmación avisa que es de producción |
| Crear o cambiar un usuario | No | Pregunta: vale para todas las bases del servidor |
| Crear un rol | No | Pregunta |
| Borrar un usuario o un rol | No | Siempre hay que escribir el nombre |

> **Ojo:** los usuarios son del servidor. Si en el mismo servidor hay bases de producción, cambiar un usuario desde el
> registro de una base de prueba también las afecta, y FbStudio solo pregunta cuando el registro es de producción.
