# Maintenance and permissions

Database maintenance tasks (backup, restore, nbackup, validation, sweep, empty database and properties) and the
management of users, roles and permissions, with the safeguards for production and for read-only registrations.

## The Maintenance tab

Open it from **Tools › Maintenance (backup, restore…)**, or right-click the database › **Maintenance**, which takes you
straight to a task: **Backup**, **Restore**, **nbackup**, **Validation**, **Sweep**, **Empty database** or
**Properties**. There is one tab per database. It opens its own connection, so you don't need to connect to the
database first, but the registration must have its password saved.

- On the left, the task list.
- At the top, the database as the registration names it, with its page size and charset: "Database ventas ·
  8,192-byte pages · WIN1252". The real path on the server is not shown: if the registration uses an alias, you see the
  alias, also in what gbak, the validation and the errors say.
- In the middle, the task options and its buttons: the run button (it changes with the task: **Back up**,
  **Restore**…), **Stop**, **Check status**, **Register the new database** and **Copy log**.
- At the bottom, the log: what was done, with the time, the options used and what the server says (the gbak output,
  the validation output or the error).

While a task runs, the list and the options are disabled.

> **Note:** paths are server paths, as Firebird sees them, not paths on this PC. Type the full path
> (`D:\Backups\ventas.fbk` or `/backups/ventas.fbk`) or, for a new database, an alias. FbStudio does not suggest
> folders, because the database folder on the server is not shown. The Firebird server itself writes the files, so the
> folder must exist there and Firebird must be able to write to it.

## Backup

The **Backup** task makes a backup with gbak, run by the server (through the services API).

1. In **Backup file (path on the server)**, type the file with its full path.
2. Choose the options you need.
3. Click **Back up**.

The server does the work: if you close FbStudio or the network drops, the backup keeps going until the end. If the
file already exists, it is replaced. FbStudio waits until the server has really finished and logs it: "Backup finished:
… (N s)". The file also becomes the source for the **Restore** task.

> **Note:** the target must be a backup file (`.fbk` or `.gbk`). FbStudio rejects a name with a database extension
> (`.fdb`, `.gdb`, `.ib`…) or the registration's own database, because gbak would overwrite it.

### Backup options

