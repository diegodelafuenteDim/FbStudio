# Getting started

How to install FbStudio, register your databases, connect, and read the banner at the top, which always tells you where
what you type will run. Also how to organize the tree with your own folders and how to move your registrations to
another computer.

## Installing

Download FbStudio from the **Releases** page of the FbStudio repository on GitHub. You don't need to install anything
else: neither .NET nor the Firebird client, because FbStudio talks directly to the server. It is tested with Firebird 3.0.

| System | File |
|---|---|
| Windows 10 or 11 (64-bit) | `FbStudio-<version>-win-x64.zip` |
| Debian, Ubuntu and derivatives | `fbstudio_amd64.deb` |
| Other Linux distributions | `FbStudio-<version>-linux-x64.tar.gz` |
| macOS on Apple Silicon (M1 or later) | `FbStudio-<version>-osx-arm64.zip` |
| macOS on Intel | `FbStudio-<version>-osx-x64.zip` |

### Windows

1. Unzip the `.zip` into a folder.
2. Open `FbStudio.exe`.
3. FbStudio is not signed: the first time, Windows may show "Windows protected your PC". Click **More info** and then
   **Run anyway**.

### Linux

With the `.deb` package (Debian, Ubuntu and derivatives):

```bash
sudo apt install ./fbstudio_amd64.deb
```

It shows up in the applications menu and as the `fbstudio` command. The package recommends `libsecret-tools` and a
keyring (GNOME Keyring or KWallet), which are what let FbStudio remember passwords. To uninstall it:
`sudo apt remove fbstudio`.

With the `.tar.gz` (any other distribution):

1. Extract it.
2. Run `./FbStudio` from that folder.
3. Install `libsecret-tools` (the `secret-tool` command) and a desktop keyring, so FbStudio can remember your
   passwords.

