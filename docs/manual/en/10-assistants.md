# AI assistants (MCP)

FbStudio can act as a connector for an AI assistant compatible with MCP (Model Context Protocol). The assistant asks
FbStudio about the structure, the performance and the server details of the databases you enabled, and uses that to help
you write and optimize queries, review procedures or measure the impact of a change. It never receives table data and
cannot change anything.

## What the assistant sees and what it does not

FbStudio offers it these tools:

| Tool | What it returns |
|---|---|
| `list_databases` | The enabled databases, with their environment and their areas. |
| `get_schema` | The objects of a database: the tables, or those of an area, of some kinds or matching a pattern (`LAB_*`). Big databases come 400 objects at a time. |
| `describe_object` | The DDL of a table (columns, keys, indexes, triggers, comments and logical foreign keys) or the source code of a view, procedure, function, trigger or package. |
| `search_metadata` | Where a text appears in names, columns, comments and source code. It does not search the data. |
| `get_dependencies` | What an object uses and what uses it. |
| `find_column_usage` | Where a column is used. |
| `find_join_path` | The shortest chain of relationships between two tables, with the JOINs written. |
| `explain_query` | The plan of a SELECT, WITH, INSERT, UPDATE, DELETE, MERGE or EXECUTE BLOCK statement. |
| `get_database_info` | The server version and architecture, ODS, dialect, page size, charset, cache, sweep, forced writes, read-only flag and nbackup state. |
| `get_monitor_snapshot` | A snapshot of the activity (MON$): transaction counters, the OAT with its age and who holds it, the oldest open transactions, the running statements (with their SQL without values), those waiting on a locked record and the connections by program. |
| `get_statistics` | The header statistics (counters, gaps, sweep, attributes) and, for the tables you ask for (up to 20), records, versions, pages and indexes, flagging what is worth checking. |

What it never sees or does:

- **Table data.** There is no tool that runs SQL or reads rows, not even a `COUNT`.
- **Changes.** Everything is read in read-only transactions. For the plan, FbStudio prepares the statement and discards
  it: it does not run it, fire triggers or move generators. It does not kill connections or cancel statements either.
- **The values in other programs' SQL.** In the monitoring snapshot, the SQL of each running statement comes with texts
  and numbers replaced by `?`, without comments. Each program name comes without its folder.
- **The connection.** It does not see the server, the database path, the user or the password: it names the database by
  its registration name.

## Enabling a database

Open the registrations window (**Database › Register database…**), pick the registration, tick **Available to AI assistants (MCP)** and save. It is
off in every registration. The password has to be saved: the assistant cannot type it.

A production registration can be enabled too: the assistant sees no data, and knowing the structure or the plan of a
query is useful precisely there.

## Connecting the assistant

In **Tools › Options…**, **Assistants (MCP)** tab, the configuration is ready to copy with **Copy**. It is the standard
MCP configuration, with the path of this FbStudio:

```json
{
  "mcpServers": {
    "fbstudio": {
      "command": "C:\\FbStudio\\FbStudio.exe",
      "args": ["mcp"]
    }
  }
}
```

It goes in the assistant's MCP server configuration. If the assistant reads a `.mcp.json` file in each project folder, add
the `fbstudio` entry inside `"mcpServers"`, next to the ones already there. It has no server, user or password, so that
file can be shared without exposing anything.

- With the Linux `.deb`, the command is `fbstudio`.
- On macOS, `/Applications/FbStudio.app/Contents/MacOS/FbStudio`.
- If you move FbStudio to another folder, copy the configuration again.

The assistant starts FbStudio by itself, with no window, and talks to it through standard input and output: FbStudio
does not need to be open and no port is opened. The first time, the assistant usually asks you to approve the new
server. To try it, ask it which databases you have enabled in FbStudio.

### Windows: assistants installed as an MSIX package

If the assistant is an app installed as an MSIX package, Windows shows the programs it starts their own copy of
`%APPDATA%`. If FbStudio was ever opened from that app, an old copy of its data was left there and hides the real one:
the assistant sees old registrations and databases are missing.

- FbStudio detects it and tells the assistant.
- To fix it, close the assistant and rename the `FbStudio` folder in
  `%LOCALAPPDATA%\Packages\<app package>\LocalCache\Roaming`. Renaming it instead of deleting it lets you go back.
- The connector does not write to `%APPDATA%` except for its call log, so the copy does not come back from using it.
- An assistant started from a regular terminal does not have this problem.

### Linux and macOS

- **Linux:** passwords come from the desktop keyring (GNOME Keyring or KWallet), so the assistant has to run inside the
  desktop session. Over SSH or on a computer without a desktop there is no keyring, and the tools say the password is
  missing.
- **macOS:** before setting it up, open FbStudio once from the Finder (**Open Anyway**); otherwise Gatekeeper blocks the
  executable when the assistant starts it. The first time, and after each new version, macOS asks whether FbStudio may
  read the Keychain: choose **Always Allow**.

## The answers

- Each request has 30 seconds at most; if it takes longer, it is cancelled and the assistant gets a note. The statistics
  of some tables have 2 minutes, because they read all their pages (on a big database that loads the disk: ask only for
  the ones you need).
- In monitoring, who holds each connection (user, computer and program) is shown or not depending on the **In
  monitoring, show who holds each connection** option of the **Assistants (MCP)** tab, on by default. It helps to find who
  holds back the OAT; off, the assistant only sees connection numbers.
- An answer longer than the maximum is cut, with a note on how to ask for less (an area, a kind, a pattern or the next
  page). The maximum is set in the **Assistants (MCP)** tab: 40,000 characters by default, between 5,000 and 200,000, for
  new assistant sessions.
- Dependencies of more than 60 objects are grouped by kind on one line, without details, to use less.
- The connection to each database opens with the first request and closes by itself after 2 minutes without use. What
  FbStudio already read (the object list, the relationships) is reused while the connection stays open.
- Errors arrive without the server, the path or the user, and with quoted texts and the values of a duplicate key
  replaced by `?`.

## The call log

Every call from the assistant is logged: the date, the tool, the database, how long it took and the size of the answer.
What it asked for (the SQL of a plan, the text of a search) and the errors are stored encrypted like the history, and are
deleted after the same period (**Options › History**). Without a keyring, that part is not stored.

The recent calls are shown in the **Assistants (MCP)** tab of **Tools › Options…**. On Windows, the calls of an assistant
installed as an MSIX package appear too, even though they were written to that package's copy of `%APPDATA%`.

## Privacy

What the assistant receives leaves your computer for whoever runs the model:

- Object names, DDL, comments and the source code of procedures, triggers and views. If some code has a value written by
  hand, that value goes with the code.
- Plans and server details.
- In monitoring, the SQL of running statements (without values) and, if the option is on, the user, computer and program
  of each connection.
- The SQL the assistant itself writes to ask for a plan.

Table rows never leave. Enable only the databases the assistant needs.

## When something does not work

| What happens | What to do |
|---|---|
| The assistant does not find FbStudio. | Check the path in the configuration (copy it again from Options) and open a new assistant session. |
| "No database is enabled for assistants". | Tick **Available to AI assistants (MCP)** in the database registration. |
| On Windows, databases are missing or old registrations show up. | It is an MSIX package copy: see [Windows: assistants installed as an MSIX package](#windows-assistants-installed-as-an-msix-package). |
| "FbStudio has no saved password". | Type the password in the registration and save it. On Linux, the assistant has to run in the desktop session. |
| "The request took longer than 30 seconds". | Ask for less: an area, a kind or a name pattern. |
