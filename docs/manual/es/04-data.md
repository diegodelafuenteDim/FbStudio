# La pestaña Datos

Hojear una tabla o una vista entera, con los filtros y el orden resueltos en el servidor, y editarla sin dejar registros
bloqueados: los cambios esperan en FbStudio y se aplican todos juntos.

## Abrir la pestaña Datos

Las tablas y las vistas tienen la pestaña **Datos**:

- En el árbol, clic derecho en una tabla o una vista › **Ver datos (editar sin bloquear)**.
- O abrí la tabla o la vista (ver [El árbol, las áreas y los diagramas](05-tree.md)) y elegí **Datos** entre las
  pestañas de abajo (`Ctrl+7`).

La tabla se lee recién al elegir la pestaña. La grilla es la misma de los resultados (ver [Resultados](03-results.md)):
moverse, copiar, **Ver el valor…**, **Ver la fila en vertical…**, **Gráfico…** y `Ctrl+F` funcionan igual. Lo que
cambia se cuenta acá.

## Hojear por páginas

La pestaña trae las filas de a 500. Al bajar y acercarte a la última, trae las 500 siguientes sola.

- El pie dice cuántas filas hay cargadas y, si quedan más, **bajá para traer más**.
- La línea de abajo dice cuántas filas se leyeron, en cuánto tiempo y si la tabla se puede editar (y si no, por qué).
- El botón con la flecha circular (**Recargar**) vuelve a leer desde el principio.

Sin otro orden, las filas vienen por la clave primaria. Una tabla sin clave primaria viene en su orden físico, y una
vista que no muestra la clave de su tabla, sin un orden fijo.

La pestaña usa su propia conexión. Cada página se lee en una transacción corta de solo lectura que se cierra enseguida:
hojear una tabla grande no frena la OAT ni bloquea nada.

Cuando se puede, la página siguiente se pide «después de la última fila traída», por la clave: tarda lo mismo en
cualquier página. Si la tabla no tiene clave primaria, o si ordenás por una columna que admite NULL, las páginas se
piden por posición y, en una tabla muy grande, cuanto más bajás más tarda cada una.

## El WHERE adicional

Arriba de la grilla está el cuadro **WHERE adicional (SQL)**. Escribí una condición en SQL y apretá `Enter`:

```sql
FECHA >= '2026-10-01' AND ESTADO = 1
```

Va tal cual al WHERE de la lectura, junto con los filtros de las columnas (se tienen que cumplir los dos). Para sacarlo,
borralo y apretá `Enter`.

## Filtros y orden en el servidor

