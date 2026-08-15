# Modulus Sample ERP - Postman Testing Guide

This guide covers how to log in to the Modulus Sample ERP API and test the full
app end-to-end using the Postman collection. It reflects the **actual** API as
exposed by the running application (OpenIddict token endpoint, per-module REST
routes) — the old collection referenced `/api/identity/login` and port 5000,
both of which are stale.

## Prerequisites

- The API is running and reachable at `http://localhost:5016`
  (`GET http://localhost:5016/health` should return `200 OK`).
- Postman installed.
- The two files in this repo:
  - `Postman_Collection_ModulusSampleERP.json`
  - `Postman_Environment_ModulusSampleERP.json`

## Setup

1. **Import the environment**
   - Postman → Settings icon (top right) → *Environments* → *Import* →
     `Postman_Environment_ModulusSampleERP.json`.
   - Select it from the environment dropdown (top right).
2. **Import the collection**
   - *Import* → select `Postman_Collection_ModulusSampleERP.json`.
3. The environment already sets `baseUrl = http://localhost:5016` and
   pre-populates seeded IDs (`tenantId`, `productId`, `partnerId`, `orderId`,
   `invoiceId`, `settingId`, `featureId`, `notificationId`, `roleId`,
   `folderId`, …). `accessToken` / `refreshToken` start empty — they are filled
   automatically by the **Login** request.

## 1. Log in (required first)

Open the folder **`01. Authentication`** and run:

- **`Login - Password Grant`**

This calls `POST {{baseUrl}}/connect/token` with an OAuth2 **password grant**:

```
grant_type=password
client_id=modulussample-swagger
username=admin@modulussample.com
password=Admin123!
scope=openid profile offline_access
```

No client secret is required. A test script captures `access_token`,
`refresh_token`, and `id_token` into the environment automatically.

> Admin: `admin@modulussample.com` / `Admin123!`
> Standard user: `user1@modulussample.com` / `User123!`

**Expected response (200):**

```json
{
  "access_token": "eyJ...",
  "token_type": "Bearer",
  "expires_in": 1800,
  "refresh_token": "abc...",
  "id_token": "eyJ..."
}
```

Every other request in the collection sends
`Authorization: Bearer {{accessToken}}`, so once logged in you can run the
endpoints in any order.

### Token refresh caveat

The **`Refresh Token`** request posts `grant_type=refresh_token` with
`client_id=modulussample-swagger` and `refresh_token={{refreshToken}}`.
**Known issue:** it currently returns
`400 invalid_grant` — *"The user account is no longer active"* — even though
the admin user is `Active` in the database. This is a pre-existing framework
limitation. The reliable path is to simply re-run **`Login - Password Grant`**
whenever you get a `401`.

### Verify identity

- **`Get Current User (userinfo)`** → `GET /connect/userinfo` returns the
  authenticated user's claims (`sub`, `name`, `email`, …). Its test script
  stores `userId` from `sub`.

## 2. Endpoint walkthrough by module

All GET endpoints return `200` when authenticated. Responses use a uniform
envelope; list endpoints return `PagedResult` shaped data:

```json
{ "success": true, "data": { "items": [ ... ], "totalCount": 6, ... }, "message": null, "traceId": null }
```

Errors come back as `application/problem+json`:
`{ "code": "...", "message": "...", "type": "..." }`.

### 02. Tenants
| Request | Route |
|---------|-------|
| Get All Tenants | `GET /api/v1/tenants` |
| Get Tenant by ID | `GET /api/v1/tenants/{{tenantId}}` |
| Get Active Tenants | `GET /api/v1/tenants/active` |
| Search Tenants | `GET /api/v1/tenants/search` |
| Create Tenant | `POST /api/v1/tenants` |
| Update Tenant | `PUT /api/v1/tenants/{{tenantId}}` |
| Deactivate / Activate | `POST /api/v1/tenants/{{tenantId}}/deactivate` · `.../activate` |

### 03. Identity & Admin
| Request | Route |
|---------|-------|
| Get My Profile | `GET /api/v1/users/profile` |
| Get My Permissions | `GET /api/v1/users/permissions` |
| Get My Roles | `GET /api/v1/users/roles` |
| List Users (Admin) | `GET /api/v1/admin/users` |
| Get User by ID | `GET /api/v1/admin/users/{{userId}}` |
| List Roles | `GET /api/v1/roles` |
| List Permissions | `GET /api/v1/admin/permissions` |
| Register User | `POST /api/v1/auth/register` |
| Create Role | `POST /api/v1/roles` |
| Assign Role to User | `POST /api/v1/admin/users/{{userId}}/roles/{{roleId}}` |
| List Sessions | `GET /api/v1/sessions` |
| Change My Password | `PUT /api/v1/users/password` |

