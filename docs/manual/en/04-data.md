# The Data tab

Browse a whole table or view, with filtering and sorting done on the server, and edit it without leaving records
locked: your changes wait in FbStudio and are applied all together.

## Opening the Data tab

Tables and views have a **Data** tab:

- In the tree, right-click a table or a view › **View data (edit without locking)**.
- Or open the table or view (see [The tree, areas and diagrams](05-tree.md)) and pick **Data** among the bottom
  tabs (`Ctrl+7`).

The table is only read when you select the tab. The grid is the same one used for results (see
[Results](03-results.md)): moving around, copying, **View value…**, **View row vertically…**, **Chart…** and `Ctrl+F`
work the same way. This chapter covers what's different.

## Browsing page by page

The tab fetches rows 500 at a time. As you scroll down and get close to the last one, it fetches the next 500 on its
own.

- The footer says how many rows are loaded and, if there are more, **scroll down to fetch more**.
- The line at the bottom says how many rows were read, how long it took and whether the table can be edited (and if
  not, why).
- The button with the circular arrow (**Reload**) reads the table again from the start.

With no other sort order, rows come by primary key. A table without a primary key comes in its physical order, and a
view that doesn't show its table's key comes in no fixed order.

The tab uses its own connection. Each page is read in a short read-only transaction that closes right away: browsing a
large table doesn't hold back the OAT or lock anything.

When possible, the next page is requested "after the last row fetched", by key: every page takes the same time. If the
table has no primary key, or if you sort by a column that allows NULL, pages are requested by position and, on a very
large table, the further down you go, the longer each page takes.

## The additional WHERE

Above the grid is the **Additional WHERE (SQL)** box. Type a condition in SQL and press `Enter`:

```sql
FECHA >= '2026-10-01' AND ESTADO = 1
```

It goes as is into the WHERE of the read, together with the column filters (both must be met). To remove it, clear it
and press `Enter`.

## Filters and sorting on the server