La fila de filtros, la ventanita de cada columna y el panel de filtros son los mismos de **Resultados**, con la misma
sintaxis (ver [Sintaxis de los filtros](03-results.md#sintaxis-de-los-filtros)). Clic en un encabezado también ordena.
La diferencia es que acá no se filtra ni se ordena lo cargado: FbStudio vuelve a leer la tabla con el WHERE y el ORDER BY
que corresponden, así el filtro y el orden valen para la tabla entera.

- Con un filtro puesto, la barra de arriba dice **Filtro resuelto en el servidor** y ofrece **Quitar filtros** y
  **Panel de filtros**. Sin filtros esa barra no se ve: el panel se abre con clic derecho en un encabezado ›
  **Filtrar en el panel de filtros**.
- En los textos no se distinguen mayúsculas, pero los acentos se comparan según la intercalación (collation) de la
  columna: puede que «perez» no encuentre «PÉREZ».
- La lista de valores para tildar no se carga sola, porque recorre la tabla: en la ventanita aparece el botón **Traer
  los valores del servidor**, que trae los 500 más repetidos teniendo en cuenta los demás filtros. En una columna con
  fecha y hora van por día.
- No se agrupa ni hay totales en el pie.
- `Ctrl+F` busca solo en las filas que ya se trajeron.

## Contar

- **Contar** pregunta al servidor cuántas filas cumplen el filtro y el WHERE de ahora (un `SELECT COUNT(*)`). La línea de
  abajo dice **La tabla tiene N filas con el filtro actual** y el pie pasa a decir «N de M filas cargadas».
- **Contar al filtrar**, tildado, cuenta automáticamente después de cada lectura que tenga un filtro o un WHERE. Si
  todo entró en la primera página, no hace falta preguntarle al servidor. La casilla se recuerda para todas las tablas.

En tablas grandes, contar puede tardar.

## Cómo se edita

Editar en esta pestaña no toca la base hasta que lo pedís:

1. Cambiás celdas, agregás filas o marcás filas para borrar. Todo queda pendiente en FbStudio: no se escribe nada ni se
   bloquea ningún registro.
2. Una franja de aviso dice que hay cambios sin aplicar, y arriba se ve el resumen: «N fila(s) cambiadas · N para
   borrar · N nueva(s)».
3. **Aplicar** ejecuta todo junto en una transacción corta: entran todos los cambios o ninguno.

Los cambios pendientes no se pierden al ordenar, filtrar, traer más filas o recargar: cada fila se reconoce por su clave
primaria.

> **Ojo:** el **Commit** de la barra de herramientas es para las consultas del editor, no para esto. Los cambios de la
> pestaña **Datos** se guardan solo con **Aplicar**.

Si cerrás la pestaña con cambios sin aplicar, FbStudio avisa que se pierden y pide **Descartar y cerrar**.

## Editar celdas

Para editar una celda:

- doble clic, `F2` o `Enter`;
- o empezá a escribir sobre ella, como en una planilla;
- o clic derecho › **Editar celda (F2)**.

En el cuadro, `Enter` guarda y baja a la fila siguiente, `Tab` guarda y pasa a la derecha (`Shift+Tab`, a la izquierda)
y `Esc` cancela. Si lo escrito no se entiende para el tipo de la columna, el cuadro queda abierto con el error.

| Tipo | Cómo se escribe |
| --- | --- |
| Números | Con el separador decimal del sistema o con punto, y nunca con separador de miles: `1.5` es uno y medio |
| DATE | `2026-07-01` o `01/07/2026` |
| TIMESTAMP | `2026-07-01 14:30` o `01/07/2026 14:30` (con o sin segundos) |
| TIME | `14:30` o `14:30:00` |
| BOOLEAN | `true` o `false`, `sí` o `no`, `1` o `0` |
| Texto | Tal cual |

La celda cambiada se ve con el fondo marcado y el valor nuevo en negrita. Si le volvés a poner lo que tenía, deja de ser
un cambio.

### Poner NULL

Dejar vacía una celda la pone en NULL, salvo en los textos: un texto vacío es `''`. Para poner NULL en cualquier
columna: clic derecho › **Poner NULL**.

## Nueva fila

Con el botón **Nueva fila**, la tecla `Insert` o clic derecho › **Nueva fila (Insert)**:

1. Aparece una fila vacía arriba de todo, con un `+` en el número de fila, y se abre la primera columna que se puede
   escribir.
2. Completá lo que haga falta. Lo que dejes vacío queda afuera del INSERT, así vale el valor por defecto, el trigger o la
   identity. Sin nada completado, va un `INSERT … DEFAULT VALUES`.
3. **Aplicar** la guarda.

Las filas nuevas siguen arriba aunque cambies el orden o el filtro. Al aplicar, toman lo que devolvió el servidor (la
clave que puso un trigger o una identity, los valores por defecto). En una vista, la tabla se vuelve a leer después del
INSERT.

## Borrar y recuperar

- `Supr` marca para borrar las filas elegidas, o les saca la marca si ya la tenían. También con clic derecho ›
  **Marcar para borrar (N) (Supr)** y **Desmarcar el borrado**.
- Una fila marcada se ve en rojo y tachada. Se borra recién al aplicar.
- Una fila nueva que se borra antes de aplicar simplemente desaparece.
- Clic derecho › **Deshacer los cambios de la fila** devuelve las filas elegidas a como se leyeron: saca sus cambios, su
  marca de borrado o, si son nuevas, las quita.

## Descartar

**Descartar** tira todos los cambios pendientes de la tabla: las celdas vuelven a lo que se leyó, las marcas de borrado
se van y las filas nuevas desaparecen. La línea de abajo dice **Cambios descartados.**

## Aplicar

El botón **Aplicar** dice cuántos cambios esperan: **Aplicar (3)**. Al apretarlo:

1. Si así está elegido en las opciones (es lo que viene de fábrica), primero muestra los comandos, con los avisos de
   producción o de borrados arriba (ver [La vista previa de los comandos](#la-vista-previa-de-los-comandos)). Si no,
   y la base es de producción o hay filas para borrar, pide confirmación.
2. Ejecuta todo en una transacción corta: primero los DELETE, después los UPDATE y al final los INSERT.

Cada DELETE y cada UPDATE tiene que encontrar exactamente una fila por su clave primaria. Además, cada UPDATE exige que
las columnas que cambiaste sigan valiendo lo que se leyó (salvo los blobs y los números de punto flotante). Si alguna
sentencia no encuentra su fila, encuentra más de una o el servidor da un error, se deshace todo: no se guarda nada y los
cambios siguen pendientes. La línea de abajo dice qué pasó y en qué fila:

- **No se guardó nada: la fila … la cambió o la borró otro usuario desde que se leyó.** Recargá y volvé a hacer el
  cambio.
- **No se guardó nada: la clave de la fila … no es única.**
- **No se guardó nada (se deshizo todo). Falló la fila …**, con el error del servidor. Si es un registro bloqueado,
  dice quién lo tiene tomado.

Si otra transacción tiene bloqueado un registro que hay que cambiar, la escritura espera hasta el tiempo de
**Opciones › Consultas › Espera ante un registro bloqueado** y después da error (ver
[Opciones, atajos y privacidad](09-options.md)).

Si sale bien, dice **Guardado y confirmado** con cuántas filas cambiaron, se borraron y se agregaron. Las filas cargadas
se actualizan sin volver a leer la tabla, así no perdés dónde estabas. Las columnas calculadas se ven actualizadas al
recargar.

## La vista previa de los comandos

En **Opciones › Consultas**, **Al aplicar cambios en la pestaña Datos** tiene dos formas:

- **Mostrar los comandos antes (ejecutarlos, copiarlos o llevarlos a una pestaña de consulta)**: la de fábrica.
- **Ejecutarlos directamente**: **Aplicar** ejecuta sin mostrar nada (con producción o con borrados, pide confirmación
  igual).

De las dos formas se ejecuta lo mismo. La ventana **Comandos para aplicar en TABLA** muestra arriba los avisos
(producción, borrados) y después las sentencias con los valores escritos, por ejemplo:

```sql
-- Cambios de datos en TURNOS (FbStudio): 1 UPDATE, 1 DELETE, 0 INSERT
-- Cada sentencia tiene que afectar 1 fila. Si alguna afecta 0, otro cambió o borró esa fila desde que se leyó: ROLLBACK y recargar.
DELETE FROM TURNOS WHERE ID = 1041;
UPDATE TURNOS SET ESTADO = 2 WHERE ID = 1050 AND ESTADO IS NOT DISTINCT FROM 1;
```

El `IS NOT DISTINCT FROM` es el control de que nadie cambió la fila desde que se leyó. Los botones:

- **Ejecutar**: lo aplica en su transacción corta y la confirma.
- **Abrir en una pestaña de consulta**: lleva las sentencias a una pestaña nueva (**Cambios en TABLA**), sin ejecutarlas.
  Los cambios dejan de estar pendientes en **Datos**: revisalos, ejecutalos ahí con `F9`, confirmá con **Commit** y
  después **Recargar** la pestaña **Datos**.
- **Copiar**: copia el texto al portapapeles.
- **Cancelar**: no hace nada; los cambios siguen pendientes.

**Terminar con COMMIT** agrega un `COMMIT;` al final del texto que se copia o se lleva a una consulta. **Ejecutar**
confirma siempre, con o sin la casilla. FbStudio recuerda la casilla para la próxima vez.

## Generar script

**Generar script…** abre la misma ventana de comandos aunque en las opciones esté elegido **Ejecutarlos directamente**.
Sirve para llevarte los comandos de los cambios pendientes: copiarlos, abrirlos en una pestaña de consulta (con
**Terminar con COMMIT** o sin él) o ejecutarlos.

## Qué se puede editar

| Qué | Se edita |
| --- | --- |
| Tabla con clave primaria | Sí |
| Tabla sin clave primaria | No: se ve, pero no hay cómo decir qué fila cambiar |
| Vista que sale de una sola tabla y muestra toda su clave primaria | Sí: los cambios van a esa tabla a través de la vista |
| Otra vista | No: se ve, pero no se edita |
| Columna calculada (o una expresión de una vista) | No |
| Blob binario | No (sí se puede ver con **Ver el valor…**) |
| Blob de texto (`BLOB SUB_TYPE TEXT`) | Sí |

Cuando algo no se edita, la línea de abajo dice por qué, y el doble clic en esa celda abre **Ver el valor…**.

## Producción y solo lectura

- **Producción:** se puede editar, pero **Aplicar** pide confirmación con cuántos cambios van, en qué tabla y en qué
  base. Con la vista previa, el aviso aparece arriba de los comandos.
- **Borrados:** en cualquier entorno, si hay filas para borrar, **Aplicar** avisa cuántas y de qué tabla antes de seguir.
- **Registro de solo lectura:** la tabla se ve, se filtra y se copia, pero no se edita (**Perfil de solo lectura: los
  datos no se editan.**) y **Nueva fila** queda apagado.

## Copiar las filas

**Copiar como INSERT** y **Copiar como UPDATE** (clic derecho en una celda) usan siempre la tabla y su clave primaria, y
copian las filas como se ven: con los cambios sin aplicar. Las columnas calculadas quedan afuera. El resto de las formas
de copiar es igual que en [Resultados](03-results.md#copiar).

**Exportar** trabaja sobre el resultado de una consulta, no sobre esta pestaña: para llevarte una tabla a un archivo,
ejecutá un SELECT y exportá el resultado (ver [Exportar](03-results.md#exportar)).

## Columnas recordadas

En la pestaña **Datos**, FbStudio recuerda para cada tabla de cada base cómo dejaste las columnas: el orden, el ancho y
las que ocultaste. La próxima vez que la abrís, se ven igual.

- Las columnas nuevas de la tabla aparecen al final; las que ya no existen se ignoran.
- Clic derecho en un encabezado › **Restaurar el orden y el ancho de las columnas** vuelve al orden de la tabla, con los
  anchos según el contenido y todas a la vista.
- Se guardan las últimas 500 tablas que tocaste.
