# Backend Audit Report

## Executive Summary

The backend is operational and reasonably well-structured, but it is not production-secure in its current state. The codebase builds cleanly and the automated test suite passed in this review (`dotnet build EJCFitnessGym.sln`, `dotnet test EJCFitnessGym.Tests\EJCFitnessGym.Tests.csproj --no-build`, 56/56 tests passed), but there are two critical security issues that should be treated as immediate blockers for a public deployment:

1. Sensitive credentials and signing keys are committed in `appsettings.json`.
2. PayMongo webhook signature enforcement is effectively disabled by configuration.

The architecture itself is solid for a monolithic ASP.NET Core application: it has clear service boundaries, an outbox pattern, background workers, health checks, and a fairly broad test suite. The current risk is less about missing business logic and more about unsafe operational defaults.

## Scorecard

| Area | Score | Notes |
| --- | --- | --- |
| Architecture | 8/10 | Strong modular service split, background workers, health checks, integration outbox, and good EF Core constraints/indexes. |
| Security | 3/10 | Major secret-management failures and webhook trust issues materially weaken production posture. |
| Testing | 7/10 | Good service/controller coverage and payment/webhook tests; however, tests target a different runtime than the app. |
| Deployment Readiness | 4/10 | Good readiness checks exist, but startup failure handling is too permissive and production config is unsafe by default. |

## Strengths

- The application is organized as a coherent backend monolith with explicit domain services registered in `Program.cs:257-293`.
- The integration outbox pattern is implemented cleanly in `Services/Integration/IntegrationOutboxService.cs:18-91` and dispatched with retries/backoff in `Services/Integration/IntegrationOutboxDispatcherWorker.cs:58-131`.
- Database schema protection is better than average for a project of this size. There are important unique indexes and constraints in `Data/ApplicationDbContext.cs:67-75`, `Data/ApplicationDbContext.cs:77-85`, `Data/ApplicationDbContext.cs:218-223`, and `Data/ApplicationDbContext.cs:225-242`.
- Operational monitoring exists through readiness/liveness endpoints in `Program.cs:279-287` and `Program.cs:891-900`, backed by an actual health check in `Services/Monitoring/OperationalReadinessHealthCheck.cs:22-109`.
- Payment/webhook behavior has real integration-style automated tests, for example `EJCFitnessGym.Tests/PayMongoWebhookIntegrationTests.cs:23-59`.

## Critical Findings

### BE-001: Secrets are committed to source control

Impact: Anyone with repository access can obtain live infrastructure and application credentials, which is sufficient to compromise the database, email sender, OAuth integration, payment integration, and JWT issuance.

Evidence:

- `appsettings.json:3` contains the SQL Server connection string including username and password.
- `appsettings.json:16-22` contains SMTP credentials.
- `appsettings.json:27-28` contains the Google OAuth client secret.
- `appsettings.json:31-43` contains PayMongo keys and the JWT signing key.
- The project already indicates intended secret separation via `EJCFitnessGym.csproj:7` (`UserSecretsId`), but the runtime secrets are still committed in config.

Recommendation:

- Rotate all exposed credentials immediately.
- Move database, SMTP, Google OAuth, PayMongo, and JWT secrets into environment variables, user secrets for local development, or the production platform secret store.
- Keep only non-sensitive defaults in `appsettings.json`.

### BE-002: PayMongo webhook signature verification is disabled by default

Impact: An attacker who can send HTTP requests to the webhook endpoint can forge paid or failed payment events and drive billing state changes without proof that the event originated from PayMongo.

Evidence:

- `appsettings.json:36-37` sets `WebhookSecret` to empty and `RequireWebhookSignature` to `false`.
- The webhook endpoint is anonymous in `Controllers/PayMongoWebhookController.cs:22-25`.
- `Controllers/PayMongoWebhookController.cs:619-630` explicitly returns `true` from signature verification when the secret is missing and signatures are not required.
- The same controller updates payment and invoice state from webhook input in `Controllers/PayMongoWebhookController.cs:315-399`.

Recommendation:

- Make webhook signature verification fail closed in non-development environments.
- Require a configured webhook secret before the app is considered ready.
- Treat unsigned webhook delivery as a deployment misconfiguration, not an optional behavior.

