# Design: Autenticación y Sesión (BACKLOG #2)

> **Size note.** Deliberately over the 800-word SDD budget, for the same reason item #1's design was:
> this change stands up the first HTTP artifact of the project and the first credential path. Eight
> decisions are carried with visible rationale because every later item inherits them, and a silent
> default here is a silent default in sixteen downstream items.
>
> **Revision 2.** Decision 8 is new: the user fixed the lockout escalation sequence (15 → 30 → 60 →
> 120 min, capped) and ruled that a lock expiry must be followed by a *margin* of failures rather than
> an immediate re-lock. Those two rulings are not jointly satisfiable by the single-counter formula
> revision 1 carried, so Decision 8 replaces it and adds one column. Decision 5's ports block is
> updated in the same pass so no stale signature survives.

## Technical Approach

A hexagonal slice in three new .NET projects plus one new versioned SQL script. `SmartNet.Auth.Core`
holds the login/lockout decision logic and the ports, with no reference to SQL, HTTP or the ambient
clock (ADR 0019 level 1). `SmartNet.Auth.Infrastructure` implements those ports over
`Microsoft.Data.SqlClient` and Argon2id. `SmartNet.Api` (Minimal APIs, `net10.0`) and `SmartNet.Admin`
(console) are two thin drivers of the same core. `011_sesion.sql` adds `fact.Sesion` **and its grants
in the same file**, per the forward rule item #1's design already wrote down.

Session state is a real server-side store reached through ASP.NET Core's `ITicketStore`, so ADR 0007's
"el cierre de sesión invalida la sesión del lado del servidor" is structural — no row, no principal —
rather than a property some event handler has to keep remembering to enforce.

---

## Decision 1 — Argon2id library: `Konscious.Security.Cryptography.Argon2`

| Option | What it costs / buys | Verdict |
|---|---|---|
| **`Konscious.Security.Cryptography.Argon2`** | MIT. Pure managed C#, no native binary and no P/Invoke — matters for a self-contained publish on Windows and for the throwaway-database test path. `netstandard2.0/2.1`, so it loads on `net10.0`. Widest install base of any .NET Argon2, therefore the most eyes. Ships the raw KDF only: **no PHC encode/decode** | **Chosen** |
| `Isopoh.Cryptography.Argon2` | Ships PHC `Hash()`/`Verify()` and `SecureArray` memory zeroing/locking, which is genuinely more than Konscious gives. Rejected on two counts: `SecureArray` calls the OS locked-memory APIs and has a real history of failing where the locked-pages quota is low (containers, restricted service accounts) — a startup failure mode bought for a benefit we do not need on a single-user internal host; and the one-call static API hides the RNG and the encoding inside infrastructure, which is exactly the seam we want on the core side | Rejected |
| BCL / `Microsoft.AspNetCore.Identity.PasswordHasher` | Zero dependencies. Rejected: it is PBKDF2, and the user fixed Argon2id | Rejected |
| A native `libargon2` binding | Reference C implementation. Rejected: a native asset per RID in a project that otherwise has none | Rejected |

**The missing PHC codec is treated as a benefit, not a gap.** `002_seguridad.sql` already fixes
`ClaveHash` as a PHC string, so the encoder/parser has to exist regardless. Putting it in
`SmartNet.Auth.Core` makes format handling — including a malformed or unknown-algorithm hash — pure,
deterministic and unit-testable with zero infrastructure, which is precisely ADR 0019 level 1. Only
the raw transform crosses into infrastructure.

**Parameters:** `m = 19456 KiB, t = 2, p = 1`, 16-byte salt, 32-byte output. Encoded
`$argon2id$v=19$m=19456,t=2,p=1$<salt>$<hash>` ≈ 96 chars — comfortably inside `NVARCHAR(200)`, so
`002_seguridad.sql` needs no change. Raising the work factor later is a value change, not a migration,
because the parameters travel in the string.

**Verification gate, not an assumption.** This document cannot query nuget.org. Before the
`PackageReference` is added, the implementer confirms the current version, that the licence is still
MIT, and that no first-party `System.Security.Cryptography` Argon2 shipped in .NET 10 — if one did, it
wins and this decision is reversed in writing. Carried into `tasks.md` as a gate, not left implicit.

**Gate closed (task 0.1, verified 2026-08-16, with network access).**

- **Version pinned:** `1.3.1` (published 2024-06-19T14:48:43Z) — current published version on
  nuget.org at verification time; no newer version exists.
- **License:** confirmed `MIT` (nuspec `<license type="expression">MIT</license>`, matching
  `licenses.nuget.org/MIT`) — unchanged from the assumption above.
