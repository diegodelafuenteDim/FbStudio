# The tree, areas and diagrams

How to browse a connected database from the tree, open and compile its objects, search the metadata, organize a large
database with areas and draw its relationships.

## The database tree

The tree on the left shows your registered databases, grouped by environment or in your own folders. Registering,
connecting and organizing databases is covered in [Getting started](01-start.md). This chapter covers what is inside a
connected database.

### Categories

When you connect a database, it expands into categories with the number of objects next to each one (for example,
**Tables** 245):

- **Tables**, **Views** and **Procedures** are always there, even when they are empty.
- **Functions**, **Packages**, **Triggers**, **Generators**, **Exceptions**, **Domains**, **Indices** and **Roles**
  only appear if the database has any.
- **Areas** comes last, only if the database has areas (see [Areas in the tree](05-tree.md#areas-in-the-tree)).

The objects in a category are read the first time you expand it. Each object shows one extra detail next to it: the
table of a trigger or an index, the type of a domain. Inactive triggers and indexes are dimmed.

Tables and views expand in turn into their columns, with the type and the PK and NOT NULL marks; primary key columns
have a key icon. Hover over a column to see its comment.

### Filtering the tree

The box above the tree filters the objects of the connected databases:

- Without wildcards, it shows the objects whose name contains the text, case-insensitive: `turno` finds TURNOS,
  TURNOS_DET and P_TURNO_ALTA.
- With `*` or `%`, the pattern must match the whole name: `LAB_*` (starts with LAB_), `*_DET` (ends with _DET).
- It filters objects: databases, folders and columns are not filtered.
- Each category shows how many objects match (“3 of 245”), expands by itself if it has any, and is dimmed if it has
  none. It also filters inside areas.
- Categories you had not expanded yet are read automatically (names only). Meanwhile they show “… of 245” and the
  status bar says it is searching every category; when it finishes, it says how many objects match.

While a filter is active, the box is highlighted. To clear it, delete the text or use the cross in the box (**Clear the
filter**).

### Expanding, collapsing and double-clicking

- A click on the triangle expands or collapses the row, and does nothing else.
- `→` expands the selected row and `←` collapses it.
- Double-click (or `Enter`):
  - on a database, connects and expands it, and the query tab switches to it (or a new query opens on it);
  - on an object, opens its tab (see [The object tab](05-tree.md#the-object-tab));
  - on a column, types its name into the editor at the cursor;
  - on a category, a folder or an area, expands or collapses it.
- Right-click: the menu for that row (see [The object menu](05-tree.md#the-object-menu)).

## The object tab

Open an object by double-clicking it in the tree, with **Open (DDL and dependencies)** or **Open (edit, compile and
dependencies)** from its menu, or with `Ctrl+click` on its name in the editor (see [The SQL editor](02-editor.md)).
Each object has a single tab per database: if it is already open, it is selected.

The tab has three parts:

- At the top, a bar with the object's icon and name, its comment (if it has one) and the **Compile** and **Reload**
  buttons.
- In the middle, the DDL in the editor.
- At the bottom, the usual tabs (**Results**, **Messages**…) plus **Dependencies**, which is the one shown when the tab
  opens (`Ctrl+6`). Tables and views also have **Data** (`Ctrl+7`), which is explained in
  [The Data tab](04-data.md).

### The DDL

Procedures, triggers, views and functions are shown as a single `CREATE OR ALTER` statement, ready to edit and
compile. Other objects (tables, domains, generators, exceptions, indexes, packages and roles) show DDL built from the
metadata, for viewing or copying: in those tabs the editor is read-only and nothing runs (not `F9`, `Ctrl+Enter`,
`Ctrl+F9` nor the plan), because running a table's DDL would try to create it again with its keys, indexes, triggers
and permissions. To use part of that DDL, copy it to a new query (`Ctrl+T`).

- **Reload** reads the DDL from the database again.
- `Ctrl+S` saves a copy of the text to a .sql file. It always asks where, and it does not compile anything.

### Dependencies

The **Dependencies** tab has two lists:

- **Uses**: what the object needs (the tables a procedure reads, the table an FK points to…).
- **Used by**: what breaks if you change or drop it.

Each list is grouped by type, with the count, and each object shows its detail next to it: the columns it uses, the FK
name, a computed column or a CHECK. The **Filter by name or column** box narrows both lists (the titles then say, for
example, “Uses (3 of 12)”). Double-click opens each object.

Dependencies come from RDB$DEPENDENCIES, plus the FKs between tables and, for a domain, the columns and parameters
that use it.

> **Note:** dynamic SQL (`EXECUTE STATEMENT`) and application queries are not listed. An empty **Used by** list does
> not guarantee that nobody uses the object.

### Compiling

Procedures, triggers, views and functions are edited and compiled in their tab:

1. Change the text. **● not compiled** appears next to the name.
2. Click **Compile**, or press `F9` (or `Ctrl+Enter`) with nothing selected.
3. If it compiles, the change is committed right away (it does not stay in an open transaction), the marker goes away,
   the status bar shows how long it took and the dependencies are read again.
4. If it does not compile, the statement is rolled back: the cursor moves to the line and column of the error, the
   status bar shows a summary and **Messages** shows the full error.

With text selected, `F9` runs the selection, just like in a query.

To be able to compile:

- The text must be a single `CREATE OR ALTER` (or `ALTER` or `RECREATE`) statement for the same object. To create a
  different object, use a query tab.
- There can be no open transaction in that tab: Commit or Rollback first.
- In a read-only registration, **Compile** is disabled.
- The statement goes through the same safety check as any other (see [The SQL editor](02-editor.md)).

### Uncompiled changes

While the text does not match what is in the database, the bar shows **● not compiled**. Changes are not applied until
you compile, and FbStudio asks before losing them:

- when you close the tab: **Discard and close** or **Keep editing**;
- when you click **Reload**: **Discard and reload** or **Keep editing**;
- when you close several tabs or exit FbStudio: a single warning with the names of the tabs.

## The object menu

Right-click an object or a column in the tree. Anything that does not apply to that kind of object is disabled or not
shown.

### Tables

- **Open (DDL and dependencies)**: the object tab.
- **View data (edit without locking)**: the same tab, on **Data** (see [The Data tab](04-data.md)).
- `SELECT FIRST 100 *`: runs that query in a tab named after the table (if there is already a free one, it reuses it).
- **Generate SQL**: see [Generate SQL](05-tree.md#generate-sql).
- **Relationship diagram**: see [Diagrams](05-tree.md#diagrams).
- **Import data (CSV or Excel)…**: see [Scripts, comparisons and import](08-scripts.md). Disabled in a read-only
  registration.
- **Compare data with another database…**: compares the table's rows with those in another database and builds the
  script to make them match. It only appears when more than one database is registered. It is explained in
  [Scripts, comparisons and import](08-scripts.md).
- **Generate test data…**: only in Development and Test databases that are not read-only (see
  [Scripts, comparisons and import](08-scripts.md)).
- **Permissions…**, **Type the name into the query** and **Copy the name**: see below.

The menu of the **Tables** category has **Import into a new table (CSV or Excel)…** (except in a read-only
registration).

### Views

Same as tables, but with **Open (edit, compile and dependencies)**, because a view is compiled, and **Generate SQL**
only with the SELECT. Views have no import, data comparison or test data.

### Procedures and functions

- **Open (edit, compile and dependencies)**.
- **Generate SQL**: the call with its parameters (see [Generate SQL](05-tree.md#generate-sql)).
- **Permissions…**, **Type the name into the query** and **Copy the name**.

### Other objects

- Triggers: **Open (edit, compile and dependencies)** and the name options.
- Packages, generators and exceptions: **Open (DDL and dependencies)**, **Permissions…** and the name options.
- Domains, indexes and roles: **Open (DDL and dependencies)** and the name options.

### Columns

Table and view columns have **Where is it used?…**, **Type the name into the query** and **Copy the name**.

### Generate SQL

**Generate SQL** builds the statement and puts it in a new query on that database, without running it, with the cursor
in the editor.

| Object | Options |
|---|---|
| Table | **SELECT with all columns**, **INSERT**, **UPDATE by primary key**, **DELETE by primary key**, **UPDATE OR INSERT** |
| View | **SELECT with all columns** |
| Procedure | **SELECT * FROM …**, **EXECUTE PROCEDURE …** |
| Function | **SELECT … FROM RDB$DATABASE** |

For tables:

- Values go in as `:COLUMN` parameters, which FbStudio asks for when you run the statement (see
  [The SQL editor](02-editor.md)). If a column name has spaces or other symbols, those characters become `_` in the
  parameter; if it starts with a digit, it gets a `P_` prefix.
- INSERT, UPDATE and UPDATE OR INSERT leave out computed columns (COMPUTED BY).
- UPDATE changes every column that is not part of the key. UPDATE and DELETE find the row by the primary key, and
  UPDATE OR INSERT uses it in MATCHING.
- Long column lists are split over several lines.

For example, **UPDATE by primary key** on TURNOS:

```
UPDATE TURNOS
SET FECHA = :FECHA,
    PACIENTE = :PACIENTE,
    ESTADO = :ESTADO
WHERE ID = :ID
```

If the table has no primary key, the condition is left for you to fill in, so the statement cannot run by mistake:

```
DELETE FROM TURNOS_LOG
WHERE /* no primary key: fill in the condition */
```

For procedures and functions, each input parameter goes in as `:NAME`; the parameters of an external function (UDF),
which have no name, go in as `?`. **SELECT * FROM …** is for procedures that return rows: if the procedure returns
nothing, it builds an `EXECUTE PROCEDURE` instead.

```
SELECT *
FROM TURNOS_LIBRES(:DESDE, :HASTA)
```

If the object was dropped after the tree was loaded, FbStudio tells you: reload the metadata.

### Where is it used?

On a column, **Where is it used?…** opens a window with everything you should check before changing or dropping it:

- **Used by**: the views, procedures, functions and triggers that name it; the computed columns and CHECKs that use
  it; its indexes and the key they back; the FKs of other tables that point to it, logical ones included (see
  [Logical FKs](05-tree.md#logical-fks)).
- **Points to**: the table that the column's own FK points to.

Double-click opens each object (except indexes). The window does not block you: you can keep working while it is open.

> **Note:** dynamic SQL (`EXECUTE STATEMENT`) and application queries are not listed. An empty list does not
> guarantee that nobody uses the column.

### Permissions

**Permissions…** is available on tables, views, procedures, functions, packages, generators and exceptions. It opens
**Users and permissions** at **By object: who has permissions**, with that object selected: you see who has which
permission on it, and you change them with the checkboxes and **Apply**. This is explained in
[Maintenance and permissions](07-maintenance.md).

### Typing or copying the name

- **Type the name into the query**: types it at the cursor of the editor in the selected tab, in double quotes if
  needed (if it has lowercase letters, spaces or other symbols, or is a reserved word).
- **Copy the name**: copies it as is, without quotes.

Double-clicking a column does the same as **Type the name into the query**.

## Reloading metadata

The tree reads the objects when you connect and does not pick up later changes by itself. If you create, change or
drop an object from a query (or someone else does), use **Reload metadata** in the database menu. It only appears when
the database is connected.

It reads the categories, the areas and the server details again (for example, if the database left backup mode), as
well as what autocomplete and diagrams use. Categories that were expanded end up collapsed.

## Searching the metadata

Searches for text in the sources, names, columns and descriptions of a database. Open it with `Ctrl+Shift+F`, with
**Query › Search metadata…**, with the **Search** button on the toolbar or with **Search metadata…** in the database
menu. It opens in a tab, on the database of whatever you touched last: the one selected in the tree, or the tab's
database if you went back to it.

### How to search

1. Type the text (at least 2 characters) and press `Enter` or click **Search**. The search is case-insensitive and
   looks for the text as is, without wildcards.
2. Choose where to search (all are ticked by default): **Sources (procedures, triggers, views, functions, packages)**,
   **Object names**, **Columns** and **Descriptions**.
3. If the database has areas, **Area:** shows only what is in one area (or in **No area**). You can change it after
   searching, without searching again; triggers and indexes go with their table.

The server runs the search and only returns what matches. `Esc` cancels a long search.

### The results

- They are grouped by object type, with the count: “Procedures · 148 objects · 160 matches”.
- Each object shows its first match: where it is (**line 12**, **name**, **column** or **description**) and the
  snippet with the text highlighted.
- An object with several matches expands with the triangle so you can see each one.
- Double-click or `Enter` opens the object with the cursor on that line. `→` expands and `←` collapses.
- At the top, the summary: how many matches, in how many objects and how long it took. The tab is named after what
  you searched for.

## Dragging into the editor

Drag a table, a view or another object, or a column, from the tree into the SQL editor: its name is typed where you
drop it, in quotes if needed. Databases and folders, on the other hand, are dragged within the tree to organize them
(see [Getting started](01-start.md)).

## Areas

### What an area is

An area is a named group of objects, so you do not have to look at hundreds of tables at once: for example, LAB with
everything for the lab and TURNOS with everything for appointments. An area groups objects by rules: prefix patterns
such as `LAB_*` and individual object names.

- An object can be in several areas.
- Triggers and indexes go with their table.
- Anything that is not in any area goes to **No area**.
- Areas do not touch the database: they are saved in a separate file, which you can share.

### The Areas tab

Open it with **Tools › Database areas…**, with **Tools › Areas…** in the database menu in the tree, or with **Edit
areas…** on the **Areas** node in the tree. There is one per database.

- On the left, the list of areas: the name, the rules and how many objects each one groups. At the end, **No area**.
- On the right, the selected area: **Name**, **Description**, the rules, the exceptions, **Add what uses only its
  tables (procedures, views and triggers)** and **Color:**.
- Below, the preview: what the area groups, with a summary by type (“35 objects: 12 tables · 3 views · 20
  procedures”). You can filter it by name (**Filter by name**, with `*`, `?` and `%`) and by type (**All types** or a
  single one). Each object says whether it came in **by what it uses** and which other areas it is in (“also in
  TURNOS”). Double-click or `Enter` opens the object.
- At the top, the **New area**, **Remove area**, **Suggest by prefix**, **Diagram**, **Reload** and **Save**
  buttons.
- At the very bottom, which file is in use (see [The areas file](05-tree.md#the-areas-file)).

The preview changes as you type, but nothing is saved until you click **Save**. You cannot save an area without a name,
or two areas with the same name. If you close the tab with unsaved changes, it asks first.

### Rules and exceptions

- **Rules: one per line (names or patterns with * and ?)**: an object is included if its name matches any of them. You
  can also separate them with commas or semicolons.
- `*` is anything (including nothing), `?` is one character and `%` works like `*`. Matching is case-insensitive. A
  name without wildcards matches only that object.
- Each rule is compared with the object's name, whatever its type, and for triggers and indexes also with the name of
  their table.
- **Exceptions: left out even if a rule matches**: you write them the same way, and they always win.

For example:

| Rules | Exceptions |
|---|---|
| `LAB_*`, `LABORATORIO_*`, `PACIENTES` | `LAB_TMP_*` |

groups everything that starts with LAB_ or LABORATORIO_ (tables, views, procedures… and the triggers and indexes of
those tables) plus the PACIENTES table, except whatever starts with LAB_TMP_.

### Adding what it uses

With **Add what uses only its tables (procedures, views and triggers)** ticked, a procedure, a view or a trigger is
also included if every table it uses is in the area, according to the database dependencies (system tables do not
count). This helps when procedures do not follow the table prefix.

- A view included this way counts as an area table for the others.
- A trigger also needs its own table to be in the area.
- Exceptions still win.
- In the preview, whatever came in this way says **by what it uses**.

New areas come with this checkbox ticked.

### Suggesting by prefix

**Suggest by prefix** builds one area for each prefix family among the tables that do not have an area yet:

- The prefix is what comes before the first `_`. One-letter prefixes do not count.
- Prefixes that start the same way go together, under the name of the shortest one: LAB_ and LABORATORIO_ make up the
  LAB area, with the rules `LAB_*` and `LABORATORIO_*`.
- A family is suggested when it groups 3 or more tables, unless an area with that name already exists. The largest
  ones come first.
- Each suggestion comes with a description and the [Adding what it uses](05-tree.md#adding-what-it-uses) checkbox
  ticked.

The suggestions are there for you to review: nothing is saved until you click **Save**. If there are none, the status
bar says so.

### Adding or removing by hand

In the preview, select one or more objects and right-click:

- **Open** (with a single object selected).
- **Add to** › an area: adds the name as a rule of that area (or removes the exception that kept it out, if that
  exception was its exact name).
- **Remove from “LAB”**: removes the rule with its name or, if a pattern matches it or it came in by what it uses, adds
  an exception with its name.

To distribute the leftovers, select **No area** on the left, select the objects and use **Add to**. If an exception
with a pattern still keeps an object out, the status bar tells you: change the exceptions.

### Color

**Color:** leaves the area with **No color** or gives it one from the folder palette, without reds (those are kept for
production). The color shows on the area icon in the tree and in the Areas tab, and it travels with the areas file
and with the registrations backup.

### The areas file

The areas of each database are saved in their own JSON file, with no connection details: you can pass it on to other
people. The bottom of the tab shows which file is in use:

- **This PC's file:** the default one, in your user's FbStudio data folder (see
  [Options, shortcuts and privacy](09-options.md)).
- **Shared file:** one you chose, for example in a network folder that the whole team uses.

The buttons at the bottom:

- **Use another file…**: the database switches to that file (the database registration remembers it).
- **Save as…**: saves the areas to another file and the database switches to it. Use it to share them.
- **Back to this PC's file**: only appears when a shared file is in use.

If the file changed since you opened it (someone else saved it), **Save** warns you before overwriting it: **Replace**
discards those changes; to see them first, cancel and use **Reload**. **Reload** reads the file and the database
objects again; if you have unsaved changes, it asks first (**Discard and reload**). **Use another file…** asks too
(**Discard and switch**).

The tree and the search use whatever was read or saved last; the Areas tab reads the file again every time it opens.

The registrations backup (**Export registrations…**) can include the areas of each database: when you import it,
databases that have none yet receive them, and the rest keep their own (see [Getting started](01-start.md)).

### Areas in the tree

When a database has areas, its last node is **Areas**, with the count. Inside it, each area shows its color and how
many objects it groups, and each area lists its objects by type (a type with no objects in the area does not appear).
At the end, **No area**.

The objects in an area work just like in the rest of the tree: double-click, menu and drag. When you save the areas,
the tree rebuilds itself.

Right-click an area: **Area diagram** and **Edit areas…**. On **Areas** or **No area**: **Edit areas…**.

### Where areas are used

- **Search metadata**: the **Area:** filter (see [Searching the metadata](05-tree.md#searching-the-metadata)).
- **Statistics**: **Tick the tables of an area…** (see [Monitoring and performance](06-monitoring.md)).
- **Database script…** and **HTML documentation…**: you can limit them to one area. The script includes its objects,
  the domains they use and their permissions; FKs to tables outside the area are not written, so it runs on an empty
  database (see [Scripts, comparisons and import](08-scripts.md)).
- **Compare databases…**: the rules of the source area are applied to both databases and only what falls inside it is
  compared (see [Scripts, comparisons and import](08-scripts.md)).
- **Diagrams**: the diagram of an area (see [Diagrams](05-tree.md#diagrams)).

## Diagrams

A diagram draws tables and views with their relationships: the FOREIGN KEYs and, if you want, the logical FKs from the
comments. It only reads the database.

### Opening a diagram

- For a table or a view: **Relationship diagram** in its tree menu. It draws the table and its neighbors.
- For an area: **Area diagram** in the tree, or **Diagram** in the Areas tab (with the area as it is currently
  written, even if you have not saved it). It draws the tables and views of the area.
- A saved one: see [Saving and opening](05-tree.md#saving-and-opening).

Each diagram opens in its own tab (“Diagram · TURNOS”, “Diagram · area LAB”); if it was already open, it is selected.

### What it shows

- Each table is a box with its name at the top. Views have a header in a different color.
- Columns show their type and the PK, FK or PK FK mark. The columns that are not shown are summarized (“+ 12
  columns”).
- The arrow points to the referenced table. A dashed line is a logical FK. An FK to the same table is a loop.
- On the right: the summary (“12 tables · 15 links · 2 logical · 3 hidden”), the selected table and the hidden tables.

### Options

| Option | What it does |
|---|---|
| **Neighbors:** | Only in a table diagram: **1 step (neighbors)** or **2 steps** (the neighbors' neighbors too). |
| **Show neighbors outside the area** | Only in an area diagram: adds the tables outside the area that are related to the area's tables, as small boxes without columns. |
| **Hide heavily referenced tables** | See [Heavily referenced tables](05-tree.md#heavily-referenced-tables). |
| **Logical FKs (dashed)** | Also draws the FKs from the comments (see [Logical FKs](05-tree.md#logical-fks)). |
| **Columns:** | **Keys only** (the primary key columns and those that point to another table), **All** or **None**. |
| **Layout:** | **Automatic**, **Layered**, **Force-directed** or **By distance** (see below). |
| **Rearrange** | Lays everything out again. Anything you moved by hand is lost. |
| **Fit** | Fits the whole diagram in view. |
| **Re-read database** | Reads the tables and relationships again, after you change the database structure. |
| **Add table…** | Adds a table or view by name, with its relationships to what is already there. |
| **Path to…** | See [Path to another table](05-tree.md#path-to-another-table). |
| **Save diagram…** | See [Saving and opening](05-tree.md#saving-and-opening). |
| **Copy as Mermaid** and **Export…** | See [Exporting](05-tree.md#exporting). |

The layouts:

- **Layered**: referenced tables at the top and the tables that use them below.
- **Force-directed**: related tables pull together and the rest push apart.
- **By distance**: each table sits near the ones that are a few steps away.
- **Automatic**: layered, unless with more than 8 tables it gets too wide or too tall; in that case, by distance.

### Heavily referenced tables

Some tables are related to almost everything (users, patients…) and would leave everything connected to everything.
With **Hide heavily referenced tables** ticked (the default), tables related to more than 30 tables are not drawn and
the neighbor search does not go through them.

- They are listed on the right, under **Hidden as heavily referenced**, with how many links they have to what is drawn
  and how many neighbors they have in total.
- **Show** draws one of them.
- If the diagram starts from one of them, it is drawn anyway. **Add table…** and **Path to…** also show it when they
  need it.

### Logical FKs

Some databases do not declare all their FOREIGN KEYs and note the relationship in the column comment instead, with the
`#FK_%TABLE,%COLUMN` marker (whatever follows, such as more fields or a closing `#`, does not matter). For example, the
comment `#FK_%PACIENTES,%ID#` on the TURNOS.PACIENTE column says that this column points to PACIENTES.ID. The marker
can sit next to other markers in the same comment.

FbStudio uses them in:

- diagrams, as dashed lines, with **Logical FKs (dashed)** ticked;
- **Where is it used?…** for a column;
- autocomplete after JOIN (see [The SQL editor](02-editor.md));
- the HTML documentation (see [Scripts, comparisons and import](08-scripts.md)).

### Moving, zooming and selecting

- Drag a table to move it; drag the background to pan around the diagram.
- The mouse wheel zooms in and out at the pointer (from 10% to 300%).
- A click selects a table and highlights its lines. On the right you see its columns, what it uses and what uses it,
  with the **Open**, **Add its neighbors**, **Remove from diagram** and **Path to…** buttons. A click on the background
  clears the selection.
- Double-clicking a table opens it in its tab.

Whatever you move by hand stays in place when you add or remove tables, until you click **Rearrange**.

### Path to another table

**Path to…** finds the shortest chain of FKs between two tables, no matter which way each FK points:

1. Select the starting table in the diagram. In a table diagram, if you select none, it starts from that table.
2. Click **Path to…** and type the destination table. It does not have to be drawn.
3. FbStudio adds the tables in between, highlights the path and selects the destination table. The status bar shows
   it: “Path: TURNOS_DET → TURNOS → PACIENTES (2 relationships)”.

If there is another path, it avoids heavily referenced tables; if not, it goes wherever it has to and shows them. It
uses logical FKs only with **Logical FKs (dashed)** ticked. If no chain joins the tables, it tells you. Among equally
short paths, it always picks the same one.

### Saving and opening

**Save diagram…** writes a .fbsdiagram file with the options, the starting table or area, the tables you added and
removed, the heavily referenced tables you showed, the highlighted path and where each table was placed. An area
diagram also saves the area definition as it was.

It does not save the database structure: it is read again when you open the file, so a new table or a changed FK shows
up as it is now.

To open it, use **Tools › Open saved diagram…**: from the menu bar, it opens on the database selected in the tree (or
the tab's database); from the menu of a database in the tree, it opens on that database. You can open it on any
database: if it was saved on a different one, the status bar says so. Opening the same file twice opens two tabs.

### Exporting

**Export…** saves the whole diagram in the format you choose:

| Format | What it is for |
|---|---|
| **PNG image** | Documents and emails. It always comes out with a white background and light colors, at twice the resolution (less if the diagram is huge). |
| **SVG drawing** | An image that scales up without losing quality; it opens in the browser, in draw.io or in Inkscape. |
| **draw.io diagram (editable)** | A .drawio file you can keep editing in draw.io: each table with its columns (mark, name and type), the relationships attached to the tables (logical ones dashed) and the positions they had. When you move a table, its lines follow it. |
| **Mermaid (text for GitHub and wikis)** | `erDiagram` text in a .mmd file, or in a .md file already inside a mermaid code block. GitHub, GitLab and many wikis render it on their own. It does not keep the positions: Mermaid lays it out by itself. |
| **FbStudio diagram** | The same .fbsdiagram file as **Save diagram…**. |

**Copy as Mermaid** puts that text on the clipboard: paste it into a code block marked as `mermaid` in a README, an
issue or a wiki.
