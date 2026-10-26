# Primeros pasos

Cómo instalar FbStudio, registrar tus bases, conectarte y leer la franja de arriba, que siempre dice dónde se ejecuta lo
que escribís. También cómo ordenar el árbol con carpetas propias y cómo pasar los registros a otro equipo.

## Instalar

FbStudio se baja de la página **Releases** del repositorio de FbStudio en GitHub. No hace falta instalar nada más: ni
.NET ni el cliente de Firebird, porque FbStudio habla directo con el servidor. Está probado con Firebird 3.0.

| Sistema | Archivo |
|---|---|
| Windows 10 u 11 (64 bits) | `FbStudio-<versión>-win-x64.zip` |
| Debian, Ubuntu y derivadas | `fbstudio_amd64.deb` |
| Otras distribuciones de Linux | `FbStudio-<versión>-linux-x64.tar.gz` |
| macOS con Apple Silicon (M1 en adelante) | `FbStudio-<versión>-osx-arm64.zip` |
| macOS con Intel | `FbStudio-<versión>-osx-x64.zip` |

### Windows

1. Descomprimí el `.zip` en una carpeta.
2. Abrí `FbStudio.exe`.
3. FbStudio no está firmado: la primera vez Windows puede mostrar «Windows protegió su PC». Tocá **Más información** y
   después **Ejecutar de todas formas**.

### Linux

Con el paquete `.deb` (Debian, Ubuntu y derivadas):

```bash
sudo apt install ./fbstudio_amd64.deb
```

Queda en el menú de aplicaciones y como comando `fbstudio`. El paquete recomienda `libsecret-tools` y un llavero (GNOME
Keyring o KWallet), que son los que permiten recordar las contraseñas. Para desinstalarlo: `sudo apt remove fbstudio`.

Con el `.tar.gz` (cualquier otra distribución):

1. Descomprimilo.
2. Corré `./FbStudio` desde esa carpeta.
3. Instalá `libsecret-tools` (el comando `secret-tool`) y un llavero del escritorio, para que FbStudio pueda recordar las
   contraseñas.

