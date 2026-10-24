# Contributing to FbStudio

Thanks for wanting to help! There are several ways, and most of them need no programming. English and Spanish are both
welcome everywhere.

## Reporting a bug

Open an issue with the **Bug report** form. What helps most:

- what you did, what you expected and what happened instead;
- the FbStudio version (Help → About) and the Firebird version and server type (shown in the tree after connecting);
- if possible, steps to reproduce it with a test database.

**Do not paste real data** (customers, patients, passwords, internal server names): replace it with sample data.

## Requesting a feature

Open an issue with the **Feature request** form: explain what you want to do and why, not only how. If another tool
already does it, describe the behavior in your own words (there is no need, and it is better not, to paste screenshots or
text from other products).

Requests are triaged with labels: `bug`, `enhancement`, `priority: high / medium / low`, `needs info`,
`good first issue`. To discuss an idea before requesting it, use **Discussions**.

## Contributing code

1. Open an issue first (or comment on an existing one) to agree on the approach, so nobody does extra work.
2. Fork the repository and create a branch for your change.
3. Requirements: the .NET 10 SDK and Docker (the integration tests start Firebird 3 in a container).
4. Before opening the pull request:
   - `dotnet build` with no new warnings and `dotnet test` green;
   - a test covering the change (core, integration with Firebird in Docker, or headless UI);
   - UI texts in `Strings.resx` (Spanish, the neutral language) **and** `Strings.en.resx` (English), with the same keys.
5. One pull request per topic, describing what changes and how it was tested.

Project rules:

- **Nothing from other products:** no code, files, icons or text copied from other tools (commercial or not) unless
  their license allows it. FbStudio is designed from Firebird's public documentation.
- **Never** test against real or production databases: Docker only.
- Passwords are never stored in plain text or written to logs.

## License of contributions

FbStudio is published under Apache 2.0 with the Commons Clause condition (see [LICENSE](LICENSE)). By opening a pull
request you agree that your contribution is published under that same license, and you confirm you have the right to do
so (the code is yours, or its license allows it).

---

## En español

- **Reportar un error:** formulario *Bug report*. Contá qué hiciste, qué esperabas y qué pasó, y la versión de FbStudio y
  de Firebird. **No pegues datos reales**: cambialos por datos de ejemplo.
- **Pedir una función:** formulario *Feature request*. Contá qué querés hacer y para qué; si otra herramienta lo hace,
  describilo con tus palabras.
- **Aportar código:** primero un issue para acordar el enfoque. Hace falta el SDK de .NET 10 y Docker. El pull request
  tiene que dejar `dotnet test` en verde, traer un test y tener los textos en los dos `.resx`.
- **Reglas:** nada copiado de otros productos, nunca probar contra bases reales, contraseñas nunca en claro.
- **Licencia:** al mandar un pull request aceptás que tu aporte se publique bajo la misma licencia del proyecto.
