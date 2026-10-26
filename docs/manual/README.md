# FbStudio — Manual

- [Español](es/README.md)
- [English](en/README.md)

## Cómo se mantiene / How it is maintained

**Español.** El manual está en Markdown, en dos carpetas con los mismos archivos: `es/` y `en/`. FbStudio lo trae adentro
(Ayuda › Manual, y F1 abre el capítulo de lo que se está usando) y GitHub lo muestra tal cual. Al agregar o cambiar una
función:

1. Actualizar el capítulo que corresponde en `es/` y lo mismo en `en/` (las pruebas verifican que los dos idiomas tengan
   los mismos capítulos y la misma cantidad de títulos).
2. Usar los nombres exactos de la interfaz (menús, botones, opciones) en negrita, los menús con `›` y los atajos como
   código: **Consulta › Abrir archivo .sql…**, `Ctrl+Shift+E`.
3. Solo este Markdown: títulos `#`, `##` y `###`, párrafos, listas con `-` o `1.`, tablas, citas `>` para avisos, bloques
   de código y enlaces a otros capítulos (`02-editor.md#título`). Sin HTML ni imágenes.
4. Ejemplos con nombres genéricos (TURNOS, PACIENTES) y nunca datos reales.

**English.** The manual is Markdown in two folders with the same files: `es/` and `en/`. FbStudio ships it inside (Help ›
Manual, and F1 opens the chapter for what you are using) and GitHub shows it as is. When a feature is added or changed,
update the chapter in both folders, use the exact UI names in bold, menus with `›` and shortcuts as code, keep to the
same Markdown subset, and use generic example names, never real data.