- **Maintenance — confirmed but flagged, not simply "clean".** The GitHub repo
  (`kmaragon/Konscious.Security.Cryptography`) is **not archived and not disabled**, 277 stars, 20
  open issues, no closed-as-abandoned signal. However: the `master` branch's last commit is
  **2024-06-18** — over two years stale as of this verification — and there is an **open, unmerged PR
  ("Add .NET 10 target", #66, last activity 2026-06-17)** that has not been reviewed or merged by the
  maintainer. No GitHub Releases are published (versions ship straight to NuGet from tags). Read
  plainly: the package still installs and runs correctly on `net10.0` today via its `net8.0`
  dependency group (NuGet resolves the nearest compatible TFM; there is no `net10.0`-specific asset,
  and none is required for correctness), but the project shows weak maintainer responsiveness. This is
  a real but non-blocking risk, not a reason to reverse Decision 1: pure managed C#, MIT, no native
  binary, and — see below — no first-party alternative exists. **Recorded, not hidden**, because
  `SmartNet.Auth.Infrastructure` (Work Unit 4) inherits this dependency and a future re-evaluation
  should re-check this repo's activity before assuming it is still the best option.
- **First-party .NET 10 Argon2id — confirmed it did NOT ship.** Checked
  `dotnet/core` release notes and `docs/core/whats-new/dotnet-10/libraries.md` (Cryptography section):
  .NET 10's cryptography additions are certificate-thumbprint lookup by non-SHA-1 algorithms, PEM/PKCS#12
  changes, AES KeyWrap with padding, and post-quantum cryptography (ML-DSA, CNG support). **No Argon2
  of any kind.** Cross-checked against `dotnet/runtime` issue **#19933** ("Add Argon2 support to
  System.Security.Cryptography"), which is **still open**, sitting in the generic "Future" milestone
  with no shipped-release attached. Decision 1 stands **unreversed**.

Paper trail for Work Unit 4's `PackageReference`:
`<PackageReference Include="Konscious.Security.Cryptography.Argon2" Version="1.3.1" />`.

---

## Decision 2 — `fact.Sesion`: new script `011_sesion.sql`, never an edit to `008`

Editing `008` is not a checksum inconvenience — **it is a change that does not deploy.** DbUp journals
a script by *name* (item #1's design, Decision 1, cost 1): an amended `008` is silently skipped on
every database that already ran it, so the `fact.Sesion` grants would exist in a fresh database and
nowhere else. `ChecksumManifestTests.RealManifest_MatchesTheRealScripts_Exactly` would additionally
turn that edit into a hard build **error** ("content has changed since it was hashed"), which is the
manifest doing its job. A new script produces only the documented transient **warning** until
`generate-checksums.ps1` is re-run. `DboWriteLintTests` scans `SmartNet/db/schema/*.sql` top-level, so
`011` is covered with no test change.

This is also the rule item #1 already wrote for exactly this situation: *"a new table's grants ship in
the same numbered file as its DDL, so a table can never exist without its permissions."* `008` having
its own file was the stated exception for the initial change.

```sql
CREATE TABLE fact.Sesion
(
    SesionId          BIGINT IDENTITY(1,1)                     NOT NULL,
    -- SHA-256 hex (minuscula) del token que viaja en la cookie. El token en claro NUNCA se
    -- almacena: misma disciplina que ClaveHash. CHAR(64) + BIN2 replica design.md item 14
    -- (HashContenido) -- busqueda byte-exacta y sin dependencia de la collation compartida.
    TokenHash         CHAR(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
    UsuarioId         BIGINT                                   NOT NULL,
    CreadaEn          DATETIME2(3) NOT NULL CONSTRAINT DF_Sesion_CreadaEn DEFAULT (SYSUTCDATETIME()),
    ExpiraEn          DATETIME2(3)                             NOT NULL,
    UltimaActividadEn DATETIME2(3)                             NOT NULL,
    RevocadaEn        DATETIME2(3)                             NULL,
    MotivoRevocacion  VARCHAR(20)                              NULL,
    -- Ticket de autenticacion serializado (ITicketStore), en Base64. NVARCHAR y no VARBINARY:
    -- design.md item 9, legibilidad en una ventana de consulta.
    Ticket            NVARCHAR(MAX)                            NOT NULL,
    CONSTRAINT PK_Sesion PRIMARY KEY (SesionId),
    CONSTRAINT UQ_Sesion_TokenHash UNIQUE (TokenHash),
    CONSTRAINT FK_Sesion_Usuario FOREIGN KEY (UsuarioId) REFERENCES fact.Usuario (UsuarioId),
    CONSTRAINT CK_Sesion_Revocacion CHECK
        ((RevocadaEn IS NULL AND MotivoRevocacion IS NULL)
         OR (RevocadaEn IS NOT NULL AND MotivoRevocacion IS NOT NULL)),
    CONSTRAINT CK_Sesion_MotivoRevocacion CHECK
        (MotivoRevocacion IS NULL
         OR MotivoRevocacion IN ('CIERRE_SESION', 'RESTABLECIMIENTO', 'ADMIN'))
);

CREATE INDEX IX_Sesion_UsuarioId_Activa
    ON fact.Sesion (UsuarioId, ExpiraEn)
    WHERE RevocadaEn IS NULL;
```

`UQ_Sesion_TokenHash` **is** the hot-path index: every authenticated request is one unique seek. The
filtered index serves the one other query that exists — "revoke every live session of this user",
which the reset command must run. No index for the purge: it scans a table that grows by ~1–2k rows a
year, and an index for it would be premature. The paired `CHECK` is the same discipline
`AdjuntoManual` uses: a revocation without a recorded reason cannot be stored. Timestamps are
`DATETIME2(3)` UTC, per item #1's global rule. Naming follows `CONVENTIONS.md`: `PascalCase`, no
accents, no `ñ`. Zero references to `dbo`. CRLF, like every other `.sql`.

`011` also ships `rollback/011_down.sql` (advisory, never applied) and a new `checksums.txt` line.

---

## Decision 3 — Logout **revokes**; a separate command purges

| Option | Tradeoff | Verdict |
|---|---|---|
| **`UPDATE … SET RevocadaEn, MotivoRevocacion`** | Keeps the record that a session existed and how it ended, which is the only trace of "the account was used" this system will have. Idempotent and atomic. Needs no verb the API does not already hold. Cost: rows accumulate | **Chosen** |
| `DELETE` the row on logout | Self-trimming, no retention question. Rejected: it destroys the one audit trail, and — decisively — `008` grants **no `DELETE` on any table to either role**; DELETE appears only inside `DENY` lines. Handing the API's request path a destructive verb, for housekeeping it can already achieve with an `UPDATE`, widens the blast radius of every future endpoint bug in the host | Rejected |

**Retention.** `GRANT DELETE ON OBJECT::fact.Sesion TO fact_api` is added, and it is the **only**
`DELETE` grant in the whole matrix. It exists for one caller: the `sesion purgar` verb of
`SmartNet.Admin` (Decision 7), a deliberately invoked operator command with a retention argument.
No HTTP path ever issues a `DELETE` — that is a review property, not an engine property, and it is
declared as such rather than implied. The grant is justified because `fact.Sesion` is the one `fact`
table whose rows are pure operational ephemera: losing an expired session row costs the accounting
record nothing. Reversal, if the user prefers a matrix with zero `DELETE`, is one line in `011` plus
accepting unbounded (but tiny) growth.

`usr_worker` gets the identical explicit four-verb `DENY` already applied to `fact.Usuario`:
`DENY SELECT, INSERT, UPDATE, DELETE ON OBJECT::fact.Sesion TO fact_worker`.

---

## Decision 4 — Token: 256 random bits in the cookie, SHA-256 at rest

`RandomNumberGenerator.GetBytes(32)` → **256 bits**, Base64Url-encoded to 43 characters, which is what
the ticket in the `__Host-session` cookie carries. The database stores **`SHA256(token)` as lowercase
hex**, never the token.

**Why hashed:** the same reason `ClaveHash` is hashed. A `SELECT` — a support query, a backup file, an
over-broad grant — must not hand out live sessions. Verification only ever goes one way: hash what was
presented, seek the unique index. Cost, stated: nothing can reconstruct a token from a row, so
"re-issue this user's cookie" is not an operation that exists. That is the point.

**Why SHA-256 and not Argon2id for this one:** the token is 256 bits of uniform CSPRNG output, so it
is not dictionary- or brute-forceable, and a deliberately slow KDF would buy nothing while adding
~100 ms to **every authenticated request**. Argon2id is for low-entropy human secrets; a plain
cryptographic hash is the correct primitive for a high-entropy bearer token. Applying the slow KDF
here would be cargo-culting the previous decision, not applying it consistently.

**Sliding expiration, concretely.** ADR 0007 fixes `ExpireTimeSpan = 8h` and `SlidingExpiration =
true`. The cookie middleware — not our code — decides when to slide: it renews once more than half the
window has elapsed, and only then calls `ITicketStore.RenewAsync`. So the row is written on **create**
(INSERT), on **renew** (UPDATE `ExpiraEn`/`UltimaActividadEn`, at most once per ~4 h per session), and
on **revoke** (UPDATE). **Never on a plain read.** The authoritative freshness check happens on read
anyway (`WHERE TokenHash = @h AND RevocadaEn IS NULL AND ExpiraEn > @ahora`), so a write per request
would buy only a more precise `UltimaActividadEn`, which nothing consumes. Declared cost:
`UltimaActividadEn` is accurate to the renewal granularity, so it is a diagnostic field and must not
be documented as an audit-grade "last seen".

**Data-protection key ring — the failure this decision would otherwise hide.** The cookie's ticket is
protected by ASP.NET Core Data Protection. With the default ephemeral/profile-local key ring, a host
restart invalidates every cookie — which would quietly defeat the entire reason a *table* was chosen
over an in-memory store. Therefore: `PersistKeysToFileSystem(<path from configuration>)`, on a
directory that must be added to ADR 0014's backup set. Stated here because nothing else in the design
would surface it.

**Gate closed (task 0.2).** The path itself is deployment scoping, not code — Work Unit 5 is where
`PersistKeysToFileSystem` is actually called (task 4.10 / 4.8's gate check). What is fixed here:

- **Resolution pattern matches `SMARTNET_API_DB_CONNECTION` (Decision 6) and ADR 0012/0013's "raíz
  configurable" for the document volume**: an environment variable, `SMARTNET_API_KEYRING_PATH`, read
  exactly like the connection string — no default, no committed fallback, startup failure with a usage
  message if absent. A hardcoded path in `Program.cs` was rejected for the same reason
  `SMARTNET_API_DB_CONNECTION` is not hardcoded: the value differs per environment (ADR 0012's separate
  test/production environments), and a committed path invites the same "works on my machine, fails in
  prod" class of bug a committed connection string would.
- **Recommended concrete value for this project's single-instance Windows deployment** (ADR 0012: one
  host, reverse proxy in front of Kestrel, no container orchestrator):
  `C:\ProgramData\SmartNet\dataprotection-keys`. `ProgramData` is the standard Windows location for
  machine-wide application state that must outlive a user profile or service-account login session —
  the same property the key ring needs, since Kestrel may run under a dedicated service account whose
  profile is not guaranteed to persist. This mirrors the project's existing naming discipline: a
  `SmartNet` root followed by a component-scoped subfolder, the same shape `SmartNet/db/schema/` and
  `SmartNet/db/fixtures/` already use inside the repo — except this directory lives on the deployment
  host's filesystem, **never inside the Git checkout**, because (a) key material must never be
  committed and (b) a `git pull`/redeploy that replaces the working tree must not be able to delete or
  rotate the keys out from under a running host.
- **Directory must exist and be writable by the account Kestrel runs as before first startup** —
  `PersistKeysToFileSystem` does not create parent directories reliably across all deployment
  scripts, so provisioning this path is a deployment-checklist item, not an assumption the API host
  makes silently.
- **Why this does not need code yet.** Fixing the *value* of `SMARTNET_API_KEYRING_PATH` and its
  filesystem location is independent of the `PersistKeysToFileSystem(...)` call site; task 4.10 wires
  the call, this gate only removes the "someone will figure out a path in production" open question
  Decision 4 originally left implicit.
- **ADR 0014 addition.** Recorded directly in ADR 0014's backup set (see that ADR's Revisión 4) with
  the justification: losing this directory invalidates every live session cookie on restart, silently
  defeating the reason `fact.Sesion` was chosen over an in-memory store (the same failure mode this
  paragraph opened with).

---

## Decision 5 — Domain core: `SmartNet.Auth.Core`, ports-and-adapters

```
SmartNet/
  auth/
    SmartNet.Auth.Core/            classlib, net10.0 — ZERO infrastructure package references
    SmartNet.Auth.Core.Tests/      xUnit — no DB, no HTTP, no ambient clock
    SmartNet.Auth.Infrastructure/  classlib — Microsoft.Data.SqlClient + Konscious Argon2
  api/
    SmartNet.Api/                  Microsoft.NET.Sdk.Web, net10.0
    SmartNet.Api.Tests/            xUnit — WebApplicationFactory + the existing TestDatabaseFixture
  admin/
    SmartNet.Admin/                console, net10.0, OutputType=Exe
  db/                              unchanged
SmartNet.sln                       new — the repo has no solution file today
```

**Naming.** English throughout, with entity nouns spelled exactly as the schema spells them
(`Usuario`, `Sesion`, `ClaveHash`, `IntentosFallidos`, `BloqueadoHasta`). `CONVENTIONS.md`'s own
boundary test decides it: *"¿Existiría igual en cualquier otro proyecto? → inglés"* — authentication,
sessions and lockout would exist identically in any project and appear nowhere in `REGLAS.md` or the
plan de cuentas. Keeping the entity nouns in Spanish is the same 1:1-with-the-normative-source
argument `CONVENTIONS.md` makes for `REGLAS.md`, applied where the normative source is
`002_seguridad.sql`. `SmartNet.Autenticacion.Dominio` was rejected under the document's own *"no
traduzcas a medias"* rule and against the existing precedent (`SmartNet.Db.Runner`,
`SmartNet.Db.TestBootstrap`).

