# FinSense — API Design & Contracts Specification

**Version:** 1.0  
**Status:** Approved Baseline (Phase 0)  

---

## 1. Global Public API Conventions

- **Base URL:** `/api/v1`
- **Content-Type:** `application/json; charset=utf-8` (except receipt upload: `multipart/form-data`)
- **Authentication:** `Authorization: Bearer <jwt_access_token>`
- **Localization Header:** `Accept-Language: tr-TR` (default) or `en-US`
- **Idempotency Header (Mutating POSTs):** `Idempotency-Key: <uuid-v4>`
- **Monetary JSON Serialization Convention:**
  - **Public REST APIs (`Mobile <-> ASP.NET Core`):** Serialize `decimal` values as JSON numbers rounded to 2 display decimal places (e.g., `1420.75`), backed by `decimal` in C#.
  - **Internal Service APIs (`ASP.NET Core <-> Python FastAPI`):** Serialize `Decimal` values as canonical decimal strings (e.g., `"1420.75"`) to guarantee exact, lossless Pydantic v2 `Decimal` parsing without intermediate IEEE 754 float conversion.
- **Dates & Timestamps:**
  - Calendar dates (`transactionDate`, `targetDate`): ISO 8601 `YYYY-MM-DD` (rendered as `DD.MM.YYYY` in the Turkish Mobile UI).
  - System timestamps (`createdAtUtc`, `expiresAtUtc`): ISO 8601 UTC `YYYY-MM-DDTHH:mm:ssZ`.

### 1.1 Standard Error Contract (RFC 7807 `ProblemDetails`)
All `4xx` and `5xx` responses conform to RFC 7807:

```json
{
  "type": "https://finsense.app/errors/insufficient-role",
  "title": "Yetkisiz İşlem",
  "status": 403,
  "detail": "Bu çalışma alanında bütçe oluşturmak için Yönetici (Admin) veya Kurucu (Owner) yetkisine sahip olmalısınız.",
  "instance": "/api/v1/workspaces/7b2e1100-4d3a-4b11-9c22-112233445566/budgets",
  "errorCode": "WORKSPACE_INSUFFICIENT_ROLE",
  "traceId": "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01",
  "errors": {}
}
```

### 1.2 Standard Paginated Response Wrapper
```json
{
  "items": [],
  "pageNumber": 1,
  "pageSize": 20,
  "totalCount": 145,
  "totalPages": 8,
  "hasPreviousPage": false,
  "hasNextPage": true
}
```

---

## 2. Public REST Endpoint Catalog (`ASP.NET Core Web API`)

### 2.1 Authentication & User Profile (`/api/v1/auth` & `/api/v1/users`)

| Method | Route | Auth | Description |
| :--- | :--- | :---: | :--- |
| `POST` | `/api/v1/auth/register` | Public | Registers user, hashes password, provisions default `Personal` workspace & system categories, returns tokens. |
| `POST` | `/api/v1/auth/login` | Public | Authenticates email/password, issues JWT (`15m`) + rotating Refresh Token (`30d`). |
| `POST` | `/api/v1/auth/refresh` | Public | Exchanges valid Refresh Token for a new JWT + rotated Refresh Token (with reuse detection). |
| `POST` | `/api/v1/auth/logout` | Bearer | Revokes current refresh token family. |
| `GET` | `/api/v1/users/me` | Bearer | Returns current user profile + list of member workspaces (`Personal` + `Household`). |
| `PATCH` | `/api/v1/users/me` | Bearer | Updates user profile fields (`displayName`, `preferredLocale`). |
| `DELETE` | `/api/v1/users/me` | Bearer | Permanently deletes user account, Personal Space, and personal receipts from Object Storage. |

### 2.2 Workspaces & Household Invitations (`/api/v1/workspaces`)

| Method | Route | Min Role | Description |
| :--- | :--- | :---: | :--- |
| `GET` | `/api/v1/workspaces` | Bearer | Lists all workspaces the user belongs to with their role. |
| `POST` | `/api/v1/workspaces` | Bearer | Creates a new `Household` workspace (caller becomes `Owner`). |
| `DELETE` | `/api/v1/workspaces/{wsId}` | `Owner` | Permanently deletes a `Household` workspace and all its associated records and receipts (`Personal` workspace cannot be deleted independently of account deletion). |
| `GET` | `/api/v1/workspaces/{wsId}/members` | `Viewer` | Lists members of the workspace and their roles. |
| `POST` | `/api/v1/workspaces/{wsId}/invitations` | `Admin` | Generates an 8-character alphanumeric invitation code (`expiresAtUtc`, `assignedRole`) and optionally sends email via `IEmailService`. |
| `POST` | `/api/v1/workspaces/invitations/accept` | Bearer | Accepts an invitation using the 8-character `invitationCode`. |
| `PATCH` | `/api/v1/workspaces/{wsId}/members/{userId}` | `Admin` | Updates a member's role (`Admin`, `Contributor`, `Viewer`). |
| `DELETE` | `/api/v1/workspaces/{wsId}/members/{userId}` | `Admin` / Self | Removes a member or allows a member to leave the household. |

