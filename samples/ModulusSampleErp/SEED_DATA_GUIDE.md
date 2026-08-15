# Seed Data Integration Guide

## Overview
This guide covers the comprehensive seed data system for Modulus Sample ERP, including module-specific seeders and the main integration point.

## Seed Data Strategy

### Module-Level Seeders
Each module contains its own seeder class in `Infrastructure/Database`:

```
ModulusSample.Modules.{Module}.Infrastructure.Database/
├── {Module}DbContextSeed.cs
├── {Module}DbContext.cs
└── Migrations/
```

### Integration Point
The main seeder is located in `Modulus.EntityFrameworkCore` and orchestrates all module seeders.

## Available Seeders

### Core Modules

#### 1. Identity Module (`IdentityDbContextSeed.cs`)
**Seed Data:**
- **Permissions**: All system permissions from `AppPermissions`
- **Roles**: Admin and User roles with proper mappings
- **Users**: System users for testing

**Default Credentials:**
- Admin: `admin@modulussample.com` / `Admin123!`
- User: `user1@modulussample.com` / `User123!`

**Features:**
- Automatic permission synchronization
- Role-permission mapping
- System user creation for initialization
- Development credential logging

#### 2. Catalog Module (`CatalogDbContextSeed.cs`)
**Seed Data:**
- **Products**: 10 sample products with varying price points
  - Widgets: $100-250 range
  - Gadgets: $120-320 range  
  - Components: $30-85 range
  - Kits: $350-1200 range
  - Accessories: $50-150 range

**Product Structure:**
```
Products:
├── Widget Products (Acme Widget, Premium Widget)
├── Gadget Products (Universal Gadget, Smart Gadget)
├── Component Products (Standard Component, Advanced Component)
├── Kit Products (Starter Kit, Professional Kit)
└── Accessory Products (Standard Accessory, Premium Accessory)
```

#### 3. Inventory Module (`InventoryDbContextSeed.cs`)
**Seed Data:**
- **Stock**: Sample stock entries for products
- **Locations**: Warehouse locations with proper codes
- **Movements**: Initial stock movements
- **Reservations**: Sample reservations

**Location Structure:**
```
Warehouse Locations:
├── WH-A-01-01 (Zone A, Aisle 01, Rack 01, Shelf 01)
├── WH-A-01-02 (Zone A, Aisle 01, Rack 01, Shelf 02)
├── WH-B-02-01 (Zone B, Aisle 02, Rack 01, Shelf 01)
└── WH-B-02-02 (Zone B, Aisle 02, Rack 01, Shelf 02)
```

#### 4. Partners Module (`PartnersDbContextSeed.cs`)
**Seed Data:**
- **Partners**: 4 sample partners
  - **Customers**: Acme Corp, Global Distributors
  - **Suppliers**: Tech Supplies Ltd, Premium Materials Inc
- **Contacts**: Contact persons for each partner
- **Addresses**: Multiple addresses per partner

**Partner Structure:**
```
Partners:
├── Customers
│   ├── Acme Corp (Credit Limit: $50,000)
│   └── Global Distributors (Credit Limit: $100,000)
└── Suppliers
    ├── Tech Supplies Ltd
    └── Premium Materials Inc
```

#### 5. Sales Module (`SalesDbContextSeed.cs`)
**Seed Data:**
- **Orders**: 6 sample orders in various states
  - **Completed**: 2 orders (delivered)
  - **Confirmed**: 2 orders (processing/ready)
  - **Processing**: 1 order (confirmed but not shipped)
  - **Cancelled**: 1 order
- **Order Items**: 3 items per order
- **Order History**: Status change history

**Order States:**
```
Orders:
├── Completed (2)
│   ├── ORD-001 → Confirmed → Processing → Shipped → Delivered
│   └── ORD-002 → Confirmed → Processing → Shipped → Delivered
├── Confirmed (1)
│   └── ORD-003 → Confirmed
├── Processing (1)
│   └── ORD-004 → Confirmed → Processing
└── Cancelled (1)
    └── ORD-005 → Cancelled
```

#### 6. Billing Module (`BillingDbContextSeed.cs`)
**Seed Data:**
- **Invoices**: 5 sample invoices
  - **Paid**: 2 invoices
  - **Pending**: 2 invoices
  - **Overdue**: 1 invoice
- **Payments**: Sample payment records
- **Credit Notes**: Sample credit notes