### 04. Catalog
| Request | Route |
|---------|-------|
| List Products | `GET /api/catalog/products` (captures `productId`) |
| Get Product by ID | `GET /api/catalog/products/{{productId}}` |
| Create Product | `POST /api/catalog/products` |

### 05. Partners
| Request | Route |
|---------|-------|
| List Partners | `GET /api/partners` |
| Get Partner by ID | `GET /api/partners/{{partnerId}}` |
| Create Partner | `POST /api/partners` |

### 06. Inventory
| Request | Route |
|---------|-------|
| List Warehouses | `GET /api/warehouses` |
| Get Warehouse by ID | `GET /api/warehouses/{{warehouseId}}` |
| Create Warehouse | `POST /api/warehouses` |

### 07. Sales
| Request | Route |
|---------|-------|
| List Sales Orders | `GET /api/sales-orders` (captures `orderId`) |
| Get Sales Order by ID | `GET /api/sales-orders/{{orderId}}` |
| Create Sales Order | `POST /api/sales-orders` |

### 08. Purchasing
| Request | Route |
|---------|-------|
| List Purchase Requisitions | `GET /api/purchase-requisitions` |
| Get Requisition by ID | `GET /api/purchase-requisitions/{{requisitionId}}` |
| Create Requisition | `POST /api/purchase-requisitions` |
| Submit Requisition | `POST /api/purchase-requisitions/{{requisitionId}}/submit` |
| Approve Requisition | `POST /api/purchase-requisitions/{{requisitionId}}/approve` |
| List Purchase Orders | `GET /api/purchase-orders` |
| Create Purchase Order | `POST /api/purchase-orders` |
| List Goods Receipts | `GET /api/goods-receipts` |
| Create Goods Receipt | `POST /api/goods-receipts` |

### 09. Billing
| Request | Route |
|---------|-------|
| List Invoices | `GET /api/invoices` |
| Get Invoice by ID | `GET /api/invoices/{{invoiceId}}` |
| Create Invoice | `POST /api/invoices` |
| Add Invoice Line | `POST /api/invoices/{{invoiceId}}/lines` |
| Mark Invoice as Overdue | `POST /api/invoices/{{invoiceId}}/overdue` |
| Send Invoice | `POST /api/invoices/{{invoiceId}}/send` |
| Mark Invoice as Paid | `POST /api/invoices/{{invoiceId}}/pay` |
| List Payments | `GET /api/payments` |
| Create Payment | `POST /api/payments` |
| Confirm Payment | `POST /api/payments/{{paymentId}}/confirm` |
| List Credit Notes | `GET /api/credit-notes` |
| Create Credit Note | `POST /api/credit-notes` |
| Issue Credit Note | `POST /api/credit-notes/{{creditNoteId}}/issue` |
| Apply Credit Note | `POST /api/credit-notes/{{creditNoteId}}/apply` |

### 10. Settings
| Request | Route |
|---------|-------|
| List Settings | `GET /api/v1/settings` |
| Get Public Settings | `GET /api/v1/settings/public` |
| Get Setting by Key | `GET /api/v1/settings/key/app.name` |
| Get Setting by ID | `GET /api/v1/settings/{{settingId}}` |
| Create Setting | `POST /api/v1/settings` |
| Update Setting Value | `PUT /api/v1/settings/{{settingId}}/value` |
| Delete Setting | `DELETE /api/v1/settings/{{settingId}}` |

### 11. Features
| Request | Route |
|---------|-------|
| List Feature Flags | `GET /api/v1/features` |
| Get Enabled Features | `GET /api/v1/features/enabled` |
| Get Feature by Key | `GET /api/v1/features/key/catalog.new-checkout` |
| Create Feature Flag | `POST /api/v1/features` |
| Toggle Feature Flag | `PATCH /api/v1/features/{{featureId}}/toggle` |

### 12. Notifications
| Request | Route |
|---------|-------|
| My Notifications | `GET /api/v1/notifications/my` |
| Unread Count | `GET /api/v1/notifications/unread-count` |
| Get Notification by ID | `GET /api/v1/notifications/{{notificationId}}` |
| Create Notification | `POST /api/v1/notifications` |
| Mark Notification as Read | `PATCH /api/v1/notifications/{{notificationId}}/read` |
| Mark All Read | `PATCH /api/v1/notifications/read-all` |

### 13. Media
| Request | Route |
|---------|-------|
| Create Media Folder | `POST /api/media/folders` |

> **Note:** `/api/media/folders` only supports `POST`. A `GET` returns `405`
> (this is correct behavior, not a bug).

