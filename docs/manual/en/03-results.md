# Results

The grid where the output of a query appears: moving around, filtering, sorting, grouping, totals, copying, viewing a
whole value, charting and exporting.

## The grid

When you run a SELECT, the rows appear on the **Results** tab, below the editor (`Ctrl+1` selects it). The same grid is
used on a table's **Data** tab, with a few differences described in [The Data tab](04-data.md).

- At the top is the grid bar: the grouping panel and the **Filter panel** button. With filters set, **Clear filters**
  appears, and with groups, **Expand all** and **Collapse all**.
- Below the headers is the **Filter** row.
- On the left, the `#` column numbers the rows.
- At the bottom is the footer: on the left, how many rows are visible and, below each column, its total if you chose
  one.

The grid works on the rows already fetched, up to the **Limit** on the toolbar (see [The SQL editor](02-editor.md)).
Filtering, sorting or grouping doesn't query the database again: if the limit was reached, rows that weren't fetched
are left out. To filter a whole table, put the condition in the query's WHERE or use the **Data** tab, which filters on
the server.

## Moving around and selecting

Clicking a cell selects it, along with its row. With the keyboard:

| Key | What it does |
| --- | --- |
| `↑` `↓` `←` `→` | Moves to the next cell |
| `PgUp` / `PgDn` | Moves up or down one screen |
| `Home` / `End` | First or last column of the row |
| `Ctrl+Home` / `Ctrl+End` | First or last cell of the grid |
| `Shift+↑` / `Shift+↓` (also with `PgUp` and `PgDn`) | Selects a range of rows |
| `Shift+click` | Selects the rows between the current one and the clicked one |
| `Ctrl+click` | Adds a row to the selection or removes it |
| `Ctrl+A` | Selects all rows |

The mouse wheel scrolls up and down; with `Shift`, sideways. Left and right follow the order in which the columns are
shown, even if you moved them, and skip hidden ones.

## The filter row

Each column has its own box in the **Filter** row, below the header:

