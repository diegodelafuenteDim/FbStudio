# Opciones, atajos y privacidad

Las opciones del sistema pestaña por pestaña, todos los atajos de teclado y qué guarda FbStudio en tu equipo: dónde, para
qué y cómo lo protege.

## La ventana Opciones

Se abre con **Herramientas › Opciones…**. Las pestañas están a la izquierda: **General**, **Editor**, **Plantillas**,
**Consultas**, **Monitoreo** e **Historial**. Valen para todas las bases; lo propio de cada base (entorno, solo lectura,
cifrado, historial) va en su registro (ver [Registrar una base](01-start.md#registrar-una-base)).

- Nada cambia hasta **Aceptar**. **Cancelar** deja todo como estaba.
- **Valores por defecto** vuelve todas las opciones a como vienen de fábrica, también las plantillas y los colores de los
  entornos (los colores de las carpetas no se tocan). No se guarda hasta **Aceptar**.
- Un campo de número que quede vacío toma su valor de fábrica, y uno fuera de rango se lleva al límite.
- Al aceptar, el tema, los colores y las opciones del editor se aplican enseguida, también en las pestañas abiertas. El
  idioma cambia al volver a abrir FbStudio.

### General

| Opción | Qué hace | De fábrica |
|---|---|---|
| **Tema** | **Igual que Windows** sigue el modo claro u oscuro del sistema; **Claro** y **Oscuro** lo fijan. | **Igual que Windows** |
| **Idioma** | **Igual que Windows**, **Español** o **English**. «Igual que Windows» usa español si el sistema está en español y, si no, inglés. Los números y las fechas siguen el formato regional del sistema. | **Igual que Windows** |
| **Colores de los entornos**: **Desarrollo** | El color de las bases de desarrollo. | Verde |
| **Colores de los entornos**: **Prueba** | El color de las bases de prueba. | Naranja |
| **Colores de los entornos**: **Producción** | El color de las bases de producción. | Rojo |

Al cambiar el idioma, FbStudio pregunta (ya en el idioma elegido) si reiniciar: **Reiniciar ahora** cierra las pestañas
abiertas y vuelve a abrir FbStudio (si alguna tiene una transacción abierta, pregunta antes); **Más tarde** lo deja para
la próxima vez que lo abras.

Los colores de los entornos se ven en la franja de la base, en el borde de las pestañas y en el ícono de la base en el
árbol. Se eligen de una paleta que se lee bien con letra blanca:

- **Desarrollo** y **Prueba**: Verde, Verde oscuro, Verde azulado, Turquesa, Azul, Índigo, Violeta, Naranja, Oliva,
  Marrón o Gris azulado.
- **Producción**: Rojo, Rojo oscuro, Rojo vivo, Bordó o Carmesí. Los rojos quedan solo para producción, así nada se
  confunde con ella.

En el tema oscuro, el árbol y las pestañas usan el color un poco más claro, para que se vea sobre el fondo; la franja
usa el mismo. Los colores de las carpetas propias se eligen desde el árbol (ver
[Colores de las carpetas](01-start.md#colores-de-las-carpetas)).

### Editor

| Opción | Qué hace | De fábrica |
|---|---|---|
| **Letra** | La letra del editor de SQL. Se ofrecen las de ancho fijo que estén instaladas: Cascadia Mono, Cascadia Code, Consolas, JetBrains Mono, Fira Code, Source Code Pro, Courier New y Lucida Console. | Cascadia Mono |
| **Tamaño de la letra** | De 8 a 40. | 14 |
| **Ancho de la tabulación** | Cuántos espacios ocupa una tabulación, de 1 a 16. | 4 |
| **Tab inserta espacios (no un carácter de tabulación)** | `Tab` escribe espacios en vez de un carácter de tabulación. | Sí |
| **Mostrar los números de línea** | El número de cada renglón en el margen. | Sí |
| **Resaltar la línea del cursor** | Marca el renglón donde está el cursor. | Sí |
| **Poder bajar más allá de la última línea** | Deja desplazar el texto más abajo del último renglón. | Sí |
| **Ctrl + rueda del mouse agranda o achica la letra (Ctrl+0 la vuelve al tamaño elegido)** | Zoom de la letra en esa pestaña, sin cambiar la opción. `Ctrl+0` vuelve al tamaño de arriba. | Sí |
| **Plegar bloques (BEGIN…END, CASE, paréntesis y comentarios largos) con marcas en el margen** | Marcas en el margen para plegar y desplegar los bloques de varios renglones. | Sí |
| **Al formatear, palabras clave en mayúsculas** | **Formatear SQL** pasa las palabras clave a mayúsculas; apagada, quedan como se escribieron. | Sí |
| **Al cerrar, recordar las consultas abiertas y volver a abrirlas** | Al cerrar FbStudio guarda las pestañas de consulta, cada una con su base y su texto (cifrado como el historial), y las vuelve a abrir la próxima vez. Una pestaña de un archivo .sql se vuelve a leer del archivo. Apagada, se borra lo guardado. | Sí |

Las pestañas de consulta vacías no se recuerdan, ni las de objetos o herramientas.

### Plantillas

Una plantilla es un texto que se escribe con una abreviatura: escribí la abreviatura y apretá `Tab`, o apretá `Ctrl+J`
para elegir de la lista (**Consulta › Insertar plantilla…**). En el texto, `${cursor}` marca dónde queda el cursor.

En esta pestaña está la lista de plantillas, con su abreviatura y su descripción. Para cambiarlas:

- **Nueva** agrega una plantilla (con la abreviatura «nueva1», «nueva2»…) para completar.
- **Borrar** quita la plantilla elegida.
- **Las de fábrica** vuelve la lista a las plantillas que trae FbStudio.
- A la derecha se editan la **Abreviatura**, la **Descripción** y el **Texto** de la plantilla elegida.

Las abreviaturas no distinguen mayúsculas; si dos se repiten, queda la primera. Mientras no cambies las de fábrica, si
una versión nueva de FbStudio las mejora, las ves sin tocar nada.

| Abreviatura | Descripción |
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

### Consultas

| Opción | Qué hace | De fábrica |
|---|---|---|
| **Filas que trae un SELECT** | Cuántas filas trae de entrada un SELECT: 1.000, 10.000, 100.000 o 1.000.000. Es el valor con que arranca FbStudio; el selector **Límite** de la barra de herramientas lo cambia solo hasta cerrar la aplicación. Exportar «todas las filas» no tiene límite. | 10.000 filas |
| **Espera ante un registro bloqueado** | Si otra transacción tiene bloqueado el registro que querés modificar, la escritura espera hasta este tiempo y después da error, en vez de dejar la pestaña colgada. Vale para las transacciones que se abran desde ahora. De 1 a 600 segundos. | 10 segundos |
| **Transacción de escritura abierta: ámbar a los** | Pasados estos minutos, el aviso de transacción abierta de la franja va sobre ámbar. De 1 a 1.440. | 5 minutos |
| **…y rojo, con un aviso, a los** | Pasados estos, va en rojo y la barra de estado avisa una vez. No puede ser menos que el anterior; hasta 10.080. | 15 minutos |
| **Al aplicar cambios en la pestaña Datos** | **Mostrar los comandos antes (ejecutarlos, copiarlos o llevarlos a una pestaña de consulta)** o **Ejecutarlos directamente**. | Mostrar los comandos antes |
| **Ante un error en un script** | **Parar en el primer error** o **Seguir con la siguiente sentencia**. | Parar en el primer error |

Sobre la transacción abierta: una transacción olvidada, con varias personas trabajando, traba la OAT y la recolección de
basura del servidor (ver [La franja de la base](01-start.md#la-franja-de-la-base)).

Al aplicar cambios en la pestaña Datos, de las dos formas se ejecuta lo mismo: todo junto en una transacción corta, con el
control de que nadie haya cambiado la fila. Con producción o con borrados se pide confirmación igual (ver
[La pestaña Datos](04-data.md)).

Ante un error en un script: una sentencia que falla se deshace sola; lo anterior y la transacción siguen en pie. Al final
el registro lista los errores y el cursor va al primero. Es la misma opción que
**Consulta › Seguir ante errores en scripts**.

### Monitoreo

| Opción | Qué hace | De fábrica |
|---|---|---|
| **OAT en ámbar si lleva abierta más de** | En el monitoreo, la transacción activa más vieja (OAT) se pinta en ámbar pasados estos minutos. De 1 a 1.440. | 2 minutos |
| **y en rojo si lleva más de** | Pasados estos, en rojo. No puede ser menos que el anterior; hasta 10.080. | 10 minutos |
| **No alarmar si la OAT es de un backup o un sweep (gbak, gfix, nbackup): se informa sin color** | Una OAT de mantenimiento se muestra sin color de alarma. | Sí |

Siempre hay una OAT mientras haya transacciones abiertas; lo que preocupa es que dure. Un backup con gbak o un sweep la
mantienen abierta mientras trabajan. Más en [Monitoreo y rendimiento](06-monitoring.md).

### Historial

| Opción | Qué hace | De fábrica |
|---|---|---|
| **Guardar las sentencias durante** | Cuántos días se guardan las sentencias en el historial, de 1 a 3.650. Al achicar el plazo, las más viejas se borran en el momento; también se purgan al abrir FbStudio. | 90 días |

Cada registro puede no guardar historial: apagá **Guardar historial de sentencias** en su registro. El historial se ve
con `Ctrl+4` (ver [El editor de SQL](02-editor.md)).

### Lo que se recuerda sin la ventana

Algunas preferencias se guardan junto con las opciones aunque no estén en esta ventana:

- El color de cada carpeta propia (desde el menú de la carpeta en el árbol).
- Los últimos archivos .sql abiertos o guardados, hasta 10 (**Consulta › Abrir reciente**).
- En la pestaña Datos, **Contar al filtrar** y **Terminar con COMMIT** de la ventana de los comandos.
- La última versión de FbStudio que se abrió, para mostrar **Novedades** una sola vez.

El orden, el ancho y las columnas ocultas de la pestaña Datos se recuerdan por base y tabla, en otro archivo (ver
[La carpeta de FbStudio](#la-carpeta-de-fbstudio)). El selector **Límite** de la barra de herramientas no se recuerda.

## Atajos de teclado

**Ayuda › Atajos de teclado** muestra un resumen; acá está la lista completa. Los menús también muestran el atajo al lado
de cada comando.

### Ejecutar

| Atajo | Qué hace |
|---|---|
| `F9` o `F5` | Ejecuta la selección; sin selección, todo el editor (varias sentencias corren en orden, como script). En la pestaña de un procedimiento, trigger, vista o función, sin selección, lo compila. |
| `Ctrl+Enter` | Ejecuta solo la sentencia donde está el cursor (o la selección). |
| `Ctrl+F9` | Ejecuta la selección o todo el editor como script, sin grilla (para scripts largos). |
| `Ctrl+Shift+E` | Muestra el plan de la sentencia del cursor (o de la selección) sin ejecutarla. |
| `Esc` | Cancela la ejecución. |
| `Ctrl+Alt+C` | Commit de la transacción de la pestaña. |
| `Ctrl+Alt+R` | Rollback de la transacción de la pestaña. |
| `F9` o `Ctrl+Enter` en monitoreo, estadísticas, usuarios y permisos o búsqueda | Refresca la pestaña (en la búsqueda, busca). |

### Escribir en el editor

| Atajo | Qué hace |
|---|---|
| `Ctrl+Espacio` | Abre el autocompletado. |
| `Enter` o `Tab` | Con el autocompletado abierto, elige la sugerencia. |
| `Tab` | Después de la abreviatura de una plantilla, la expande. |
| `Ctrl+J` | Elige una plantilla de la lista. |
| `Ctrl+Shift+L` | Formatea el SQL. |
| `F12` | Abre el objeto cuyo nombre está bajo el cursor. |
| `Ctrl+clic` | Abre el objeto del nombre donde hacés clic. |
| `Ctrl+/` | Comenta o descomenta los renglones de la selección (o el del cursor). En el teclado español es la tecla de esa posición, `-`. |
| `Ctrl+D` | Duplica la selección o, sin selección, el renglón del cursor. |
| `Alt+↑` / `Alt+↓` | Sube o baja los renglones de la selección (o el del cursor). |
| `Ctrl+Shift+U` | Pasa la selección (o la palabra del cursor) a mayúsculas. |
| `Ctrl+U` | Pasa la selección (o la palabra del cursor) a minúsculas. |
| `Ctrl+G` | Va a un renglón. |
| `Ctrl+Z` | Deshace. Comentar, duplicar, mover renglones y cambiar mayúsculas se deshacen con un solo `Ctrl+Z`. |
| `Ctrl` + rueda del mouse | Agranda o achica la letra (si está prendido en **Opciones**, pestaña **Editor**). |
| `Ctrl+0` | Vuelve la letra al tamaño elegido en **Opciones**. |

### Buscar

| Atajo | Qué hace |
|---|---|
| `Ctrl+F` | En el editor, busca en el texto. Con la grilla elegida, busca en los resultados (sin distinguir mayúsculas ni acentos). |
| `Ctrl+H` | Busca y reemplaza en el editor. |
| `F3` / `Shift+F3` | Siguiente / anterior coincidencia. |
| `Enter` / `Shift+Enter` | En el cuadro de búsqueda, siguiente / anterior coincidencia. En el cuadro de reemplazo, reemplaza la siguiente. |
| `Tab` / `Shift+Tab` | Con el reemplazo abierto, pasa entre el cuadro de búsqueda y el de reemplazo. |
| `Esc` | Cierra la barra de búsqueda. |
| `Ctrl+Shift+F` | Busca en los metadatos, en la base de lo último que tocaste. |
| `Enter` en la pestaña de búsqueda | En el cuadro, busca; en los resultados, abre el elegido. `→` y `←` despliegan y pliegan. |

### Pestañas y paneles

| Atajo | Qué hace |
|---|---|
| `Ctrl+T` | Consulta nueva, en la base de lo último que tocaste (lo mismo que el botón **+**). |
| `Ctrl+Shift+T` | Vuelve a abrir la última consulta cerrada, con su base y su texto. |
| `Ctrl+W` o `Ctrl+F4` | Cierra la pestaña. El clic del medio sobre la pestaña también. |
| `Ctrl+Tab` / `Ctrl+Shift+Tab` | Pasa a la pestaña siguiente / anterior. |
| `Ctrl+O` | Abre un archivo .sql. |
| `Ctrl+S` | Guarda la consulta en su archivo (si todavía no tiene, pregunta dónde). En la pestaña de un objeto, guarda una copia del DDL. |
| `Ctrl+Shift+S` | Guarda como. |
| `Ctrl+1` … `Ctrl+5` | Paneles de abajo: Resultados, Plan, Estadísticas, Historial y Mensajes. |
| `Ctrl+6` | Dependencias, en la pestaña de un objeto. |
| `Ctrl+7` | Datos, en la pestaña de una tabla o vista. |
| `F1` | Abre el capítulo del manual de lo que estás usando. |

### Grilla de resultados

| Atajo | Qué hace |
|---|---|
| `↑` `↓` `←` `→` | Se mueve de celda. |
| `RePág` / `AvPág` | Sube o baja una pantalla. |
| `Inicio` / `Fin` | Va a la primera / última columna. |
| `Ctrl+Inicio` / `Ctrl+Fin` | Va a la primera / última celda. |
| `Shift` + `↑` `↓` `RePág` `AvPág` | Extiende la selección de filas. |
| `Shift+clic` / `Ctrl+clic` en una fila | Elige un rango de filas / suma o saca filas sueltas. |
| `Ctrl+A` | Elige todas las filas. |
| `Ctrl+C` | Copia la celda; con varias filas elegidas, las copia todas con encabezados. |
| `Ctrl+Shift+C` | Copia la fila (o las filas elegidas) con encabezados. |
| `Ctrl+F` | Busca en los resultados. |
| `Enter` o `Espacio` en una fila de grupo | Abre o cierra el grupo. |
| `Enter` / `Esc` en la fila de filtro | Aplica / descarta el filtro que escribiste. |
| Clic en un encabezado | Ordena por esa columna (otra vez: al revés). `Ctrl+clic` quita el orden. |
| Doble clic en el borde de un encabezado | Ajusta el ancho de la columna a su contenido. |
| Doble clic en una celda que no se edita | Abre **Ver el valor…** con la celda entera. |
| `Shift` + rueda del mouse | Desplaza a los costados. |

Todo lo de la grilla está en [Resultados](03-results.md).

### Pestaña Datos

Nada se guarda en la base hasta **Aplicar** (ver [La pestaña Datos](04-data.md)).

| Atajo | Qué hace |
|---|---|
| `F2`, `Enter`, doble clic o empezar a escribir | Edita la celda. |
| `Enter` | Guarda la celda y baja a la siguiente fila. |
| `Tab` / `Shift+Tab` | Guarda la celda y pasa a la de la derecha / izquierda. |
| `Esc` | Descarta lo escrito en la celda. |
| `Supr` | Marca o desmarca para borrar las filas elegidas. |
| `Insert` | Agrega una fila arriba de todo y empieza a editarla. |
| `Enter` en el cuadro del WHERE | Vuelve a leer la tabla con esa condición. |

### Árbol de bases

| Atajo | Qué hace |
|---|---|
| `Enter` o doble clic en una base | La conecta y la usa en la pestaña. |
| `Enter` o doble clic en un objeto | Lo abre en su pestaña (DDL y dependencias; los procedimientos, triggers, vistas y funciones se editan y compilan ahí). |
| `Enter` o doble clic en una columna | Escribe su nombre en el editor. |
| `Enter` o doble clic en una carpeta o un tipo de objeto | Lo abre o lo cierra. |
| `→` / `←` | Abre / cierra el nodo elegido. |
| Arrastrar un objeto o una columna al editor | Escribe su nombre donde lo soltás. |
| Arrastrar una base o una carpeta | La mueve a otra carpeta (ver [Mover bases y carpetas](01-start.md#mover-bases-y-carpetas)). |

## Privacidad y seguridad

FbStudio guarda en tu equipo lo que necesita para trabajar: los registros, las opciones, el historial y poco más. Las
contraseñas nunca se escriben en claro, y lo que puede tener datos (el SQL del historial y las consultas recordadas) va
cifrado.

### Contraseñas

Las contraseñas de los registros no se guardan en el archivo de registros sino en el llavero de cada sistema, y nunca se
vuelven a mostrar:

| Sistema | Dónde se guardan | Qué hace falta |
|---|---|---|
| Windows | DPAPI, con alcance de tu usuario: solo tu usuario en esa PC las puede descifrar. | Nada. |
| Linux | El llavero del escritorio (Secret Service: GNOME Keyring, KWallet…), por medio del comando `secret-tool`. | `libsecret-tools` y un llavero. |
| macOS | El Llavero del usuario, con el nombre de servicio «FbStudio». | Nada. |

- En Linux, la contraseña le llega a `secret-tool` por la entrada estándar, nunca en la línea de comandos (ahí se vería
  con `ps`). FbStudio busca `secret-tool` solo en las carpetas absolutas del PATH, nunca en la carpeta desde donde se
  abrió. Al arrancar, comprueba que el llavero guarde de verdad. Un llavero bloqueado puede pedir tu clave en una
  ventana.
- En macOS, si la aplicación cambia (por ejemplo, otra versión sin firmar), el sistema pregunta si la deja leer el
  Llavero.
- Una contraseña escrita en el SQL (`CREATE USER … PASSWORD '…'` o `ALTER USER … SET PASSWORD '…'`) queda con
  asteriscos en el historial y en el registro de errores. Lo que se ejecuta no cambia.

### Historial y pestañas recordadas

- El historial de sentencias va en `history.db`. El SQL y los mensajes de error van cifrados; el plan, los tiempos y las
  cantidades de filas, no. Lo que se borra (por vencido) se pisa con ceros en el archivo.
- Las pestañas de consulta recordadas van en `session.bin`, cifradas igual que el historial. Con la opción apagada, ese
  archivo se borra al cerrar.
- En Windows se cifran con DPAPI, con tu cuenta. En Linux y macOS, con AES-256 y HMAC-SHA256, con una clave al azar que
  se guarda en el llavero.
- Cuánto dura el historial se elige en **Opciones**, pestaña **Historial**, y cada registro puede no guardarlo.

### Sin llavero

Si el sistema no tiene llavero (por ejemplo, un Linux sin escritorio), FbStudio funciona igual, pero:

- Las contraseñas se recuerdan solo mientras FbStudio está abierto; al volver a abrirlo hay que escribirlas de nuevo en
  cada registro. Nunca se escriben en claro.
- El historial no se guarda.
- Las pestañas recordadas viven solo en memoria: no se vuelven a abrir la próxima vez.
- La barra de estado lo avisa al arrancar, y al importar registros con contraseñas también se avisa.

En Linux, instalá GNOME Keyring o KWallet y `libsecret-tools`.

### Backup de los registros

El archivo de **Exportar registros…** sin contraseñas es un JSON legible: tiene servidores, bases y, si lo elegiste,
usuarios. Con contraseñas, todo el contenido va cifrado con la frase que elegís (AES-256 con HMAC-SHA256 y la clave
derivada con PBKDF2-SHA256 de 600.000 vueltas). Va todo cifrado, y no solo las contraseñas, para que nadie pueda cambiar
el servidor de un registro en el archivo y quedarse con su contraseña. La frase no se guarda en ningún lado. Es la forma
de llevar los registros, con sus contraseñas, a otro equipo. Cómo se usa:
[Exportar e importar registros](01-start.md#exportar-e-importar-registros).

### La carpeta de FbStudio

Todo lo que guarda FbStudio está en una carpeta de tu usuario:

| Sistema | Carpeta |
|---|---|
| Windows | `%APPDATA%\FbStudio` |
| Linux y macOS | `~/.config/FbStudio` |

En Linux y macOS, la carpeta y sus archivos quedan solo para tu usuario (permisos 0700 y 0600). En Windows, la carpeta
de tu perfil ya es privada.

| Archivo | Para qué es |
|---|---|
| `profiles.json` | Los registros de las bases: nombre, servidor, puerto, base, usuario, rol, charset, entorno, carpeta, cifrado, solo lectura e historial. Sin contraseñas. |
| `settings.json` | Las opciones, y lo que se recuerda sin la ventana de opciones (colores de las carpetas, archivos recientes…). |
| `secrets` | Solo en Windows: una carpeta con la contraseña de cada registro, cifrada con DPAPI, en un archivo `.bin` por registro. |
| `history.db` | El historial de sentencias, con el SQL y los errores cifrados. |
| `session.bin` | Las pestañas de consulta recordadas, cifradas. |
| `grid-layouts.json` | El orden, el ancho y las columnas ocultas de la pestaña Datos, por base y tabla (las últimas 500 tablas). |
| `monitor-queries.json` | Las consultas propias del monitoreo; valen para todas las bases. |
| `areas` | Una carpeta con las áreas de cada base, un `.json` por registro. Una base que usa otro archivo de áreas (por ejemplo, uno compartido por el equipo) lo lee de donde lo elegiste. |
| `errors.log` | Los errores no previstos, con las contraseñas tapadas, para adjuntar en un reporte. Pasado 1 MB se borra y empieza de nuevo. |

Fuera de esa carpeta, FbStudio escribe el manual (**Ayuda › Manual** y `F1`) como HTML en una carpeta `FbStudio` dentro
de la carpeta temporal del sistema, para abrirlo en el navegador sin internet. Lo que exportás o guardás va donde lo
elijas.

> **Ojo:** copiar la carpeta a otro equipo no lleva las contraseñas (están en el llavero o cifradas para tu usuario en esa
> PC). Para pasar los registros, usá **Exportar registros…**.

### Rutas del servidor

En las estadísticas, el mantenimiento y el trace, FbStudio cambia la ruta de la base en el servidor por lo que dice el
campo **Base (alias o ruta)** del registro, así no queda a la vista dónde están los archivos. Si en ese campo escribiste
la ruta en vez de un alias, se ve esa ruta: por eso conviene registrar las bases con su alias.