### 2.3 Wallets & Categories (`/api/v1/workspaces/{wsId}/...`)

| Method | Route | Min Role | Description |
| :--- | :--- | :---: | :--- |
| `GET` | `/api/v1/workspaces/{wsId}/wallets` | `Viewer` | Lists active wallets and balances (`Cash`, `BankAccount`, `CreditCard`, `Savings`). |
| `POST` | `/api/v1/workspaces/{wsId}/wallets` | `Admin` | Creates a new wallet (`TRY` default). |
| `PUT` | `/api/v1/workspaces/{wsId}/wallets/{id}` | `Admin` | Updates wallet metadata (`name`, `creditLimit`, `icon`, `color`, `isArchived`). **`currentBalance` cannot be arbitrarily overwritten without auditability**—balance adjustments either shift by the `initialBalance` delta or record an explicit adjustment transaction. |
| `GET` | `/api/v1/workspaces/{wsId}/categories` | `Viewer` | Lists localized system and custom workspace categories (`Income` & `Expense`). |
| `POST` | `/api/v1/workspaces/{wsId}/categories` | `Admin` | Creates a custom category in the workspace (`isSystem = false`). |
| `PUT` | `/api/v1/workspaces/{wsId}/categories/{id}` | `Admin` | Updates a custom category. **System categories (`isSystem = true`) cannot be modified (`400/403`).** |
| `DELETE` | `/api/v1/workspaces/{wsId}/categories/{id}` | `Admin` | Deletes a custom category. **System categories (`isSystem = true`) cannot be deleted.** Custom categories referenced by existing transactions are soft-deleted/archived so historical transaction category references remain intact. |

### 2.4 Transactions & Inter-Wallet Transfers (`/api/v1/workspaces/{wsId}/transactions`)

| Method | Route | Min Role | Description |
| :--- | :--- | :---: | :--- |
| `GET` | `/api/v1/workspaces/{wsId}/transactions` | `Viewer` | Paginated transaction list with filters (`dateFrom`, `dateTo`, `walletId`, `categoryId`, `type`, `paidByUserId`, `search`). |
| `GET` | `/api/v1/workspaces/{wsId}/transactions/{id}` | `Viewer` | Returns full transaction detail including receipt metadata if linked. |
| `POST` | `/api/v1/workspaces/{wsId}/transactions` | `Contributor` | Creates an `Income`, `Expense`, or `Transfer` transaction and atomically updates wallet balance(s). |
| `PUT` | `/api/v1/workspaces/{wsId}/transactions/{id}` | `Contributor`* | Updates transaction and reconciles wallet balance(s) (*Contributors can only edit their own; Admins/Owners can edit any). |
| `DELETE` | `/api/v1/workspaces/{wsId}/transactions/{id}` | `Contributor`* | Soft-deletes transaction and reverses wallet balance impact. |

#### Example: `POST /api/v1/workspaces/{wsId}/transactions` Request
```json
{
  "type": "Expense",
  "walletId": "9a112233-4455-6677-8899-aabbccddeeff",
  "counterpartyWalletId": null,
  "categoryId": "3c445566-7788-99aa-bbcc-ddeeff001122",
  "receiptId": "f1e2d3c4-b5a6-4789-8012-3456789abcde",
  "amount": 1420.75,
  "currencyCode": "TRY",
  "transactionDate": "2026-11-10",
  "merchantName": "Migros Ticaret A.Ş.",
  "notes": "Haftalık mutfak alışverişi",
  "paidByUserId": null
}
```

### 2.5 Budgets, Savings Goals & Analytics

| Method | Route | Min Role | Description |
| :--- | :--- | :---: | :--- |
| `GET` | `/api/v1/workspaces/{wsId}/budgets?year=2026&month=11` | `Viewer` | Returns overall and category budgets with real-time `spentAmount`, `remainingAmount`, `utilizationPercent`, and `status` (`Normal`, `Warning`, `Exceeded`). |
| `POST` | `/api/v1/workspaces/{wsId}/budgets` | `Admin` | Creates or updates a monthly budget (`categoryId = null` for overall, or specific `categoryId`). |
| `DELETE` | `/api/v1/workspaces/{wsId}/budgets/{id}` | `Admin` | Soft-deletes a monthly budget (`Admin` or `Owner` only). |
| `GET` | `/api/v1/workspaces/{wsId}/savings-goals` | `Viewer` | Lists workspace savings goals and completion percentages. If `linkedWalletId` is set, progress dynamically reflects that wallet's `currentBalance`; otherwise it uses `currentAmount`. |
| `POST` | `/api/v1/workspaces/{wsId}/savings-goals` | `Admin` | Creates a new savings goal (`linkedWalletId` optional). |
| `PUT` | `/api/v1/workspaces/{wsId}/savings-goals/{id}` | `Admin` | Updates a savings goal (`name`, `targetAmount`, `targetDate`, `linkedWalletId`, or manual `currentAmount` when `linkedWalletId` is null). |
| `DELETE` | `/api/v1/workspaces/{wsId}/savings-goals/{id}` | `Admin` | Soft-deletes a savings goal. |
| `GET` | `/api/v1/workspaces/{wsId}/analytics/summary` | `Viewer` | Returns deterministic cash flow (`totalIncome`, `totalExpense`, `netCashFlow`), category breakdown, and member attribution for a date range (`Transfer` excluded). |