> **Note:** without a keyring FbStudio still works, but passwords are remembered only while it is open and the
> statement history is not saved. More details in [Options, shortcuts and privacy](09-options.md#without-a-keyring).

### macOS

1. Download the `.zip` for your Mac: `osx-arm64` for Apple Silicon, `osx-x64` for Intel.
2. Unzip it and open `FbStudio.app`.
3. FbStudio is not signed by Apple, so macOS blocks it the first time. Go to System Settings › Privacy & Security and
   click **Open Anyway**. You can also unblock it from the Terminal:

```bash
xattr -dr com.apple.quarantine FbStudio.app
```

### Verifying the download

Each release comes with a `SHA256SUMS.txt` file. With the package and that file in the same folder, run:

```bash
sha256sum -c SHA256SUMS.txt --ignore-missing
```

### Updating

Download the new package and replace the old one (with the `.deb`, run `sudo apt install` again with the new file).
Your registrations, options and history are not in the program folder but in your user's FbStudio folder, so they are
kept. What that folder contains: [The FbStudio folder](09-options.md#the-fbstudio-folder).

## The first time

FbStudio opens maximized. The language and the theme follow the system: if the system is in Spanish, the interface is
in Spanish; in any other language, it is in English. You can change both in **Tools › Options…**, **General** tab
(see [Options, shortcuts and privacy](09-options.md#general)).

### The welcome panel

With no registered databases, **Register your first database** appears on the left with two paths:

- **Register database**: opens the registrations window, where you enter server, database, user and password (see
  [Registering a database](#registering-a-database)).
- **Import registrations…**: if someone gave you the file with your team's registrations, import it and you get all
  the databases, with their areas; you enter your own user and password (see [Importing](#importing)).

With registered databases but no tabs open, **No tabs open** appears in the center: double-click a database in the
tree to connect.

### Notices at startup

- On Linux without a keyring, the status bar warns you that passwords are remembered only while FbStudio is open.
- If the options file could not be read, you get a notice and the default values are used.
- The first time you open a new version, the **What's new** window opens on its own (only once, and not on a fresh
  install). You can see it again in **Help › What's new**.
- The queries that were open last time are reopened, each with its database and its text. You can turn this off in
  **Tools › Options…**, **Editor** tab.

> **Shortcut:** `F1` opens the manual chapter for whatever you are using, and **Help › Manual** opens the index.

## Registering a database

A registration stores how to connect to a database: server, database, user, password, and how to treat it
(environment, read-only, encryption). To register a database, use any of these:

- **Database › Register database…**
- The **Register** button on the toolbar.
- Right-click an empty spot in the tree or a folder › **Register database…** (from a folder, the new registration
  already comes with that folder).

To change a registration: **Database › Edit registrations…** (opens on the selected tab's database) or right-click the
database › **Registration › Edit registration…**.

### The Registered databases window

On the left is the list of registrations, each with its folder and environment; below it, **New** and **Delete**. On
the right are the form for the selected registration and the **Test connection**, **Save** and **Close** buttons.

| Field | What goes in it |
|---|---|
| **Name (alias)** | How the database appears in the tree, on the tabs and in the banner. Required. |
| **Server** | The name or address of the Firebird server, for example `servidor`. Required. |
| **Port** | `3050` by default. |
| **Database (alias or path)** | The alias the database has on the server (for example `ventas`) or the path of the file on the server. Required. |
| **User** | The Firebird user. Required. |
| **Password** | Stored encrypted in the system keyring and never shown again. If you leave it empty, the saved one is kept: the field says "(unchanged)", or "(password missing)" if there is none. |
| **Role** | Optional: the role to connect with. |
| **Charset** | The character set of the connection: `ISO8859_1` (default), `WIN1252`, `UTF8` or `NONE`. |
| **Environment** | **Development** (default), **Test** or **Production**. |
| **Folder** | Your own tree folder where the database goes. If empty, the database goes to its environment group. |
| **Connection encryption** | **If the server accepts it (Enabled)** (default), **Required** or **Not encrypted (Disabled)**. |
| **Read-only (blocks DML, DDL and EXECUTE)** | Off by default. |
| **Keep statement history (encrypted; the number of days is set in Options)** | On by default. |

> **Note:** it is best to type the alias in **Database (alias or path)**: that text appears in the banner and in the
> tree tooltip. Wherever FbStudio reads the database path on the server by itself (statistics, maintenance, trace), it
> replaces it with that text.

### Environment

The environment says how much care the database needs and gives it its color: green for **Development**, orange for
**Test** and red for **Production** (you can change the colors in **Tools › Options…**; reds are always reserved for
production). The color shows in the banner at the top, on the tab borders and on the database icon in the tree.
Databases without a folder of their own are grouped by environment in the tree.

In any environment, an `UPDATE` or `DELETE` without `WHERE` asks for confirmation. With **Production**, also:

- The banner says **PRODUCTION**.
- Every statement that changes something asks for confirmation before it goes to the server, and so does a `SELECT`
  that uses procedures or functions inside an open write transaction.
- Applying changes in the Data tab and importing a file ask for confirmation.
- You cannot choose Autocommit or the transaction isolation: it is always an explicit **Commit** and read committed. In
  Development and Test, each tab can turn on **Autocommit** and choose the isolation on the toolbar.
- **Generate test data…** is not offered.
- When you choose **Production** in the form, if encryption was set to **If the server accepts it (Enabled)**, it
  switches to **Required**, with a notice. If the server does not accept encryption, it will not connect: test the
  connection before saving. You can change it back by hand.

### Read-only

With **Read-only**, FbStudio blocks everything that changes something before it reaches the server: DML, DDL and
`EXECUTE`, and also moving a generator (`NEXT VALUE FOR`, or `GEN_ID` with a step other than 0; to read the value, use
`GEN_ID(generator, 0)`). In addition, you cannot compile, edit data in the Data tab, import or change permissions;
Maintenance does not offer restore, empty database, sweep or changing properties; and in monitoring you cannot kill a
connection or cancel a statement. The banner adds " · read-only".

> **Note:** read-only is a FbStudio safety net; it does not replace the database permissions. For a production
> database you only query, combine **Production** with **Read-only**.

### Connection encryption

Sets whether data travels encrypted between FbStudio and the server:

- **If the server accepts it (Enabled)**: encrypts if the server allows it; otherwise, connects without encryption.
- **Required**: connects only with encryption. If the server does not accept it, the connection fails.
- **Not encrypted (Disabled)**: does not encrypt.

You can see whether the connection ended up encrypted when you test it, in the banner and in the database tooltip in
the tree (see [The database banner](#the-database-banner)).

### Charset

This is the character set used to read and write text. Many old databases have their text columns in `ISO8859_1`,
which is the default. If a database has `NONE` columns that store Windows text, use `WIN1252` so accented characters
read correctly.

### Testing, saving and deleting

- **Test connection** connects with what is in the form (without saving it) and says `OK` with the server version and
  whether the connection is encrypted, or shows the error.
- **Save** requires name, server, database and user, plus a password if none is saved yet. Once saved, the
  registration appears in the tree.
- **New** clears the form for a new registration.
- **Delete** removes the selected registration and its password saved on this computer, after asking. The database is
  not touched.

To make another similar registration (another database on the same server, another user or another role), right-click
the database › **Registration › Clone registration… (and open its properties)**: it makes a copy with everything,
password included, named "(copy)", and opens it so you can change whatever you need.

## Connecting and disconnecting

To connect to a database, double-click it in the tree. These also work: select it and press `Enter`, the **Connect**
button on the toolbar, **Database › Connect the selected database**, or right-click › **Connect**.

When it connects, FbStudio:

1. Opens the tree connection and shows the database objects by type (tables, views, procedures…), with how many there
   are of each.
2. Reads the server details: next to the database, "connected · Firebird 3.0.x · Classic" appears (or whatever the
   server type is), and the tooltip adds the operating system, the ODS, the dialect, the page size, the charset and
   whether the connection is encrypted. If the database was left in nbackup backup mode, "⚠ backup mode" appears next
   to it and the tooltip explains what to do.
3. Puts the database in the selected query tab, if it is free (no open transaction and nothing running). If not, or if
   it is another kind of tab, it opens a new query on that database. With no tabs, it opens the first one.

Each query tab opens its own connection the first time you run something, with its own transaction. The tools
(monitoring, trace, maintenance…) open their own: you don't need to connect the database first.

Once the database is connected, the right-click menu offers **Use this database in the tab** and **Reload metadata**
(reads the objects again, for example after creating a table from another program).

If the database has no saved password, the status bar says so: fill it in in the registration. If it cannot connect,
the status bar shows the server error. If the server is not Firebird 3.x, it also warns you: FbStudio is tested with
Firebird 3.x and some metadata reads may fail.

### Disconnecting

Right-click the database › **Disconnect**, or **Database › Disconnect the selected database**. All the tabs for that
database (queries, objects, search, monitoring…) and the tree connection are closed.

- If any tab has an open transaction or unsaved changes, it asks once and names them. On closing, the transactions are
  rolled back.
- If any tab is running something, nothing is disconnected: cancel it or wait for it to finish.

### When you exit

When you close FbStudio, if there are open write transactions, uncompiled or unsaved changes, or queries running, it
asks first with **Exit and undo** (or **Exit and discard**) and **Don't exit**. On exit, anything not committed is
rolled back and the connections are closed.

## The database tree

On the left is the tree with all the registered databases:

- Databases without a folder of their own go to their environment group: **Production** at the top, then **Test** and
  **Development**.
- Then come your own folders, by name. Inside each folder, its subfolders come first and then its databases, by name.
- Each database icon has the color of its environment. A database that is not connected looks dimmer; the one in the
  selected tab is shown in bold.

Above the tree, the filter box searches objects by name in the connected databases: a text (contains, case-insensitive)
or a pattern with `*` or `%`, for example `TURNOS*`. Each object type says how many match ("3 of 479") and expands on
its own if there are any; types that had not been opened yet are read automatically. The cross on the right clears the
filter.

What is inside each database (tables, columns, DDL, areas, generating SQL) is covered in
[The tree, areas and diagrams](05-tree.md).

### Your own folders

Your own folders organize databases your way, for example by customer or by system. A folder can be inside another:
write it with `/`, for example `Ventas/Producción`. A folder exists as long as it has some database in it, and "ventas"
and "Ventas" are the same folder.

To create a folder:

1. **Database › New folder…**, or right-click an empty spot in the tree or a folder › **New folder…**.
2. Type the **Folder name**.
3. Check the databases that go inside (the one selected in the tree is already checked). You need at least one.
4. Click **Create**. If there was already a folder with that name, the databases are added to it.

You can also choose the folder in the **Folder** field of the registration: type a new one or pick an existing one with
**▾** ("(no folder: environment group)" takes the database out of its folder).

### Moving databases and folders

Drag in the tree:

- A database onto a folder: it goes into that folder. Onto another database: it goes into that database's folder. To
  an empty spot or to its environment group: it is left without a folder.
- A folder onto another: it goes inside it. To an empty spot: it goes to the root.

You cannot move a database to another environment's group by dragging it (the environment is changed in the
registration), and a folder cannot go inside itself.

Without dragging:

- Right-click the database › **Registration › Move to folder…**: pick a folder or type a new one. If you leave it empty,
  the database goes back to its environment group.
- Right-click one of your folders › **Rename folder…**: changes the name and takes everything inside along. If the new
  name already exists, the folders are merged; with a different path (`Producción` instead of `Ventas/Producción`) the
  folder moves; if empty, its databases go back to their environment group.

### Folder colors

Right-click one of your folders › **Folder color** and pick a color from the palette, or **No color of its own**. The
color goes on the folder icon; the databases inside keep the color of their environment, which is what warns you where
things run. The folder palette has no reds (those belong to production), and the color stays with the folder if you
rename or move it. The environment groups (**Production**, **Test** and **Development**) cannot be renamed or colored.

## The database banner

Below the toolbar there is a colored banner that follows the selected tab. It is what tells you where what you type
will run.

### Environment and color

The banner has the environment color of the tab's database: green in Development, orange in Test and red in
Production, or the ones you choose in **Tools › Options…**, **General** tab (see
[Options, shortcuts and privacy](09-options.md#general)). With no database, it is gray.

### The tab's database

The text shows the environment (in production, **PRODUCTION**), the registration name and
`user@server:port/database`, for example "Development · Ventas · SYSDBA@servidor:3050/ventas". If the registration is
read-only, it adds " · read-only".

When there is no database, the banner tells you what to do: with no registered databases, register the first one; with
no tabs, double-click a database to connect; in a tab without a database, double-click a database to use it there.

### Whether the connection is encrypted

Once the database is connected in the tree, the banner adds " · encrypted connection" or " · ⚠ not encrypted". Without
encryption, queries and data travel readable over the network; the tooltip explains how to require encryption: in the
registration, choose **Required** in **Connection encryption** (the server has to accept it).

### Open transaction

On the right, if the tab has an open write transaction, the banner says for how long: "Write transaction OPEN for
12 min: Commit or Rollback". It updates every 30 seconds.

- After the minutes set in **Open write transaction: amber after** (5 by default), the notice turns amber.
- After the minutes set in **…and red, with a notice, after** (15 by default), it shows in red text on white and the
  status bar warns you once.

You set both in **Tools › Options…**, **Queries** tab. A forgotten transaction holds back the server's garbage
collection: commit it with **Commit** (`Ctrl+Alt+C`) or roll it back with **Rollback** (`Ctrl+Alt+R`). The tab also
shows a red dot next to its name.

### Other connected databases

If other databases are connected in the tree, **Also connected:** appears to the right of the text, with each one as a
chip in its environment color. The chip tooltip shows its environment and its server. A click takes you to the first
tab of that database or, if it has none, opens a query on it. The current tab does not change database.

## Several databases at once

You can have several databases connected and work with all of them at the same time:

- Each tab has its own database, connection and transaction. The tab border has the environment color and the tooltip
  says which database it belongs to.
- The banner always shows the database of the selected tab; the other connected ones appear as chips.
- Double-click another database in the tree: the selected tab switches to that database if it is free; if it has an
  open transaction or is running something, the database opens in a new tab and the previous one keeps its own.
- **Query › New tab** (`Ctrl+T`) or the **+** button opens a query on the database you touched last: the one selected
  in the tree, if you clicked the tree after visiting the tab; otherwise, the selected tab's database. The same goes for
  **Query › Search metadata…** (`Ctrl+Shift+F`).
- Right-click a database › **New query on this database** always opens a query on that database.
- `Ctrl+Tab` and `Ctrl+Shift+Tab` move from one tab to another.

> **Note:** before running anything, look at the banner. With several databases open, the color and text of the banner
> are what tell you where the statement will run.

## Exporting and importing registrations

You can save your registrations to a file to keep a backup, move them to another computer (Linux or macOS too) or share
them with your team. Each system keeps passwords in its own keyring, so this is the way to take them to another
computer.

### Exporting

1. Choose one of these options:
   - **Database › Export registrations…**, or right-click an empty spot in the tree › **Export registrations…**.
   - Right-click a folder › **Export the registrations in this folder…** (its databases come checked).
   - Right-click a database › **Registration › Export this registration…**.
2. In **Registrations to export:** check the ones to include (**All** and **None** check or uncheck the whole list).
3. Choose what they include:
   - **Include my user name for each connection (otherwise whoever imports enters their own)**: on by default.
   - **Also include my password for each connection (the file is encrypted with a passphrase)**: off by default; only
     possible together with the user name. It asks for a passphrase of at least 10 characters, twice.
   - **Include each database's areas (on import, databases that have none get them)**: on by default.
4. Click **Export…** and choose where to save the file (`fbstudio-registros-YYYYMMDD.json` is suggested).

Without passwords, the file is readable JSON. Without your user name, it is good for sharing the registrations with
your team: whoever imports it enters their own user and password. With passwords, the whole content (registrations,
passwords and areas) is encrypted with the passphrase. Areas travel with their content, even if the database uses a
shared areas file.

> **Note:** the file contains servers and databases: keep it somewhere safe. The passphrase is not stored anywhere:
> without it, the file cannot be imported. It is not your Firebird password; several words work best.

### Importing

1. **Database › Import registrations…**, the **Import registrations…** button on the welcome panel, or right-click an
   empty spot in the tree or a folder › **Import registrations…**.
2. Choose the file. If it is encrypted, type the passphrase it was exported with; if it is wrong, you are told and can
   try again.
3. The **Import registrations** window shows how many registrations the file has, how many are new and how many are
   already registered, and how many come with a password (those go to this computer's keyring).
4. If some registrations already exist, choose **Leave the ones already registered as they are** (default) or
   **Update them with the data in the file**.
5. If some registrations have no user, type the user and password for them (you can leave them empty and fill them in
   later in each registration).
6. Click **Import**. The status bar sums up how many were added, updated or left as they were.

A registration in the file counts as already registered if it is the same registration (imported again) or if it
points to the same database, on the same server and port, with the same user (if the file includes the user). A new
registration with a name that already exists is added as "Name (2)". Registrations without a name, server or database
are not imported. A file that is not a FbStudio registrations file is rejected, and one made with a newer version asks
you to update FbStudio.

### What is protected on import

- A file cannot lower the protection of an existing registration: **Read-only**, **Production** and the connection
  encryption keep the strongest setting. If you want to lower them, change them by hand in the registration.
- If updating a registration changes the server, the database or the user and the file has no password, the saved
  password is forgotten: it is not sent to another server. You have to type it again.
- The path of a shared areas file is not imported: each computer chooses its own.
- The areas in the file go to the databases that don't have any yet; a database that already has areas, or uses a
  shared file, keeps its own.
- If the file has passwords and the computer has no keyring, you are warned first: the imported passwords are
  remembered only until FbStudio is closed.