**Ports exposed by the core** (illustrative signatures — shapes, not implementations):

```csharp
public interface IUsuarioRepository
{
    Task<UsuarioCredentialState?> FindByNameAsync(string nombreUsuario, CancellationToken ct);
    Task SaveCredentialStateAsync(UsuarioCredentialState estado, CancellationToken ct);
    Task UpdateClaveHashAsync(long usuarioId, string claveHash, CancellationToken ct);
}

public interface ISesionRepository            // ITicketStore is an ADAPTER over this, not the port
{
    Task CreateAsync(long usuarioId, string tokenHash, DateTimeOffset expiraEn, string ticket, CancellationToken ct);
    Task<SesionActiva?> FindActiveAsync(string tokenHash, DateTimeOffset ahora, CancellationToken ct);
    Task RenewAsync(string tokenHash, DateTimeOffset expiraEn, DateTimeOffset ahora, CancellationToken ct);
    Task RevokeAsync(string tokenHash, MotivoRevocacion motivo, DateTimeOffset ahora, CancellationToken ct);
    Task RevokeAllForUsuarioAsync(long usuarioId, MotivoRevocacion motivo, DateTimeOffset ahora, CancellationToken ct);
}

public interface IPasswordHasher                       // Argon2id lives behind this
{
    string Hash(string clave);                          // → PHC string
    PasswordVerification Verify(string clave, string phc);
}

public interface ISessionTokenFactory                  // CSPRNG lives behind this
{
    (string Token, string TokenHash) Create();
    string HashOf(string token);
}

// The state the policy reads and rewrites — one field per persisted column, nothing derived.
// THREE lockout fields, not two: see Decision 8 for why IntentosFallidos alone cannot carry both
// "how many failures until the next lock" and "how long that lock will be".
public sealed record UsuarioCredentialState(
    long             UsuarioId,
    string           NombreUsuario,
    string           ClaveHash,
    int              IntentosFallidos,   // failures inside the CURRENT window; 0 while locked
    int              NivelBloqueo,       // locks already served; picks the NEXT duration
    DateTimeOffset?  BloqueadoHasta,
    bool             Activo);

// ADR 0007's numbers, in one place, so every spec.md scenario maps to one file.
public sealed record LockoutPolicy(
    int      UmbralFallos,      // 5   — ADR 0007, "cinco fallos consecutivos"
    TimeSpan DuracionBase,      // 15 min — ADR 0007
    int      Factor,            // 2   — doubling (user ruling, Decision 8)
    int      NivelMaximo)       // 3   — ceiling at 120 min (user ruling, Decision 8)
{
    public static LockoutPolicy Adr0007 { get; } = new(5, TimeSpan.FromMinutes(15), 2, 3);
}

// The heart. Pure, static, allocation-cheap, fully deterministic.
public static class AccessPolicy
{
    public static AccessDecision Evaluate(UsuarioCredentialState estado, DateTimeOffset ahora);

    // Takes the policy EXPLICITLY rather than reading a constant: it keeps the function total over
    // its inputs, and it lets a test drive the cap boundary without twenty-five calls. There is
    // exactly one production call site and it passes LockoutPolicy.Adr0007.
    public static UsuarioCredentialState ApplyFailure(
        UsuarioCredentialState estado, LockoutPolicy politica, DateTimeOffset ahora);

    public static UsuarioCredentialState ApplySuccess(UsuarioCredentialState estado);
}
```