### 2.6 Receipt OCR & AI Assistant (`/api/v1/workspaces/{wsId}/...`)

| Method | Route | Min Role | Description |
| :--- | :--- | :---: | :--- |
| `POST` | `/api/v1/workspaces/{wsId}/ai/scan-receipt` | `Contributor` | Uploads receipt image (`multipart/form-data` or JSON base64, max 10 MB), strips EXIF, runs local RapidOCR via Python service, and returns draft extraction for user confirmation. |
| `POST` | `/api/v1/workspaces/{wsId}/ai/chat` | `Viewer` | Sends a natural-language question scoped strictly to `{wsId}` and returns a deterministic financial answer. |
| `GET` | `/api/v1/workspaces/{wsId}/ai/history` | `Viewer` | Returns recent workspace AI chat history. |
| `DELETE` | `/api/v1/workspaces/{wsId}/ai/history` | `Viewer` | Clears workspace AI chat history. |

---

## 3. Internal Service Contracts (`ASP.NET Core` -> `Python FastAPI`)

All internal endpoints require header: `X-Internal-Api-Key: <secret_from_env>`.

### 3.1 `POST /internal/v1/ocr/extract`

#### Request Payload (Sent by ASP.NET Core)
```json
{
  "receiptId": "f1e2d3c4-b5a6-4789-8012-3456789abcde",
  "locale": "tr-TR",
  "currencyCode": "TRY",
  "imageBase64": "<base64_encoded_exif_stripped_image>",
  "mimeType": "image/jpeg",
  "availableCategories": [
    { "id": "3c445566-7788-99aa-bbcc-ddeeff001122", "name": "Market" },
    { "id": "4d556677-8899-aabb-ccdd-eeff00112233", "name": "Yeme-İçme" },
    { "id": "5e667788-99aa-bbcc-ddee-ff0011223344", "name": "Ulaşım" }
  ]
}
```

#### Response Payload (Validated by Pydantic v2 in FastAPI)
```json
{
  "receiptId": "f1e2d3c4-b5a6-4789-8012-3456789abcde",
  "providerUsed": "deterministic-engine",
  "merchantName": "MİGROS TİCARET A.Ş.",
  "transactionDate": "2026-11-10",
  "totalAmount": "1420.75",
  "taxAmount": "128.45",
  "currencyCode": "TRY",
  "suggestedCategoryId": "3c445566-7788-99aa-bbcc-ddeeff001122",
  "confidenceScore": 0.96,
  "lineItems": [
    { "description": "TAM YAĞLI PEYNİR", "quantity": 1, "totalPrice": "345.00" },
    { "description": "ZEYTİNYAĞI 1L", "quantity": 1, "totalPrice": "495.50" }
  ],
  "warnings": []
}
```

---

### 3.2 `POST /internal/v1/ai/chat`

#### Request Payload (Deterministic Context Sent by ASP.NET Core)
```json
{
  "locale": "tr-TR",
  "workspaceType": "Household",
  "currencyCode": "TRY",
  "currentDate": "2026-11-10",
  "userQuestion": "Bu ay markete toplam ne kadar harcadık ve bütçemizden ne kadar kaldı?",
  "deterministicContext": {
    "periodStart": "2026-11-01",
    "periodEnd": "2026-11-30",
    "totalIncome": "85000.00",
    "totalExpense": "34200.50",
    "netCashFlow": "50799.50",
    "categoryBreakdown": [
      {
        "categoryName": "Market",
        "spentAmount": "12450.00",
        "transactionCount": 14,
        "previousMonthSpentAmount": "10100.00",
        "monthOverMonthChangePercent": "23.27"
      }
    ],
    "budgets": [
      {
        "scopeName": "Market",
        "limitAmount": "15000.00",
        "spentAmount": "12450.00",
        "remainingAmount": "2550.00",
        "utilizationPercent": "83.00",
        "status": "Warning"
      }
    ]
  }
}
```

#### Response Payload (Returned by FastAPI)
```json
{
  "answer": "Kasım ayında **Market** kategorisinde toplam **₺12.450,00** (14 işlem) harcama yaptınız. ₺15.000,00 olan aylık market bütçenizin **%83,00**'ünü kullandınız ve geriye **₺2.550,00** kullanılabilir bütçeniz kaldı. Geçen aya kıyasla market harcamalarınızda %23,27'lik bir artış bulunuyor.",
  "referencedMetrics": [
    { "label": "Kasım Market Harcaması", "value": "₺12.450,00" },
    { "label": "Kalan Market Bütçesi", "value": "₺2.550,00" }
  ],
  "providerUsed": "deterministic-engine"
}
```