> **Ojo:** sin llavero FbStudio funciona igual, pero las contraseñas se recuerdan solo mientras está abierto y el
> historial de sentencias no se guarda. Más detalles en [Opciones, atajos y privacidad](09-options.md#sin-llavero).

### macOS

1. Bajá el `.zip` de tu Mac: `osx-arm64` para Apple Silicon, `osx-x64` para Intel.
2. Descomprimilo y abrí `FbStudio.app`.
3. FbStudio no está firmado por Apple, así que la primera vez macOS lo bloquea. Andá a Configuración del Sistema ›
   Privacidad y seguridad y tocá **Abrir igualmente**. También se puede destrabar desde la Terminal:

```bash
xattr -dr com.apple.quarantine FbStudio.app
```

### Comprobar la descarga

Cada versión trae un archivo `SHA256SUMS.txt`. Con el paquete y ese archivo en la misma carpeta:

```bash
sha256sum -c SHA256SUMS.txt --ignore-missing
```

### Actualizar

Bajá el paquete nuevo y reemplazá el anterior (con el `.deb`, volvé a correr `sudo apt install` con el archivo nuevo).
Los registros, las opciones y el historial no están en la carpeta del programa sino en la carpeta de FbStudio de tu
usuario, así que se conservan. Qué hay en esa carpeta: [La carpeta de FbStudio](09-options.md#la-carpeta-de-fbstudio).

## La primera vez

FbStudio abre maximizado. El idioma y el tema siguen al sistema: si el sistema está en español, la interfaz va en
español; con cualquier otro idioma, en inglés. Los dos se cambian en **Herramientas › Opciones…**, pestaña **General**
(ver [Opciones, atajos y privacidad](09-options.md#general)).

### La bienvenida

Sin bases registradas, a la izquierda aparece **Registrá tu primera base** con dos caminos:

- **Registrar base**: abre la ventana de registros para cargar servidor, base, usuario y contraseña (ver
  [Registrar una base](#registrar-una-base)).
- **Importar registros…**: si te pasaron el archivo con los registros del equipo, lo importás y quedan todas las bases,
  con sus áreas; vos ponés tu usuario y contraseña (ver [Importar](#importar)).

Con bases registradas pero ninguna pestaña abierta, en el centro aparece **Ninguna pestaña abierta**: hacé doble clic en
una base del árbol para conectarte.

### Avisos al arrancar

- En Linux sin llavero, la barra de estado avisa que las contraseñas se recuerdan solo mientras FbStudio está abierto.
- Si el archivo de opciones no se pudo leer, se avisa y se usan los valores de fábrica.
- La primera vez que abrís una versión nueva, se abre sola la ventana **Novedades** (una sola vez; en una instalación
  nueva, no). Se vuelve a ver en **Ayuda › Novedades**.
- Las consultas que quedaron abiertas la última vez se vuelven a abrir, cada una con su base y su texto. Se apaga en
  **Herramientas › Opciones…**, pestaña **Editor**.

> **Atajo:** `F1` abre el capítulo del manual de lo que estás usando, y **Ayuda › Manual** abre el índice.

## Registrar una base

Un registro guarda cómo conectarse a una base: servidor, base, usuario, contraseña y cómo tratarla (entorno, solo
lectura, cifrado). Para registrar una base, usá cualquiera de estos:

- **Base de datos › Registrar base…**
- El botón **Registrar** de la barra de herramientas.
- Clic derecho en un lugar vacío del árbol o en una carpeta › **Registrar base…** (desde una carpeta, el registro nuevo
  ya viene con esa carpeta).

Para cambiar un registro: **Base de datos › Editar registros…** (abre con la base de la pestaña elegida) o clic derecho
en la base › **Registro › Editar registro…**.

### La ventana Bases registradas

A la izquierda está la lista de registros, cada uno con su carpeta y su entorno; abajo, **Nuevo** y **Borrar**. A la
derecha, el formulario del registro elegido y los botones **Probar conexión**, **Guardar** y **Cerrar**.

| Campo | Qué va |
|---|---|
| **Nombre (alias)** | Cómo se ve la base en el árbol, en las pestañas y en la franja. Obligatorio. |
| **Servidor** | El nombre o la dirección del servidor Firebird, por ejemplo `servidor`. Obligatorio. |
| **Puerto** | De fábrica `3050`. |
| **Base (alias o ruta)** | El alias que la base tiene en el servidor (por ejemplo `ventas`) o la ruta del archivo en el servidor. Obligatorio. |
| **Usuario** | El usuario de Firebird. Obligatorio. |
| **Contraseña** | Se guarda cifrada en el llavero del sistema y nunca se vuelve a mostrar. Vacía, se conserva la guardada: el campo dice «(sin cambios)», o «(falta la contraseña)» si no hay ninguna. |
| **Rol** | Opcional: el rol con el que se conecta. |
| **Charset** | El juego de caracteres de la conexión: `ISO8859_1` (de fábrica), `WIN1252`, `UTF8` o `NONE`. |
| **Entorno** | **Desarrollo** (de fábrica), **Prueba** o **Producción**. |
| **Carpeta** | La carpeta propia del árbol donde va la base. Vacía, la base va al grupo de su entorno. |
| **Cifrado de la conexión** | **Si el servidor lo acepta (Enabled)** (de fábrica), **Obligatorio (Required)** o **Sin cifrar (Disabled)**. |
| **Solo lectura (bloquea DML, DDL y EXECUTE)** | Apagado de fábrica. |
| **Guardar historial de sentencias (cifrado; los días se eligen en Opciones)** | Prendido de fábrica. |

> **Ojo:** conviene escribir el alias en **Base (alias o ruta)**: ese texto se ve en la franja y en el tooltip del árbol.
> Donde FbStudio lee por su cuenta la ruta de la base en el servidor (estadísticas, mantenimiento, trace), la cambia por
> ese texto.

### Entorno

El entorno dice qué cuidado necesita la base y le da su color: verde para **Desarrollo**, naranja para **Prueba** y rojo
para **Producción** (los colores se cambian en **Herramientas › Opciones…**; los rojos quedan siempre para producción).
El color se ve en la franja de arriba, en el borde de las pestañas y en el ícono de la base en el árbol. Las bases sin
carpeta propia se agrupan en el árbol por entorno.

En cualquier entorno, un `UPDATE` o `DELETE` sin `WHERE` pide confirmación. Con **Producción**, además:

- La franja dice **PRODUCCIÓN**.
- Toda sentencia que modifica algo pide confirmación antes de ir al servidor, y también un `SELECT` que usa
  procedimientos o funciones dentro de una transacción de escritura abierta.
- Aplicar cambios en la pestaña Datos e importar un archivo piden confirmación.
- Autocommit y el aislamiento de la transacción no se pueden elegir: siempre es con **Commit** explícito y read
  committed. En Desarrollo y Prueba, cada pestaña puede prender **Autocommit** y elegir el aislamiento en la barra de
  herramientas.
- No se ofrece **Generar datos de prueba…**.
- Al elegir **Producción** en el formulario, si el cifrado estaba en **Si el servidor lo acepta (Enabled)** pasa a
  **Obligatorio (Required)**, con un aviso. Si el servidor no acepta cifrado, no va a conectar: probá la conexión antes
  de guardar. Lo podés volver a cambiar a mano.

### Solo lectura

Con **Solo lectura**, FbStudio bloquea antes de llegar al servidor todo lo que modifica: DML, DDL y `EXECUTE`, y también
mover un generador (`NEXT VALUE FOR`, o `GEN_ID` con un paso distinto de 0; para leer el valor, `GEN_ID(generador, 0)`).
Además no se compila, no se editan datos en la pestaña Datos, no se importa, no se cambian permisos, en Mantenimiento no
se ofrecen restore, base vacía, sweep ni cambiar las propiedades, y en el monitoreo no se corta una conexión ni se
cancela una sentencia. La franja suma « · solo lectura».

> **Ojo:** el solo lectura es una red de seguridad de FbStudio, no reemplaza los permisos de la base. Para una base de
> producción que solo consultás, combiná **Producción** con **Solo lectura**.

### Cifrado de la conexión

Define si los datos viajan cifrados entre FbStudio y el servidor:

- **Si el servidor lo acepta (Enabled)**: cifra si el servidor lo permite; si no, conecta sin cifrar.
- **Obligatorio (Required)**: solo conecta con cifrado. Si el servidor no lo acepta, la conexión falla.
- **Sin cifrar (Disabled)**: no cifra.

Si la conexión quedó cifrada se ve al probarla, en la franja y en el tooltip de la base en el árbol (ver
[La franja de la base](#la-franja-de-la-base)).

### Charset

Es el juego de caracteres con que se leen y escriben los textos. Muchas bases viejas tienen sus columnas de texto en
`ISO8859_1`, que es el de fábrica. Si una base tiene columnas con `NONE` que guardan texto de Windows, usá `WIN1252`
para que los acentos se lean bien.

### Probar, guardar y borrar

- **Probar conexión** se conecta con lo que está en el formulario (sin guardarlo) y dice `OK` con la versión del
  servidor y si la conexión va cifrada, o el error.
- **Guardar** pide nombre, servidor, base y usuario, y una contraseña si todavía no hay ninguna guardada. Al guardar, el
  registro aparece en el árbol.
- **Nuevo** limpia el formulario para un registro nuevo.
- **Borrar** quita el registro elegido y su contraseña guardada en este equipo, después de preguntar. La base no se toca.

Para hacer otro registro parecido (otra base del mismo servidor, otro usuario u otro rol), usá clic derecho en la base ›
**Registro › Clonar registro… (y abrir sus propiedades)**: hace una copia con todo, contraseña incluida, llamada
«(copia)», y la abre para cambiarle lo que haga falta.

## Conectar y desconectar

Para conectarte a una base, hacé doble clic en ella en el árbol. También sirven: elegirla y apretar `Enter`, el botón
**Conectar** de la barra de herramientas, **Base de datos › Conectar la base elegida** o clic derecho › **Conectar**.

Al conectar, FbStudio:

1. Abre la conexión del árbol y muestra los objetos de la base por tipo (tablas, vistas, procedimientos…), con cuántos
   hay de cada uno.
2. Lee los datos del servidor: al lado de la base aparece «conectada · Firebird 3.0.x · Classic» (o el tipo de servidor
   que sea) y el tooltip suma el sistema, la ODS, el dialecto, el tamaño de página, el charset y si la conexión va
   cifrada. Si la base quedó en modo backup de nbackup, al lado aparece «⚠ modo backup» y el tooltip explica qué hacer.
3. Pone la base en la pestaña de consulta elegida, si está libre (sin transacción abierta ni algo ejecutándose). Si no,
   o si es otro tipo de pestaña, abre una consulta nueva en esa base. Sin pestañas, abre la primera.

Cada pestaña de consulta abre su propia conexión la primera vez que ejecutás algo, con su propia transacción. Las
herramientas (monitoreo, trace, mantenimiento…) abren la suya: no hace falta conectar la base antes.

Con la base ya conectada, el clic derecho ofrece **Usar esta base en la pestaña** y **Recargar metadatos** (vuelve a
leer los objetos, por ejemplo después de crear una tabla desde otro programa).

Si la base no tiene contraseña guardada, la barra de estado lo dice: completala en el registro. Si no se puede conectar,
la barra de estado muestra el error del servidor. Si el servidor no es un Firebird 3.x, también avisa: FbStudio está
probado con Firebird 3.x y algunas lecturas de metadatos pueden fallar.

### Desconectar

Clic derecho en la base › **Desconectar**, o **Base de datos › Desconectar la base elegida**. Se cierran todas las
pestañas de esa base (consultas, objetos, búsqueda, monitoreo…) y la conexión del árbol.

- Si alguna pestaña tiene una transacción abierta o cambios sin guardar, pregunta una sola vez y las nombra. Al cerrar,
  las transacciones se deshacen (rollback).
- Si alguna pestaña está ejecutando, no se desconecta nada: cancelala o esperá a que termine.

### Al salir

Al cerrar FbStudio, si hay transacciones de escritura abiertas, cambios sin compilar o sin guardar, o consultas
ejecutándose, pregunta antes con **Salir y deshacer** (o **Salir y descartar**) y **No salir**. Al salir, lo que no se
confirmó se deshace y las conexiones se cierran.

## El árbol de bases

A la izquierda está el árbol con todas las bases registradas:

- Las bases sin carpeta propia van al grupo de su entorno: **Producción** arriba, después **Prueba** y **Desarrollo**.
- Después vienen las carpetas propias, por nombre. Dentro de cada carpeta van primero sus carpetas y después sus bases,
  por nombre.
- El ícono de cada base lleva el color de su entorno. Una base sin conectar se ve más tenue; la de la pestaña elegida va
  en negrita.

Arriba del árbol, el cuadro de filtro busca objetos por nombre en las bases conectadas: un texto (contiene, sin
distinguir mayúsculas) o un patrón con `*` o `%`, por ejemplo `TURNOS*`. Cada tipo de objeto dice cuántos coinciden
(«3 de 479») y se abre solo si hay alguno; los tipos que todavía no se habían abierto se leen solos. La cruz de la
derecha quita el filtro.

Qué hay adentro de cada base (tablas, columnas, DDL, áreas, generar SQL) está en
[El árbol, las áreas y los diagramas](05-tree.md).

### Carpetas propias

Las carpetas propias ordenan las bases a tu manera, por ejemplo por cliente o por sistema. Una carpeta puede estar dentro
de otra: se escribe con `/`, por ejemplo `Ventas/Producción`. Una carpeta existe mientras tenga alguna base, y
«ventas» y «Ventas» son la misma.

Para crear una carpeta:

1. **Base de datos › Nueva carpeta…**, o clic derecho en un lugar vacío del árbol o en una carpeta › **Nueva carpeta…**.
2. Escribí el **Nombre de la carpeta**.
3. Tildá las bases que van adentro (la elegida en el árbol ya viene tildada). Hace falta al menos una.
4. Tocá **Crear**. Si ya había una carpeta con ese nombre, las bases se suman a esa.

También se elige la carpeta en el campo **Carpeta** del registro: escribí una nueva o elegí una existente con **▾**
(«(sin carpeta: grupo del entorno)» la saca de su carpeta).

### Mover bases y carpetas

Arrastrá en el árbol:

- Una base sobre una carpeta: va a esa carpeta. Sobre otra base: va a la carpeta de esa base. A un lugar vacío o al
  grupo de su entorno: queda sin carpeta.
- Una carpeta sobre otra: queda adentro. A un lugar vacío: va a la raíz.

Una base no se puede pasar al grupo de otro entorno arrastrándola (el entorno se cambia en el registro), y una carpeta no
puede ir adentro de sí misma.

Sin arrastrar:

- Clic derecho en la base › **Registro › Mover a carpeta…**: elegí una carpeta o escribí una nueva. Vacía, la base
  vuelve al grupo de su entorno.
- Clic derecho en una carpeta propia › **Renombrar carpeta…**: cambia el nombre y se lleva todo lo de adentro. Si el
  nombre nuevo ya existe, las carpetas se juntan; con otra ruta (`Producción` en vez de `Ventas/Producción`) la carpeta
  se mueve; vacío, sus bases vuelven al grupo de su entorno.

### Colores de las carpetas

Clic derecho en una carpeta propia › **Color de la carpeta** y elegí un color de la paleta, o **Sin color propio**. El
color va en el ícono de la carpeta; las bases de adentro siguen con el color de su entorno, que es el aviso de dónde se
ejecuta. La paleta de las carpetas no tiene rojos (son de producción), y el color acompaña a la carpeta si la renombrás
o la movés. Los grupos de entorno (**Producción**, **Prueba** y **Desarrollo**) no se pueden renombrar ni colorear.

## La franja de la base

Abajo de la barra de herramientas hay una franja de color que sigue a la pestaña elegida. Es el aviso de dónde se
ejecuta lo que escribís.

### Entorno y color

La franja lleva el color del entorno de la base de la pestaña: verde en Desarrollo, naranja en Prueba y rojo en
Producción, o los que elijas en **Herramientas › Opciones…**, pestaña **General** (ver
[Opciones, atajos y privacidad](09-options.md#general)). Sin base, es gris.

### La base de la pestaña

El texto dice el entorno (en producción, **PRODUCCIÓN**), el nombre del registro y `usuario@servidor:puerto/base`, por
ejemplo «Desarrollo · Ventas · SYSDBA@servidor:3050/ventas». Si el registro es de solo lectura, suma « · solo lectura».

Cuando no hay base, la franja dice qué hacer: sin bases registradas, registrar la primera; sin pestañas, hacer doble clic
en una base para conectarte; en una pestaña sin base, hacer doble clic en una base para usarla ahí.

### Si la conexión va cifrada

Cuando la base ya está conectada en el árbol, la franja suma « · conexión cifrada» o « · ⚠ sin cifrar». Sin cifrar,
las consultas y los datos viajan legibles por la red; el tooltip explica cómo exigir el cifrado: en el registro, elegí
**Obligatorio (Required)** en **Cifrado de la conexión** (el servidor tiene que aceptarlo).

### Transacción abierta

A la derecha, si la pestaña tiene una transacción de escritura abierta, la franja dice hace cuánto: «Transacción de
escritura ABIERTA hace 12 min: confirmá (Commit) o deshacé (Rollback)». Se actualiza cada 30 segundos.

- Pasados los minutos de **Transacción de escritura abierta: ámbar a los** (de fábrica 5), el aviso va sobre ámbar.
- Pasados los de **…y rojo, con un aviso, a los** (de fábrica 15), va en letra roja sobre blanco y la barra de estado
  avisa una vez.

Los dos se eligen en **Herramientas › Opciones…**, pestaña **Consultas**. Una transacción olvidada traba la recolección
de basura del servidor: confirmala con **Commit** (`Ctrl+Alt+C`) o deshacela con **Rollback** (`Ctrl+Alt+R`). La pestaña
también lo marca con un punto rojo al lado de su nombre.

### Otras bases conectadas

Si hay otras bases conectadas en el árbol, a la derecha del texto aparece **También conectadas:** y cada una como un chip
con el color de su entorno. El tooltip del chip dice su entorno y su servidor. Un clic lleva a la primera pestaña de esa
base o, si no tiene, abre una consulta en ella. La pestaña actual no cambia de base.

## Varias bases a la vez

Podés tener varias bases conectadas y trabajar con todas al mismo tiempo:

- Cada pestaña tiene su base, su conexión y su transacción. El borde de la pestaña lleva el color del entorno y el
  tooltip dice a qué base pertenece.
- La franja muestra siempre la base de la pestaña elegida; las demás conectadas aparecen como chips.
- Doble clic en otra base del árbol: la pestaña elegida pasa a esa base si está libre; si tiene una transacción abierta
  o está ejecutando, la base se abre en una pestaña nueva y la anterior sigue con la suya.
- **Consulta › Pestaña nueva** (`Ctrl+T`) o el botón **+** abren una consulta en la base de lo último que tocaste: la
  elegida en el árbol, si hiciste clic en el árbol después de pasar por la pestaña; si no, la de la pestaña elegida. Lo
  mismo vale para **Consulta › Buscar en metadatos…** (`Ctrl+Shift+F`).
- Clic derecho en una base › **Consulta nueva en esta base** abre siempre una consulta en esa base.
- `Ctrl+Tab` y `Ctrl+Shift+Tab` pasan de una pestaña a otra.

> **Ojo:** antes de ejecutar, mirá la franja. Con varias bases abiertas, el color y el texto de la franja son lo que dice
> dónde va a correr la sentencia.

## Exportar e importar registros

Los registros se pueden guardar en un archivo para tener un backup, pasarlos a otro equipo (también a Linux o macOS) o
compartirlos con el equipo. Cada sistema guarda las contraseñas en su llavero, así que esta es la forma de llevarlas a
otro equipo.

### Exportar

1. Elegí una de estas opciones:
   - **Base de datos › Exportar registros…**, o clic derecho en un lugar vacío del árbol › **Exportar registros…**.
   - Clic derecho en una carpeta › **Exportar los registros de esta carpeta…** (vienen tildadas sus bases).
   - Clic derecho en una base › **Registro › Exportar este registro…**.
2. En **Registros a exportar:** tildá cuáles van (**Todos** y **Ninguno** tildan o destildan la lista entera).
3. Elegí qué llevan:
   - **Incluir mi usuario de cada conexión (si no, quien importe pone el suyo)**: prendido de fábrica.
   - **Incluir también mi contraseña de cada conexión (el archivo va cifrado con una frase)**: apagado de fábrica; solo
     se puede con el usuario. Pide una frase de al menos 10 caracteres, dos veces.
   - **Incluir las áreas de cada base (al importar, las reciben las bases que todavía no tienen)**: prendido de fábrica.
4. Tocá **Exportar…** y elegí dónde guardar el archivo (se propone `fbstudio-registros-AAAAMMDD.json`).

Sin contraseñas, el archivo es un JSON legible. Sin tu usuario, sirve para compartir los registros con el equipo: quien
lo importe pone su propio usuario y contraseña. Con contraseñas, todo el contenido (registros, contraseñas y áreas) va
cifrado con la frase. Las áreas viajan con su contenido, aunque la base use un archivo de áreas compartido.

> **Ojo:** el archivo tiene servidores y bases: guardalo en un lugar seguro. La frase no se guarda en ningún lado: sin
> ella el archivo no se puede importar. No es tu contraseña de Firebird; mejor varias palabras.

### Importar

1. **Base de datos › Importar registros…**, el botón **Importar registros…** de la bienvenida, o clic derecho en un
   lugar vacío del árbol o en una carpeta › **Importar registros…**.
2. Elegí el archivo. Si va cifrado, escribí la frase con la que se exportó; si no es la correcta, lo dice y podés probar
   de nuevo.
3. La ventana **Importar registros** dice cuántos registros trae, cuántos son nuevos y cuántos ya están registrados, y
   cuántos traen contraseña (van al llavero de este equipo).
4. Si hay registros que ya están, elegí **Dejar como están los que ya están registrados** (de fábrica) o
   **Actualizarlos con los datos del archivo**.
5. Si hay registros sin usuario, escribí el usuario y la contraseña para ellos (se pueden dejar vacíos y completar
   después en cada registro).
6. Tocá **Importar**. La barra de estado resume cuántos se agregaron, se actualizaron o quedaron sin tocar.

Un registro del archivo ya está registrado si es el mismo registro (importado otra vez) o si apunta a la misma base, en
el mismo servidor y puerto, con el mismo usuario (si el archivo trae usuario). Un registro nuevo con un nombre que ya
existe se agrega como «Nombre (2)». Los que no tienen nombre, servidor o base no se importan. Un archivo que no es de
registros de FbStudio se rechaza, y uno hecho con una versión más nueva pide actualizar FbStudio.

### Qué se protege al importar

- Un archivo no puede bajar la protección de un registro que ya existe: **Solo lectura**, **Producción** y el cifrado
  de la conexión conservan lo más fuerte. Si querés bajarlos, cambialo a mano en el registro.
- Si al actualizar un registro cambia el servidor, la base o el usuario y el archivo no trae contraseña, la contraseña
  guardada se olvida: no se manda a otro servidor. Hay que volver a escribirla.
- La ruta de un archivo de áreas compartido no se importa: cada equipo elige el suyo.
- Las áreas del archivo van a las bases que todavía no tienen; la que ya tiene áreas, o usa un archivo compartido,
  conserva las suyas.
- Si el archivo trae contraseñas y el equipo no tiene llavero, se avisa antes: las contraseñas importadas se recuerdan
  solo hasta cerrar FbStudio.