**Invoice States:**
```
Invoices:
├── Paid (2)
│   ├── INV-2024-001 (CreditCard payment)
│   └── INV-2024-002 (BankTransfer payment)
├── Pending (2)
│   ├── INV-2024-003 (Issued and sent)
│   └── INV-2024-004 (Issued only)
└── Overdue (1)
    └── INV-2024-005 (Past due date)
```

#### 7. Purchasing Module (`PurchasingDbContextSeed.cs`)
**Seed Data:**
- **Purchase Orders**: Sample purchase orders
- **Requisitions**: Sample purchase requisitions
- **Suppliers**: Integration with Partners module

#### 8. Features Module (`FeaturesDbContextSeed.cs`)
**Seed Data:**
- **Features**: Core system features
- **Tenant Features**: Feature assignments per tenant
- **Configurations**: Feature settings

#### 9. Settings Module (`SettingsDbContextSeed.cs`)
**Seed Data:**
- **System Settings**: Global application settings
- **Tenant Settings**: Tenant-specific overrides

## Usage

### Running Seed Data

#### Manual Seeding (Development)
```csharp
// In your Program.cs or during application startup
await app.Services.MigrateModulusDatabasesAsync();
```

#### Seeding Specific Module
```csharp
// Example: Seed catalog module only
var catalogContext = serviceProvider.GetRequiredService<CatalogDbContext>();
var logger = serviceProvider.GetRequiredService<ILogger<CatalogDbContext>>();
var tenantId = Guid.Parse("your-tenant-id");

await CatalogDbContextSeed.SeedEnhancedAsync(catalogContext, logger, tenantId);
```

#### Enhanced Seeding
```csharp
// Use enhanced seeders for more comprehensive data
await CatalogDbContextSeed.SeedEnhancedAsync(catalogContext, logger, tenantId);
await SalesDbContextSeed.SeedEnhancedAsync(salesContext, logger, tenantId);
await BillingDbContextSeed.SeedEnhancedAsync(billingContext, logger, tenantId);
```

### Integration with Modulus Framework

The seeders integrate with the Modulus framework's `MigrateModulusDatabasesAsync` extension:

```csharp
// This method automatically calls all module seeders
await app.Services.MigrateModulusDatabasesAsync();
```

## Seed Data IDs

### Predictable IDs for Testing
For consistent testing, enhanced seeders use predictable GUIDs:

```csharp
// Product IDs
"aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa" // Acme Widget
"bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb" // Premium Widget
"cccccccc-cccc-cccc-cccc-cccccccccccc" // Universal Gadget
"dddddddd-dddd-dddd-dddd-dddddddddddd" // Smart Gadget

// Invoice IDs
"aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaa001" // INV-2024-001
"aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaa002" // INV-2024-002
"aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaa003" // INV-2024-003

// Order IDs
"oooooooo-oooo-oooo-oooo-ooooooooo001" // ORD-001
"oooooooo-oooo-oooo-oooo-ooooooooo002" // ORD-002
"oooooooo-oooo-oooo-oooo-ooooooooo003" // ORD-003

// Customer IDs
"cccccccc-cccc-cccc-cccc-cccccccccc01" // Customer 1
"cccccccc-cccc-cccc-cccc-cccccccccc02" // Customer 2
"cccccccc-cccc-cccc-cccc-cccccccccc03" // Customer 3
```

## Environment-Specific Seeding

### Development Environment
- Full seed data with all sample entities
- Development credentials logged
- Comprehensive test scenarios

### Production Environment
- Minimal seed data (system users, basic settings)
- No test credentials logged
- Production-ready defaults

### Testing Environment
- Enhanced seed data for comprehensive testing
- Predictable IDs for test automation
- Full coverage of business scenarios

## Seeding Best Practices

### 1. Idempotent Seeders
All seeders check for existing data before inserting:

```csharp
if (await context.Products.AnyAsync())
    return; // Skip if data already exists
```

### 2. Error Handling
All seeders include comprehensive error handling:

```csharp
try
{
    // Seeding logic
}
catch (Exception ex)
{
    logger.LogError(ex, "An error occurred while seeding {Module} module");
    throw;
}
```

### 3. Transaction Management
Complex seeding operations use transactions:

```csharp
await context.Database.BeginTransactionAsync();
try
{
    // Seeding operations
    await context.CommitAsync();
    await context.Database.CommitTransactionAsync();
}
catch
{
    await context.Database.RollbackTransactionAsync();
    throw;
}
```