| Option | What it does |
|---|---|
| **Structure only (no data)** | `gbak -m`. When you restore it, generators go back to 0 and identities to 1. For a database with no data that keeps numbering the same way, use [Empty database](#empty-database). |
| **No garbage collection (-g): faster** | It does not clean up old record versions while reading. The database stays the same. |
| **Ignore checksum errors (to rescue a damaged database)** | It keeps going even with damaged pages; the backup may be incomplete. Use it to get whatever you can out of a database that a normal backup cannot finish. |
| **Non-transportable format (faster; restores only on the same kind of system)** | The server platform's own format. |
| **Without the data of these tables (the structure is kept):** | These tables go empty into the backup, for example `AUDITORIA, LOG_TURNOS`. |

Table lists are written as in SQL: separated by commas, semicolons or spaces. Names without quotes are taken in
uppercase, and names in double quotes exactly as written. Each name must match exactly: `TURNOS` does not include
`TURNOS_HIST`.

## Restore

The **Restore** task restores a backup on the server, always into a new database.

1. In **Backup to restore (path on the server)**, the `.fbk` file.
2. In **New database (path on the server)**, the full path (`/datos/ventas_prueba.fdb`) or an alias.
3. Choose the options and click **Restore**.

The server does it even if you close FbStudio; on a large database it can take hours. FbStudio watches the new
database's header until it is ready and logs "Still restoring…" every minute. At the end it says "Restore finished: …
is ready to use".

The registration's database is never overwritten: FbStudio compares the target's path, alias and real path (it
connects to the target to find out). If there is already a database at the target and you did not tick
**Replace if it exists**, it tells you before starting. To create the new database, the registration's user must be
allowed to create databases on the server.

### Restore options

| Option | What it does |
|---|---|
| **Replace if it exists (asks you to type the file name)** | Overwrites a database that is already at the target (see [Replacing a database](#replacing-a-database)). |
| **Structure only** | Restores without the data. |
| **Without activating indexes (-i): faster; activate them later** | The indexes stay inactive. |
| **Commit table by table (if one table's data fails, the others stay)** | Each table is committed separately. |
| **Without shadow files** | Does not recreate any shadows the database had. |
| **Page size** | **Same as before** (the size the database had), or 4,096, 8,192, 16,384 or 32,768. |
| **Without the columns' NOT NULL rules (to rescue old data)** | `gbak -n`, with no validity check: it restores even when rows have NULL in a NOT NULL column, which makes a normal restore fail. |
| **Without the data of these tables (the structure is kept):** | These tables are restored empty. Write the names as for the backup. |

> **Note:** with **Without the columns' NOT NULL rules**, the database ends up WITHOUT those NOT NULL constraints (the
> CHECK constraints are kept). Afterwards you have to fix the data and add them back.

### Replacing a database

With **Replace if it exists**, before starting FbStudio asks you to type the target file name, without the folder or
the extension: for `/datos/ventas_prueba.fdb`, you type `ventas_prueba`. Everything in that file is lost. The
registration's database is never replaced, even if you type it with a different alias.

### Registering the new database

When a restore or an empty database finishes, **Register the new database** appears: it registers the new database just
like this tab's one (same server, user, password and folder), named "registration · file", for example "Ventas ·
ventas_prueba". A copy of a production database is registered as Test. Afterwards you edit it from the tree, like any
other (see [Getting started](01-start.md)).

## Empty database

The **Empty database** task creates a new database with this one's whole structure, built from the database script:
same charset, page size and dialect, with tables, views, procedures, triggers and permissions. Use it to get a database
at zero that keeps numbering the same way, which a structure-only backup does not give you.

1. In **New database (path on the server)**, the full path or an alias. An existing file is never overwritten.
2. Leave **Generators and identities with their current value** ticked so the new database keeps numbering like this
   one. Without it they start from zero, and if you copy tables with data, their keys may collide.
3. In **Tables to copy with their data**, tick the ones you want (for example, the lookup tables).
   **Filter tables** searches by name and **None** clears the selection. With none ticked, only the structure is copied.
4. Click **Create the empty database**.

How it builds it:

1. It creates the database and runs the structure part of the script: tables, keys, indexes, views, procedures…
2. It copies the data of the chosen tables and of the tables their FKs reference (these are added automatically and
   the log says which ones), read in a single snapshot of the source database.
3. It runs the deferred part: FKs, CHECKs, triggers and permissions. That way the data goes in before the FKs and the
   triggers.

Each statement is committed on its own: a statement that fails is written to the log and the next one runs. The final
message says how many failed and which tables were not copied.

> **Note:** FbStudio builds the empty database, not the server: if you close FbStudio, the work stops and the database
> is left half built (delete it, or build it again with another name). The data is copied row by row, so it is meant
> for small or lookup tables. To move the whole database, use backup and restore.

## Validation

The **Validation** task runs an online validation (Firebird 3 and later): with the database in use, without an
exclusive connection. It checks the pages and indexes of each table, locking each table for a few moments while it
checks it. It does not fix anything: it only reports.

1. To validate only some tables, type them in **Only these tables, with their indexes (empty: the whole database):**,
   for example `TURNOS, PACIENTES`.
2. Click **Validate**.

The result shows in the status bar and in the log: "Validation finished with no errors", "Validation found N error(s)
and M warning(s): see the log" or "The validation did not reach the end". Classic validation, with the database in
exclusive mode (`gfix -v`), is not offered.

On a production database it asks first: each table stays locked for a while as it is checked, so it is best done
outside working hours.

## Sweep

A sweep goes through the whole database and cleans up old record versions (the same as `gfix -sweep`). On a large
database it keeps the server busy for quite a while.

Choose the **Sweep** task and click **Run sweep**. It always asks first; on a production database you have to type the
database name (the registration name). When it finishes, it says how long it took; in
[Statistics](06-monitoring.md#the-header) you can see where the OIT and the OAT ended up.

If an old transaction is still open, the sweep cannot clean what that transaction can still see: check the OAT in
[monitoring](06-monitoring.md#the-oat-and-its-warnings) first. The automatic sweep is set in
[Database properties](#database-properties).

## nbackup

nbackup makes physical backups by levels: level 0 is a full copy, and each level N backup is based on the last level
N − 1 backup, so incrementals depend on the last level 0. While a backup runs, the database is in backup mode: writes go
to the delta file, which is merged into the database when the backup ends.

For now FbStudio only looks: nbackup itself is still run on the server. The **nbackup** task shows:

- whether the database is in backup mode (with a warning) or not ("The database is not in backup mode");
- where the delta file goes: next to the database (the default) or to another file defined in the database with
  `ALTER DATABASE ADD DIFFERENCE FILE`;
- the history of backups recorded in the database (RDB$BACKUP_HISTORY), most recent first: **Date**, **Level** ("Level
  0 (full)", "Level 1 (incremental)") and **File** (just the name, without the server folder).

**Check status** reads it again. Any user can see it: it only reads and changes nothing.

> **Note:** if the database is in backup mode and no nbackup is running, it got stuck: the delta keeps growing until
> you end the backup (`nbackup -N` or `ALTER DATABASE END BACKUP`), and you should check the server disk.

Backup mode is also flagged when you connect (the database shows "⚠ backup mode" in the tree, with the details in the
status bar), in [monitoring](06-monitoring.md#which-connections-you-see) and in [statistics](06-monitoring.md#the-header).

## Database properties

The **Properties** task reads the database properties when you choose it:

| Property | What it is |
|---|---|
| **Automatic sweep (0 = off)** | The server runs a sweep when OST − OIT exceeds this number. 20,000 is the default. |
| **Cache pages (0 = the server's)** | The database's own cache; with 0 it uses the one in the server configuration. |
| **Forced writes (each change goes to disk on commit)** | Without them, a power cut or a server crash can damage the database. |
| **Read-only (nobody can write)** | The database accepts no changes until it is switched back. |

Change what you need and click **Apply changes**. Only what changed is applied, and the confirmation lists it
("Automatic sweep: 20,000 → 0"), with a warning if you turn off forced writes or switch to read-only.
**Check status** reads them again.

- The server makes the changes, like gfix: you have to connect as SYSDBA, as the database owner or with the RDB$ADMIN
  role.
- To switch to read-only, nobody else may be connected, not even FbStudio in another tab.
- Leaving read-only is applied first and entering it is applied last: while the database is read-only, nothing else
  can be changed.

> **Note:** on a production database, turning off forced writes or switching to read-only asks you to type the
> database name (the registration name). Other changes ask for the usual confirmation.

## Check status and stop

**Stop** appears while a task runs:

- In backup and restore, it only stops watching: the server keeps going until it finishes, and the log says so.
- In an empty database, it stops the work, and the new database is left half built.

**Check status** (in backup, restore, nbackup and properties) tells you where things stand with what you stopped
watching:

| Task | What it checks |
|---|---|
| Backup | Whether the server is still running a backup for your user. It recognizes it by the connection the backup service opens: from your user and with no client program. |
| Restore | Where the new database stands: "is ready to use", "is still being restored (or was left half restored if the restore failed)" or "There is no database…". If it is ready, it offers **Register the new database**. |
| nbackup and Properties | Reads them again. |

**Copy log** copies the tab's whole log, so you can paste it into a ticket.

## Users and permissions

The **Users and permissions** tab manages the server's users and the database's roles and permissions, with Firebird 3
SQL: it reads SEC$USERS, RDB$ROLES and RDB$USER_PRIVILEGES, and makes changes with CREATE, ALTER and DROP USER, CREATE
and DROP ROLE, GRANT and REVOKE. You can open it:

- from **Tools › Users and permissions**;
- by right-clicking the database › **Tools › Users and permissions…**;
- by right-clicking a table, view, procedure, function, package, generator or exception in the tree ›
  **Permissions…**, which opens the [By object](#permissions-by-object) view with that object selected.

> **Note:** users belong to the server and work for all its databases; roles and permissions belong to this database.

At the top are **Refresh** (`F9`), **New user…** and **New role…**. On the left is the list chosen with the first
option:

- **Users and roles**: the server's users (with their name, and "inactive" or "manages users" when it applies), those
  who have permissions but are not on the server ("not on the server"), **PUBLIC** ("all users") and the database's
  roles, with their description.
- **Procedures, functions, packages, triggers and views**: these objects also receive permissions (for example, a
  procedure that writes to TURNOS).
- **By object: who has permissions**: see [Permissions by object](#permissions-by-object).

**Filter** searches the list by name. At the very bottom, a panel shows the SQL that is about to run or that already ran.

To see all users and to create, edit or delete them, you have to connect as SYSDBA or with the RDB$ADMIN role;
otherwise the tab tells you so and the list shows only your own user.

## Users

### Creating a user

**New user…** opens a window with:

- **User**, **Password** and **Repeat the password**;
- **First name**, **Middle name**, **Last name** and **Description**;
- **User manager**: appears only if the server uses more than one; the default is "(the server's)";
- **Active (can connect)** and **Manages users (GRANT ADMIN ROLE)**.

With **Save**, the user is created right away, without waiting for **Apply**. If you had unapplied permission changes,
it first asks whether to discard them. A plain name is saved in uppercase, as if typed without quotes ("pepe" becomes
PEPE); a name with spaces or other symbols is kept exactly as typed.

Firebird stores the user only on commit: if the user already existed, it fails at that point and nothing is applied.

### Editing a user

Choose the user and click **Edit user…**. It is the same window, but the name cannot be changed; if you leave the
password empty, it is not changed. Only what changed is run.

### Deleting a user

**Delete user…** asks you to type the user's name. The user will no longer be able to connect to any database on the
server; their permissions in the databases remain, shown as "not on the server". SYSDBA and the user you are connected
as cannot be deleted from here.

> **Note:** passwords are never shown or logged: in the on-screen SQL they appear as `'********'`, and they are also
> masked if they show up in an error.

## Roles

- **New role…** asks for the name (with the same uppercase rule as users) and creates the role right away.
- To give a role to a user, choose the user: under **Roles** there is one checkbox per role, including
  **RDB$ADMIN (database administrator)**. A role granted with permission to pass it on says "(can give it to others)".
  These changes stay pending until you click **Apply**, like permissions.
- You view and change a role's permissions by choosing it in the list, just like a user's.
- **Delete role…** asks you to type the role's name. The role is deleted with its permissions: the users who have it
  lose them.

## Permissions by user or role

Choose a user, PUBLIC, a role or a PSQL object in the list. On the right, under **Permissions**, there is one row per
database object, with checkboxes. To find what you are looking for, use:

- the kind list: **All objects**, **Tables**, **Views**, **Procedures**, **Functions**, **Packages**,
  **Generators** or **Exceptions**;
- **Filter**, by name;
- **Only granted**: only the objects it already has some permission on (or that you changed).

Each row shows only the checkboxes that make sense for that kind of object:

| Object | Permissions |
|---|---|
| Table | SELECT, INSERT, UPDATE, DELETE, REFERENCES |
| View | SELECT, INSERT, UPDATE, DELETE |
| Procedure, function or package | EXECUTE |
| Generator or exception | USAGE |

- **Can grant** is WITH GRANT OPTION: the grantee can give those permissions to others. It applies to all the
  permissions in the row.
- A row with unapplied changes has a dot (●) on the left.
- Permissions on only some columns are shown ("Only some columns: UPDATE (OBSERVACIONES)") but cannot be changed
  from here.
- A permission granted by another user is removed with GRANTED BY, which an administrator can do.

## Permissions by object

With **By object: who has permissions**, the tab is turned around: on the left are the objects (with the same kind
list and **Filter**), each with how many grantees have permissions on it ("Table · 3 with permissions"). When you choose
one, on the right there is one row per user, PUBLIC, role and PSQL object with permissions on it, with the same
checkboxes. **Filter** and **Only granted** search among the users and roles.

Each row is the same in both views: a change made in one shows in the other, and **Apply** runs it only
once.

## Structure permissions

When you choose a user or a role, the **Structure permissions (create, alter and drop objects)** drop-down has Firebird
3's DDL permissions: create tables, alter any procedure, drop any domain… There is one row per object class
(**Tables**, **Views**, **Procedures**, **Functions**, **Packages**, **Generators**, **Domains**, **Exceptions**,
**Roles**, **Character sets**, **Collations**, **BLOB filters** and **The database (ALTER DATABASE)**) with the
checkboxes CREATE, ALTER ANY, DROP ANY and **Can grant**. The database only has ALTER.

For example, ticking CREATE on **Tables** and ALTER ANY on **Procedures** for a role generates:

```sql
GRANT CREATE TABLE TO ROLE DESARROLLO;
GRANT ALTER ANY PROCEDURE TO ROLE DESARROLLO;
```

One statement per permission runs, together with the other changes, when you click **Apply**. An object's owner can
always alter it, with or without these permissions. There is no row for triggers: in Firebird 3, creating triggers
comes with ALTER ANY TABLE (the **Tables** row).

## Applying and showing the SQL

Permission and role changes add up, even if you move from one user to another or from one view to the other; the bottom
of the tab shows how many are unapplied.

- **Show SQL** shows the GRANT and REVOKE statements in the bottom panel, without running them.
- **Apply** shows the SQL, asks ("N GRANT/REVOKE statements will run on ventas, all together (if one fails, none is
  applied)") and runs them in a single transaction. Then it reads everything again, and the panel keeps the SQL that
  ran.
- **Discard** leaves everything as it was.
- **Refresh** with unapplied changes first asks whether to discard them.

If a statement fails, none is applied, and the status bar says which one it was and what Firebird answered.

```sql
-- 3 statements (not run yet)
GRANT SELECT, INSERT, UPDATE ON PACIENTES TO VENTAS_APP;
REVOKE DELETE ON TURNOS FROM VENTAS_APP;
GRANT EXECUTE ON PROCEDURE ALTA_TURNO TO ROLE RECEPCION;
```

## Production and read-only

A read-only registration (the registration's **Read-only (blocks DML, DDL and EXECUTE)** option, see
[Getting started](01-start.md)) lets you look but not change anything. With a production database, FbStudio asks more.

| Action | Read-only registration | Production database |
|---|---|---|
| Backup | Allowed | No extra prompt |
| Restore | Not offered | No extra prompt: it never touches the registration's database (replacing another one always asks for its name) |
| Empty database | Not offered | No extra prompt: it never overwrites a file |
| Validation | Allowed | Asks first |
| Sweep | Not offered | You have to type the database name |
| Properties | Shown, not applied | Asks; turning off forced writes or switching to read-only asks you to type the name |
| nbackup | Shown | View only |
| A user's permissions and roles (**Apply**) | View only | The confirmation warns that it is production |
| Creating or editing a user | No | Asks: it applies to all databases on the server |
| Creating a role | No | Asks |
| Deleting a user or a role | No | You always have to type the name |

> **Note:** users belong to the server. If the same server has production databases, changing a user from a test
> database's registration affects them too, and FbStudio only asks when the registration is a production one.