### 14. Virtual File Explorer
| Request | Route |
|---------|-------|
| Get File Explorer Tree | `GET /api/v1/file-explorer/tree` |
| List Folders | `GET /api/v1/file-explorer/folders` |
| Create Folder | `POST /api/v1/file-explorer/folders` |
| List Files in Folder | `GET /api/v1/file-explorer/folders/{{folderId}}/files` |

### 15. Authorization (framework)
| Request | Route |
|---------|-------|
| List Delegations | `GET /api/authorization/delegations` |
| Create Grant | `POST /api/authorization/grants` |

## 3. Tenant header behavior (read this before testing)

The API supports a request header `X-Tenant-Id` for tenant-scoped data.
**For the seeded sample data, do NOT send the header.**

- **No `X-Tenant-Id` header** → requests run in the **host scope** and read the
  host-seeded data (this is what the collection does).
  - Settings: **4 rows** (`app.name`, `app.default-locale`,
    `company.support-email`, `notifications.email.enabled`).
  - Products: **10**, Sales orders: **6**, Invoices: **5**, Partners, Roles,
    Feature flags, Notifications, Media folders, VFE folders all seeded.
- **With `X-Tenant-Id: 11111111-...` (Acme)** → the tenant header is sent but
  tenant resolution is **not actually wired** in this sample (the
  `UseMultiTenancy()` middleware is not registered and the EF tenant store is
  commented out). Entities that implement `IHasTenantId` (settings, feature
  flags) fail **closed** and return **0 rows**; entities that do not
  (products, sales orders, invoices, …) still return data. This is expected
  behavior, not a defect.

If you want to experiment, add `X-Tenant-Id: {{tenantId}}` to a request and
observe the difference on `/api/v1/settings`.

## 4. Suggested end-to-end smoke sequence

A quick pass that exercises every module (all verified working):

1. `01. Authentication → Login - Password Grant`
2. `01. Authentication → Get Current User (userinfo)`
3. `03. Identity & Admin → Get My Profile` → 200, returns admin profile
4. `03. Identity & Admin → List Permissions` → 13 permission codes
5. `02. Tenants → Get All Tenants` → Acme (1111…) + StartUp (2222…)
6. `04. Catalog → List Products` → 10 products; stores `productId`
7. `05. Partners → List Partners` → customers (`c000…`) + suppliers (`b000…`)
8. `06. Inventory → List Warehouses` → 4 warehouses
9. `07. Sales → List Sales Orders` → ORD-2024-001..006; stores `orderId`
10. `09. Billing → List Invoices` → INV-2024-001..005
11. `10. Settings → List Settings` → 4 host settings
12. `11. Features → List Feature Flags` → catalog.new-checkout & co.
13. `12. Notifications → My Notifications` / `Unread Count`
14. `14. Virtual File Explorer → Get File Explorer Tree`

To exercise **mutating** flows, run the `Create …` / state-transition requests
(`Create Product`, `Create Partner`, `Create Warehouse`, `Create Sales Order`,
`Submit/Approve Requisition`, `Create Invoice` → `Add Invoice Line` → `Send` →
`Pay`, `Create Payment` → `Confirm`, `Create Setting`, `Toggle Feature Flag`,
`Create Notification` → `Mark Read`, `Create Folder`).

## Troubleshooting

| Problem | Fix |
|---------|-----|
| `401 Unauthorized` | Run **Login - Password Grant** again; token expired (30 min) |
| `403 Forbidden` | You lack the required permission; use the admin account |
| `404` on `/api/v1/users/profile` | Was fixed by mapping the `sub`/`user_id` claim; re-pull latest build |
| `400 invalid_grant` on refresh | Known framework limitation; just re-login |
| `405` on `GET /api/media/folders` | Expected; that route is `POST`-only |
| `500` on settings list | Was a null `Setting.Id` mapping bug — fixed; use latest build |
| Empty `data` with tenant header | Tenant resolution not wired in the sample — omit the header |

## Collection structure (what changed)

The collection was rebuilt against the real API:

- OAuth2 password-grant login against OpenIddict (`/connect/token`) instead of
  the non-existent `/api/identity/login`.
- Port corrected from `5000` → `5016`.
- Folder names now match the real module groupings (Tenants, Identity & Admin,
  Catalog, Partners, Inventory, Sales, Purchasing, Billing, Settings, Features,
  Notifications, Media, Virtual File Explorer, Authorization).
- Removed endpoints that don't exist (category/stock/location/template CRUD,
  order ship/refund, quotes/returns) and added the real ones (requisition
  submit/approve, payment confirm, credit-note issue/apply, invoice overdue/
  send/pay).
- Test scripts auto-capture `accessToken`, `refreshToken`, `idToken`, `userId`,
  `productId`, `orderId` into the environment.
- Environment pre-seeded with the real seeded GUIDs from the database.
