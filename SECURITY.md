# Security Policy

## Supported Versions

| Version | Supported |
|---------|-----------|
| master  | ✅ Yes    |

## Reporting a Vulnerability

If you discover a security vulnerability in Vidly, please report it responsibly:

1. **Do NOT open a public issue.** Security issues should be disclosed privately.
2. **Email:** [online.saurav@gmail.com](mailto:online.saurav@gmail.com)
3. Include:
   - A clear description of the vulnerability
   - Steps to reproduce (or a proof-of-concept)
   - Impact assessment (what an attacker could do)
   - Suggested fix, if you have one

You should receive an acknowledgment within **48 hours** and a detailed response within **5 business days**.

## Security Considerations

Vidly is a demonstration/educational project. It uses an **in-memory data store** (no database) and is designed for local development. However, the following security measures are implemented:

### What's Protected

- **Anti-Forgery Tokens** — All POST endpoints (`Create`, `Edit`, `Delete`) use `[ValidateAntiForgeryToken]` to prevent CSRF attacks.
- **Model Validation** — Server-side validation with `[Required]` and `[StringLength]` attributes prevents invalid data from entering the system.
- **Input Constraints** — URL route constraints (`year:range(1888,2100)`, `month:range(1,12)`) prevent out-of-range input in the `ByReleaseDate` action.
- **Defensive Copying** — The `InMemoryMovieRepository` clones objects when returning them, preventing external mutation of internal state.
- **Thread Safety** — Repository operations use `lock` statements to ensure atomic writes and prevent race conditions.

### Known Limitations (By Design)

- **No Authentication/Authorization** — This is a demo app; all routes are publicly accessible.
- **No HTTPS Enforcement** — Runs on HTTP via IIS Express for local development.
- **In-Memory Storage** — Data is lost on application restart. Not for production use.
- **No Rate Limiting** — No protection against brute-force or DoS (appropriate for a demo).

### Dependencies

This project uses several NuGet packages. Known considerations:

| Package | Version | Notes |
|---------|---------|-------|
| jQuery | 1.10.2 | Legacy version — used for demo purposes only |
| Bootstrap | 3.0.0 | Legacy version — Lumen theme dependency |
| Newtonsoft.Json | 6.0.4 | Older version; update recommended for production use |
| Application Insights | 2.2.0 | Telemetry SDK — review data collection settings |

**Dependabot** is configured to monitor for dependency updates and will open PRs for newer versions automatically.

## Best Practices for Extending

If you're building on top of Vidly for a real application:

1. **Add authentication** — Use ASP.NET Identity or OWIN middleware
2. **Enable HTTPS** — Configure SSL in `Web.config` and add `RequireHttpsAttribute`
3. **Update dependencies** — Especially jQuery, Bootstrap, and Newtonsoft.Json
4. **Add CSP headers** — Prevent XSS via Content Security Policy
5. **Replace in-memory store** — Use Entity Framework with a proper database
6. **Add logging** — Structured logging with Serilog or NLog for audit trails
