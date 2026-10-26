# Monitoring and performance

How to see what is happening on the server: live connections, transactions and locks (MON$), the statement trace with
its per-query analysis, the database statistics (like gstat) and the server details.

## How to open them

All these tools work on a registered database and open their own connection: you do not need to connect it in the
tree first, but the registration must have its password saved. Each tool opens one tab per database; if that tab is
already open, it is selected.

| Tool | Main menu | Right-click the database in the tree |
|---|---|---|
| Monitoring | **Tools › Monitoring (connections and transactions)** | **Tools › Monitoring (connections and transactions)** |
| Trace | **Tools › Trace (live statements)…** | **Tools › Trace (live statements)** |
| Statistics | **Tools › Database statistics** | **Tools › Statistics** |
| Server details | — | **Tools › Server details…** |

The main menu uses the database selected in the tree or, if there is none, the database of the current tab. In these
tabs, `F9` does the most common action: in the monitor it refreshes, and in the statistics it reads the header again.

> **Note:** FbStudio is tested with Firebird 3.x. If you connect to another version, it warns you when you connect:
> some metadata reads may fail.

## Monitoring

The **Monitoring** tab shows the database transaction counters at the top and, below them, a query on the MON$ tables:
connections, transactions, statements or locks. It always opens on **Transactions**.

Each refresh is a new transaction that is committed right away. MON$ is a snapshot taken when the transaction starts:
this way you always see the latest state, and the monitor never holds back the OAT it is watching.

### The counters