1. Click the column's box.
2. Type the filter (see [Filter syntax](#filter-syntax)). It's applied as soon as you stop
   typing.
3. `Enter` confirms it and takes you back to the grid.

An empty box shows an example for its type: `>100, <=5, =3` for numbers, `2026-07-01` for dates and
`text, PÉREZ*, null` for everything else. Filters on several columns add up: a row has to meet all of them.

With any filter set, the bar at the top says **Filtered: N of M rows** and **Clear filters** appears, which removes
them all. To remove just one, delete its text or right-click the header › **Remove this column's filter**.

If the text doesn't make sense for the column's type (for example `>abc` in a numeric column), the filter turns red,
doesn't filter anything, and the bar at the top explains why.

## Filter syntax

What you type in the filter row, what you type in the filter panel and what each column's filter window builds are
the same text. It depends on the column's type.

### Text

| You type | Shows the rows that… |
| --- | --- |
| `perez` | contain "perez", ignoring case and accents (it finds "PÉREZ") |
| `GAR*` | start with "GAR" |
| `*EZ` | end with "EZ" |
| `*AR*` | contain "AR" (`*` and `%` stand for any number of characters) |
| `!perez` | don't contain "perez" (rows with NULL are included too) |
| `=GARCIA` | are equal to "GARCIA", ignoring case and accents |
| `<>GARCIA` or `!=GARCIA` | are different from "GARCIA" |
| `>M`, `<F` | come after "M" or before "F" in alphabetical order |

### Numbers

| You type | Shows the rows that… |
| --- | --- |
| `1500` | equal 1500 |
| `>100`, `>=100`, `<5`, `<=5` | are greater than, greater than or equal to, less than, less than or equal to |
| `=3`, `<>0` | are equal or different |
| `10..20` | are between 10 and 20, both included |

The decimal separator can be a comma or a dot. If you type a comma, the comma is the decimal separator and the dot
separates thousands: `1.234,5` is 1234.5. So don't use a comma as a thousands separator: type `1234.5`.

### Dates

These work for DATE and TIMESTAMP columns.

| You type | Shows the rows that… |
| --- | --- |
| `2026-07-01` or `01/07/2026` | are from that day (day/month/year; in a column with a time, the whole day) |
| `2026-07-01 08:30` | are from that exact moment |
| `>=2026-07-01`, `<2026-08-01` | are from that date on, or before it |
| `<=2026-07-05`, `>2026-07-05` | are up to the 5th (that whole day included) or from the 6th on |
| `2026-07-01..2026-07-31` | are between the two days, both whole days included |
| `today`, `yesterday`, `tomorrow` | are from that day |
| `this week`, `last week` | are from that week (weeks start on Monday) |
| `this month`, `last month` | are from that month |
| `this year`, `last year` | are from that year |
| `last 7 days` | are from the last 7 days, today included (any number works) |

The Spanish words work too (`hoy`, `este mes`, `últimos 7 días`…), with or without accents.

### Times

| You type | Shows the rows that… |
| --- | --- |
| `08:30` | have that time |
| `>=08:00`, `<12:00` | are from that time on, or before it |
| `08:00..12:00` | are between the two times, both included |

### BOOLEAN

`yes` or `no`. `true`, `false`, `1` and `0` work too (a single `y` doesn't).

### Multiple values and NULL

These work for any column type:

| You type | Shows the rows that… |
| --- | --- |
| `1;5;9` | for numbers, equal 1, 5 or 9 (separate the values with `;`) |
| `perez;gomez` | for text, contain either of the two |
| `=PENDIENTE;CONFIRMADO` | are equal to any of them (the operator in front applies to the values that don't have their own) |
| `null` | are NULL |
| `!null` | have a value |

## The filter window

Each box in the filter row has a `▾` on the right. It opens a small window where you build the filter without typing
it, with options for the column's type. You can also open it by right-clicking the header › **Filter…**.

| Type | Options |
| --- | --- |
| Text | contains, does not contain, starts with, ends with, equals, is not, empty (NULL), has a value |
| Numbers | equals, is not, greater than, greater than or equal to, less than, less than or equal to, between, empty (NULL), has a value |
| Dates | is the day, in the period, between, from, until (inclusive), before, after, any day but, empty (NULL), has a value |
| Times | equals, between, from, until (inclusive), empty (NULL), has a value |
| BOOLEAN | yes, no, empty (NULL), has a value |

- The **in the period** option offers today, yesterday, this week, last week, this month, last month, this year, last
  7 days and last 30 days.
- For dates, the calendar button (**Pick the day on a calendar**) types the day you pick.
- Below, **Values (tick one or more)** lists the column's values with how many rows each one has, taking the filters on
  the other columns into account. In a column with date and time they're grouped by day (**Days (tick one or
  more)**). The 500 most frequent are shown, NULL appears as **(empty)**, and with more than ten values you also get
  **Search a value**. If you tick values, they take precedence over the option above.
- At the bottom you can see how it will look in the filter row (**In the filter row: …**), with the error if there is
  one.

**Apply** (or `Enter` in a box) sets the filter, **Remove filter** clears it and **Cancel** (or `Esc`) closes without
changing anything. Since the result is the same text as in the filter row, you can fix it by hand afterwards.

BLOB columns don't list values.

## The filter panel

With many columns, the filter panel shows them all in a list, each with its filter. Use it to find a column by name and
to see all the filters set at once, even those off screen.

- Open it with the **Filter panel** button on the bar at the top, or by right-clicking a header › **Filter in the filter
  panel** (which puts the cursor on that column).
- It appears to the right of the grid. Its title says how many columns have a filter.
- **Find a column** narrows the list by name, and **Only filtered columns** leaves only the filtered ones.
- Each column shows its type (and **hidden** if you hid it), and its name is in bold if it has a filter.
- Each column's box works like the filter row: `Enter` (or leaving the box) applies it and `Esc` restores what was
  there. The `▾` opens that column's filter window.
- **Clear all** removes every filter.

Close it with the × at the top (**Close the filter panel**) or with the same **Filter panel** button.

## Sorting

- Click a header: ascending order. Click again: descending. `Ctrl+click`: removes the sorting.
- You can also right-click the header › **Sort ascending**, **Sort descending** or **Remove sorting**.

The `▲` or `▼` arrow in the header marks the sorted column. Sorting follows the type (numbers as numbers and dates as
dates) and runs in the background: with many rows, the bar at the top says **Processing…** in the meantime.

## Grouping

Grouping gathers the rows that have the same value in a column. It's only available in **Results**.

1. Drag a header onto the panel at the top (it says "Drag a header here…"), or right-click the header › **Group by this
   column**.
2. Each group shows the column, the value and how many rows it has. Groups start collapsed: click a group (or press
   `Enter` or `Space`) to expand or collapse it.
3. **Expand all** and **Collapse all** expand or collapse every group.

You can group by several columns: each one adds a level inside the previous one. Each grouped column appears in the
panel at the top with an ×: click it to remove that grouping (or use **Ungroup this column** on the header). Groups
follow the sort order: if you sort a grouped column in descending order, its groups are reversed.

## Totals

The grid footer shows one total per column:

1. Right-click the footer, below the column.
2. Choose **Count**, **Sum**, **Average**, **Minimum** or **Maximum** (**No total** removes it).

**Sum** and **Average** are only offered for numeric columns. Totals are calculated over the visible rows (with the
filters applied) and don't count NULLs. They're shown with a prefix: `n =` for the count, `Σ` for the sum, `avg.`,
`min.` and `max.`.

Totals are only available in **Results**: on the **Data** tab, the footer says how many rows were loaded.

## Columns

Everything about columns only affects the view: it doesn't change the query, and sorting, filters and totals stay with
their column.

### Reordering

Drag a header to another position: a line marks where it will go and, near the edges, the grid scrolls by itself.

### Width

- Drag the right edge of a header.
- Double-click the edge, or right-click › **Fit width to content**, to fit it to what's visible.

When a result arrives, each width adjusts by itself to the header and the first rows.

### Hiding and showing

- Right-click the header › **Hide this column**. The last visible column can't be hidden.
- **Show hidden columns (N)** lists the hidden ones so you can show them one at a time, or all of them with **Show
  all**.

A hidden column still filters, sorts and keeps its total, and it's still included in **Copy as INSERT**, **Copy as
UPDATE** and exports. It isn't copied as text and the keyboard skips it.

### Restoring

Right-click a header › **Reset column order and widths**: it goes back to the query's column order, with widths fitted
to the content and every column visible.

In **Results**, each execution starts with the columns as they come. On the **Data** tab, the order, the widths and the
hidden columns are remembered for each table (see [Remembered columns](04-data.md#remembered-columns)).

## Copying

From the keyboard, or by right-clicking a cell or a header:

| What | Where | Copies |
| --- | --- | --- |
| **Copy cell** | `Ctrl+C`, cell | The value as shown |
| **Copy row with headers** | `Ctrl+Shift+C`, cell | The row, with the column names |
| **Copy the N selected rows with headers** | `Ctrl+C` with several rows selected, cell | The selected rows |
| **Copy all rows (N) with headers** | Cell | All visible rows, in their order |
| **Copy column (N values)** | Header | The column's values in the visible rows, one per line |
| **Copy row as Markdown table** | Cell | The row (or the selected ones) as a Markdown table |
| **Copy as «COLUMN IN (…)»** | Header | The values of the visible rows as an IN condition |
| **Copy COLUMN of the selected rows as IN (…)** | Cell | The same, only with the selected rows |
| **Copy as INSERT** / **Copy as UPDATE** | Cell | The selected rows as statements |

### As text

Rows are copied separated by tabs, ready to paste into a spreadsheet: NULL is left empty, numbers use the system's
decimal separator, and values lose any tabs and line breaks they contain. Columns go in the order you see them,
without the hidden ones.

With more than 100,000 rows, FbStudio asks first (**Copy anyway** or **Cancel**): for that many rows, exporting is the
better choice.

### As a Markdown table

For pasting into a ticket, a chat or a document: a header, one row per line, numbers right-aligned and the columns in
the order you see them. With several rows selected, the menu says **Copy the N rows as Markdown table**.

### As «COLUMN IN (…)»

It builds a condition to paste into another query, for example `ID IN (1041, 1050, 1063)`:

- without repeated values or NULLs;
- with Firebird literals (strings in quotes, dates as `DATE '2026-07-01'`);
- with more than 1,500 values, split into several INs joined with OR.

From the header it takes the visible rows; from a cell, only the selected rows (the menu shows the column name and how
many rows there are).

### As INSERT or UPDATE

The selected rows, one statement per row, without COMMIT, with the values as Firebird literals. Columns go in the order
you see them, hidden ones included.

- The table is the one in the query's FROM. If it can't be recognized, the INSERT goes to a table called `TABLE_NAME`,
  which you have to change after pasting.
- Columns that don't belong to that table, or that are computed, are left out, and the status bar says which ones.
- **Copy as UPDATE** needs the table's primary key, and the result must include all of its columns: it builds
  `UPDATE … SET` with the other columns and `WHERE` with the key.

On the **Data** tab, the table and its key are always known.

## Filtering by a value

Right-click a cell:

- **Filter by this value** keeps only the rows with that value in the column (it types `=value` in the filter row, or
  `null` if the cell is NULL).
- **Exclude this value** removes them (it types `<>value`, or `!null` if the cell is NULL). Rows with NULL in that
  column are left out too.

## Viewing the value

A cell shows a single line and cuts off whatever doesn't fit. To see the whole value: right-click › **View value…**, or
double-click the cell (on **Data**, double-clicking edits; use the menu there, except on cells that can't be edited).

The **Value of COLUMN** window opens. At the top it shows the type, what the value contains and its size, and lets you
choose how to view it:

| Content | Ways to view it |
| --- | --- |
| Long text | **Text** |
| JSON or XML | **Indented** and **Text** |
| RTF | **RTF text** (without formatting) and **RTF source** |
| PNG, JPEG, GIF or BMP image | **Image** and **Hexadecimal** |
| Other binary (PDF, ZIP or unknown) | **Hexadecimal** |
| Text stored in a binary blob | The text, plus **Hexadecimal** |

- **Wrap lines** wraps long lines to the width of the window.
- **Copy** copies the text you're viewing.
- **Save to file…** saves the value as is: the bytes of a binary or the text in UTF-8, with the extension that matches
  its content (`.png`, `.pdf`, `.json`, `.rtf`, `.txt`…).

The hexadecimal view shows up to 64 KB; **Save to file…** saves the whole value. The window is view-only (it doesn't
edit anything) and you can have several open at once.

## Viewing the row vertically

For wide tables with many columns: right-click a cell › **View row vertically…**. A window opens with one line per
column: **Column**, **Type** and **Value**.

- It's a grid like this one: you can filter and sort it, for example to find a column by name.
- `◀` and `▶` move to the previous or next row among those visible in the grid (with its filters and sort order).
- Columns go in the order you see them, without the hidden ones.

## Searching the results

With the grid selected (click it), `Ctrl+F` opens **Search in results…** at the top right. In the editor, `Ctrl+F` still
searches the text.

- It searches the text of the cells as shown, ignoring case and accents. NULL matches nothing.
- It goes through the visible rows (with the filters) and, in each row, the columns in their order, without the hidden
  ones.
- It jumps to the first match from the selected cell and shows **3 of 27**, or **No matches**.
- `Enter` (or `F3`, or `▼`) goes to the next match and `Shift+Enter` (or `▲`) to the previous one; at the end it starts
  over.
- `Esc` (or the ×) closes the search.

With more than 100,000 matches it stops searching and the counter shows a `+`.

## Chart

A quick chart of what you see: right-click a cell › **Chart…**, or right-click a header (that column becomes the label
or the value, depending on its type).

- **Labels (X):** the column on the horizontal axis.
- **Value (Y, sum):** a numeric column, summed for each label, or **(row count)**.
- **Bars** or **Line**: if the labels are dates or times, it starts with a line.

It uses the rows visible in the grid, with its filters. If the labels are numbers or dates, they're sorted; otherwise,
they stay in the order they appear. More than 500 different labels are cut off (with a warning). Hover to see each
value, and **Save PNG…** saves the chart as an image.

## Exporting

Exporting saves the result of the tab's query to a file. It's in the **Query** menu and on the **Export** button on the
toolbar:

| **Query** menu | **Export** button | File |
| --- | --- | --- |
| **Export to CSV…** | **CSV for Excel (; and decimal comma)** | `.csv` |
| **Export to Excel (.xlsx)…** | **Excel (.xlsx)** | `.xlsx` |
| **Export as INSERT (.sql)…** | **INSERT script (.sql)** | `.sql` |
| **Export to JSON…** | **JSON (.json)** | `.json` |
| **Export to Markdown…** | **Markdown (.md)** | `.md` |

> **Note:** every loaded row and every column is exported, in the query's order. The grid's filters, sorting, groups
> and hidden columns don't apply. To export less, adjust the query.

### Each format

- **CSV:** separated by `;` and with a decimal comma, the way Excel opens it with Spanish regional settings; in UTF-8
  with BOM (so accented characters display correctly) and with a header. NULL is left empty, numbers have no thousands
  separator, dates are in ISO format and binaries in hexadecimal. Text that starts with `=`, `+`, `-` or `@` gets a `'`
  in front so Excel doesn't treat it as a formula.
- **Excel:** numbers as numbers and dates as dates, the header in bold, with a filter and frozen when you scroll. Past
  the maximum number of rows in an Excel sheet, it continues on another sheet.
- **INSERT script:** first it asks for the target table (it suggests the one in the FROM). It writes one INSERT per row,
  with a COMMIT every 1,000 rows and at the end; you can run it in FbStudio or in another Firebird tool.
- **JSON:** a list of objects, one per row, keyed by column name (a repeated name gets `_2`, `_3`…). Numbers and
  BOOLEANs go as such, dates in ISO 8601, binaries in base64 and NULL as `null`.
- **Markdown:** a table, with numbers right-aligned.

### All or only what's loaded

If the SELECT hit the row **Limit** or you cancelled it, the grid doesn't have everything. When you export, FbStudio
asks:

- **Export ALL (runs the query again)**: runs the SELECT again on the server, with no limit, and writes the rows to the
  file as they arrive (without loading them into memory). Meanwhile it counts the rows over another connection, to show
  the progress and how much is left.
- **Only the N loaded**: exports what's already in the grid.

The status bar shows the progress and, when it's done, how many rows were written and where. **Cancel** (or `Esc`)
stops the export, and the file is left incomplete.
