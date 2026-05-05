# Member Accounts Management

<cite>
**Referenced Files in This Document**
- [Register.cshtml.cs](file://Areas/Identity/Pages/Account/Register.cshtml.cs)
- [ConfirmEmail.cshtml.cs](file://Areas/Identity/Pages/Account/ConfirmEmail.cshtml.cs)
- [ResendEmailConfirmation.cshtml.cs](file://Areas/Identity/Pages/Account/ResendEmailConfirmation.cshtml.cs)
- [MemberAccountsController.cs](file://Controllers/MemberAccountsController.cs)
- [MemberProfile.cs](file://Models/MemberProfile.cs)
- [ApplicationDbContext.cs](file://Data/ApplicationDbContext.cs)
- [Dashboard.cshtml.cs](file://Pages/Member/Dashboard.cshtml.cs)
- [Profile.cshtml.cs](file://Pages/Member/Profile.cshtml.cs)
- [EmailVerificationCodeService.cs](file://Services/Identity/EmailVerificationCodeService.cs)
- [MemberBranchAssignment.cs](file://Services/Memberships/MemberBranchAssignment.cs)
- [MemberAccountViewModels.cs](file://Models/Admin/MemberAccountViewModels.cs)
- [_MemberForm.cshtml](file://Views/MemberAccounts/_MemberForm.cshtml)
- [BranchAccess.cs](file://Security/BranchAccess.cs)
- [MemberSubscription.cs](file://Models/Billing/MemberSubscription.cs)
</cite>

## Table of Contents
1. [Introduction](#introduction)
2. [Project Structure](#project-structure)
3. [Core Components](#core-components)
4. [Architecture Overview](#architecture-overview)
5. [Detailed Component Analysis](#detailed-component-analysis)
6. [Dependency Analysis](#dependency-analysis)
7. [Performance Considerations](#performance-considerations)
8. [Troubleshooting Guide](#troubleshooting-guide)
9. [Conclusion](#conclusion)

## Introduction
This document describes the member accounts management system for EJCFitnessGym. It covers the complete lifecycle from member registration and email verification to profile completion, membership administration, and dashboard insights. It also documents the integration with the ASP.NET Core Identity system for authentication, password management, and email verification, along with branch assignment and data privacy controls.

## Project Structure
The member accounts system spans several layers:
- Identity pages for registration, email verification, and resending confirmations
- Controllers for administrative member account management
- Models for member profiles and subscriptions
- Services for identity verification and branch assignment
- Pages for member self-service dashboards and profile editing
- Security helpers for branch scoping and access control

```mermaid
graph TB
subgraph "Identity Layer"
R["Register.cshtml.cs"]
CE["ConfirmEmail.cshtml.cs"]
RE["ResendEmailConfirmation.cshtml.cs"]
end
subgraph "Services"
EV["EmailVerificationCodeService.cs"]
BA["MemberBranchAssignment.cs"]
end
subgraph "Controllers"
MAC["MemberAccountsController.cs"]
end
subgraph "Pages"
MD["Member/Dashboard.cshtml.cs"]
MP["Member/Profile.cshtml.cs"]
end
subgraph "Data & Models"
DB["ApplicationDbContext.cs"]
MPF["MemberProfile.cs"]
MS["MemberSubscription.cs"]
end
subgraph "Security"
BAcs["BranchAccess.cs"]
end
R --> EV
R --> DB
R --> BA
CE --> DB
RE --> EV
MAC --> DB
MAC --> BA
MD --> DB
MP --> DB
MPF --> DB
MS --> DB
BAcs --> MAC
BAcs --> MD
```

**Diagram sources**
- [Register.cshtml.cs:1-354](file://Areas/Identity/Pages/Account/Register.cshtml.cs#L1-L354)
- [ConfirmEmail.cshtml.cs:1-63](file://Areas/Identity/Pages/Account/ConfirmEmail.cshtml.cs#L1-L63)
- [ResendEmailConfirmation.cshtml.cs:1-78](file://Areas/Identity/Pages/Account/ResendEmailConfirmation.cshtml.cs#L1-L78)
- [MemberAccountsController.cs:1-900](file://Controllers/MemberAccountsController.cs#L1-L900)
- [MemberProfile.cs:1-44](file://Models/MemberProfile.cs#L1-L44)
- [ApplicationDbContext.cs:1-414](file://Data/ApplicationDbContext.cs#L1-L414)
- [EmailVerificationCodeService.cs:1-179](file://Services/Identity/EmailVerificationCodeService.cs#L1-L179)
- [MemberBranchAssignment.cs:1-156](file://Services/Memberships/MemberBranchAssignment.cs#L1-L156)
- [BranchAccess.cs:1-31](file://Security/BranchAccess.cs#L1-L31)
- [MemberSubscription.cs:1-30](file://Models/Billing/MemberSubscription.cs#L1-L30)

**Section sources**
- [Register.cshtml.cs:1-354](file://Areas/Identity/Pages/Account/Register.cshtml.cs#L1-L354)
- [MemberAccountsController.cs:1-900](file://Controllers/MemberAccountsController.cs#L1-L900)
- [MemberProfile.cs:1-44](file://Models/MemberProfile.cs#L1-L44)
- [ApplicationDbContext.cs:1-414](file://Data/ApplicationDbContext.cs#L1-L414)
- [EmailVerificationCodeService.cs:1-179](file://Services/Identity/EmailVerificationCodeService.cs#L1-L179)
- [MemberBranchAssignment.cs:1-156](file://Services/Memberships/MemberBranchAssignment.cs#L1-L156)
- [BranchAccess.cs:1-31](file://Security/BranchAccess.cs#L1-L31)
- [MemberSubscription.cs:1-30](file://Models/Billing/MemberSubscription.cs#L1-L30)

## Core Components
- Member registration pipeline with Identity integration, role assignment, and branch assignment
- Email verification via time-bound codes with rate limiting and secure comparison
- Administrative member CRUD operations with validation and branch scoping
- Member self-service profile management with BMI calculation and completeness scoring
- Member dashboard with subscription status, billing metrics, and profile completion indicators
- Branch assignment and access control for multi-branch environments

**Section sources**
- [Register.cshtml.cs:114-259](file://Areas/Identity/Pages/Account/Register.cshtml.cs#L114-L259)
- [EmailVerificationCodeService.cs:32-131](file://Services/Identity/EmailVerificationCodeService.cs#L32-L131)
- [MemberAccountsController.cs:320-650](file://Controllers/MemberAccountsController.cs#L320-L650)
- [Profile.cshtml.cs:118-175](file://Pages/Member/Profile.cshtml.cs#L118-L175)
- [Dashboard.cshtml.cs:50-200](file://Pages/Member/Dashboard.cshtml.cs#L50-L200)
- [MemberBranchAssignment.cs:95-147](file://Services/Memberships/MemberBranchAssignment.cs#L95-L147)

## Architecture Overview
The system integrates ASP.NET Core Identity for authentication and authorization, with custom services for email verification and branch assignment. Data persistence uses Entity Framework with dedicated models for profiles and subscriptions. Administrative views leverage strongly-typed view models, while member-facing pages provide self-service capabilities.

```mermaid
sequenceDiagram
participant U as "User"
participant RP as "Register.cshtml.cs"
participant UM as "UserManager"
participant US as "UserStore"
participant DB as "ApplicationDbContext"
participant EV as "EmailVerificationCodeService"
participant BA as "MemberBranchAssignment"
U->>RP : Submit registration form
RP->>UM : CreateAsync(user, password)
UM-->>RP : IdentityResult
RP->>UM : AddToRoleAsync(user, "Member")
RP->>BA : AssignHomeBranchAsync(db, user, branchId, profile)
RP->>DB : SaveChangesAsync()
alt RequireConfirmedAccount
RP->>EV : SendVerificationCodeAsync(user)
EV-->>RP : Email sent
RP-->>U : Redirect to RegisterConfirmation
else No confirmation required
RP-->>U : Sign in and redirect
end
```

**Diagram sources**
- [Register.cshtml.cs:141-249](file://Areas/Identity/Pages/Account/Register.cshtml.cs#L141-L249)
- [EmailVerificationCodeService.cs:32-51](file://Services/Identity/EmailVerificationCodeService.cs#L32-L51)
- [MemberBranchAssignment.cs:95-147](file://Services/Memberships/MemberBranchAssignment.cs#L95-L147)

## Detailed Component Analysis

### Member Registration Workflow
- Input validation ensures required fields, phone formatting, password length, and terms acceptance
- User creation, role assignment, and profile initialization occur within a transaction
- Branch resolution prioritizes configuration, active branches, fallbacks, and claims; creates default branch if needed
- Optional email verification sends a time-limited code and redirects accordingly

```mermaid
flowchart TD
Start(["POST /Register"]) --> Validate["Validate InputModel"]
Validate --> Exists{"Duplicate email?"}
Exists --> |Yes| Error["Add model error"] --> Return["Return Page"]
Exists --> |No| CreateUser["Create IdentityUser"]
CreateUser --> Role["AddToRole 'Member'"]
Role --> Branch["Resolve Registration BranchId"]
Branch --> Txn["Begin Transaction"]
Txn --> Save["Save MemberProfile + AssignHomeBranch"]
Save --> Commit["Commit Transaction"]
Commit --> Confirm{"RequireConfirmedAccount?"}
Confirm --> |Yes| SendCode["SendVerificationCodeAsync"]
SendCode --> RedirectConfirm["Redirect to RegisterConfirmation"]
Confirm --> |No| SignIn["SignInAsync"] --> Done(["Redirect to ReturnUrl"])
```

**Diagram sources**
- [Register.cshtml.cs:114-259](file://Areas/Identity/Pages/Account/Register.cshtml.cs#L114-L259)
- [Register.cshtml.cs:262-330](file://Areas/Identity/Pages/Account/Register.cshtml.cs#L262-L330)
- [EmailVerificationCodeService.cs:32-51](file://Services/Identity/EmailVerificationCodeService.cs#L32-L51)

**Section sources**
- [Register.cshtml.cs:114-259](file://Areas/Identity/Pages/Account/Register.cshtml.cs#L114-L259)
- [Register.cshtml.cs:262-330](file://Areas/Identity/Pages/Account/Register.cshtml.cs#L262-L330)

### Email Verification and Resend
- Verification service generates a 6-digit code, stores a hashed token with expiry and attempt count, and emails the code
- Verification compares the submitted code against the stored hash using constant-time comparison
- Resend endpoint validates email, checks existence and confirmation status, and triggers a new code send

```mermaid
sequenceDiagram
participant U as "User"
participant RE as "ResendEmailConfirmation.cshtml.cs"
participant UM as "UserManager"
participant EV as "EmailVerificationCodeService"
U->>RE : Post email
RE->>UM : FindByEmailAsync(email)
alt User exists and not confirmed
RE->>EV : SendVerificationCodeAsync(user)
EV-->>RE : Success
RE-->>U : StatusMessage + Redirect
else Already confirmed or not found
RE-->>U : StatusMessage + Redirect
end
```

**Diagram sources**
- [ResendEmailConfirmation.cshtml.cs:46-76](file://Areas/Identity/Pages/Account/ResendEmailConfirmation.cshtml.cs#L46-L76)
- [EmailVerificationCodeService.cs:32-51](file://Services/Identity/EmailVerificationCodeService.cs#L32-L51)

**Section sources**
- [EmailVerificationCodeService.cs:32-131](file://Services/Identity/EmailVerificationCodeService.cs#L32-L131)
- [ResendEmailConfirmation.cshtml.cs:46-76](file://Areas/Identity/Pages/Account/ResendEmailConfirmation.cshtml.cs#L46-L76)

### Member Profile Model
- Stores personal info (names, phone), health metrics (age, height, weight, BMI), profile image path, and home branch association
- Enforces field lengths and ranges via attributes
- Includes audit timestamps for creation and updates

```mermaid
classDiagram
class MemberProfile {
+int Id
+string UserId
+string? FirstName
+string? LastName
+int? Age
+string? PhoneNumber
+decimal? HeightCm
+decimal? WeightKg
+decimal? Bmi
+string? ProfileImagePath
+string? HomeBranchId
+DateTime CreatedUtc
+DateTime UpdatedUtc
}
```

**Diagram sources**
- [MemberProfile.cs:5-44](file://Models/MemberProfile.cs#L5-L44)

**Section sources**
- [MemberProfile.cs:5-44](file://Models/MemberProfile.cs#L5-L44)
- [ApplicationDbContext.cs:105-127](file://Data/ApplicationDbContext.cs#L105-L127)

### Administrative Member CRUD Operations
- Index lists members with branch scoping for non-super admins
- Create validates branch activity, plan availability, and uniqueness; assigns role and profile
- Edit updates user details, optional password reset, profile, and subscription; enforces branch validity
- Delete removes user, subscriptions, and profile with cascading safety

```mermaid
sequenceDiagram
participant Admin as "Admin/Finance"
participant C as "MemberAccountsController"
participant UM as "UserManager"
participant DB as "ApplicationDbContext"
participant BA as "MemberBranchAssignment"
Admin->>C : GET Create/Edit/Delete
C->>DB : Load plans, branches, user/profile/subscription
Admin->>C : POST Create/Edit
C->>UM : Create/AddToRole or Update
C->>BA : AssignHomeBranchAsync
C->>DB : SaveChanges
C-->>Admin : Redirect with StatusMessage
```

**Diagram sources**
- [MemberAccountsController.cs:320-650](file://Controllers/MemberAccountsController.cs#L320-L650)
- [MemberBranchAssignment.cs:95-147](file://Services/Memberships/MemberBranchAssignment.cs#L95-L147)

**Section sources**
- [MemberAccountsController.cs:41-318](file://Controllers/MemberAccountsController.cs#L41-L318)
- [MemberAccountsController.cs:320-650](file://Controllers/MemberAccountsController.cs#L320-L650)
- [MemberAccountViewModels.cs:60-142](file://Models/Admin/MemberAccountViewModels.cs#L60-L142)
- [_MemberForm.cshtml:1-72](file://Views/MemberAccounts/_MemberForm.cshtml#L1-L72)

### Member Self-Service Profile Management
- GET loads current profile data and calculates BMI category and profile completeness
- POST validates input, updates profile fields, recalculates BMI, and persists changes
- Provides subscription sidebar context for member navigation

```mermaid
flowchart TD
PGet["GET /Member/Profile"] --> Load["Load MemberProfile + User"]
Load --> Calc["Calculate BMI + Completeness"]
Calc --> Render["Render Profile Page"]
PPost["POST /Member/Profile"] --> Validate["Validate InputModel"]
Validate --> Valid{"Valid?"}
Valid --> |No| ReRender["Re-render with errors"]
Valid --> |Yes| Upsert["Upsert MemberProfile + BMI"]
Upsert --> Persist["SaveChanges"]
Persist --> Success["Redirect with success message"]
```

**Diagram sources**
- [Profile.cshtml.cs:75-175](file://Pages/Member/Profile.cshtml.cs#L75-L175)

**Section sources**
- [Profile.cshtml.cs:75-175](file://Pages/Member/Profile.cshtml.cs#L75-L175)

### Member Dashboard Functionality
- Displays subscription status, days remaining, outstanding balances, and recent invoices
- Computes profile completeness percentage and highlights missing items
- Provides badge classes for subscription and invoice statuses
- Loads overdue counts for notifications

```mermaid
sequenceDiagram
participant M as "Member"
participant D as "Dashboard.cshtml.cs"
participant DB as "ApplicationDbContext"
M->>D : GET Dashboard
D->>DB : Load MemberProfile
D->>DB : Load MemberSubscriptions
D->>DB : Sum Unpaid/Overdue Invoices
D->>DB : Count Recent Payments + Saved Methods
D->>DB : Load Recent Invoices
D->>DB : Compute Completeness
D-->>M : Render Dashboard with metrics
```

**Diagram sources**
- [Dashboard.cshtml.cs:50-154](file://Pages/Member/Dashboard.cshtml.cs#L50-L154)

**Section sources**
- [Dashboard.cshtml.cs:50-200](file://Pages/Member/Dashboard.cshtml.cs#L50-L200)

### Branch Assignment and Access Control
- MemberBranchAssignment resolves and normalizes branch IDs, assigns claims, and updates profiles
- BranchAccess provides extension methods to extract branch claims and enforce scope
- Controllers and pages use branch-aware queries and authorization policies

```mermaid
flowchart TD
Resolve["ResolveHomeBranchIdAsync"] --> Map["ResolveHomeBranchMapAsync"]
Map --> Fallback["UserClaims fallback"]
Fallback --> Normalize["NormalizeBranchId"]
Normalize --> Assign["AssignHomeBranchAsync"]
Assign --> Claims["Add/Remove branch_id Claim"]
Claims --> Update["Update MemberProfile.HomeBranchId"]
```

**Diagram sources**
- [MemberBranchAssignment.cs:12-147](file://Services/Memberships/MemberBranchAssignment.cs#L12-L147)
- [BranchAccess.cs:9-28](file://Security/BranchAccess.cs#L9-L28)

**Section sources**
- [MemberBranchAssignment.cs:12-147](file://Services/Memberships/MemberBranchAssignment.cs#L12-L147)
- [BranchAccess.cs:9-28](file://Security/BranchAccess.cs#L9-L28)

## Dependency Analysis
- Identity integration: UserManager, SignInManager, IdentityUser, and authentication tokens
- Data access: ApplicationDbContext with strongly-typed DbSets for profiles, subscriptions, invoices, payments
- Services: EmailVerificationCodeService for secure code delivery and verification; MemberBranchAssignment for branch scoping
- Security: BranchAccess for claim-based branch scoping; authorization policies for member access

```mermaid
graph LR
UM["UserManager"] --> IR["IdentityUser"]
IR --> RP["Register.cshtml.cs"]
RP --> DB["ApplicationDbContext"]
EV["EmailVerificationCodeService"] --> UM
BA["MemberBranchAssignment"] --> DB
BA --> UM
MAC["MemberAccountsController"] --> DB
MD["Member/Dashboard.cshtml.cs"] --> DB
MP["Member/Profile.cshtml.cs"] --> DB
BAcs["BranchAccess"] --> MAC
BAcs --> MD
```

**Diagram sources**
- [Register.cshtml.cs:24-51](file://Areas/Identity/Pages/Account/Register.cshtml.cs#L24-L51)
- [MemberAccountsController.cs:21-39](file://Controllers/MemberAccountsController.cs#L21-L39)
- [Dashboard.cshtml.cs:16](file://Pages/Member/Dashboard.cshtml.cs#L16)
- [Profile.cshtml.cs:16-23](file://Pages/Member/Profile.cshtml.cs#L16-L23)
- [EmailVerificationCodeService.cs:18-30](file://Services/Identity/EmailVerificationCodeService.cs#L18-L30)
- [MemberBranchAssignment.cs:8-101](file://Services/Memberships/MemberBranchAssignment.cs#L8-L101)
- [BranchAccess.cs:5-28](file://Security/BranchAccess.cs#L5-L28)

**Section sources**
- [ApplicationDbContext.cs:19-42](file://Data/ApplicationDbContext.cs#L19-L42)
- [MemberSubscription.cs:5-28](file://Models/Billing/MemberSubscription.cs#L5-L28)

## Performance Considerations
- Use AsNoTracking for read-only queries in controllers and pages to reduce change tracking overhead
- Batch database operations within transactions to minimize round trips
- Leverage indexed columns (e.g., MemberProfile.UserId, BranchRecord.BranchId) for efficient lookups
- Avoid unnecessary projections and limit result sets (e.g., Take(5) for recent invoices)
- Cache frequently accessed configuration values (e.g., default branch) when appropriate

## Troubleshooting Guide
Common issues and resolutions:
- Duplicate email during registration: The controller adds a model error and returns the page; ensure unique emails are used
- Branch configuration missing: Registration fails if no branch is configured; verify configuration or seed default branch
- Email verification failures: Check logs for exceptions, verify SMTP configuration, and ensure codes are not expired or exceeded attempts
- Authorization failures: Non-super admins require a branch scope; verify claims and branch assignments
- Validation errors in admin forms: Review model state errors for invalid branch selection, plan availability, or date constraints

**Section sources**
- [Register.cshtml.cs:134-139](file://Areas/Identity/Pages/Account/Register.cshtml.cs#L134-L139)
- [Register.cshtml.cs:166-172](file://Areas/Identity/Pages/Account/Register.cshtml.cs#L166-L172)
- [EmailVerificationCodeService.cs:53-131](file://Services/Identity/EmailVerificationCodeService.cs#L53-L131)
- [MemberAccountsController.cs:364-367](file://Controllers/MemberAccountsController.cs#L364-L367)
- [MemberAccountsController.cs:537-551](file://Controllers/MemberAccountsController.cs#L537-L551)

## Conclusion
The member accounts management system provides a robust foundation for onboarding, verification, profile maintenance, and administrative oversight. It leverages ASP.NET Core Identity for authentication and authorization, integrates secure email verification, and supports multi-branch operations through claim-based scoping. The dashboard and self-service profile features enhance user engagement and data completeness, while administrative controllers offer comprehensive CRUD capabilities with strong validation and branch-aware access control.