**`ApplyFailure` gained a parameter in revision 2** — `Evaluate` did not, because expiry is still a
single comparison of `BloqueadoHasta` against `ahora` and needs no policy. `ApplySuccess` keeps its
signature but not its meaning: it now clears **three** fields (Decision 8).

**Clock: `TimeProvider`, not a hand-rolled `IRelojDominio`.** It has been in the BCL since .NET 8, and
`Microsoft.Extensions.TimeProvider.Testing`'s `FakeTimeProvider` gives deterministic control of the
15-minute lockout window and the 8-hour session window without inventing an abstraction the framework
already owns. ADR 0019's "sin dependencias de reloj" means the core must never read the ambient clock;
an injected `TimeProvider` is the compliant form of exactly that, and `AccessPolicy` takes `ahora` as
a parameter so the policy itself needs no clock at all.

**Login sequence, and the order is load-bearing:**

1. `FindByNameAsync`. **If the user does not exist, still run one Argon2id verification against a
   decoy PHC hash generated at startup from random bytes**, then return the standard failure. Without
   this, "invalid username and invalid password are indistinguishable" is true of the response body
   and false of the response *time* — a ~100 ms gap that enumerates usernames. The decoy must carry
   the same parameters as real hashes.
2. `AccessPolicy.Evaluate(estado, ahora)`. If `Locked`, return the standard failure **without
   verifying the password** — this is the success criterion "a 6th within that window is rejected
   without querying the password", and it holds because the check precedes the hash, not because
   anything short-circuits later.
3. Verify. On failure `ApplyFailure` → persist; on success `ApplySuccess` → persist, then create the
   session.

**Growing lockout does need a schema change — see Decision 8.** Revision 1 of this document derived
the tier as `floor(IntentosFallidos / 5)` and asserted that the two existing columns carried it. They
do not, once the escalation is required to survive a margin of post-expiry failures. Decision 8
supersedes that paragraph in full.

---

## Decision 6 — API host: Minimal APIs, no reference to the schema runner

| Option | Tradeoff | Verdict |
|---|---|---|
| **Minimal APIs** | Three endpoints in this item. `TypedResults` gives a testable, explicit surface with no controller/convention ceremony, and endpoints stay visibly thin — which is what ADR 0019 demands of them | **Chosen** |
| MVC Controllers | Model binding, filters, conventions — these pay off at item #11's scale (`If-Match`, `409`/`412`/`422`, many routes). Rejected **for now, not forever**: both hosting models coexist in one host, so item #11 adding controllers is additive and mechanical. Declared as a reversal point rather than pretended away | Deferred |

**The API host does not reference `SmartNet.Db.Runner`, and must not.** ADR 0012 orders schema → API →
worker: the runner runs as the deploy principal, exits, and the API then connects to an
already-migrated database as `usr_api`. No `EnsureCreated`, no migrate-on-startup, no schema DDL from
the web host — a runtime that can alter a shared database at boot is precisely the thing item #1's
permission boundary exists to prevent. A startup probe of `fact.SchemaVersions` was considered and
**rejected**: `fact_api` has no grant on the journal, and adding one so the runtime can inspect deploy
metadata couples the two on purpose. A missing table already fails loudly (SQL error 208) on the first
query.

**Routes.** ADR 0008 defines no authentication endpoints at all, so this design extends it. Per ADR
0008's cut rule, this is the one case where the resource *is* the operation:

```http
POST   /api/sesion     → 204 + Set-Cookie __Host-session   (login)
DELETE /api/sesion     → 204                               (logout, server-side revocation)
GET    /api/sesion     → 200 { nombreUsuario } | 401       (session probe)
```

`GET /api/sesion` is not scope creep: without one authenticated endpoint, the success criterion *"the
cookie no longer authenticates after logout"* has nothing to assert against, and the SPA's later boot
check would otherwise be re-decided in a downstream item. Errors are `application/problem+json` (ADR
0008). **Every** login failure — unknown user, wrong password, locked account — returns the identical
`401` problem document. Consequence, declared: a locked-out user is told nothing about the lock. That
is ADR 0007's deliberate choice and it inherits ADR 0007's declared DoS cost.