The filter row, each column's filter window and the filter panel are the same as in **Results**, with the same syntax
(see [Filter syntax](03-results.md#filter-syntax)). Clicking a header sorts here too. The difference is that
here the loaded rows aren't filtered or sorted: FbStudio reads the table again with the matching WHERE and ORDER BY, so
the filter and the sort order apply to the whole table.

- With a filter set, the bar at the top says **Filter resolved on the server** and offers **Clear filters** and
  **Filter panel**. Without filters that bar is hidden: open the panel by right-clicking a header ›
  **Filter in the filter panel**.
- Text comparisons ignore case, but accents are compared according to the column's collation: "perez" may not find
  "PÉREZ".
- The list of values to tick isn't loaded automatically, because it scans the table: the filter window shows a **Load
  the values from the server** button, which fetches the 500 most frequent values, taking the other filters into
  account. In a column with date and time they're grouped by day.
- There's no grouping and no totals in the footer.
- `Ctrl+F` only searches the rows already fetched.

## Counting

- **Count** asks the server how many rows match the current filter and WHERE (a `SELECT COUNT(*)`). The line at the
  bottom says **The table has N rows with the current filter** and the footer changes to "N of M rows loaded".
- **Count when filtering**, when checked, counts automatically after each read that has a filter or a WHERE. If
  everything fit in the first page, there's no need to ask the server. The check box is remembered for all tables.

On large tables, counting can take a while.

## How editing works

Editing on this tab doesn't touch the database until you ask it to:

1. You change cells, add rows or mark rows for deletion. Everything stays pending in FbStudio: nothing is written and
   no record is locked.
2. A notice strip says there are unapplied changes, and the summary at the top shows "N row(s) changed · N to
   delete · N new".
3. **Apply** runs everything together in a short transaction: either all the changes go in or none do.

Pending changes aren't lost when you sort, filter, fetch more rows or reload: each row is identified by its primary
key.

> **Note:** the **Commit** button on the toolbar is for editor queries, not for this. Changes on the **Data** tab are
> saved only with **Apply**.

If you close the tab with unapplied changes, FbStudio warns you that they will be lost and asks you to confirm with
**Discard and close**.

## Editing cells

To edit a cell:

- double-click it, or press `F2` or `Enter`;
- or just start typing on it, as in a spreadsheet;
- or right-click › **Edit cell (F2)**.

In the edit box, `Enter` saves and moves down to the next row, `Tab` saves and moves right (`Shift+Tab`, left) and
`Esc` cancels. If what you typed doesn't make sense for the column's type, the box stays open and shows the error.

| Type | How to type it |
| --- | --- |
| Numbers | With the system's decimal separator or a dot, and never with a thousands separator: `1.5` is one and a half |
| DATE | `2026-07-01` or `01/07/2026` (day/month/year) |
| TIMESTAMP | `2026-07-01 14:30` or `01/07/2026 14:30` (with or without seconds) |
| TIME | `14:30` or `14:30:00` |
| BOOLEAN | `true` or `false`, `yes` or `no`, `1` or `0` |
| Text | As is |

A changed cell is shown with a highlighted background and the new value in bold. If you put back the value it had, it
stops being a change.

### Setting NULL

Leaving a cell empty sets it to NULL, except in text columns: empty text is `''`. To set NULL in any column:
right-click › **Set to NULL**.

## New row

With the **New row** button, the `Insert` key or right-click › **New row (Insert)**:

1. An empty row appears at the very top, with a `+` in the row number, and the first editable column opens for
   editing.
2. Fill in what you need. Anything you leave empty is left out of the INSERT, so the default value, the trigger or the
   identity applies. With nothing filled in, an `INSERT … DEFAULT VALUES` is sent.
3. **Apply** saves it.

New rows stay at the top even if you change the sort order or the filter. When applied, they take what the server
returned (the key set by a trigger or an identity, the default values). In a view, the table is read again after the
INSERT.

## Deleting and recovering

- `Del` marks the selected rows for deletion, or unmarks them if they were already marked. Also with right-click ›
  **Mark for deletion (N) (Del)** and **Unmark for deletion**.
- A marked row is shown in red and struck through. It's only deleted when you apply.
- A new row that you delete before applying simply disappears.
- Right-click › **Undo the row's changes** returns the selected rows to how they were read: it drops their changes,
  their deletion mark or, if they're new, removes them.

## Discarding

**Discard** throws away all of the table's pending changes: cells go back to what was read, deletion marks go away and
new rows disappear. The line at the bottom says **Changes discarded.**

## Applying

The **Apply** button says how many changes are waiting: **Apply (3)**. When you press it:

1. If the options say so (the default), it first shows the commands, with any production or deletion warnings at the
   top (see [The command preview](#the-command-preview)). Otherwise, if the database is a production
   one or there are rows to delete, it asks for confirmation.
2. It runs everything in a short transaction: first the DELETEs, then the UPDATEs and finally the INSERTs.

Each DELETE and each UPDATE must find exactly one row by its primary key. In addition, each UPDATE requires the columns
you changed to still hold the values that were read (except blobs and floating-point numbers). If any statement doesn't
find its row, finds more than one, or the server returns an error, everything is rolled back: nothing is saved and the
changes stay pending. The line at the bottom says what happened and on which row:

- **Nothing was saved: row … was changed or deleted by someone else since it was read.** Reload and make the change
  again.
- **Nothing was saved: the key of row … is not unique.**
- **Nothing was saved (everything was undone). Row … failed**, with the server's error. If it's a locked record, it
  says who holds it.

If another transaction has locked a record that needs to change, the write waits up to the time set in
**Options › Queries › Wait for a locked record** and then fails (see
[Options, shortcuts and privacy](09-options.md)).

If it succeeds, it says **Saved and committed** with how many rows were changed, deleted and added. The loaded rows are
updated without reading the table again, so you don't lose your place. Computed columns show their new values when you
reload.

## The command preview

In **Options › Queries**, **When applying changes in the Data tab** has two choices:

- **Show the commands first (run them, copy them or take them to a query tab)**: the default.
- **Run them directly**: **Apply** runs without showing anything (with production or deletions, it still asks for
  confirmation).

Either way, the same thing runs. The **Commands to apply to TABLE** window shows the warnings at the top (production,
deletions) and then the statements with the values written out, for example:

```sql
-- Data changes in TURNOS (FbStudio): 1 UPDATE, 1 DELETE, 0 INSERT
-- Each statement must affect 1 row. If one affects 0, someone else changed or deleted that row since it was read: ROLLBACK and reload.
DELETE FROM TURNOS WHERE ID = 1041;
UPDATE TURNOS SET ESTADO = 2 WHERE ID = 1050 AND ESTADO IS NOT DISTINCT FROM 1;
```

The `IS NOT DISTINCT FROM` is the check that nobody changed the row since it was read. The buttons:

- **Run**: applies it in its short transaction and commits it.
- **Open in a query tab**: takes the statements to a new tab (**Changes to TABLE**), without running them. The changes
  are no longer pending on **Data**: review them, run them there with `F9`, commit with **Commit**, and then **Reload**
  the **Data** tab.
- **Copy**: copies the text to the clipboard.
- **Cancel**: does nothing; the changes stay pending.

**End with COMMIT** adds a `COMMIT;` at the end of the text you copy or take to a query. **Run** always commits, with or
without the check box. FbStudio remembers the check box for next time.

## Generating a script

**Generate script…** opens the same commands window even if **Run them directly** is chosen in the options. Use it to
take the commands for the pending changes with you: copy them, open them in a query tab (with or without **End with
COMMIT**) or run them.

## What can be edited

| What | Editable |
| --- | --- |
| Table with a primary key | Yes |
| Table without a primary key | No: you can see it, but there's no way to tell which row to change |
| View based on a single table that shows its whole primary key | Yes: the changes go to that table through the view |
| Any other view | No: you can see it, but not edit it |
| Computed column (or an expression in a view) | No |
| Binary blob | No (you can view it with **View value…**) |
| Text blob (`BLOB SUB_TYPE TEXT`) | Yes |

When something can't be edited, the line at the bottom says why, and double-clicking that cell opens **View value…**.

## Production and read-only

- **Production:** you can edit, but **Apply** asks for confirmation, stating how many changes there are, on which table
  and on which database. With the preview, the warning appears above the commands.
- **Deletions:** in any environment, if there are rows to delete, **Apply** tells you how many and from which table
  before going on.
- **Read-only registration:** you can view, filter and copy the table, but not edit it (**Read-only profile: data
  cannot be edited.**), and **New row** is disabled.

## Copying rows

**Copy as INSERT** and **Copy as UPDATE** (right-click a cell) always use the table and its primary key, and copy the
rows as you see them: with the unapplied changes. Computed columns are left out. The other ways of copying work the
same as in [Results](03-results.md#copying).

**Export** works on the result of a query, not on this tab: to save a table to a file, run a SELECT and export the
result (see [Exporting](03-results.md#exporting)).

## Remembered columns

On the **Data** tab, FbStudio remembers, for each table of each database, how you left the columns: the order, the
widths and the ones you hid. The next time you open it, it looks the same.

- New columns in the table appear at the end; columns that no longer exist are ignored.
- Right-click a header › **Reset column order and widths** goes back to the table's column order, with widths fitted to
  the content and every column visible.
- The last 500 tables you touched are remembered.