| Card | What it shows |
|---|---|
| **Next transaction** | The number the next transaction to start will get. |
| **OAT (oldest active)** | The Oldest Active Transaction and, below it, who holds it: user, IP, computer and program. A click opens the **Transactions** query. |
| **Gap (next − OAT)** | How many transactions there have been since the OAT. If it keeps growing, someone has had a transaction open for a long time. |
| **OAT open for** | How long ago the OAT started, based on the server clock. It says “not visible” if your user cannot see that transaction. |
| **OIT** | The Oldest Interesting Transaction: the oldest one that did not finish cleanly (active, rolled back or in limbo). |
| **OST** | The Oldest Snapshot: how far garbage can be collected. |
| **Waiting on a lock** | How many statements are waiting for a record locked by another transaction, and how long the longest one has been waiting. Amber if there is any; red if any has been waiting 30 s or more. A click opens the **Locks** query. |
| **Now** | How many connections, transactions and running statements there are. |
| **Server** | The server type: Classic, SuperClassic or SuperServer (see [Server details](#server-details)). It is detected on the first refresh; the tooltip explains what it means and how it was detected. |

### The OAT and its warnings

There is always an OAT while there are open transactions; what matters is how long it lasts. While it stays open,
Firebird cannot clean up old record versions (the garbage), and the gap grows.

When the OAT has been open longer than the limits in the options, the **OAT (oldest active)**,
**Gap (next − OAT)** and **OAT open for** cards turn amber and, past the second limit, red. A warning also appears
with the transaction number, how long it has been open, how many transactions have started since and who holds it. In
the **Connections** query that connection gets the note “⚠ holds the OAT”, and in **Transactions** the transaction
gets “⚠ is the OAT”.

You change the limits in **Tools › Options…**, **Monitoring** section (see
[Options, shortcuts and privacy](09-options.md)):

- **OAT in amber if open for more than**: 2 minutes by default.
- **and in red if open for more than**: 10 minutes by default.
- **Do not alarm if the OAT belongs to a backup or a sweep (gbak, gfix, nbackup): it is reported without color**:
  ticked by default.

A gbak backup or a sweep keeps a transaction open while it works. With the last option ticked, if the OAT belongs to
gbak, gfix, nbackup or an internal server process, nothing is colored: a separate line says it is expected while it
lasts.

### Which connections you see

Without the RDB$ADMIN role, without being SYSDBA and without being the database owner, MON$ only shows your own user's
connections. The tab tells you: “You only see your own connections…”. To see everyone's (and be able to kill them),
connect with the RDB$ADMIN role, as SYSDBA or as the user who owns the database.

If the database was left in nbackup backup mode, the monitor also warns you on every refresh (see
[nbackup](07-maintenance.md#nbackup)).

### Automatic refresh

- **Refresh** (or `F9`) reads again right now.
- The list next to it chooses the automatic refresh: **Manual**, **Every 2 s**, **Every 5 s** (the default),
  **Every 10 s** or **Every 30 s**. It only refreshes while the tab is visible: if you switch to another tab, it stops.
- Next to it you see the time of the last refresh: “updated 10:42:07”.
- With the same query, each refresh keeps the grid's sort order, filters, column widths and selected row.

> **Note:** on a Classic server, each refresh asks every server process for its data, one per connection. That is why,
> on Classic, the automatic refresh starts at every 10 s (unless you chose another interval), and if you set it below
> 10 s the tab warns you that with many connections it loads the server.

### Killing a connection

1. Select a row in the grid that has the connection number. All four built-in queries work, as do your own queries
   that return that column.
2. Click **Kill connection**.
3. Read the confirmation and click **Kill connection** or **Don't kill**.

FbStudio kills the connection with `DELETE FROM MON$ATTACHMENTS`: if it had an open transaction, that transaction is
rolled back, and the program gets an error.

The confirmation shows the user, address, computer and program of that connection, read from the server at that
moment, not from the grid. It also warns you if it is an FbStudio connection on this PC (that tab will lose its
connection) and if the database is a production one. If the connection was already gone, it says so: “no longer
existed (or is not visible without RDB$ADMIN)”.

> **Note:** the monitor cannot kill its own connection. With a read-only registration, **Kill connection** and
> **Cancel statement** are disabled.

### Cancelling a statement

To stop a heavy query without killing the connection, select its row in **Running statements** (or a “waits for a
record” row in **Locks**) and click **Cancel statement**. The confirmation shows the user and the beginning of the
SQL, and warns you if the database is a production one; you answer with **Cancel statement** or **Don't cancel**.

FbStudio uses `DELETE FROM MON$STATEMENTS`: the connection stays open, and whoever was running the statement gets an
operation cancelled error.

## Monitoring queries

On the left of the tab is the list of queries, in two groups: **Built-in** (the ones that come with FbStudio) and
**Custom (N)**. The selected one runs on every refresh and shows in the grid; hover over a query to see its SQL. The
grid sorts and filters like any other (see [Results](03-results.md)).

When the selected row includes SQL (in **Running statements** and **Locks**), the full text appears below the grid,
so you can read or copy it.

### Built-in queries

| Query | What it shows |
|---|---|
| **Connections** | One row per connection, excluding system ones: number, **Note** (“holds the OAT”, “this monitor”), server PID, state (active or idle), address, computer, remote PID, since when, program and user. |
| **Transactions** | One row per transaction, oldest first: connection, **Note** (“is the OAT”, “this monitor”), transaction number, since when and how many seconds it has been running, address, computer, program, state, and the Top, OIT and OAT it saw when it started. |
| **Running statements** | The ones that are running or waiting for fetch, excluding the monitor's own: number, connection, transaction, user, IP, computer, seconds, non-indexed and indexed reads, and the SQL. |
| **Locks** | Who is waiting for a locked record (“waits for a record”) and who might be holding it (“possible holder”), longest wait first. |

Firebird 3 does not say which transaction holds a lock on a record. That is why, in **Locks**, FbStudio suggests as
possible holders the write transactions with changes that started before the wait.

### Custom queries

Below the list there are five buttons; right-clicking the list offers the same actions.

| Button | What it does |
|---|---|
| **+** | New custom query: it starts with a sample SELECT on MON$ATTACHMENTS. |
| Copy | Clones the selected query as a custom one. It works with the built-in ones, so you can start from one of them. |
| Pencil | Edits the selected custom query (double-click works too). |
| Trash can | Deletes the selected custom query, after asking. |
| Share | Exports or imports a file of queries (see [Sharing queries](#sharing-queries)). |

The query window has:

- **Name**.
- The SQL: it must be a single SELECT, because the monitor runs it every few seconds in a read-only transaction.
- **Test**: runs it with the monitor's connection and tells you how many rows it returns and with which columns.
- **Connection no.**, **Statement no.** and **SQL text**: which column holds each item. They enable **Kill
  connection**, **Cancel statement** and the SQL below the grid. When empty, “(auto-detected)”: they are recognized by
  name (MON$ATTACHMENT_ID, ATTACHMENT_ID, "Attachment ID", MON$STATEMENT_ID, MON$SQL_TEXT…), ignoring spaces,
  underscores, `$` and accents. After **Test**, the lists offer the query's columns.
- **Save** and **Cancel**.

For example, the connections of one program:

```sql
SELECT a.MON$ATTACHMENT_ID, a.MON$USER, a.MON$REMOTE_ADDRESS,
       a.MON$REMOTE_PROCESS, a.MON$TIMESTAMP
FROM MON$ATTACHMENTS a
WHERE a.MON$SYSTEM_FLAG = 0
  AND a.MON$REMOTE_PROCESS CONTAINING 'facturacion'
ORDER BY a.MON$TIMESTAMP
```

Since it returns MON$ATTACHMENT_ID, **Kill connection** works without setting anything.

Custom queries apply to every database and are saved with the FbStudio settings on your computer.

### Sharing queries

The share button (or right-clicking the list) has:

- **Export custom queries…**: all of them, to a JSON file (it suggests `fbstudio-monitoreo.json`).
- **Import queries…**: adds the queries in the file to yours. If one was already there (the same query, exported
  earlier), it is replaced. Queries that are not a single SELECT are discarded.

> **Note:** an imported query can show anything next to the connection number. That is why the **Kill connection**
> confirmation always shows the data the server returns, not what is in the grid.

## Trace

The monitor shows what is happening now; the trace shows what loads the server over time. It is a Firebird trace
session (through the services API) that records the statements from any program that take longer than a threshold,
with their plan and their numbers, and groups them into a per-query analysis.

### Starting and stopping

1. Choose the threshold, the automatic stop and what to trace (see [What is traced](#what-is-traced)).
2. Click **Start**.
3. When you have enough, click **Stop**.

While it is running, the status line shows since when, how many events have arrived and what time it stops: “Trace
running on ventas since 10:15:00 · 1,234 events · stops at 10:25”. You cannot change the options while the trace is
running. **Clear** removes what has arrived.

The trace stops by itself:

- after the chosen time: **Stops after 5 min**, **10 min** (the default), **30 min** or **60 min**, or
  **No time limit**;
- when you close the tab;
- if FbStudio closes or the network drops: the server ends the session, which lives on the service connection.

If the server ends the trace on its own, the tab tells you, with the reason it gave.

> **Note:** on a production database, **Start** asks first, showing the threshold and when it stops. With a low
> threshold, the server records many statements, and that loads it.

### What is traced

Finished statements are always traced, with their plan and their performance. In addition:

| Option | What it does |
|---|---|
| Threshold | **Every statement**, or only the ones that take at least **From 10 ms**, 50, 100 (the default), 250, 500, 1000 or 5000 ms. In production, better not go below 100 ms. |
| **Procedures and functions** | Also procedure and function executions. Ticked by default. |
| **Triggers** | Also triggers. In a database with many of them, this multiplies the events. |
| **Errors** | Also errors. Ticked by default. |
| **Only SQL containing…** | Only the statements whose text contains what you typed, case-insensitive (for example, `TURNOS`). |

The SQL of each event is cut at 4,000 characters. Up to 50,000 events are kept; past that, the oldest ones are
discarded.

### Analysis by query

The **Analysis by query** tab groups the executions of the same statement and sorts them by total time: what loads
the server the most ends up at the top. A 50 ms query that runs 10,000 times weighs more than a 5 s query that runs
once.

| Column | What it is |
|---|---|
| **Count** | How many times it ran. |
| **Total ms** | The time of all the executions added up: the order of the list. |
| **Avg ms** and **Max ms** | The time per execution and the time of the slowest one. |
| **Query** | The SQL, on one line. |
| **Reads** | Pages read from disk. |
| **Fetches** | Pages requested from the cache. |
| **Writes** | Pages written. |
| **Rows** | The rows it returned. |
| **Failed** | How many executions failed. |
| **Warning** | “full scan of TABLE” if any execution read that whole table (non-indexed reads), or “failed”. |

**Group the ones that only differ in values** (ticked by default) replaces numbers and strings with `?`: this way
`WHERE ID = 1` and `WHERE ID = 2` are the same query even if the program does not use parameters, and `IN (…)` lists
are grouped too.

When you select a row, the bottom shows the summary (“Ran 120 times · 9,840 total ms · 82.0 avg ms · 410 ms the
slowest (this one):”) and the full slowest execution: who ran it, its numbers, the SQL, the plan, the parameters and
what it did on each table (non-indexed and indexed reads, rows updated, inserted and deleted).

### Events and details

The **Events** tab lists each event, newest first: time, ms, SQL, reads, fetches, writes, rows, user, IP, program and
warning. When you select one, the bottom shows it in full, with the message if it was an error.

Server paths are not shown: the database appears with its name in the registration.

### Saving and opening a log

- **Save log…** saves what arrived in this session as is (with the registration name instead of the database path),
  to a `.log` file (it suggests `fbstudio-trace-YYYYMMDD-HHMM.log`). The log is built while the trace is running: a
  log you opened is not saved again.
- **Open log…** (with the trace stopped) reads a saved log, from FbStudio or from fbtracemgr, and analyzes it just like
  live data. It replaces what you were looking at.

> **Note:** the log contains SQL with data. Until you save it, what arrives stays only in memory; if you save it, keep
> it in a safe place.

### Exporting to Excel

**Export analysis to Excel…** writes an `.xlsx` file with the queries as they appear in the analysis grid (with its
sort order and filters) and all their columns.

### Comparing with a log

**Compare with a log…** compares a log saved earlier (another period) with the current data, whether it is a live
trace or an opened log, query by query. The **Comparison** tab appears:

| Column | What it is |
|---|---|
| **Change** | “slower”, “faster”, “new”, “gone” or “same”. |
| **Query** | The SQL. |
| **Runs before** and **Runs now** | How many times it ran in each period. |
| **Avg ms before**, **Avg ms now** and **% avg** | The time per execution in each period and how much it changed. |
| **Total ms before**, **Total ms now** and **Difference ms** | The total time in each period and the difference. |
| **Reads/run before** and **Reads/run now** | The reads per execution. |

- The average per execution is compared, which does not depend on how long each trace lasted. A query is slower or
  faster if its average changed by at least 20% and at least 5 ms.
- The order is by how much the total time changed: what changed the server load the most comes first.
- The line at the top sums it up: how many queries there are in each period and how many are slower, faster, new or
  gone.
- When you select a row, the bottom shows the numbers for both periods and the slowest current execution (or the
  earlier one, if the query is gone).
- The **Group the ones that only differ in values** option applies to the comparison too.
- **Remove the comparison** closes it.

For a fair comparison, use the same threshold and the same options in both periods: with a different threshold,
different statements come in.

### Trace permissions

As SYSDBA, the trace sees everyone's statements. With another user it may see only the statements of your own
connections, and the tab tells you. The RDB$ADMIN role does not help here: Firebird 3 does not accept the role when
starting a trace, so FbStudio starts it without a role.

## Database statistics

The **Statistics** tab gives you the same as gstat, without installing it: it goes through the services API, it does
not open transactions (it does not affect the OAT) and you do not need to connect the database in the tree.

### The header

When the tab opens, it reads the database header (like `gstat -h`): a single page, instant even if the database is
huge. **Read header** (or `F9`) reads it again, and it is also read again when you reopen the tab from the menu.

| Card | What it shows |
|---|---|
| **OIT** | The Oldest Interesting Transaction: below it, everything is committed or cleaned up. |
| **OAT** | The Oldest Active Transaction. |
| **OST** | The oldest snapshot someone can still see: garbage collection can clean up to that point. |
| **Next transaction** | The number of the next transaction. |
| **Next − OAT** | Transactions that started after the oldest active one. If it keeps growing, something has had a transaction open for a long time (see [Monitoring](#monitoring)). |
| **Automatic sweep** | The sweep interval (“off” if it is 0; “20,000 (default)” if it was never changed) and, below it, OST − OIT. |
| **Page · ODS** | The page size and the ODS version. |
| **Forced writes** | “Yes” or “No (risky)”. |

The automatic sweep runs when OST − OIT goes past the interval. If it already has, the card turns amber and a warning
says that the automatic sweep should already have run and that, if the gap keeps growing, an old transaction is
holding it back. You change the interval in [Database properties](07-maintenance.md#database-properties).

**Forced writes** set to “No (risky)” also turns amber: without them, a power cut can damage the database. If the
database was left in nbackup backup mode (gstat reports it as “backup lock”), a warning appears at the top.

On the left is the list with all the header data, with translated names (the tooltip shows the name gstat uses). On the
right, the **gstat text** tab has the raw output, with the registration name instead of the database path; **Copy
text** copies it so you can paste it into an email or a ticket.

### The full statistics

**Full statistics…** reads all the data and index pages of every table, with the record versions (like
`gstat -a -r`). It asks first: on a large database it can take minutes and it loads the server disk, although it does
not block anyone. On a production database it adds that you should run it outside business hours. While it reads, the
status line counts the lines as they arrive, and **Cancel** stops it.

The result goes into two grids, which sort and filter like any other:

- **Tables (N)**: records, average record length, versions, maximum versions of a record, version length, fragments,
  data pages, fill %, empty and full pages, compression, formats and **Warning**.
- **Indexes (N)**: table, index, depth, leaf pages, nodes, duplicates, % duplicated, maximum duplicates, key length,
  clustering and **Warning**.

**Versions** are the old record versions that garbage collection has not cleaned up yet. The status line says how many
tables and indexes there are and how many have a warning.

### The Warning column

| Grid | Warning | When it appears |
|---|---|---|
| Tables | “many old versions: garbage is not being collected (a long-open transaction?)” | Versions are more than half the records, or some record has 10 versions or more. |
| Indexes | “depth N: worth rebuilding” | The index has 4 levels or more: each lookup reads more pages. You fix it by rebuilding the index (or with larger pages). |
| Indexes | “poorly selective (N% duplicated)” | It has 1,000 nodes or more and 95% or more of its values are duplicated: the optimizer can barely use it. |

With many old versions, check the OAT in the [monitor](#the-oat-and-its-warnings) first; then a
[sweep](07-maintenance.md#sweep) cleans them up.

## Statistics of chosen tables

On a large database, reading only some tables is much faster (like `gstat -t`).

1. Click **Tables: all**. The first time, the database tables are read.
2. Tick the ones you want. **Filter tables** searches by name, **All** clears the selection and, if the database has
   areas, **Tick the tables of an area…** adds the tables of an area (see [The tree, areas and diagrams](05-tree.md)).
3. The button changes to “Tables: 3 chosen” and the full statistics button changes to **Statistics of 3 tables…**:
   click it.
4. The confirmation says which tables will be read; continue with **Read**.

The data and index pages of those tables and their indexes are read, and the result goes into the same grids. With
none ticked, all of them are read.

## Comparing statistics

To put two databases side by side (for example, the test one against the production one):

1. In **Compare with**, choose another registered database (by default it says “(do not compare)”).
2. Click **Full statistics…** (or the one for the chosen tables). The confirmation says that the same will be read
   afterwards on the other database, and warns you if it is a production one.
3. When it finishes with this database, it reads the other one (you can cancel).

Two more tabs appear. In both, A is this database and B is the other one, and the difference is B − A.

- **Compare tables (N)**: records in A and in B and their difference, pages in A and in B and their difference,
  versions, fill % and a **Note** with “Only in …” if the table exists in only one of them.
- **Compare indexes (N)**: depth, nodes and % duplicates in A and in B, and a **Note**: “Deeper in …” if it has more
  levels in B, or “Only in …”.

The gstat text of the other database goes below this one's, in **gstat text**, with its name as a heading. If the
other database cannot be read (it has no saved password, it is missing one of the tables…), you keep this database's
results and the status line says why the comparison was not done.

## Server details

Right-click the database › **Tools › Server details…** opens a window that fills in when the server answers:

| Item | What it shows |
|---|---|
| **Server** | The server and port from the registration, for example `server:3050`. |
| **Version** | “Firebird 3.0.x” and the full version the server reports. |
| **Type** | Classic, SuperClassic or SuperServer. |
| **What it means** | How that type handles connections. |
| **How it was detected** | Where the type came from. |
| **System** | The server's operating system and architecture: “Windows x64”, “Linux x64”. |
| **Database** | ODS, dialect, page size and charset. |
| **Cache** | The cache pages and how much memory they take: shared by all connections (SuperServer) or one per connection (Classic and SuperClassic). |

**Copy** copies everything as text, so you can paste it into a report. The window does not show server paths (neither
the database file nor the Firebird folder), even if you connect as SYSDBA: that is what aliases are for.

The type and version also show in the tree when you connect (“connected · Firebird 3.0.11 · Classic”) and on the
**Server** card in the monitor.

### How the type is detected

| Type | What it means |
|---|---|
| Classic | One server process per connection, each with its own cache. If a process crashes, the other connections keep working. Monitoring is more expensive: every MON$ query asks all the processes for their data. |
| SuperClassic | A single server process, with a separate cache for each connection. |
| SuperServer | A single server process, with one cache shared by all connections. |

FbStudio works it out from MON$ATTACHMENTS:

- If the remote connections run in different processes, it is Classic. Any user can see this.
- If they share a process and the internal “Cache Writer” connection, which only SuperServer has, is there, it is
  SuperServer; if it is not there, it is SuperClassic.
- Only SYSDBA, users with the RDB$ADMIN role and the database owner can see the internal connections. Without that,
  the type shows as “SuperServer/SuperClassic”.
- If fewer than two remote connections are visible, FbStudio opens a short second connection to compare. If it still
  cannot tell, it says “type not detected”.