**Connection string: `SMARTNET_API_DB_CONNECTION`, parsed exactly like `RunnerOptions`** — environment
variable or explicit flag, no default, no committed fallback, absent ⇒ fail at startup with a usage
message. **Deliberately a different name from the runner's `SMARTNET_DB_CONNECTION`**: those two hold
*different principals*. Reusing the name would mean an operator who exported the deploy credential for
a migration silently hands the API host deploy-level rights on a shared database — the exact failure
the permission matrix exists to prevent. Same pattern, different key, on purpose. ADR 0015's secret
manager is what fills the variable in a real environment; building that port is out of scope here.

---

## Decision 7 — Reset is a CLI verb in a new `SmartNet.Admin` console project

| Option | Tradeoff | Verdict |
|---|---|---|
| **Verb in `SmartNet.Admin` (new console, `net10.0`)** | One more artifact to build and publish. Requires shell access to the host — which *is* the "administrador de la instancia" ADR 0007 names. Reuses `SmartNet.Auth.Core` + `.Infrastructure`, so the derivation is provably the same code the API uses | **Chosen** |
| Admin-only HTTP endpoint | Rejected on two independent grounds. (a) **It cannot bootstrap.** `fact.Usuario` is deliberately empty; there is no user to authenticate as, so the very first `usuario crear` is unreachable over an authenticated endpoint — and any gate that works without a user (static admin token, IP allowlist) is a second authentication surface invented for one operation, reachable from the network. (b) It needs an authorization concept ADR 0007 explicitly refuses to have (`No existen Rol ni UsuarioRol`), and the proposal names an admin UI/role system as a non-goal | Rejected |
| A verb bolted onto `SmartNet.Db.Runner` | Zero new projects. Rejected: the runner is a *schema* artifact running as the deploy principal at ADR 0012 step 1. Putting a credential-writing command in it re-couples schema deployment to application data and gives the deploy principal a reason to touch `fact.Usuario` | Rejected |

```
smartnet-admin usuario crear             --nombre <u>
smartnet-admin usuario restablecer-clave --nombre <u>
smartnet-admin sesion  purgar            --retencion-dias <n>
```

The password is read from an **interactive no-echo prompt (stdin), never from an argument** — argv
lands in shell history, in `ps`, and in Windows process-creation audit records, which would put the
new password in three logs at once. `restablecer-clave` also calls `RevokeAllForUsuarioAsync(…,
MotivoRevocacion.RESTABLECIMIENTO)`: a password reset that leaves the old cookie working is not a
reset. `SmartNet.Admin` connects with `SMARTNET_API_DB_CONNECTION` — it is "un comando de la propia
aplicación" (ADR 0007) and therefore runs with `usr_api`'s grants, not the deploy principal's.

---

## Decision 8 — Lockout escalation needs a second counter: `fact.Usuario.NivelBloqueo`

### The conflict revision 1 could not see

The user fixed two rules that revision 1's formula cannot hold at the same time:

1. **The sequence is 15 → 30 → 60 → 120 minutes, capped at 120.** Each successive lockout doubles the
   previous one until the ceiling.
2. **After a lockout expires there is a margin of failures before re-locking** — a single failure
   after expiry must *not* re-lock — **but the escalation level survives that margin.** If the margin
   is then exhausted, the new lock must still be longer than the previous one, per ADR 0007's
   "creciente en bloqueos sucesivos" and the scenario `spec.md` already carries.

Revision 1 used one column, `IntentosFallidos`, for two unrelated questions: *how many failures until
the next lock* (the counter) and *which tier applies* (`floor(IntentosFallidos / 5)`). Those two want
opposite lifetimes. Rule 2 requires the counter to return to zero when a lock expires, so the account
gets a fresh budget; but zeroing the counter is, in the same assignment, the erasure of the tier —
the next lock recomputes as tier 0 and the account is a first offender again. Keeping the counter
instead makes rule 2 impossible: at `IntentosFallidos = 5`, failure number six is `floor(6/5) = 1`
and re-locks immediately, which is exactly the behaviour the user rejected.

**No arithmetic on a single integer resolves this, because the two facts have different reset
events.** The counter resets at every lock; the tier resets only on proof of credential possession.
Two facts, two columns.

### The column

```sql
-- 012_usuario_nivel_bloqueo.sql
ALTER TABLE fact.Usuario
    ADD NivelBloqueo INT NOT NULL
        CONSTRAINT DF_Usuario_NivelBloqueo DEFAULT (0);
GO
ALTER TABLE fact.Usuario
    ADD CONSTRAINT CK_Usuario_NivelBloqueo CHECK (NivelBloqueo >= 0);
```

| Choice | Why |
|---|---|
| Name `NivelBloqueo` | Spanish, matching its four siblings on this table (`IntentosFallidos`, `BloqueadoHasta`, `Activo`, `CreadoEn`). `CONVENTIONS.md`'s boundary test nominally sends lockout to English, but Decision 5 already fixed the narrower rule that governs here: schema nouns are spelled as the schema spells them, and a lone `LockoutLevel` between `IntentosFallidos` and `BloqueadoHasta` is the "traducir a medias" the document forbids. `PascalCase`, no accents, no `ñ` |
| `INT`, not `TINYINT` | Symmetry with `IntentosFallidos`, at zero practical storage cost on a table holding one row. `TINYINT` would encode the cap in the type and make raising it a type change |
| `NOT NULL DEFAULT (0)` | 0 is "first offender", the correct state for every existing row. The default is what makes the migration additive: `fact.Usuario` is deliberately empty today, but the script must not depend on that |
| `CHECK (NivelBloqueo >= 0)` and **not** `BETWEEN 0 AND 3` | Guards a negative from a broken `UPDATE` without pinning the *policy* ceiling into the *schema*. The cap is 3 in `LockoutPolicy`, where a change is a value change and a re-run of the unit tests — not a migration |

**A new script, not an edit to `002_seguridad.sql`.** Decision 2's reasoning applies unchanged and
with more force: `002` is shipped and journalled by name, so an amended `002` is silently skipped on
every database that already ran it, and `ChecksumManifestTests` turns the edit into a build error.
`002` stays byte-for-byte as it is. This does **not** reopen item #1: adding a defaulted column via a
new numbered script is the same additive class ADR 0016 already sanctions and that `011_sesion.sql`
itself uses. Item #1's closed work is untouched.

**Also not folded into `011`.** `011` is `fact.Sesion`'s DDL plus its grants, one table per file. A
file named `011_sesion.sql` that quietly alters `fact.Usuario` is unsearchable and misnamed. `012`
ships `rollback/012_down.sql` (advisory, never applied) and a second new line in `checksums.txt`.

