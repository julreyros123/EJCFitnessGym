# EJC Fitness Gym - ERP & Management System

A comprehensive, enterprise-grade Enterprise Resource Planning (ERP) and Gym Management System built with **ASP.NET Core 8.0**, designed to streamline fitness business operations across multiple branches.

## 🚀 Key Features

*   **Multi-Branch Support**: Seamlessly manage multiple gym locations with branch-scoped data isolation.
*   **Membership Lifecycle**: Automated handling of member signups, renewals, and retention actions.
*   **Finance & Accounting**:
    *   **Running Costs** (Operating Expenses) tracking.
    *   **Income & Earnings** (Revenue & Profit) analytics.
    *   General Ledger integration for automated bookkeeping.
*   **Inventory & Asset Management**:
    *   Gym equipment tracking and unit cost analysis.
    *   Retail product sales with VAT calculation.
    *   Supply requesting and approval workflows.
*   **Automated Billing**: Logic for scheduled invoice generation and payment processing integration.
*   **Real-time Operations**: SignalR integration for live dashboard updates and event notifications.
*   **Security & Compliance**:
    *   Role-based Access Control (RBAC) with specific roles for Staff, Finance, Admin, and SuperAdmin.
    *   JWT-based authentication for API endpoints.
    *   Rate limiting to prevent brute-force attacks.
    *   Secure cookie and webhook signature verification (for production).

## 🛠️ Technology Stack

*   **Framework**: [ASP.NET Core 8.0](https://dotnet.microsoft.com/en-us/apps/aspnet)
*   **Database**: [SQL Server](https://www.microsoft.com/en-us/sql-server) with Entity Framework Core (Code-First)
*   **Authentication**: [ASP.NET Core Identity](https://docs.microsoft.com/en-us/aspnet/core/security/authentication/identity)
*   **Payments**: [PayMongo](https://www.paymongo.com/) Integration
*   **Real-time**: [SignalR](https://dotnet.microsoft.com/en-us/apps/aspnet/signalr)
*   **Testing**: [xUnit](https://xunit.net/) (70+ Automated Tests)
*   **Frontend**: Razor Pages, Bootstrap 5, Vanilla CSS

## 🏁 Getting Started

### Prerequisites

*   [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
*   [LocalDB](https://docs.microsoft.com/en-us/sql/database-engine/configure-windows/sql-server-express-localdb) or a full SQL Server instance.

### Installation

1.  **Clone the repository**:
    ```bash
    git clone https://github.com/your-repo/EJCFitnessGym.git
    cd EJCFitnessGym
    ```

2.  **Restore dependencies**:
    ```bash
    dotnet restore
    ```

3.  **Apply Database Migrations**:
    ```bash
    dotnet ef database update
    ```

4.  **Run the application**:
    ```bash
    dotnet run
    ```

## 🧪 Seeding & Demo

The system automatically performs database seeding on its first run to facilitate quick demonstrations. Use the following default credentials (standard password is usually required via configuration):

| Role          | Email                       |
|---------------|-----------------------------|
| Super Admin   | `superadmin@ejcfit.local`   |
| Admin         | `admin@ejcfit.local`        |
| Finance       | `finance@ejcfit.local`      |
| Staff         | `staff@ejcfit.local`        |
| Member        | `member@ejcfit.local`       |

## 📁 Project Structure

*   **/Areas/Identity**: Identity UI and logic overrides.
*   **/Controllers**: API endpoints for metrics and authentication.
*   **/Data**: Migration history and ApplicationDbContext.
*   **/Models**: Core business entities and view models.
*   **/Pages**: Razor Pages for the Web UI.
*   **/Services**: Modularized business logic (Finance, Inventory, Payments, etc.).
*   **/wwwroot**: Static assets (CSS, JS, Libraries).
*   **/EJCFitnessGym.Tests**: Comprehensive unit and integration test suite.

## 📄 License

This project was developed for academic purposes.
