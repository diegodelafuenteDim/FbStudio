# Asistentes de IA (MCP)

FbStudio puede hacer de conector para un asistente de IA compatible con MCP (Model Context Protocol). El asistente le
pregunta a FbStudio por la estructura, el rendimiento y los datos del servidor de las bases que habilitaste, y con eso
te ayuda a escribir y optimizar consultas, revisar procedimientos o medir el impacto de un cambio. Nunca recibe datos de
las tablas y no puede cambiar nada.

## Qué ve el asistente y qué no

FbStudio le ofrece estas herramientas:

| Herramienta | Qué devuelve |
|---|---|
| `list_databases` | Las bases habilitadas, con su entorno y sus áreas. |
| `get_schema` | Los objetos de una base: las tablas, o los de un área, de algunos tipos o que cumplen un patrón (`LAB_*`). Las bases grandes van de a 400 objetos. |
| `describe_object` | El DDL de una tabla (columnas, claves, índices, triggers, comentarios y FK lógicas) o el código de una vista, procedimiento, función, trigger o paquete. |
| `search_metadata` | Dónde aparece un texto en los nombres, las columnas, los comentarios y el código. No busca en los datos. |
| `get_dependencies` | Qué usa un objeto y quién lo usa. |
| `find_column_usage` | Dónde se usa una columna. |
| `find_join_path` | El camino de relaciones más corto entre dos tablas, con los JOIN armados. |
| `explain_query` | El plan de una sentencia SELECT, WITH, INSERT, UPDATE, DELETE, MERGE o EXECUTE BLOCK. |
| `get_database_info` | La versión y el tipo de servidor, ODS, dialecto, tamaño de página, charset, caché, sweep, escrituras forzadas, solo lectura y estado de nbackup. |
| `get_monitor_snapshot` | Una foto de la actividad (MON$): los contadores de transacciones, la OAT con su antigüedad y quién la tiene, las transacciones abiertas más viejas, las sentencias en curso (con el SQL sin valores), las que esperan un registro bloqueado y las conexiones por programa. |
| `get_statistics` | El encabezado de la estadística (contadores, brechas, sweep, atributos) y, para las tablas que se pidan (hasta 20), registros, versiones, páginas e índices, marcando lo que conviene revisar. |

Lo que nunca ve ni hace:

- **Datos de las tablas.** No hay ninguna herramienta que ejecute SQL o lea filas, ni siquiera un `COUNT`.
- **Cambios.** Todo se lee en transacciones de solo lectura. Para el plan, FbStudio prepara la sentencia y la descarta:
  no la ejecuta, no corre triggers ni mueve generadores. Tampoco corta conexiones ni cancela sentencias.
- **Los valores del SQL de otros programas.** En la foto del monitoreo, el SQL de cada sentencia en curso sale con los
  textos y los números cambiados por `?`, sin comentarios. El nombre de cada programa sale sin su carpeta.
- **La conexión.** No ve el servidor, la ruta de la base, el usuario ni la contraseña: nombra la base por el nombre de su
  registro.

## Habilitar una base

Abrí la ventana de registros (**Base de datos › Registrar base…**), elegí el registro, prendé **Disponible para asistentes de IA (MCP)** y guardá. Viene
apagada en todos los registros. La contraseña tiene que estar guardada: el asistente no la puede escribir.

Un registro de producción también se puede habilitar: el asistente no ve datos, y conocer la estructura o el plan de una
consulta sirve justamente ahí.

## Conectar el asistente

En **Herramientas › Opciones…**, pestaña **Asistentes (MCP)**, está la configuración lista para copiar con **Copiar**.
Es la configuración MCP estándar, con la ruta de este FbStudio:

```json
{
  "mcpServers": {
    "fbstudio": {
      "command": "C:\\FbStudio\\FbStudio.exe",
      "args": ["mcp"]
    }
  }
}
```

Va en la configuración de servidores MCP del asistente. Si el asistente lee un archivo `.mcp.json` en la carpeta de cada
proyecto, se agrega la entrada `fbstudio` dentro de `"mcpServers"`, al lado de las que ya haya. No lleva servidor,
usuario ni contraseña, así que ese archivo se puede compartir sin exponer nada.

- Con el `.deb` de Linux, el comando es `fbstudio`.
- En macOS, `/Applications/FbStudio.app/Contents/MacOS/FbStudio`.
- Si movés FbStudio a otra carpeta, copiá la configuración de nuevo.

El asistente lanza FbStudio por su cuenta, sin ventana, y le habla por la entrada y la salida estándar: no hace falta
tener FbStudio abierto ni se abre ningún puerto. La primera vez, el asistente suele pedir que apruebes el servidor
nuevo. Para probarlo, preguntale qué bases tenés habilitadas en FbStudio.

### Windows: asistentes instalados como paquete MSIX