**No grant changes.** `008` grants and denies at object level — `GRANT SELECT, INSERT, UPDATE ON
OBJECT::fact.Usuario TO fact_api` and `DENY … TO fact_worker` — so the new column inherits both
automatically. Stated because the instinct on seeing a new column is to look for a matching grant,
and a *column*-level grant in `008` would have made this migration silently break `usr_api`'s reads.
It is object-level; verified against the shipped file, not assumed.

### The two fields, and exactly what each means

| Field | Question it answers | Increments | Resets to 0 |
|---|---|---|---|
| `IntentosFallidos` | How many failures remain before the next lock? | every failure evaluated while not locked | **when a lock is armed**, and on success/reset |
| `NivelBloqueo` | How long will the next lock be? | **when a lock is armed**, saturating at `NivelMaximo = 3` | only on success/reset — never by time passing |

**`IntentosFallidos` resets at *arming* time, not at expiry time.** This is the move that makes the
whole thing work without a background job: there is no "expiry event" for anything to hook, because
a lock simply ages out of `BloqueadoHasta > ahora`. It also keeps `IntentosFallidos` single-meaning —
"failures accumulated in the current window" — under every clock value. The rejected alternative,
holding the counter at 5 through the lock and lazily zeroing it on the first post-expiry attempt,
reintroduces the exact defect this decision exists to remove: a field whose meaning depends on
comparing a *different* field to `ahora`.

**Declared cost:** during a lock, `IntentosFallidos` reads `0`, which looks odd to an operator
querying the row mid-lock. The state is still fully legible — `NivelBloqueo = n` with `BloqueadoHasta`
in the future says "this account has just served its n-th trigger" — but it is a real readability
tax and it is the reason this paragraph exists.

**Spec delta, must be carried:** `spec.md`'s current scenario *"The 5th consecutive failure sets
BloqueadoHasta 15 minutes out"* asserts `IntentosFallidos` **becomes `5`**. Under this decision it
becomes `0` and `NivelBloqueo` becomes `1`. The follow-up spec pass must change that assertion.

### The margin, defined exactly

**The margin is the threshold, re-granted in full at every level: five failures.** It is not a new
number, and there is deliberately no second number to invent. After a lock expires the account has
the same five-failure budget it had as a first offender — the only difference is that exhausting it
buys a longer lock, because `NivelBloqueo` was preserved.

### `ApplyFailure`, precisely

Given a failure evaluated while **not** locked (`Evaluate` already rejects locked accounts before any
hash — Decision 5, step 2 — so `ApplyFailure` is unreachable during a lock):

1. `IntentosFallidos + 1`.
2. If `IntentosFallidos < UmbralFallos` → done. `NivelBloqueo` and `BloqueadoHasta` untouched.
3. If `IntentosFallidos == UmbralFallos`, arm the lock, **in this order**:
   - `duracion = DuracionBase × Factor ^ min(NivelBloqueo, NivelMaximo)` — read `NivelBloqueo`
     **before** incrementing it, which is what makes the first lock 15 minutes and not 30. The inner
     `min` is redundant against the saturating increment below and is kept anyway, so the function
     stays total against a hand-edited row.
   - `BloqueadoHasta = ahora + duracion`
   - `NivelBloqueo = min(NivelBloqueo + 1, NivelMaximo)`
   - `IntentosFallidos = 0`

`ApplySuccess` sets `IntentosFallidos = 0`, `BloqueadoHasta = NULL`, **and `NivelBloqueo = 0`.**

**Why a success clears the escalation.** ADR 0007 says "un inicio de sesión correcto pone el contador
a cero" — singular, written when there was one counter. Applying it to both is the right reading:
escalation exists to make *sustained unauthenticated guessing* expensive, and a successful login is
proof the requester holds the credential, which ends the guessing hypothesis. The alternative —
requiring a cooling-off period with no lockouts — needs a third column (`UltimoBloqueoEn`) and a
second time-based rule, to punish a legitimate operator who mistypes a password twice in a month.
Rejected. There is no leak here: an attacker who can produce a success already has the password, and
the lockout was never what stood between them and the account.

`smartnet-admin usuario restablecer-clave` clears all three fields identically. `spec.md`'s existing
reset scenario names only `IntentosFallidos` and `BloqueadoHasta`; the follow-up pass adds
`NivelBloqueo = 0` to it.

### Worked sequence — `spec.md` can lift this into Given/When/Then without inventing anything

Fresh account, every attempt a failure, no success anywhere. "Failure #" is lifetime; the parenthesis
is the position inside the current window.

| Event | `IntentosFallidos` after | `NivelBloqueo` after | `BloqueadoHasta` after | Lock armed |
|---|---|---|---|---|
| initial state | 0 | 0 | `NULL` | — |
| failure 1 (1/5) | 1 | 0 | `NULL` | — |
| failure 2 (2/5) | 2 | 0 | `NULL` | — |
| failure 3 (3/5) | 3 | 0 | `NULL` | — |
| failure 4 (4/5) | 4 | 0 | `NULL` | — |
| **failure 5 (5/5)** | **0** | **1** | `ahora + 15 min` | **lock A — 15 min** |
| any attempt while A is live | 0 (unchanged) | 1 (unchanged) | unchanged | rejected before hashing |
| *lock A expires — nothing is written, no job runs* | 0 | 1 | in the past | — |
| failure 6 (1/5) — **the margin** | 1 | 1 | past | **no re-lock** |
| failures 7–9 (2/5 … 4/5) | 2 → 4 | 1 | past | — |
| **failure 10 (5/5)** | **0** | **2** | `ahora + 30 min` | **lock B — 30 min** |
| failures 11–14 | 1 → 4 | 2 | past | — |
| **failure 15** | **0** | **3** | `ahora + 60 min` | **lock C — 60 min** |
| failures 16–19 | 1 → 4 | 3 | past | — |
| **failure 20** | **0** | **3** *(saturated)* | `ahora + 120 min` | **lock D — 120 min** |
| failures 21–24 | 1 → 4 | 3 | past | — |
| **failure 25** | **0** | **3** | `ahora + 120 min` | **lock E — 120 min, cap holds** |
| *a success at any point above* | 0 | **0** | `NULL` | escalation forgotten |

Sequence: **15, 30, 60, 120, 120, 120, …** — `NivelBloqueo` after arming: 1, 2, 3, 3, 3, ….

