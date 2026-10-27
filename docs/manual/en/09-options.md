# Options, shortcuts and privacy

The application options tab by tab, every keyboard shortcut, and what FbStudio stores on your computer: where, what
for and how it protects it.

## The Options window

Open it with **Tools › Options…**. The tabs are on the left: **General**, **Editor**, **Templates**, **Queries**,
**Monitoring** and **History**. They apply to all databases; what is specific to each database (environment, read-only,
encryption, history) goes in its registration (see [Registering a database](01-start.md#registering-a-database)).

- Nothing changes until you click **OK**. **Cancel** leaves everything as it was.
- **Defaults** resets every option to its factory value, including the templates and the environment colors (folder
  colors are not touched). Nothing is saved until **OK**.
- A number field left empty takes its default value, and a value out of range is set to the limit.
- When you accept, the theme, the colors and the editor options apply right away, also in the open tabs. The language
  changes when you reopen FbStudio.

### General

| Option | What it does | Default |
|---|---|---|
| **Theme** | **Same as Windows** follows the system's light or dark mode; **Light** and **Dark** fix it. | **Same as Windows** |
| **Language** | **Same as Windows**, **Español** or **English**. "Same as Windows" uses Spanish if the system is in Spanish and English otherwise. Numbers and dates follow the system's regional format. | **Same as Windows** |
| **Environment colors**: **Development** | The color of development databases. | Green |
| **Environment colors**: **Test** | The color of test databases. | Orange |
| **Environment colors**: **Production** | The color of production databases. | Red |

When you change the language, FbStudio asks (already in the new language) whether to restart: **Restart now** closes
the open tabs and reopens FbStudio (if any tab has an open transaction, it asks first); **Later** leaves it for the next
time you open it.

The environment colors show in the database banner, on the tab borders and on the database icon in the tree. You pick
them from a palette that reads well with white text:

- **Development** and **Test**: Green, Dark green, Teal, Cyan, Blue, Indigo, Purple, Orange, Olive, Brown or Slate.
- **Production**: Red, Dark red, Bright red, Wine or Crimson. Reds are reserved for production, so nothing gets
  confused with it.

In the dark theme, the tree and the tabs use a slightly lighter shade so it stands out against the background; the
banner keeps the original one. The colors of your own folders are chosen from the tree (see
[Folder colors](01-start.md#folder-colors)).

### Editor

| Option | What it does | Default |
|---|---|---|
| **Font** | The SQL editor font. The fixed-width fonts you have installed are offered: Cascadia Mono, Cascadia Code, Consolas, JetBrains Mono, Fira Code, Source Code Pro, Courier New and Lucida Console. | Cascadia Mono |
| **Font size** | From 8 to 40. | 14 |
| **Tab width** | How many spaces a tab takes, from 1 to 16. | 4 |
| **Tab inserts spaces (not a tab character)** | `Tab` types spaces instead of a tab character. | Yes |
| **Show line numbers** | The number of each line in the margin. | Yes |
| **Highlight the cursor line** | Marks the line where the cursor is. | Yes |
| **Allow scrolling past the last line** | Lets you scroll the text further down than the last line. | Yes |
| **Ctrl + mouse wheel enlarges or shrinks the font (Ctrl+0 resets it to the chosen size)** | Font zoom in that tab, without changing the option. `Ctrl+0` goes back to the size set above. | Yes |
| **Fold blocks (BEGIN…END, CASE, parentheses and long comments) with margin markers** | Markers in the margin to fold and unfold blocks that span several lines. | Yes |
| **When formatting, keywords in uppercase** | **Format SQL** turns keywords to uppercase; when off, they stay as you typed them. | Yes |
| **On close, remember the open queries and reopen them** | When FbStudio closes, it saves the query tabs, each with its database and its text (encrypted like the history), and reopens them next time. A tab for a .sql file is read again from the file. When off, what was saved is deleted. | Yes |

Empty query tabs are not remembered, and neither are object or tool tabs.

### Templates

A template is a piece of text you insert with an abbreviation: type the abbreviation and press `Tab`, or press `Ctrl+J`
to pick one from the list (**Query › Insert template…**). In the text, `${cursor}` marks where the cursor ends up.

This tab has the list of templates, with their abbreviation and description. To change them:

- **New** adds a template (with the abbreviation "new1", "new2"…) for you to fill in.
- **Delete** removes the selected template.
- **The defaults** resets the list to the templates that come with FbStudio.
- On the right you edit the **Abbreviation**, the **Description** and the **Text** of the selected template.

Abbreviations are case-insensitive; if two are the same, the first one wins. As long as you don't change the default
templates, if a new version of FbStudio improves them, you get the improvements without doing anything.

| Abbreviation | Description |
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

### Queries

| Option | What it does | Default |
|---|---|---|
| **Rows a SELECT fetches** | How many rows a SELECT fetches at first: 1,000, 10,000, 100,000 or 1,000,000. This is the value FbStudio starts with; the **Limit** selector on the toolbar changes it only until you close the application. Exporting "all rows" has no limit. | 10,000 rows |
| **Wait for a locked record** | If another transaction has locked the record you want to change, the write waits up to this long and then gives an error, instead of leaving the tab hanging. It applies to transactions opened from now on. From 1 to 600 seconds. | 10 seconds |
| **Open write transaction: amber after** | After these minutes, the open transaction notice in the banner turns amber. From 1 to 1,440. | 5 minutes |
| **…and red, with a notice, after** | After these minutes, it turns red and the status bar warns you once. It cannot be less than the previous value; up to 10,080. | 15 minutes |
| **When applying changes in the Data tab** | **Show the commands first (run them, copy them or take them to a query tab)** or **Run them directly**. | Show the commands first |
| **On an error in a script** | **Stop at the first error** or **Continue with the next statement**. | Stop at the first error |

About the open transaction: a forgotten transaction, with several people working, holds back the OAT and the server's
garbage collection (see [The database banner](01-start.md#the-database-banner)).

When you apply changes in the Data tab, both ways run the same thing: everything together in a short transaction,
checking that nobody else has changed the row. With production or with deletions, you are still asked for confirmation
(see [The Data tab](04-data.md)).

On an error in a script: a statement that fails is undone on its own; what came before and the transaction stay in
place. At the end, the log lists the errors and the cursor goes to the first one. It is the same option as
**Query › Continue on errors in scripts**.

### Monitoring

| Option | What it does | Default |
|---|---|---|
| **OAT in amber if open for more than** | In monitoring, the oldest active transaction (OAT) is shown in amber after these minutes. From 1 to 1,440. | 2 minutes |
| **and in red if open for more than** | After these minutes, in red. It cannot be less than the previous value; up to 10,080. | 10 minutes |
| **Do not alarm if the OAT belongs to a backup or a sweep (gbak, gfix, nbackup): it is reported without color** | A maintenance OAT is shown without an alarm color. | Yes |

There is always an OAT while there are open transactions; what matters is how long it lasts. A gbak backup or a sweep
keeps it open while it works. More in [Monitoring and performance](06-monitoring.md).

### History

| Option | What it does | Default |
|---|---|---|
| **Keep statements for** | How many days statements are kept in the history, from 1 to 3,650. When you shorten the period, the oldest ones are deleted right away; they are also purged when FbStudio opens. | 90 days |

Each registration can opt out of the history: turn off **Keep statement history** in its registration. You see the
history with `Ctrl+4` (see [The SQL editor](02-editor.md)).

### Assistants (MCP)

The databases enabled for AI assistants, the MCP configuration ready to copy (**Copy**), the maximum length of each
answer (40,000 characters by default), whether monitoring shows who holds each connection (on by default) and the
assistant's recent calls. Everything about the connector is in
[AI assistants (MCP)](10-assistants.md).

### What is remembered outside the window

Some preferences are saved along with the options even though they are not in this window:

- The color of each of your own folders (set from the folder's menu in the tree).
- The last .sql files opened or saved, up to 10 (**Query › Open recent**).
- In the Data tab, **Count when filtering**, and **End with COMMIT** in the commands window.
- The last version of FbStudio that was opened, to show **What's new** only once.

The column order, widths and hidden columns of the Data tab are remembered per database and table, in another file
(see [The FbStudio folder](#the-fbstudio-folder)). The **Limit** selector on the toolbar is not remembered.

## Keyboard shortcuts

**Help › Keyboard shortcuts** shows a summary; here is the full list. Menus also show the shortcut next to each
command.

### Running

| Shortcut | What it does |
|---|---|
| `F9` or `F5` | Runs the selection; without a selection, the whole editor (several statements run in order, as a script). In the tab of a procedure, trigger, view or function, without a selection, it compiles it. |
| `Ctrl+Enter` | Runs only the statement where the cursor is (or the selection). |
| `Ctrl+F9` | Runs the selection or the whole editor as a script, without the grid (for long scripts). |
| `Ctrl+Shift+E` | Shows the plan of the statement at the cursor (or of the selection) without running it. |
| `Esc` | Cancels the execution. |
| `Ctrl+Alt+C` | Commits the tab's transaction. |
| `Ctrl+Alt+R` | Rolls back the tab's transaction. |
| `F9` or `Ctrl+Enter` in monitoring, statistics, users and permissions, or search | Refreshes the tab (in search, it searches). |

### Typing in the editor

| Shortcut | What it does |
|---|---|
| `Ctrl+Space` | Opens autocomplete. |
| `Enter` or `Tab` | With autocomplete open, picks the suggestion. |
| `Tab` | After a template abbreviation, expands it. |
| `Ctrl+J` | Picks a template from the list. |
| `Ctrl+Shift+L` | Formats the SQL. |
| `F12` | Opens the object whose name is under the cursor. |
| `Ctrl+click` | Opens the object whose name you click. |
| `Ctrl+/` | Comments or uncomments the selected lines (or the cursor line). On a Spanish keyboard, it is the key in that position, `-`. |
| `Ctrl+D` | Duplicates the selection or, without a selection, the cursor line. |
| `Alt+↑` / `Alt+↓` | Moves the selected lines (or the cursor line) up or down. |
| `Ctrl+Shift+U` | Turns the selection (or the word at the cursor) to uppercase. |
| `Ctrl+U` | Turns the selection (or the word at the cursor) to lowercase. |
| `Ctrl+G` | Goes to a line. |
| `Ctrl+Z` | Undoes. Commenting, duplicating, moving lines and changing case are each undone with a single `Ctrl+Z`. |
| `Ctrl` + mouse wheel | Enlarges or shrinks the font (if turned on in **Options**, **Editor** tab). |
| `Ctrl+0` | Resets the font to the size chosen in **Options**. |

### Searching

| Shortcut | What it does |
|---|---|
| `Ctrl+F` | In the editor, searches the text. With the grid selected, searches the results (ignoring case and accents). |
| `Ctrl+H` | Finds and replaces in the editor. |
| `F3` / `Shift+F3` | Next / previous match. |
| `Enter` / `Shift+Enter` | In the search box, next / previous match. In the replace box, replaces the next one. |
| `Tab` / `Shift+Tab` | With replace open, moves between the search box and the replace box. |
| `Esc` | Closes the search bar. |
| `Ctrl+Shift+F` | Searches metadata, in the database you touched last. |
| `Enter` in the search tab | In the box, searches; in the results, opens the selected one. `→` and `←` expand and collapse. |

### Tabs and panels

| Shortcut | What it does |
|---|---|
| `Ctrl+T` | New query, on the database you touched last (same as the **+** button). |
| `Ctrl+Shift+T` | Reopens the last closed query, with its database and its text. |
| `Ctrl+W` or `Ctrl+F4` | Closes the tab. A middle click on the tab does too. |
| `Ctrl+Tab` / `Ctrl+Shift+Tab` | Goes to the next / previous tab. |
| `Ctrl+O` | Opens a .sql file. |
| `Ctrl+S` | Saves the query to its file (if it doesn't have one yet, asks where). In an object tab, saves a copy of the DDL. |
| `Ctrl+Shift+S` | Save as. |
| `Ctrl+1` … `Ctrl+5` | Bottom panels: Results, Plan, Statistics, History and Messages. |
| `Ctrl+6` | Dependencies, in an object tab. |
| `Ctrl+7` | Data, in a table or view tab. |
| `F1` | Opens the manual chapter for whatever you are using. |

### Results grid

| Shortcut | What it does |
|---|---|
| `↑` `↓` `←` `→` | Moves between cells. |
| `PgUp` / `PgDn` | Moves up or down one screen. |
| `Home` / `End` | Goes to the first / last column. |
| `Ctrl+Home` / `Ctrl+End` | Goes to the first / last cell. |
| `Shift` + `↑` `↓` `PgUp` `PgDn` | Extends the row selection. |
| `Shift+click` / `Ctrl+click` on a row | Selects a range of rows / adds or removes single rows. |
| `Ctrl+A` | Selects all rows. |
| `Ctrl+C` | Copies the cell; with several rows selected, copies them all with headers. |
| `Ctrl+Shift+C` | Copies the row (or the selected rows) with headers. |
| `Ctrl+F` | Searches the results. |
| `Enter` or `Space` on a group row | Expands or collapses the group. |
| `Enter` / `Esc` in the filter row | Applies / discards the filter you typed. |
| Click a header | Sorts by that column (click again: reversed). `Ctrl+click` removes the sort. |
| Double-click the edge of a header | Fits the column width to its content. |
| Double-click a cell that is not being edited | Opens **View value…** with the whole cell. |
| `Shift` + mouse wheel | Scrolls sideways. |

Everything about the grid is in [Results](03-results.md).

### Data tab

Nothing is saved to the database until you click **Apply** (see [The Data tab](04-data.md)).

| Shortcut | What it does |
|---|---|
| `F2`, `Enter`, double-click or start typing | Edits the cell. |
| `Enter` | Saves the cell and moves down to the next row. |
| `Tab` / `Shift+Tab` | Saves the cell and moves to the one on the right / left. |
| `Esc` | Discards what you typed in the cell. |
| `Del` | Marks or unmarks the selected rows for deletion. |
| `Insert` | Adds a row at the very top and starts editing it. |
| `Enter` in the WHERE box | Reads the table again with that condition. |

### Database tree

| Shortcut | What it does |
|---|---|
| `Enter` or double-click on a database | Connects it and uses it in the tab. |
| `Enter` or double-click on an object | Opens it in its own tab (DDL and dependencies; procedures, triggers, views and functions are edited and compiled there). |
| `Enter` or double-click on a column | Types its name in the editor. |
| `Enter` or double-click on a folder or an object type | Expands or collapses it. |
| `→` / `←` | Expands / collapses the selected node. |
| Drag an object or a column to the editor | Types its name where you drop it. |
| Drag a database or a folder | Moves it to another folder (see [Moving databases and folders](01-start.md#moving-databases-and-folders)). |

## Privacy and security

FbStudio stores on your computer what it needs to work: the registrations, the options, the history and little else.
Passwords are never written in plain text, and anything that may contain data (the SQL in the history and the
remembered queries) is encrypted.

### Passwords

Registration passwords are not stored in the registrations file but in each system's keyring, and they are never shown
again:

| System | Where they are stored | What you need |
|---|---|---|
| Windows | DPAPI, scoped to your user: only your user on that PC can decrypt them. | Nothing. |
| Linux | The desktop keyring (Secret Service: GNOME Keyring, KWallet…), through the `secret-tool` command. | `libsecret-tools` and a keyring. |
| macOS | The user's Keychain, under the service name "FbStudio". | Nothing. |

- On Linux, the password reaches `secret-tool` through standard input, never on the command line (where it would be
  visible with `ps`). FbStudio looks for `secret-tool` only in the absolute folders of the PATH, never in the folder it
  was started from. At startup, it checks that the keyring really stores secrets. A locked keyring may ask for your
  password in a window.
- On macOS, if the application changes (for example, another unsigned version), the system asks whether to let it read
  the Keychain.
- A password written in SQL (`CREATE USER … PASSWORD '…'` or `ALTER USER … SET PASSWORD '…'`) is replaced with
  asterisks in the history and in the error log. What gets executed does not change.

### History and remembered tabs

- The statement history is stored in `history.db`. The SQL and the error messages are encrypted; the plan, the timings
  and the row counts are not. Whatever is deleted (because it expired) is overwritten with zeros in the file.
- The remembered query tabs are stored in `session.bin`, encrypted the same way as the history. With the option turned
  off, that file is deleted on close.
- On Windows they are encrypted with DPAPI, using your account. On Linux and macOS, with AES-256 and HMAC-SHA256, using a
  random key stored in the keyring.
- How long the history is kept is set in **Options**, **History** tab, and each registration can choose not to keep it.

### Without a keyring

If the system has no keyring (for example, a Linux machine without a desktop), FbStudio still works, but:

- Passwords are remembered only while FbStudio is open; when you reopen it, you have to type them again in each
  registration. They are never written in plain text.
- The history is not saved.
- The remembered tabs live only in memory: they are not reopened next time.
- The status bar warns you at startup, and you are also warned when you import registrations with passwords.

On Linux, install GNOME Keyring or KWallet and `libsecret-tools`.

### Backing up registrations

The **Export registrations…** file without passwords is readable JSON: it contains servers, databases and, if you
chose to include them, user names. With passwords, the whole content is encrypted with the passphrase you choose
(AES-256 with HMAC-SHA256, and the key derived with PBKDF2-SHA256 using 600,000 iterations). Everything is encrypted,
not just the passwords, so nobody can change a registration's server in the file and capture its password. The
passphrase is not stored anywhere. This is the way to take your registrations, with their passwords, to another
computer. How to use it: [Exporting and importing registrations](01-start.md#exporting-and-importing-registrations).

### The FbStudio folder

Everything FbStudio stores is in a folder under your user:

| System | Folder |
|---|---|
| Windows | `%APPDATA%\FbStudio` |
| Linux and macOS | `~/.config/FbStudio` |

On Linux and macOS, the folder and its files are accessible only to your user (permissions 0700 and 0600). On Windows,
your profile folder is already private.

| File | What it is for |
|---|---|
| `profiles.json` | The database registrations: name, server, port, database, user, role, charset, environment, folder, encryption, read-only and history. No passwords. |
| `settings.json` | The options, plus what is remembered outside the Options window (folder colors, recent files…). |
| `secrets` | Windows only: a folder with the password of each registration, encrypted with DPAPI, in one `.bin` file per registration. |
| `history.db` | The statement history, with the SQL and the errors encrypted. |
| `session.bin` | The remembered query tabs, encrypted. |
| `grid-layouts.json` | The column order, widths and hidden columns of the Data tab, per database and table (the last 500 tables). |
| `monitor-queries.json` | Your own monitoring queries; they apply to all databases. |
| `areas` | A folder with the areas of each database, one `.json` per registration. A database that uses another areas file (for example, one shared by the team) reads it from wherever you chose. |
| `mcp-log.jsonl` | The calls of AI assistants to the MCP connector; what they asked for and the errors, encrypted (see [The call log](10-assistants.md#the-call-log)). |
| `errors.log` | Unexpected errors, with passwords masked, to attach to a bug report. Once it passes 1 MB, it is deleted and starts over. |

Outside that folder, FbStudio writes the manual (**Help › Manual** and `F1`) as HTML into a `FbStudio` folder inside the
system's temporary folder, so it can open in the browser without internet access. Whatever you export or save goes
wherever you choose.

> **Note:** copying the folder to another computer does not take the passwords with it (they are in the keyring, or
> encrypted for your user on that PC). To move your registrations, use **Export registrations…**.

### Server paths

In statistics, maintenance and trace, FbStudio replaces the database path on the server with what the registration's
**Database (alias or path)** field says, so where the files live is not on display. If you typed the path in that field
instead of an alias, that path shows: that is why it is best to register databases with their alias.
