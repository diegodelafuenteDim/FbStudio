# The SQL editor

How to write and run SQL in FbStudio: query tabs, running statements and scripts, transactions, the safety net and the
editor helpers (autocomplete, templates, formatting, search). The grid has its own chapter:
[Results](03-results.md).

## Query tabs

Each query tab has its own database, its own connection and its own transaction: what you do in one never mixes with
the others. The title shows the query name and the database ("Query 3 · Test"), and the bottom border takes the color
of that database's environment. The strip at the top always shows the database of the selected tab, which is where
everything you type runs (see [Getting started](01-start.md)).

### New tab

- `Ctrl+T`, the **+** button to the right of the tabs, or **Query › New tab**.
- It opens on the database of whatever you touched last: if you selected a database in the tree after visiting the
  tab, that one; otherwise, the database of the selected tab. With no tabs open, the one selected in the tree. If no
  database is selected, nothing opens and the status bar tells you so.
- From the tree: right-click a database › **New query on this database**.
- Double-clicking a database in the tree connects it and uses it in the selected query tab, if that tab is free. If the
  tab has an open transaction or is running, the database opens in a new tab, and the previous tab keeps its database
  and its transaction.

### Switching and closing

- `Ctrl+Tab` moves to the next tab and `Ctrl+Shift+Tab` to the previous one, even with the cursor in the editor.
- To close a tab: `Ctrl+W` (or `Ctrl+F4`), the tab's close button, a middle click on it, or **Query › Close tab**.
- Right-click a tab: **Close**, **Close all but this one**, **Close the ones to the right** and **Close all**.
- A tab that is running can't be closed: cancel first with `Esc`. If it has an open transaction or unsaved changes, it
  asks first (see [Closing or exiting](#closing-or-exiting)).

### Rename, duplicate and reopen

In the tab menu (right-click):

- **Rename…** gives it another name ("Query 3" → "Today's appointments"). Only for queries that don't come from a file
  (those take the file name). The name is remembered when you close FbStudio.
- **Duplicate** opens a new query on the same database with the same text (without the file, if it had one).
- **Reopen closed tab** (`Ctrl+Shift+T`) reopens the last query you closed, with its database and its text (or its
  file). Repeat it to go further back: it remembers up to 20 while FbStudio is open. Empty queries aren't remembered.

### Queries from last time

When you close FbStudio, the query tabs are saved and reopen next time, each with its database, its name and its text,
and with the same tab selected. The text is stored encrypted, like the history. A query from a .sql file is read again
from the file: anything you didn't save is lost (FbStudio warns you when you exit). Empty queries aren't saved.

To turn this off, go to **Tools › Options… › Editor** and clear **On close, remember the open queries and reopen
them**.

## .sql files

- **Query › Open .sql file…** (`Ctrl+O`) opens one or more .sql or .txt files. If a file is already open, it switches
  to its tab. Otherwise it loads the file into the selected query when that query is empty, or into a new tab on the
  database of whatever you touched last.
- **Query › Save** (`Ctrl+S`) saves to the query's file; if the query doesn't have one yet, it asks where. **Save
  as…** (`Ctrl+Shift+S`) always asks.
- **Query › Open recent** shows the last 10 files you opened or saved; hover over one to see where it is. If a file no
  longer exists, it is removed from the list.
- The tab takes the file name and marks unsaved changes with "*". If you close it, or exit FbStudio, with unsaved
  changes, it asks first.
- The file's encoding is kept (UTF-8 or Windows-1252). If you typed characters that Windows-1252 doesn't have, the file
  is saved as UTF-8 and the status bar tells you. A new file is saved as UTF-8.

## Running

| Shortcut | Menu | What it runs |
|---|---|---|
| `F9` (or `F5`) | **Query › Execute (all or the selection)** | The selection; without a selection, the whole editor. |
| `Ctrl+Enter` | **Query › Execute the statement at the cursor** | Only the statement where the cursor is (or the selection). |
| `Ctrl+F9` | **Query › Run as script (no grid)** | The selection or the whole editor, as a script without the grid. |
| | **Query › Run script from file…** | A whole .sql file, without opening it in the editor. |
| `Ctrl+Shift+E` | **Query › Show plan without running** | Nothing: it only shows the plan. |

The **Execute** button on the toolbar does the same as `F9`.

### F9: all or the selection

- With text selected, it runs the selection. Without a selection, it runs the whole editor.
- A single statement is a normal execution: the rows go to **Results** and the messages to **Messages**.
- Several statements run as a script: in order, in the same transaction, stopping at the first error (or continuing,
  see [Scripts](#scripts)). A `COMMIT` or `ROLLBACK` written in the script runs at that point.
- Statements are separated by `;`. FbStudio understands `SET TERM` (isql scripts) and also PSQL blocks without
  `SET TERM`: the `;` inside a `CREATE PROCEDURE … BEGIN … END` or an `EXECUTE BLOCK` don't split it.
- If Firebird reports the line and column of the error, the cursor jumps there.
- When it didn't run the whole editor, the status bar makes it clear: "only the selection was executed (lines 4 to 9)".
- In the tab of a procedure, trigger, view or function, `F9` without a selection compiles it (see
  [The tree, areas and diagrams](05-tree.md)).

### Ctrl+Enter: the statement at the cursor

`Ctrl+Enter` runs only the statement where the cursor is, even if the editor holds many: you don't need to select it.
If text is selected, it runs the selection. The status bar makes it clear: "only the statement at the cursor was
executed (line 3)".

```sql
SELECT * FROM TURNOS WHERE FECHA = CURRENT_DATE;

UPDATE TURNOS SET ESTADO = 'C' WHERE ID = 1234;
```

With the cursor on the UPDATE, `Ctrl+Enter` runs only the UPDATE; `F9` without a selection runs both.

### Scripts

Several statements together run as a script (with `F9`, with `Ctrl+F9` or from a file):

- Before the first statement is sent, the whole script is checked: if anything is blocked, nothing runs, and if
  anything needs confirmation you are asked once, with the line of each case (see
  [The safety net](#the-safety-net)).
- The write transaction opens with the first statement that modifies something and stays open until a `COMMIT` or
  `ROLLBACK` in the script, or until you press **Commit** or **Rollback**.
- On an error, the script stops. To have it go on with the next statement, check **Query › Continue on errors in
  scripts** (the same option as **On an error in a script**, in **Tools › Options… › Queries**). A statement that fails
  is undone on its own: everything before it and the transaction stay in place. When the script ends, the cursor goes
  to the first error.
- The log goes to **Messages**: a summary at the top and one line per statement with its line number ("✓ line 3 ·
  UPDATE: 12 rows affected", "✗ line 7 · INSERT: …").
- With `F9` (up to 200 statements), each statement goes into the history and the grid shows the last SELECT, with its
  plan and its statistics.
- Parameters aren't requested in a script (see [Parameters](#parameters)).

### Scripts without the grid

`Ctrl+F9` (**Query › Run as script (no grid)**) runs the selection or the whole editor as a script. It's meant for long
scripts, such as thousands of INSERT or UPDATE statements:

- It doesn't fetch the plan or the per-table reads of each statement, so each one takes less time.
- The grid stays as it was: for a SELECT, only the rows are counted.
- The history stores the whole script once, not one entry per statement.
- Progress shows in the status bar: "Script: 1,200 of 5,000 (24 %) · 0 with errors · Esc cancels".
- With more than 500 statements, the log shows one line per statement type and lists only the errors.

`F9` also switches to this mode when the script has more than 200 statements, and tells you so: "no grid: more than
200 statements".

### Run script from file

**Query › Run script from file…** runs a .sql file of any size without loading it into the editor. It works like
`Ctrl+F9`: the same safety check before starting, stop or continue on errors, and the log in **Messages** with the
file's line numbers. The editor cursor doesn't move.

It runs in the selected query tab, if that tab has a database and isn't running; otherwise, in a new query on the
database of whatever you touched last. The history stores the file name and how many statements it had, not its
contents.

### Cancel

`Esc`, the **Cancel** button on the toolbar or **Query › Cancel** stop whatever is running. For a single statement, the
rows that already arrived stay in the grid, and the status bar says how many there are and how long the cancellation
took. In a script, the current statement is stopped and the remaining ones don't run.

While it waits for the first row, the status bar shows the elapsed time ("Executing SELECT… 12 s · Esc cancels") and,
if the plan reads a whole table without an index, it warns you: "⚠ full scan of TURNOS (no index): it may take a while".

If the execution had opened the write transaction and it is cancelled or fails, that transaction is rolled back. If the
transaction was already open before, it stays open with what it had.

### Row limit

A SELECT fetches at most the number of rows set in the **Limit** selector on the toolbar: 1,000, 10,000, 100,000 or
1,000,000. When the limit is reached, the status bar says so: "limit of 10,000 reached (raise it to fetch more)".

The value FbStudio starts with is set in **Tools › Options… › Queries** (**Rows a SELECT fetches**; 10,000 by default).
The toolbar selector changes it only until you close the application. To export every row, with no limit, see
[Results](03-results.md).

## Transactions

Each tab works with its own transaction:

- A SELECT with no open transaction runs in a read-only one that closes on its own.
- Anything that writes (INSERT, UPDATE, DELETE, MERGE, EXECUTE PROCEDURE, EXECUTE BLOCK and DDL, such as CREATE, ALTER
  or DROP) opens a write transaction and leaves it open until you commit or roll it back. DDL also stays pending until
  the Commit.
- While it's open, everything you run in that tab runs inside it: a SELECT sees your uncommitted changes. Other tabs
  and other connections don't see them until the Commit.
- With a transaction open, the tab doesn't change database: double-clicking another database in the tree opens a new
  tab.

### Commit and Rollback

- **Commit** saves the changes: the **Commit** button on the toolbar, **Query › Commit** or `Ctrl+Alt+C`.
- **Rollback** undoes them: the **Rollback** button, **Query › Rollback** or `Ctrl+Alt+R`.
- You can also type `COMMIT;` or `ROLLBACK;` in the editor and run it.
- `SET TRANSACTION`, `SAVEPOINT` and `RELEASE` aren't supported: FbStudio doesn't run them.

### Autocommit and isolation

On the toolbar, next to **Rollback**, you'll find the **Autocommit** check box and the isolation list. They apply to
the selected tab and can only be changed on **Development** and **Test** databases, with no open transaction.

- **Autocommit**: each statement that writes commits on its own when it finishes successfully, and the status bar says
  "committed (autocommit)". If the Commit fails, the statement is rolled back and you see the error.
- The isolation applies to the next write transaction:
  - **Read committed** (the default) sees what others commit.
  - **Snapshot** sees the data as it was when the transaction started.
  - **Snapshot table stability** works like snapshot, and also locks the tables it reads.

> **Note:** on production databases and read-only registrations it's always an explicit Commit and read committed. The
> check box and the list are disabled, and the connection enforces this anyway.

### The open transaction

While a write transaction is open:

- the tab shows a red dot (●) before its title;
- to the right of the database strip you can see how long it has been open: "Write transaction OPEN for 12 min: Commit
  or Rollback";
- the status bar adds "transaction OPEN (Commit/Rollback)" after each execution.

By default, the warning on the strip turns amber after 5 minutes and red after 15, with a separate notice (shown once).
You set the minutes in **Tools › Options… › Queries**: **Open write transaction: amber after** and **…and red, with a
notice, after**.

> **Note:** a forgotten transaction holds up the server's garbage collection for everyone. Commit it or roll it back as
> soon as you're done.

### Closing or exiting

- When you close a tab with an open transaction, FbStudio asks: **Close and undo** rolls it back and closes the tab;
  **Don't close** leaves it as it is.
- When you close several tabs at once (with **Close all**, for example), it asks only once and names the tabs with
  uncommitted work.
- When you exit FbStudio with open transactions, the **Open transactions** window lists them: **Exit and undo** rolls
  them all back and exits; **Don't exit** takes you back.
- Disconnecting a database closes all its tabs, and it also asks first if any of them has an open transaction.

### Locks

If another transaction has locked the record you want to modify, the write waits (up to 10 seconds by default) and then
fails, instead of leaving the tab hanging. You can change the time in **Tools › Options… ›
Queries** (**Wait for a locked record**); it applies to transactions opened from then on.

Along with the error, FbStudio tells you who holds the record:

- If it's another FbStudio tab: "the record is held by the tab “Query 1” (transaction 4521): commit or roll it back".
- If it's another connection: the transaction number, the user, the address, the program and the time it was opened.
  If FbStudio can't read that information from the server, it gives only the transaction number (to see which one it
  is, you need the RDB$ADMIN role).

To see that connection and, if needed, cut it off: [Monitoring and performance](06-monitoring.md).

## The safety net

Before sending each statement to the server, FbStudio checks it against the tab's database: its environment and
whether the registration is read-only (both are chosen when you register the database, see
[Getting started](01-start.md)). Risky statements ask for confirmation and forbidden ones are blocked.

| Situation | What FbStudio does |
|---|---|
| Read-only registration and a statement that writes: INSERT, UPDATE, DELETE, MERGE, DDL, EXECUTE PROCEDURE or EXECUTE BLOCK | Blocks it. |
| Read-only registration and `NEXT VALUE FOR` or `GEN_ID` with a step other than 0 | Blocks it: moving a generator can't be undone. To read its value, use `GEN_ID(generator, 0)`. |
| `SET TRANSACTION`, `SAVEPOINT` or `RELEASE`, on any database | Blocks it: use Commit and Rollback. |
| Production database and a statement that writes | Asks for confirmation. |
| UPDATE or DELETE without WHERE, on any database | Asks for confirmation: it affects every row in the table. |
| A MERGE with `WHEN MATCHED THEN DELETE`, or an EXECUTE BLOCK with an UPDATE or DELETE without WHERE | Asks for confirmation. |
| Production database and a SELECT that uses procedures or functions, inside an open write transaction | Asks for confirmation: if they modify data, those changes stay in the transaction and are saved with the Commit. |

- The confirmation is the **Confirm execution** window, which lists the reasons: **Run anyway** goes ahead and **Don't
  run** sends nothing.
- Blocked statements never reach the server: the status bar says "Blocked: …" and the reason is logged in **Messages**.
- In a script, everything is checked before it starts: if even one statement is blocked, none of them run, and the
  confirmations are combined into a single question with the line of each one.
- On read-only registrations, the connection checks each statement again before sending it.
- The Data tab has its own confirmations: see [The Data tab](04-data.md).

> **Note:** this is a safety net in the tool: it doesn't replace the database's permissions.

## Parameters

In a SELECT, INSERT, UPDATE, DELETE, MERGE, UPDATE OR INSERT or EXECUTE PROCEDURE you can use `?` and `:name` instead
of values:

```sql
SELECT t.ID, t.FECHA, p.APELLIDO
FROM TURNOS t
JOIN PACIENTES p ON p.ID = t.PACIENTE
WHERE t.FECHA = :FECHA
  AND t.MEDICO = :MEDICO
```

- When you run it, the **Query parameters** window asks for a value for each name (each `?` shows up as "Parameter 1",
  "Parameter 2"…). The **NULL** check box sends a NULL.
- The values are sent separately, not pasted into the SQL. A name used more than once gets the same value.
- Type text without quotes; dates as 22/10/2026 (day/month/year) or 2026-10-22, also with a time (22/10/2026 14:30);
  numbers with a comma or a dot. Anything else goes as text and Firebird converts it to the column type: "00123" stays
  "00123" in a VARCHAR.
- FbStudio remembers the last value of each name while it's open.
- Parameters aren't looked for inside strings, quoted names or comments, nor in DDL or PSQL (procedures, triggers,
  EXECUTE BLOCK), where `:variable` is a variable.
- They are only requested when you run a single statement. In a script there would be no way to know which statement
  each value belongs to: select the statement, or put the cursor on it and use `Ctrl+Enter`.
- The history stores the statement as you wrote it, with its `:name` placeholders.

## Autocomplete

The list opens on its own when you type two letters or a dot, and with `Ctrl+Space` at any time. `Enter` or `Tab`
insert the highlighted item and `Esc` closes the list. Names get quotes only when they need them (lowercase, spaces or
reserved words).

It only looks at the statement where the cursor is. Names come from the database metadata, which is read in the
background the first time (until then, the list offers only keywords). A CREATE, ALTER or DROP run from the editor
reloads it; if something changed from somewhere else, right-click the database › **Reload metadata**.

### What it suggests

| Where the cursor is | What it suggests |
|---|---|
| After `FROM` or `JOIN` | Tables, views, WITH queries, procedures and packages. |
| After `UPDATE`, `INTO` or `TABLE` | Tables and views. |
| After `alias.` or `TABLE.` | The columns of that table or view, of a subquery with an alias (`FROM (SELECT …) x`), of a WITH query or of a selectable procedure (its output parameters), including a packaged one (`PKG.PROC(…) alias`). |
| After `PACKAGE.` | The package's procedures and functions. |
| Anywhere else (SELECT list, WHERE, ORDER BY…) | First the columns of whatever is in the FROM, without typing the alias; then keywords, functions, packages, tables and procedures. |
| After `GEN_ID(`, `NEXT VALUE FOR`, `SEQUENCE` or `GENERATOR` | Generators. |
| After `EXCEPTION` | Exceptions. |
| After `PROCEDURE`, `FUNCTION` or `PACKAGE` | Procedures, functions or packages. |

Each item says what it is: "(table, 12 columns)", "(WITH query)", "(procedure)", "(function, in PKG)"… The columns of
a subquery or a WITH come from its SELECT list:

```sql
WITH ULTIMOS AS (
  SELECT PACIENTE, MAX(FECHA) AS ULTIMA
  FROM TURNOS
  GROUP BY PACIENTE
)
SELECT p.APELLIDO, u.ULTIMA
FROM PACIENTES p
JOIN ULTIMOS u ON u.PACIENTE = p.ID
```

When you type `u.`, it suggests PACIENTE and ULTIMA.

### JOIN by FK

After `JOIN`, the list first shows the tables related by an FK to the ones already in the FROM, with a free alias and
the ON clause already written. Logical FKs count too: the ones written in a column comment with "#FK_".

```sql
SELECT *
FROM TURNOS_DET td
JOIN TURNOS t ON t.ID = td.TURNO
```

Here, when you type `JOIN`, "TURNOS t ON t.ID = td.TURNO" appears, with the FK name next to it.

- The alias is made of the table's initials in lowercase (TURNOS_DET → td), plus a number if it's already in use.
- The tables for the last table in the FROM come first: the ones it points to, then the ones that point to it.
- Tables already in the FROM go last: you may need the same table again, through another FK.

### Parameter hints

Inside the parentheses of a database procedure or function, a small popup shows its signature, with the parameter
you're on in bold, and whether it's a procedure or a function. It opens when you type `(` or `,`, follows the cursor
(moving to the next parameter at each comma) and goes away when you leave the parentheses. It also works with packaged
ones (`PKG.PROC(`) and with an `EXECUTE PROCEDURE` written without parentheses
(`EXECUTE PROCEDURE P_CERRAR_TURNO 1234, 'C'`).

## Templates

A template is a piece of text you insert with an abbreviation: type `eb`, press `Tab`, and you get the skeleton of an
EXECUTE BLOCK with the cursor in the body. `Ctrl+J` (or **Query › Insert template…**) shows the list to pick from;
whatever you've already typed of the abbreviation filters it. The template takes the indentation of the line.

`Tab` inserts the template when no text is selected and the autocomplete list isn't open. If the list is open, `Tab`
inserts the highlighted item instead: close the list with `Esc` and press `Tab`, or use `Ctrl+J`.

| Abbreviation | Template |
|---|---|
| `sel` | SELECT * FROM |
| `self` | SELECT FIRST 100 * FROM |
| `selc` | SELECT COUNT(*) FROM |
| `selw` | SELECT * FROM … WHERE |
| `ins` | INSERT INTO … VALUES |
| `upd` | UPDATE … SET … WHERE |
| `del` | DELETE FROM … WHERE |
| `uoi` | UPDATE OR INSERT … MATCHING |
| `gen` | Generator value |
| `eb` | EXECUTE BLOCK |
| `proc` | CREATE OR ALTER PROCEDURE |
| `func` | CREATE OR ALTER FUNCTION |
| `trg` | CREATE OR ALTER TRIGGER |
| `tab` | CREATE TABLE |

You change them in **Tools › Options… › Templates**: **New** adds one, **Delete** removes the selected one and **The
defaults** brings back the original ones. Each template has an **Abbreviation**, a **Description** and a **Text**; in
the text, `${cursor}` marks where the cursor ends up. For example, the abbreviation `today` with the text
`SELECT * FROM TURNOS WHERE FECHA = CURRENT_DATE AND ${cursor}`.

## Formatting SQL

**Query › Format SQL** (`Ctrl+Shift+L`) tidies up the selection or, without a selection, the whole editor:

- In queries (SELECT, WITH, INSERT, UPDATE, DELETE, MERGE and UPDATE OR INSERT), each clause goes on its own line, the
  WHERE conditions one per line, a long list one item per line, a long CASE with one WHEN per line, and subqueries
  indented.
- In procedures, functions, triggers and EXECUTE BLOCK, the body gets one statement per line, indented by BEGIN … END,
  IF … ELSE, WHILE … DO and FOR SELECT … DO, with the queries inside formatted too. The header stays as you wrote it.
  If it finds something it doesn't recognize, it leaves that block as it is: it never changes what the code does.
- The rest of the DDL, `SET TERM` lines, strings, quoted names and comments stay as they were.
- Keywords become uppercase; to turn this off, go to **Tools › Options… › Editor** and clear **When formatting,
  keywords in uppercase**.
- `Ctrl+Z` undoes it.

Before:

```sql
select t.ID, p.APELLIDO, count(*) from TURNOS t left outer join PACIENTES p on p.ID=t.PACIENTE where t.FECHA between '2026-01-01' and '2026-01-31' and t.ESTADO<>'X' group by t.ID, p.APELLIDO
```

After:

```sql
SELECT t.ID, p.APELLIDO, COUNT(*)
FROM TURNOS t
LEFT OUTER JOIN PACIENTES p ON p.ID = t.PACIENTE
WHERE t.FECHA BETWEEN '2026-01-01' AND '2026-01-31'
  AND t.ESTADO <> 'X'
GROUP BY t.ID, p.APELLIDO
```

## Opening an object

`Ctrl+click` on a name, or `F12` with the cursor on it (**Query › Open the object (Ctrl+click)**), opens the object in
its own tab, with its DDL. While you hold down `Ctrl`, the pointer turns into a hand over names.

- An alias (`t` or `t.ID`) takes you to its table.
- A column without an alias takes you to the table in the FROM that has it.
- Anything else is looked up by name in the database: table, view, procedure, function, package, generator, exception,
  trigger, index, domain or role.

What you can do in an object's tab: [The tree, areas and diagrams](05-tree.md).

## Find and replace

`Ctrl+F` (**Query › Find in the editor…**) opens the search bar above the editor, and `Ctrl+H` (**Query ›
Replace…**) opens it with the replace row. If you had text selected on a single line, that's what it searches for.

- All matches are highlighted and the bar tells you which one you're on: "3 of 12".
- `Enter` or `F3` go to the next one; `Shift+Enter` or `Shift+F3`, to the previous one. `F3` also opens the bar if it's
  closed.
- The `Aa` (**Match case**), `ab|` (**Whole word only**) and `.*` (**Regular expression**) buttons change how it
  searches. With a regular expression, `$1`, `$2`… in the replacement are its groups.
- **Replace** (or `Enter` in the replace box) changes the current match and moves to the next one. **Replace all**
  changes them all, and a single `Ctrl+Z` reverts them.
- `Tab` moves from the search box to the replace box and back. `Esc` closes the bar.

With the grid selected, `Ctrl+F` searches the results instead (see [Results](03-results.md)).

## Editing text

These commands are in **Query › Edit text**. Each one is undone with a single `Ctrl+Z`.

| Command | Shortcut | What it does |
|---|---|---|
| **Comment or uncomment lines (Ctrl+/)** | `Ctrl+/` | Adds "-- " to the selected lines (or the cursor's line); if they were all commented already, it removes it. On a Spanish keyboard it's the "-" key. |
| **Duplicate line or selection** | `Ctrl+D` | Duplicates the selection or, without one, the cursor's line. |
| **Move line up** / **Move line down** | `Alt+↑` / `Alt+↓` | Moves the selected lines (or the cursor's line). |
| **Make uppercase** / **Make lowercase** | `Ctrl+Shift+U` / `Ctrl+U` | The selection or, without one, the word at the cursor. |
| **Go to line…** | `Ctrl+G` | Asks for a line number and moves the cursor there. |

## Folding blocks

Multi-line blocks (BEGIN … END, CASE … END, parentheses and `/* … */` comments) have a marker in the margin to fold
them: the first and last lines stay visible and the inside is hidden. The markers are updated half a second after you
stop typing. To turn this off, go to **Tools › Options… › Editor** and clear **Fold blocks (BEGIN…END, CASE,
parentheses and long comments) with margin markers**.

## Font zoom

`Ctrl+wheel` makes the editor font bigger or smaller, and `Ctrl+0` resets it to the size set in **Tools ›
Options… › Editor** (**Font size**). You can turn off wheel zoom on that same tab.

## Show the plan without running

`Ctrl+Shift+E` (**Query › Show plan without running**) prepares the statement at the cursor, or the selection, and
shows its plan on the **Plan** tab, without fetching rows or changing anything. If it reads a whole table without an
index, the status bar warns you. Use it to check a heavy query before you run it on production.

- It works for SELECT, INSERT, UPDATE, DELETE, MERGE and EXECUTE.
- With parameters, it doesn't ask for values: the plan doesn't depend on them.
- If the tab has an open transaction, the statement is prepared inside it; otherwise, in a read-only transaction that
  is then discarded.

## The bottom tabs

Below the editor there are five tabs. You pick one with a click, from the **View** menu or with `Ctrl+1` to `Ctrl+5`.
In an object's tab, **Dependencies** (`Ctrl+6`) also appears and, for tables and views, **Data** (`Ctrl+7`, see
[The Data tab](04-data.md)).

### Results

The grid shows the rows of the last SELECT. The first ones appear right away and the rest keep arriving while the
status bar counts them ("Receiving… 25,000 rows · 1.8 s"); when it's done, it says how many rows it fetched and how long
it took. Sorting, filtering, grouping, totals, copying, viewing values, charts and exporting: [Results](03-results.md).

### Plan

The plan of the last statement, as Firebird reports it: the detailed one (Firebird 3 or newer) or the classic one. A
script without the grid doesn't fetch plans.

### Statistics

What the last statement cost:

- the times: prepare, until the first row, and total;
- rows fetched and affected, fetches, page reads and writes;
- the reads per table, indexed and non-indexed; a table read without an index is marked "⚠ sequential read (no
  index)".

The reads per table come from the server's monitoring tables; if they can't be read, the list says "(no data)". A
script without the grid doesn't fetch statistics.

### History

The statements you ran, newest first (up to 500): date and time, database, statement type, duration, rows, an ✗ if it
failed, and the first line. Hover over one to see the whole SQL.

- The box at the top searches the SQL and the errors.
- **Only this tab's database** (checked by default) shows only the statements of the tab's database; unchecked, those
  of every database.
- Double-clicking a statement loads it into the editor: it replaces the tab's text.
- It's stored encrypted. How long it's kept is set in **Tools › Options… › History** (**Keep statements for**; 90 days
  by default); when you shorten the period, the oldest entries are deleted right away.
- You can turn it off in each database's registration, with **Keep statement history** (see
  [Getting started](01-start.md)).
- A password typed in the editor (`CREATE USER … PASSWORD '…'`) doesn't end up in the history.
- Without a system keyring (for example, on Linux without a desktop) the history isn't saved.

### Messages

Full error messages, what a statement without rows did ("UPDATE: 12 rows affected in 35 ms"), the script log and the
reason a statement was blocked. When there are messages, **Save log…** saves them to a text file with the date and the
database (without the password), and **Copy** copies them.

## Dragging from the tree

Drag a table, a view or another object, or a column, from the tree into the editor: its name is written where you drop
it, with quotes if needed. Also:

- double-clicking a column in the tree types its name at the cursor;
- right-click an object or a column › **Type the name into the query** or **Copy the name**.

Databases and folders can be dragged too, but within the tree, to arrange them: see
[The tree, areas and diagrams](05-tree.md).
