# TradeFlow — Enterprise Multi-Tenant Procurement, Import & Landed Cost SaaS

**Complete Production Blueprint v1.0** | June 2026
Target market: Bangladesh + South Asia manufacturing, RMG, textile, pharma, food, construction, trading & import/export enterprises.

## Document Suite

| # | Document | Contents |
|---|----------|----------|
| 00 | [Executive Summary & Business Strategy](00-executive-summary-business.md) | Vision, MVP/Enterprise scope, pricing, BD market strategy, competitors, GTM, SaaS revenue model |
| 01 | [Business Requirements Specification](01-brs.md) | Business context, S2P / P2P / Import-to-Inventory lifecycles, end-to-end process flow |
| 02 | [SRS, Multi-Tenancy, Roles & Workflow Engine](02-srs-multitenancy-roles-workflow.md) | Functional/non-functional requirements, tenant isolation design, user roles, permission matrix, org structure, approval hierarchy, workflow engine |
| 03 | [Core Procurement Modules](03-procurement-modules.md) | Vendor mgmt → PR → RFQ/RFP → PO → GRN → Invoice → Budget → Spend analytics |
| 04 | [Import Management Modules](04-import-management-modules.md) | Import planning, IPO, PI/CI/PL, HS codes, permits, shipment, containers, BL/AWB, insurance, Incoterms, customs/port clearance, C&F |
| 05 | [Trade Finance Modules](05-trade-finance-modules.md) | LC, back-to-back LC, bank contracts, TT, SWIFT tracking, import loans, margin, amendments, payment schedules |
| 06 | [Customs, Tax & Landed Cost Modules](06-customs-tax-landed-cost.md) | BD duty structure (CD/RD/SD/VAT/AIT/AT), assessment tracking, duty structure mgmt, landed cost allocation & calculation |
| 07 | [Intelligence Engines & AI](07-intelligence-engines-ai.md) | Landed Cost Engine, Cost Forecasting Engine, Purchase Feasibility Engine, Supplier Risk Engine, ML pipelines |
| 08 | [Inventory & Finance Integration + Reporting](08-integrations-reporting.md) | GRN/warehouse/batch/lot/serial, valuation, AP/GL/cost centers/accruals, all 15 reporting modules |
| 09 | [Architecture: DDD & Event-Driven Design](09-architecture-ddd-eda.md) | Bounded contexts, aggregates, entities, VOs, domain services/events, EDA, sagas, .NET 9 solution layout |
| 10 | [Database Design — PostgreSQL](10-database-postgresql.md) | Full schema DDL, RLS, partitioning, indexing, reporting DB strategy |
| 11 | [Search, Infrastructure & DevOps](11-search-infra-devops.md) | OpenSearch design, AWS architecture, Terraform, Docker, ECS, CI/CD, DR, monitoring, security, audit, compliance |
| 12 | [UI/UX Screen Catalog & User Journeys](12-ui-screens-user-journeys.md) | Next.js app structure, screen inventory per module, journey maps, notification matrix |

## Module-Detail Convention
Every module section follows the template: **Objectives → Business Rules → Data Model → UI Screens → User Journey → Workflow → Reports → Notifications → Approval Process.** Deep-dive modules carry full detail; sibling modules of the same pattern reference the shared template with deltas only.

## Reading Order
- Business stakeholders: 00 → 01 → 03–06 → 08
- Architects/developers: 02 → 09 → 10 → 11 → 07
- Product/UX: 02 → 12 → 03–06