## High Findings

### BE-003: Auth cookies are not forced to `Secure` in production

Impact: If the app is ever reached over plain HTTP or through a proxy/TLS misconfiguration, authentication cookies may be transmitted without the `Secure` flag.

Evidence:

- `appsettings.json:8-9` sets `Security:UseSecureCookies` to `false`.
- `appsettings.Production.json:1` is empty, so production does not override that value.
- `Program.cs:162-168` uses the config value directly to decide cookie security policy.

Recommendation:

- Remove `UseSecureCookies=false` from the shared config baseline.
- Force `CookieSecurePolicy.Always` in production.
- Only allow opt-out for explicit local-development scenarios.

### BE-004: Forwarded headers trust all proxies

Impact: If the app is internet-facing or incorrectly network-segmented, clients can spoof `X-Forwarded-For` or `X-Forwarded-Proto`, which can corrupt security decisions, logging, and client-IP-based controls.

Evidence:

- `Program.cs:498-504` enables forwarded headers and then clears both `KnownNetworks` and `KnownProxies`, effectively trusting any proxy source.

Recommendation:

- Restrict forwarded-header trust to the real reverse proxies/load balancers in front of the app.
- Document the production proxy topology and configure only those addresses/networks.

### BE-005: Startup intentionally continues after migration and initialization failures

Impact: The service can come up in a partially initialized state, exposing users to broken finance, billing, or membership behavior while reporting as “running”.

Evidence:

- `Program.cs:550-559` logs migration failure and keeps running.
- `Program.cs:880-884` logs broader initialization failure and still continues startup.

Recommendation:

- In non-development environments, fail startup or fail readiness when migrations/critical seed steps fail.
- At minimum, gate traffic on the readiness probe until critical initialization has completed successfully.

## Medium Findings

### BE-006: Anonymous token endpoints have no visible rate-limiting protection

Impact: Brute-force login attempts and token abuse are easier than they should be for public-facing API endpoints.

Evidence:

- `Controllers/AuthTokenController.cs:48-50` exposes anonymous token issuance.
- `Controllers/AuthTokenController.cs:117-119` exposes anonymous refresh.
- `Controllers/AuthTokenController.cs:200-202` exposes anonymous revoke.
- No rate-limiter registration or middleware was found in `Program.cs` during this review.

Recommendation:

- Add ASP.NET Core rate limiting for `POST /api/auth/token`, `POST /api/auth/refresh`, and payment/webhook endpoints.
- Use stricter limits for anonymous endpoints than for authenticated ones.

### BE-007: Test runtime does not match the application runtime

Impact: Passing tests give useful confidence, but they do not perfectly represent the runtime behavior of the deployed app.

Evidence:

- The application targets `.NET 8` in `EJCFitnessGym.csproj:4`.
- The test project targets `.NET 10` in `EJCFitnessGym.Tests/EJCFitnessGym.Tests.csproj:4`.

Recommendation:

- Align the test target framework with the app target framework, or multi-target intentionally.
- Keep runtime parity in CI to reduce “tests passed, deployment failed” drift.

## Overall Backend Assessment

This backend is beyond prototype level. It has meaningful operational patterns in place:

- role and branch-scoped authorization
- JWT and cookie authentication
- payment reconciliation
- webhook idempotency
- background jobs
- operational health checks
- a real-time event path through SignalR

The main problem is that production-hardening has not caught up with feature growth. If the critical findings are fixed, the backend would move from “working but risky” to “credible production candidate”.

## Recommended Priority Order

1. Rotate and remove all committed secrets.
2. Enforce PayMongo webhook signature validation in all non-development environments.
3. Force secure cookies in production.
4. Restrict forwarded-header trust to known proxies only.
5. Change startup behavior so migration/init failure fails readiness or startup.
6. Add rate limiting to anonymous auth endpoints.
7. Align test/runtime target frameworks.

## Validation Performed

- `dotnet build EJCFitnessGym.sln`
- `dotnet test EJCFitnessGym.Tests\EJCFitnessGym.Tests.csproj --no-build`

Result:

- Build passed.
- Tests passed: 56/56.