### 4. Logging
Detailed logging for debugging and monitoring:

```csharp
logger.LogInformation("Enhanced {Module} module seeding completed: {Count} {EntityType} added", 
    count, entityType);
```

## Seed Data Relationships

### Module Interdependencies
The seed data respects module relationships:

```
Identity → Provides users and roles for other modules
Catalog → Provides products used by Sales and Inventory
Partners → Provides customers used by Sales and Billing
Inventory → Provides stock used by Sales
Sales → Provides orders used by Billing
Billing → Provides invoices linked to Sales orders
```

### Seeding Order
The framework seeds modules in dependency order:

1. Identity (foundational)
2. Tenants (multi-tenancy)
3. Partners (customers/suppliers)
4. Catalog (products)
5. Inventory (stock)
6. Sales (orders)
7. Billing (invoices)
8. Purchasing (purchase orders)
9. Features (feature flags)
10. Settings (configuration)

## Custom Seed Data

### Adding Custom Seeders
Create a custom seeder following the pattern:

```csharp
public static class CustomModuleDbContextSeed
{
    public static async Task SeedAsync(
        CustomModuleDbContext context,
        ILogger logger,
        Guid tenantId)
    {
        try
        {
            if (await context.CustomEntities.AnyAsync())
                return;

            var entities = new[]
            {
                CustomEntity.Create(
                    Guid.NewGuid(),
                    "Sample Entity",
                    tenantId,
                    "seed").Value
            };

            context.CustomEntities.AddRange(entities);
            await context.CommitAsync();

            logger.LogInformation("Custom module seeding completed: {Count} entities added", entities.Length);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An error occurred while seeding Custom module");
            throw;
        }
    }
}
```

### Registering Custom Seeders
Add your custom seeder to the migration extensions:

```csharp
public static class MigrationExtensions
{
    public static async Task ApplySeeding(this IServiceProvider serviceProvider)
    {
        // Existing seeders...
        
        // Add your custom seeder
        var customContext = serviceProvider.GetRequiredService<CustomModuleDbContext>();
        await CustomModuleDbContextSeed.SeedAsync(customContext, logger, tenantId);
    }
}
```

## Troubleshooting

### Common Issues

#### 1. Seeding Fails with Foreign Key Errors
**Cause**: Attempting to seed dependent entities before parents
**Solution**: Ensure proper seeding order and use predictable GUIDs

#### 2. Duplicate Data on Re-seeding
**Cause**: Missing idempotency checks
**Solution**: Add `if (await context.Entities.AnyAsync()) return;` at start

#### 3. Missing Data After Seeding
**Cause**: Transaction rollback or exception handling issues
**Solution**: Check logs and ensure proper commit/rollback handling

#### 4. Seed Data Not Found in Queries
**Cause**: Wrong tenant ID or filtering
**Solution**: Ensure correct tenant context and query filters

## Seed Data Maintenance

### Updating Seed Data
When updating seed data:

1. **Add New Items**: Append to existing arrays
2. **Update Existing Items**: Consider migration scripts
3. **Remove Items**: Handle gracefully with checks

### Version Control
- Commit seed data changes with clear descriptions
- Document breaking changes
- Maintain backwards compatibility when possible

## Performance Considerations

### Bulk Operations
Use bulk operations for large seed datasets:

```csharp
context.Products.AddRangeAsync(largeProductList);
await context.CommitAsync();
```

### Batch Processing
For very large datasets, process in batches:

```csharp
foreach (var batch in products.Batch(1000))
{
    context.Products.AddRange(batch);
    await context.CommitAsync();
}
```

## Security Considerations

### Sensitive Data
- **Never seed real passwords** in production
- Use strong, randomly generated passwords
- Change default credentials immediately after deployment

### Data Isolation
- Ensure seed data respects tenant boundaries
- Use proper tenant filtering in queries
- Validate tenant context in operations

## Conclusion

The comprehensive seed data system provides:
- **Consistent Testing**: Predictable data across environments
- **Development Support**: Quick setup for development and testing
- **Production Readiness**: Production-appropriate seed data
- **Maintainability**: Easy to update and extend
- **Performance**: Efficient seeding operations

Use the seed data for local development, integration testing, and initial system deployment. Customize according to your specific requirements while maintaining the established patterns.