**The cap makes the sequence non-decreasing, not strictly increasing, and that is a deliberate
deviation from a literal reading of ADR 0007's "creciente".** From lock D onward each lock equals the
previous one. It is declared rather than glossed, because the cap is precisely what bounds ADR 0007's
own declared cost: *"el bloqueo temporal es un vector de denegación de servicio contra el único
usuario"*. Without a ceiling, doubling reaches 8 hours by the sixth lock and multiple days by the
tenth — an attacker who knows the username could lock the only user out for a working week. 120
minutes is the ceiling on that cost. `spec.md` must assert the cap as its own scenario, since it is
the one point where the sequence stops satisfying the ADR's word.

**Check that the margin does not defeat the lockout.** At the cap an attacker sustains 5 guesses per
120 minutes — 60 per day, ~22k per year. Against an Argon2id-derived hash of a non-guessable password
that is not a brute-force channel, and it is the rate the user's "margin, not immediate re-lock"
ruling buys in exchange for not punishing a legitimate mistype. The residual assumption is that the
password is not guessable in ~22k attempts per year; that assumption is what `restablecer-clave`
exists to repair.

### What else moves because of this column

| Surface | Change |
|---|---|
| `UsuarioCredentialState` | Gains `NivelBloqueo`. The record shape *is* the core's contract, so this is a breaking change to it even though `Evaluate`/`ApplySuccess` signatures are textually unchanged (Decision 5, updated) |
| `AccessPolicy.ApplyFailure` | Gains a `LockoutPolicy politica` parameter |
| `LockoutPolicy` | New type. Holds 5 / 15 min / ×2 / cap 3 in one place |
| `IUsuarioRepository.SaveCredentialStateAsync` | **Signature unchanged** — it already persists the whole state object. Only the `UPDATE` behind it widens to three columns. This is why Decision 5 made it state-shaped rather than field-shaped |
| `ISesionRepository`, `IPasswordHasher`, `ISessionTokenFactory`, the `ITicketStore` adapter | **Unchanged.** `fact.Sesion` is untouched by this decision — escalation is entirely a `fact.Usuario` concern |
| `smartnet-admin usuario restablecer-clave` | Clears `NivelBloqueo` alongside the other two; still revokes all sessions |
| `SchemaShapeTests` | Add `fact.Usuario.NivelBloqueo` — `INT`, `NOT NULL`, default `0`, and the `CHECK` |
| `PermissionMatrixTests` | **No change for this column.** Object-level grants cover it. The `fact.Sesion` additions from Decision 3 still apply |

---

## Data Flow

```
POST /api/sesion ─┐
                  ├─ SmartNet.Api (Minimal API, thin)
                  │     │
                  │     ├─→ AccessPolicy.Evaluate ─── PURE, no I/O, `ahora` passed in
                  │     ├─→ IPasswordHasher.Verify ── Argon2id (adapter)
                  │     ├─→ IUsuarioRepository ────── fact.Usuario  (SELECT / UPDATE)
                  │     └─→ ISesionRepository ─────── fact.Sesion   (INSERT)
                  │
                  └─ Set-Cookie __Host-session  (ticket protected by the persisted key ring;
                                                 payload = the 256-bit token, never the claims)

any authenticated request
      │
      └─ CookieAuthentication ─→ ITicketStore.RetrieveAsync(token)
                                     └─→ SHA-256 → UQ_Sesion_TokenHash seek
                                           WHERE RevocadaEn IS NULL AND ExpiraEn > @ahora
                                     (renew, and only then UPDATE, at the >50 % mark)

DELETE /api/sesion ─→ ITicketStore.RemoveAsync ─→ UPDATE RevocadaEn = @ahora,
                                                         MotivoRevocacion = 'CIERRE_SESION'
```

## File Changes