Si el asistente es una app instalada como paquete MSIX, Windows les muestra a los programas que lanza una copia propia
de `%APPDATA%`. Si alguna vez se abrió FbStudio desde esa app, quedó ahí una copia vieja de sus datos que tapa la real:
el asistente ve registros viejos y faltan bases.

- FbStudio lo detecta y se lo avisa al asistente.
- Para arreglarlo, cerrá el asistente y renombrá la carpeta `FbStudio` que está en
  `%LOCALAPPDATA%\Packages\<paquete de la app>\LocalCache\Roaming`. Renombrarla en lugar de borrarla permite volver
  atrás.
- El conector no escribe en `%APPDATA%` salvo su registro de llamadas, así que la copia no vuelve a aparecer por usarlo.
- Un asistente que se lanza desde una terminal común no tiene este problema.

### Linux y macOS

- **Linux:** las contraseñas salen del llavero del escritorio (GNOME Keyring o KWallet), así que el asistente tiene que
  correr dentro de la sesión de escritorio. Por SSH o en un equipo sin escritorio no hay llavero, y las herramientas
  avisan que falta la contraseña.
- **macOS:** antes de configurarlo, abrí FbStudio una vez desde el Finder (**Abrir igualmente**); si no, Gatekeeper
  bloquea el ejecutable cuando lo lanza el asistente. La primera vez, y después de cada versión nueva, macOS pregunta si
  FbStudio puede leer el Llavero: elegí **Permitir siempre**.

## Las respuestas

- Cada pedido tiene 30 segundos como máximo; si tarda más, se cancela y el asistente recibe el aviso. La estadística de
  unas tablas tiene 2 minutos, porque recorre todas sus páginas (en una base grande carga el disco: pedí solo las que
  hagan falta).
- En el monitoreo, quién tiene cada conexión (usuario, equipo y programa) sale o no según la opción **En el monitoreo,
  mostrar quién tiene cada conexión** de la pestaña **Asistentes (MCP)**, prendida de fábrica. Sirve para saber quién
  frena la OAT; apagada, el asistente ve solo los números de conexión.
- Una respuesta más larga que el máximo se recorta, con un aviso de cómo pedir menos (un área, un tipo, un patrón o la
  página siguiente). El máximo se cambia en la pestaña **Asistentes (MCP)**: 40.000 caracteres de fábrica, entre 5.000 y
  200.000, para las sesiones nuevas del asistente.
- Las dependencias de más de 60 objetos se agrupan por tipo en un renglón, sin el detalle, para gastar menos.
- La conexión con cada base se abre con el primer pedido y se cierra sola a los 2 minutos sin uso. Lo que FbStudio ya
  leyó (la lista de objetos, las relaciones) se reutiliza mientras la conexión sigue abierta.
- Los errores llegan sin el servidor, la ruta ni el usuario, y con los textos entre comillas y los valores de una clave
  repetida cambiados por `?`.

## El registro de llamadas

Cada llamada del asistente queda registrada: la fecha, la herramienta, la base, cuánto tardó y el tamaño de la
respuesta. Lo que pidió (el SQL de un plan, el texto de una búsqueda) y los errores van cifrados como el historial, y se
borran con el mismo plazo (**Opciones › Historial**). Sin llavero, eso no se guarda.

Las últimas llamadas se ven en la pestaña **Asistentes (MCP)** de **Herramientas › Opciones…**. En Windows aparecen
también las de un asistente instalado como paquete MSIX, aunque hayan quedado en la copia de `%APPDATA%` de ese paquete.

## Privacidad

Lo que el asistente recibe sale de tu equipo hacia quien procesa el modelo:

- Los nombres de los objetos, el DDL, los comentarios y el código de los procedimientos, triggers y vistas. Si algún
  código tiene un valor escrito a mano, ese valor sale con el código.
- Los planes y los datos del servidor.
- En el monitoreo, el SQL de las sentencias en curso (sin valores) y, si la opción está prendida, el usuario, el equipo
  y el programa de cada conexión.
- El SQL que el propio asistente escribe para pedir un plan.

Nunca salen filas de las tablas. Habilitá solo las bases que el asistente necesita.

## Si algo no anda

| Lo que pasa | Qué hacer |
|---|---|
| El asistente no encuentra FbStudio. | Revisá la ruta de la configuración (copiala de nuevo desde Opciones) y abrí una sesión nueva del asistente. |
| «No database is enabled for assistants». | Prendé **Disponible para asistentes de IA (MCP)** en el registro de la base. |
| En Windows faltan bases o aparecen registros viejos. | Es la copia de un paquete MSIX: ver [Windows: asistentes instalados como paquete MSIX](#windows-asistentes-instalados-como-paquete-msix). |
| «FbStudio has no saved password». | Escribí la contraseña en el registro y guardalo. En Linux, el asistente tiene que correr en la sesión de escritorio. |
| «The request took longer than 30 seconds». | Pedí menos: un área, un tipo o un patrón de nombres. |
