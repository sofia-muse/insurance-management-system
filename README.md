# Insurance Management System — Backend API

Backend service (API) that acts as an engine for an insurance agent: manage customers (policyholders), issue and maintain insurance policies, and keep relationship + lifecycle rules consistent.

## Tech Stack

- **ASP.NET Core 8** Web API
- **Entity Framework Core 8** with **SQLite**
- **FluentValidation** for request validation
- **Swagger / OpenAPI** for interactive exploration

## Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)

## Setup & Run

```bash
git clone <your-repo-url>
cd "Backend Challenge - Insurance Management System"

dotnet restore
dotnet run --project InsuranceManagement.Api --launch-profile http
```

Then open Swagger UI:

- http://localhost:5275/swagger

The SQLite database file `insurance.db` is created automatically on first run (with sample seed data).

### HTTPS profile (optional)

```bash
dotnet run --project InsuranceManagement.Api --launch-profile https
```

## Architecture

```
InsuranceManagement.Api            Controllers, DTOs, validators, middleware
        │
        ▼
InsuranceManagement.Core           Entities, enums, service interfaces, domain exceptions
        ▲
        │
InsuranceManagement.Infrastructure EF Core DbContext, service implementations, seeding
```

**Separation of concerns**

| Layer | Responsibility |
|-------|----------------|
| Controllers | HTTP mapping only; no business rules |
| Services | Onboarding, issuance, filtering, update/cancel rules |
| DbContext | Persistence and relationship configuration |

**Data model**

- One **Customer** has many **Policies**
- A **Policy** belongs to exactly one **Customer**
- Policy types: `Car`, `Health`, `Life`
- Policy statuses: `Active`, `Cancelled`, `Expired`

## API Endpoints

### Customers

| Method | Path | Description |
|--------|------|-------------|
| `POST` | `/api/customers` | Onboard a new customer |
| `GET` | `/api/customers?search=` | List / filter by name or email |
| `GET` | `/api/customers/{id}` | Get customer + policies |
| `PUT` | `/api/customers/{id}` | Update customer details |

### Policies

| Method | Path | Description |
|--------|------|-------------|
| `POST` | `/api/customers/{customerId}/policies` | Issue a policy for a customer |
| `GET` | `/api/policies?type=&status=&customerId=` | List / filter policies |
| `GET` | `/api/policies/{id}` | Get policy by id |
| `PUT` | `/api/policies/{id}` | Update an active policy |
| `POST` | `/api/policies/{id}/cancel` | Cancel / terminate a policy |

### Example requests

**Create customer**

```bash
curl -X POST http://localhost:5275/api/customers \
  -H "Content-Type: application/json" \
  -d "{\"fullName\":\"Dana Levi\",\"email\":\"dana.levi@example.com\",\"phone\":\"+1-555-0199\",\"dateOfBirth\":\"1990-06-15\",\"address\":\"10 Herzl St\"}"
```

**Issue policy**

```bash
curl -X POST http://localhost:5275/api/customers/11111111-1111-1111-1111-111111111111/policies \
  -H "Content-Type: application/json" \
  -d "{\"type\":\"Health\",\"premium\":890.50,\"startDate\":\"2026-01-01\",\"endDate\":\"2026-12-31\",\"coverageDetails\":\"Hospital and outpatient coverage\"}"
```

**Filter policies by type**

```bash
curl "http://localhost:5275/api/policies?type=Car"
```

**Cancel policy**

```bash
curl -X POST http://localhost:5275/api/policies/33333333-3333-3333-3333-333333333333/cancel
```

Seeded customer ids for quick demos:

- Alice Johnson: `11111111-1111-1111-1111-111111111111`
- Bob Smith: `22222222-2222-2222-2222-222222222222`
- Sample Car policy: `33333333-3333-3333-3333-333333333333`

## Business Assumptions

1. A customer may hold many policies; each policy belongs to one customer.
2. Policies can only be issued to an **existing** customer (`404` otherwise).
3. Policy numbers are **system-generated** and unique (`POL-{TYPE}-{SHORTID}`).
4. Customer email is unique (`409` on conflict).
5. Cancel is a **soft terminate** (status → `Cancelled`, `CancelledAt` set). Records are not deleted.
6. Cancelled policies **cannot** be updated.
7. A customer may have at most **one active Car** policy at a time (underwriting simplification).
8. `Expired` is derived when `EndDate` is before today (no background job).
9. Premium must be greater than zero; `StartDate` must be on or before `EndDate`.
10. No authentication in this challenge scope — the API is intended as a local demo engine.

## Error Handling

| Status | When |
|--------|------|
| `400` | Validation or business-rule violation |
| `404` | Customer or policy not found |
| `409` | Duplicate email or duplicate active Car policy |

## Project Structure

```
InsuranceManagement.sln
InsuranceManagement.Api/
InsuranceManagement.Core/
InsuranceManagement.Infrastructure/
README.md
```

## Publish to GitHub (public repo)

If `gh` is installed and authenticated:

```bash
gh auth login
gh repo create insurance-management-api --public --source=. --remote=origin --push
```

Or create an empty public repo on GitHub, then:

```bash
git remote add origin https://github.com/<your-username>/insurance-management-api.git
git push -u origin main
```