| File | Action | Description |
|---|---|---|
| `SmartNet/db/schema/011_sesion.sql` | Create | `fact.Sesion` DDL + its `fact_api` grants and `fact_worker` DENY, one file |
| `SmartNet/db/schema/rollback/011_down.sql` | Create | Advisory, never applied (item #1 Decision 4) |
| `SmartNet/db/schema/012_usuario_nivel_bloqueo.sql` | Create | `ALTER TABLE fact.Usuario ADD NivelBloqueo` + its `CHECK` (Decision 8). Additive; no grant changes |
| `SmartNet/db/schema/rollback/012_down.sql` | Create | Advisory, never applied |
| `SmartNet/db/schema/checksums.txt` | Modify | Two new lines (`011`, `012`), via `generate-checksums.ps1` |
| `SmartNet/db/schema/002_seguridad.sql` | **Unchanged** | Shipped in item #1 and journalled by name. Listed here precisely so the reader can see it was considered and deliberately not touched |
| `SmartNet/auth/SmartNet.Auth.Core/` | Create | Ports, `AccessPolicy`, PHC codec, result types. No infra references |
| `SmartNet/auth/SmartNet.Auth.Core.Tests/` | Create | xUnit + `FakeTimeProvider`. No DB, no HTTP |
| `SmartNet/auth/SmartNet.Auth.Infrastructure/` | Create | Argon2id hasher, CSPRNG token factory, SQL Server repositories |
| `SmartNet/api/SmartNet.Api/` | Create | `Program.cs`, cookie auth per ADR 0007, `SesionEndpoints`, `SqlSesionTicketStore`, DI |
| `SmartNet/api/SmartNet.Api.Tests/` | Create | `WebApplicationFactory` over the existing `TestDatabaseFixture` |
| `SmartNet/admin/SmartNet.Admin/` | Create | `usuario crear` / `usuario restablecer-clave` / `sesion purgar` |
| `SmartNet.sln` | Create | The repo has no solution file; nine projects need one |
| `SmartNet/db/runner/SmartNet.Db.Runner.Tests/PermissionMatrixTests.cs` | Modify | Extend with `fact.Sesion` grant/deny assertions |
| `SmartNet/db/runner/SmartNet.Db.Runner.Tests/SchemaShapeTests.cs` | Modify | Extend with `fact.Sesion` shape assertions **and `fact.Usuario.NivelBloqueo`** |

Nothing under `SmartNet/db/test-bootstrap/` changes: the harness already does everything the new tests
need.

## Testing Strategy

| Layer | What | How |
|---|---|---|
| Unit (`SmartNet.Auth.Core.Tests`) | Every row of Decision 8's worked sequence: the 15/30/60/120 durations, the cap holding at lock E, the post-expiry margin **not** re-locking on one failure, `NivelBloqueo` surviving the margin, `IntentosFallidos` zeroing at arming, success clearing all three. Plus expiry boundaries, PHC encode/parse round-trip, malformed/unknown-algorithm PHC | Pure. `FakeTimeProvider`; `IPasswordHasher` and `ISessionTokenFactory` are stubbed in-test, so Argon2 never runs and the suite stays milliseconds. `LockoutPolicy` is a parameter, so the cap boundary is reachable without replaying twenty-five failures. This is ADR 0019 level 1 for the auth core |
| Schema shape | `fact.Sesion` columns, types, `DATETIME2(3)`, `CHAR(64)` + `BIN2`, the paired revocation `CHECK`, the FK, both indexes; **`fact.Usuario.NivelBloqueo`** as `INT NOT NULL` with default `0` and `CK_Usuario_NivelBloqueo` | **Extend `SchemaShapeTests.cs`** — same catalog-scanning style item #1 proved structure with, not review |
| Permission matrix | `usr_api` holds SELECT/INSERT/UPDATE/DELETE on `fact.Sesion`; `usr_worker` is denied all four | **Extend `PermissionMatrixTests.cs`** — same `EXECUTE AS USER` / error-229 pattern, no new infrastructure |
| Integration (`SmartNet.Api.Tests`) | Login sets the cookie and inserts a row; logout revokes and the cookie stops authenticating; five failures lock; a sixth is rejected without a hash; unknown user and wrong password are indistinguishable; **the three lockout columns round-trip through `SaveCredentialStateAsync` — a state field the `UPDATE` forgets to write is exactly the bug that unit tests cannot see** | `WebApplicationFactory<Program>` with `SMARTNET_API_DB_CONNECTION` pointed at `TestDatabaseFixture.ConnectionString`; per test: `CreateAsync` → `CreateWithoutLoginUserAsync("usr_api"/"usr_worker")` → `CreateExternalDboCatalogsAsync` → `RunMigrations()` |
| Permission sufficiency | The exact statements the adapters issue actually succeed under `usr_api`'s real grants | Replay them through the existing `ExecuteAsUserAsync("usr_api", …)`. Endpoint tests connect as the integrated principal, so without this a missing GRANT would ship green |

`SmartNet.Api.Tests` adds a `ProjectReference` to the existing `SmartNet.Db.TestBootstrap` — the
harness is already shaped as a shared component. Note the asymmetry and keep it: the *test project*
transitively references `SmartNet.Db.Runner` (it must, to migrate); the **`SmartNet.Api` host does
not**.

## Threat Matrix

**N/A** — every row of `references/threat-matrix.md` covers documentation-like path classification,
Git repository selection, commit/push state, or PR command composition. This change introduces no
shell invocation, no subprocess, no VCS or PR automation, and no executable-file classification.
`SmartNet.Admin` is a first-party console entry point invoked by an operator, not a shell-command
composer. The adversarial surface this change *does* have — username enumeration by timing, session
fixation, lockout DoS, key-ring loss, token theft at rest — is handled inside Decisions 4, 5 and 6
with the response stated at each, and those responses must become RED tests in `tasks.md`.

## Migration / Rollout

No data migration. ADR 0012 order: `SmartNet.Db.Runner` applies `011` and `012`, then `SmartNet.Api`
is deployed, then the operator runs `smartnet-admin usuario crear` once. `011` and `012` are
independent of each other and their relative order does not matter; they are numbered sequentially
only because DbUp is.

`012` is the **compensating migration for `002_seguridad.sql`** described in Decision 8. It is
additive and non-breaking — a new column with a `DEFAULT`, in the same class as `fact.Sesion` itself
under ADR 0016 — and it does not reopen item #1: `002` ships unchanged and its journal entry stays
valid. `fact.Usuario` is empty in every environment today, so the `DEFAULT` back-fills nothing; it is
present so the script is correct on a database where it is not.

Rollback is forward-only per item #1's Decision 4. `011` is purely additive to `fact`, so a
compensating `NNN_revierte_011_*.sql` drops the table and its grants. `012`'s reversal drops the
constraint and the column and is likewise additive-in-reverse — losing `NivelBloqueo` costs one
account's escalation memory and nothing else. Neither touches any `dbo` object, and no accounting
data can be involved.

## Open Questions — what `spec.md` must fix that this design deliberately did not invent

- [x] **RESOLVED (revision 2) — the exact lockout growth sequence.** The user fixed **15 → 30 → 60 →
      120 minutes, capped at 120**, doubling on each successive lock. Decision 8 carries it.
- [x] **RESOLVED (revision 2) — what one failure *after* a lock expires does.** The user ruled for a
      **margin**: nothing. Decision 8 sets that margin at the same five-failure threshold, re-granted
      in full at every level, with the escalation level preserved across it in the new
      `fact.Usuario.NivelBloqueo` column. Revision 1's single-counter formula could not express this
      and is superseded.
- [ ] **`spec.md` must be updated to match Decision 8** — this design does not touch it. Four deltas,
      all named in Decision 8: (a) the 5th-failure scenario asserts `IntentosFallidos` becomes `5`;
      it now becomes `0` with `NivelBloqueo` `1`. (b) The "longer than the previous lockout" scenario
      gains the literal 15/30/60/120 sequence. (c) The cap needs its own scenario, since it is where
      the sequence stops being strictly "creciente". (d) The reset scenario adds `NivelBloqueo = 0`.
      Everything those scenarios need is in Decision 8's worked table; nothing further is left to
      invent.
- [ ] **Session retention window** for `sesion purgar`. Suggested default 90 days as a
      `fact.Configuracion` key; the number is an operational decision, not a design one.
- [x] **RESOLVED (task 0.1, 2026-08-16) — Konscious package verification.** `1.3.1`, MIT, no
      first-party .NET 10 Argon2id (confirmed against `dotnet/core` release notes and
      `dotnet/runtime#19933`, still open). Maintenance flagged as stale (last commit 2024-06-18) but
      not archived/disabled — non-blocking risk, recorded above under Decision 1.
- [x] **RESOLVED (task 0.2) — Data-protection key-ring path.** `SMARTNET_API_KEYRING_PATH` env var,
      recommended value `C:\ProgramData\SmartNet\dataprotection-keys`, added to ADR 0014's backup set.
      Recorded above under Decision 4.
- [ ] **ADR 0008 has no authentication endpoints.** This design adds `POST`/`DELETE`/`GET
      /api/sesion`. Worth carrying into ADR 0008 as a revision so the contract has one owner.
- [ ] **ADR 0007 needs a revision for Decision 8.** Its sketch shows two lockout columns
      (`IntentosFallidos`, `BloqueadoHasta`) and leaves "creciente en bloqueos sucesivos"
      unquantified. There is now a third column and a fixed sequence with a cap that makes the
      sequence non-decreasing rather than strictly increasing. The ADR should own those numbers, not
      this change document — otherwise item #2's design becomes the de facto source for a rule that
      outlives it.
