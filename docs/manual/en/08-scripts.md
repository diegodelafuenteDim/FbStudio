# Scripts, comparisons and import

The tools that work with the whole database or with many rows at once: the database script and its documentation,
comparing two databases (the structure or one table's data), importing a CSV or Excel file and generating test data.

## Database script

Builds the script to recreate the database in an empty database (metadata extraction). You can open it in two ways:

- **Tools › Database script…**, for the database selected in the tree or the current tab's database.
- In the tree, right-click the database › **Script and compare** › **Database script…**.

FbStudio reads the database in a read-only transaction, so it does not block anyone. On a large database that means
thousands of short queries and it can take a while; meanwhile, the status bar shows how many objects it has read, and
**Cancel** stops it.

### What to include

The **Database script** window asks what goes into the script. Everything is ticked by default.

| Checkbox | What it adds |
|---|---|
| **Domains** | The domains. |
| **Generators** | The generators, with their initial value and increment. |
| **with their current value** | Each generator's current value (SET GENERATOR … TO) and that of the identity columns: the copy keeps numbering the same way. |
| **Exceptions** | The exceptions. |
| **Roles** | The roles. |
| **Privileges (GRANT)** | The GRANTs on tables, views, procedures, functions, packages, generators, exceptions, domains and roles. |
| **Tables (with keys, CHECKs and indexes)** | The tables with their computed columns, primary, unique and foreign keys, CHECKs and indexes. |
| **Views** | The views. |
| **Procedures** | The procedures. |
| **Functions (PSQL and UDF)** | PSQL functions and external functions. |
| **Packages** | The packages, with their bodies. |
| **Triggers** | Table, view and database triggers. |

If the database has areas (see [The tree, areas and diagrams](05-tree.md)), a list appears at the top to choose
**The whole database** or **Only area "…"**. With an area, the script contains its objects, the domains they use and
their permissions. FKs to tables outside the area are not written, so the script runs on an empty database; a comment
at the start says how many were left out.

Click **Generate**: the **Database script** tab opens with the script, read-only.

### What the script contains

The script follows an order that runs straight through on an empty database:

1. Roles, domains, generators, exceptions and external functions (UDF).
2. Tables with their primary key and unique keys.
3. The current value of the identity columns, if **with their current value** was ticked. It uses GEN_ID, which works
   the same in Firebird 3, 4 and 5.
4. Procedures, functions and packages with only their signature, so it does not matter which one uses which.
5. Views, each one after the views it uses.
6. Computed columns, put in place.
7. Foreign keys, CHECKs and indexes.
8. The bodies of procedures, functions and packages.
9. Triggers, comments and permissions.

Each section ends with COMMIT, and PSQL blocks are wrapped in SET TERM. The header says which database the script came
from and when.

> **Note:** DDL permissions (create, alter or drop a kind of object) are not written; a comment at the end says how
> many were left out, so you can review them by hand. Permissions granted to procedures, triggers, views or functions
> that no longer exist are not included either.

### Saving, copying or running it on another database

The tab's toolbar has:

- **Save as…**: saves the script to a .sql file (UTF-8).
- **Copy**: copies it to the clipboard.
- **Open in a tab without database**: takes it to a query tab that has no database. In the tree, double-click the
  **empty** database where you want to run it, then run it with `F9`, or with `Ctrl+F9` to run it as a script without
  a grid (see [The SQL editor](02-editor.md)).
- **HTML documentation…**: the documentation of what was read (see [HTML documentation](08-scripts.md#html-documentation)).
- **Generate again…**: choose again what to include and read the database again.

Next to them, "Generated at 16:50 · 4,123 objects" tells you how recent what you see is.

> **Note:** the script is meant for an empty database. On the same database it would fail or overwrite objects.

To create an empty database directly on the server, see **Empty database** in
[Maintenance and permissions](07-maintenance.md).

## HTML documentation

Generates a single HTML file with the database documentation, to read in any browser, with no internet connection.
You can open it in two ways:

- In the tree, right-click the database › **Script and compare** › **HTML documentation…**. The same window as for the
  script appears (what to include and the area), the database is read, and then it goes on with the documentation.
- With the **HTML documentation…** button on the **Database script** tab. It uses what was already read, without
  reading the database again.

FbStudio asks whether to include the code:

- **Without source**: the structure and the descriptions.
- **With source**: also adds, folded under each object, the PSQL code of procedures, functions and triggers. On a large
  database the file gets much bigger.

Then you choose where to save it (it suggests a name with the database name and the date), and it opens in the browser.

What the HTML contains:

- On the left, an index by object type, with a search box that filters by name.
- At the top, when it was generated and, if you chose one, the area.
- Each table with its columns (type, whether it accepts NULL, default value and description), the primary key marked,
  each FK as a link to the referenced table (including the logical ones from "#FK_" comments), its keys and
  constraints, the tables that reference it, its indexes and its triggers.
- Views with their columns and their SELECT, procedures with their input and output parameters, functions with their
  parameters and what they return, packages with their header, and domains, generators and exceptions (with their
  message).

The documentation follows what you chose in the script window: the area and the checkboxes.

## Comparing databases

Compares the structure of two databases and builds the script that makes the target match the source. You can open it
in two ways:

- **Tools › Compare databases…**.
- In the tree, right-click the database › **Script and compare** › **Compare with another database…**: that database
  becomes the source.

You need two registered databases.

### Source, target and options

In the **Compare databases** window:

- **Source (the model, e.g. development):** the database with the correct structure.
- **Target (the one to make equal, e.g. production):** the one that will be changed. FbStudio suggests a production
  database, if there is one.
- If the source has areas, you can choose **Only area "…"**. The source area's rules apply to both databases, and only
  what falls inside the area is compared, without the FKs to tables outside it.

The options:

- **Also compare privileges (GRANT)**: ticked by default.
- **Keep columns in the same order as in the source**: ticked by default. Adds ALTER … POSITION where the order
  differs.
- **Drop in the target what is not in the source (objects, columns, indexes)**: unticked by default. Without it,
  anything extra in the target shows in the list but is not dropped. With it, extra permissions are also removed
  (REVOKE).

Click **Compare**. FbStudio reads both databases at once in read-only transactions: it changes nothing. The script is
built afterwards, from what you tick, and you run it by hand.

### The list of differences

The **Compare** tab shows "development → production" at the top and a summary such as "12 differences: 3 new, 8
different, 1 extra". On the left there is one difference per row: the checkbox, the type (table, index, view,
procedure, permission…), the name with the first detail, and a mark:

| Mark | What it means | What the script does |
|---|---|---|
| **new** (green) | It is in the source and not in the target. | Creates it. |
| **different** (amber) | It is in both, with a different definition. | Alters it. |
| **extra** (red) | It is only in the target. | Drops it, only with the drop option. |

Extra items without the drop option have nothing to run: you can see them, but you cannot tick them. **Tick all** and
**Untick all** change all the checkboxes.

When you choose a difference, the **Difference** tab on the right shows:

- The details of what changed, from what the target has to what the source has: "column OBSERVACIONES:
  VARCHAR(100) → VARCHAR(200)", "column FECHA: becomes NOT NULL", "column order", "different comment".
- The DDL of both sides, **In the source** and **In the target**, side by side. If the object is in both, the lines
  that change are marked: in green what the source adds and in red what the target loses. The side where the object is
  missing says "(not there)".

### The sync script

1. Tick what you want to bring to the target.
2. Click **Sync script**: the script appears in the **Sync script** tab on the right.
3. Review it and click **Open in a target tab**: it goes to a query tab of the target, without running.
4. Run it with `F9`. Each stage ends with COMMIT. On production, FbStudio asks for confirmation before running it.

**Save as…** saves the script to a .sql file.

What the script is like:

- It goes in stages, in the same order as the database script: first what is dropped, then domains, tables,
  signatures, views, constraints, indexes, bodies, triggers, comments and permissions, with COMMIT between stages.
- Tables are altered column by column: they are never recreated and the data is not touched. Procedures, functions,
  views and triggers use CREATE OR ALTER.
- Constraints are compared by their definition, not by their name: the INTEG_n names the server assigns change from
  one database to another.
- What Firebird cannot change with an ALTER is left as a comment, for you to do by hand: a domain's collation, a column
  that goes from computed to regular (or the other way round) and a different identity.

> **Note:** if you change the checkboxes after building the script, the script is cleared: click **Sync script**
> again. **Compare again** reads both databases again with the same options.

> **Note:** if the target is a read-only registration, you can view and save the script, but not run it there: the
> registration blocks DDL.

## Comparing data with another database

Compares a table's rows in two databases, by primary key, and builds the script that makes the target's data match.
It is meant for small tables: configuration, types, code lists (for example, PRECIOS).

In the tree, right-click the table › **Compare data with another database…**. It appears when more than one database
is registered. The table's database is the source (the model). The **Data of PRECIOS** tab opens:

1. In **Target (the one made equal):**, choose the other database. FbStudio suggests a production database if the
   source is not one.
2. Click **Compare**. Both tables are read at once, each over its own read-only connection: nothing is written to
   either one.

### The differences

At the top, the summary: how many rows are missing in the target, how many are different, how many are extra and how
many are equal. If there are no differences, it says "They are equal". The list shows each row's key ("12", or "12 · 3"
with a multi-column key) and its mark:

| Mark | What the script does |
|---|---|
| **Missing in target** (green) | Inserts it (INSERT). |
| **Different** (amber) | Updates only the columns that change (UPDATE … WHERE by key). Under the row you see each one, from what the target has to what the source has: "IMPORTE: 100.00 → 120.00". |
| **Extra in target** (red) | Deletes it (DELETE) only if you tick **Delete extra rows**; otherwise the script leaves a comment saying how many there are. |

The columns present in both databases are compared, except computed ones. A column that is only in the source is not
compared, and one that is only in the target is not touched; the summary names them. Text is compared without trailing
spaces, and numbers by value, even if the types differ (INTEGER and BIGINT).

### The script

The script appears on the right and is rebuilt automatically when you tick or untick **Delete extra rows**. It has no
COMMIT.

1. Click **Open in a target tab**: the script goes to a query of the target, without running.
2. Review it and run it with `F9`.
3. Commit by hand with Commit (`Ctrl+Alt+C`), or undo with Rollback (`Ctrl+Alt+R`).

Changing the target does not change a script that was already built: click **Compare** again.

### Limits

- The table must have a primary key: without one, there is no way to tell which row is which.
- In the target, the table must exist and have all the primary key columns.
- With more than 100,000 rows in either database, it does not compare: the rows are compared in memory.

## Importing CSV or Excel

Loads a CSV or Excel (.xlsx) file into an existing table, or into a new table built from the file. You open it from the
tree:

- Right-click a table › **Import data (CSV or Excel)…**: into that table.
- Right-click the database or **Tables** › **Import into a new table (CSV or Excel)…**.

### The file

Click **Choose file…** and pick a .csv, .txt or .xlsx file.

- For a CSV: **Separator** (**Automatic**, `;`, `,`, **Tab** or `|`) and **Encoding** (**Automatic**,
  **UTF-8** or **Windows-1252 (Excel, Western)**). In automatic mode, the separator is the one that appears most often
  in the first line, and the encoding is UTF-8 if the file is valid UTF-8, and Windows-1252 otherwise. A quoted value
  can contain the separator and line breaks.
- For an Excel file: **Sheet**, to choose which one.
- **The first row has the column names**: untick it if the file has no header; the columns are then named "Column 1",
  "Column 2"…

On the right you see the **Preview (first 100 rows of the file)**.

> **Note:** an Excel file with a huge amount of text (more than 50 million characters) is rejected with a warning:
> split it into smaller files or save it as CSV.

### Into an existing table

On the left, **Which file column goes to each table column**: each table column (with its type, NOT NULL and PK) has a
list with the file's columns and **(do not import)**. Columns with the same name are matched automatically, ignoring
case, accents, spaces and underscores: "Fecha alta" goes to FECHA_ALTA. Computed columns do not appear, because they
are not written.

How the values read are converted:

- In a date column, a date in the PC's format ("10/22/2026") or an Excel date number is converted.
- In a numeric column, the PC's decimal separator (such as a comma) becomes a point.
- In a BOOLEAN column, yes/no, true/false and 1/0 are accepted.
- Everything else goes as text and Firebird converts it, or reports the row where it could not.

The options:

- **Empty cells as NULL**: ticked by default.
- **If a row fails, stop and undo everything**, or **If a row fails, skip it and go on**.
- **Empty the table before importing**: deletes all the rows first, in the same transaction. To confirm, you have to
  type the table name. If the import fails or is cancelled, the rows come back.

Click **Import N rows**. Everything goes in a single transaction, with no partial commits:

- With "stop", the first row that fails undoes everything and nothing is saved.
- With "skip it", the rest is saved at the end and the rows with errors are listed below, with their line in the file
  ("Line 15: …").
- **Cancel** undoes everything.

### Updating by key

Choose **Update the ones already there (by key) and add the rest** instead of **Add the rows**. Each row is written
with UPDATE OR INSERT … MATCHING: a row that already has that key is updated with the file's values, and one that does
not is added.

The key is made of the columns ticked to the left of each table column; by default, the primary key. At least one key
column must be mapped to the file. This mode cannot be combined with
**Empty the table before importing**.

### Into a new table

FbStudio reads the whole file and builds a table with one column for each column in the file.

- **New table:** the name comes from the file name, in uppercase, without accents, with "_" instead of spaces and
  symbols, and up to 31 characters long: "Precios 2026.xlsx" gives PRECIOS_2026.
- **Its columns: the name and type of each one (untick the ones to leave out)**: each name comes from the header in the
  same way, with no repeats, and the type comes from what the column contains.

| What the column contains | Proposed type |
|---|---|
| Yes/no cells from an Excel file | BOOLEAN |
| Only whole numbers | INTEGER, or BIGINT if they do not fit |
| Numbers with decimals | NUMERIC(18,n) with at least 2 decimals, or DOUBLE PRECISION if they do not fit |
| Dates (2026-10-22, or in the PC's format) | DATE, or TIMESTAMP if any of them has a time |
| Text | VARCHAR with the length rounded up (10, 20, 50, 100, 255, or in steps of 500 up to 8,000), or BLOB SUB_TYPE TEXT if longer |
| Nothing (empty column) | VARCHAR(100) |

A number with leading zeros ("00123") stays as text, so it keeps its zeros.

Before creating the table you can change its name and each column's name and type (you type them by hand), or untick
the columns to leave out. Under each one you can see which file column it comes from. FbStudio warns you if the table
name is missing, if a column has no name or no type, if a name is repeated or if an object with that name already
exists.

Click **Create the table and import N rows**:

- The table is created without a primary key (you can add one later) and is committed before importing.
- If the import is not saved (a row fails with "stop", or you cancel), the new table is dropped again: no half-done
  empty table is left behind.
- If it goes well, the tree shows the table and the tab becomes the import tab for that table.

### Production and read-only

- On a production database, before importing FbStudio asks for confirmation, with the number of rows, the table and
  the database: "You are about to insert 1,250 rows into PACIENTES in 'production', a PRODUCTION database." (or "You
  are about to create table…").
- **Empty the table before importing** deletes all the table's rows: that is why it asks you to type the table name,
  also outside production.
- With a read-only registration you cannot import: **Import data (CSV or Excel)…** is disabled,
  **Import into a new table (CSV or Excel)…** does not appear, and the tab tells you so.

## Generating test data

Fills a table with made-up rows to test screens, queries or indexes. In the tree, right-click the table
› **Generate test data…**.

> **Note:** it only appears on **Development** and **Test** databases, with a registration that is not read-only. No
> data is generated on production.

The **Test data · TURNOS** tab opens. At the top:

- **Rows:** how many to add, from 1 to 100,000.
- **Empty in optional columns:** what percentage of rows (0, 10, 25 or 50%) gets NULL in the columns that accept it.
- **Seed:** the same seed gives the same data, so you can repeat a test. After adding, it goes up by itself.

### What goes in each column

In **What goes in each column**, each column (with its type, NOT NULL, PK and FK) has a list of what to put in it.
FbStudio suggests what fits the column's type and name, ignoring case and accents, and you can change it.

| What goes in | What it generates | Suggested for |
|---|---|---|
| **Numbered (from the highest)** | Consecutive numbers starting from the highest one already in the table, plus one. | An integer primary key. |
| **A value of the referenced table** | One of the values that already exist in the table the FK points to. | Columns with a single-column FK. |
| **First name**, **Last name**, **Full name** | Made-up first and last names; the full one is "First Last" ("Last, First" with the Spanish interface). | Names containing NOMBRE or APELLIDO; the full one, names containing NOMBRE_COMPLETO, APELLIDO_Y_NOMBRE, RAZON_SOCIAL, PACIENTE, CLIENTE, PERSONA or CONTACTO. |
| **Email** | A made-up email address at the example.com domain. | Names containing MAIL or EMAIL. |
| **Phone** | A made-up phone number. | Names containing TELEFONO, CELULAR, MOVIL or TEL. |
| **ID number** | An 8-digit number. | Names containing DNI or DOCUMENTO; in a text column, also CUIT or CUIL. |
| **Address** | A street name with a number. | Names containing DIRECCION, DOMICILIO or CALLE. |
| **City** | A city. | Names containing CIUDAD, LOCALIDAD or PROVINCIA. |
| **Code** | Random letters and digits, up to 8 characters. | Names containing CODIGO or starting with COD, and text columns of up to 2 characters. |
| **Text** | A short phrase. | Any other text column. |
| **Integer**, **Decimal number** | A random number that fits the type. | Integer and decimal types. |
| **Date (last year)**, **Date and time (last year)**, **Time** | Dates from the last year; times between 7:00 and 21:00. | DATE, TIMESTAMP and TIME. |
| **Yes or no** | True or false. | BOOLEAN. |
| **(left out)** | Nothing: the column stays NULL or gets its DEFAULT. | Computed columns and types that are not generated, such as a binary BLOB. |

Names, streets and texts come out in Spanish if the interface is in Spanish, and in English otherwise. A text value
never exceeds the column length.

- **Numbered (from the highest)** looks up the highest existing value, so it does not collide with the existing rows.
- **A value of the referenced table** picks at random among up to 1,000 distinct values from the referenced table. If
  the column is required and that table is empty, FbStudio warns you and generates nothing.

### Preview and add

1. Click **Preview**: the grid on the right shows the first 20 rows. Nothing is written yet.
2. Click **Add N rows** and confirm.

Everything goes in one transaction. A row that does not fit (for example, because of a unique index) is skipped and
the rest go on; the first rows with errors are shown below. **Cancel** undoes everything.
