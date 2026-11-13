using MapDoc.Core.Models;
using MapDoc.Core.Presets;
using Microsoft.EntityFrameworkCore;

namespace MapDoc.Data.Seeding;

public static class DatabaseInitializer
{
    public static async Task InitializeAsync(
        IDbContextFactory<MapDocDbContext> dbFactory,
        bool autoMigrate,
        bool seedDemoData,
        CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        if (autoMigrate)
        {
            await db.Database.MigrateAsync(ct);
        }

        await SeedPresetFunctoidsAsync(db, ct);

        if (seedDemoData && !await db.Types.AnyAsync(ct))
        {
            await SeedShowcaseDemoAsync(db, ct);
        }
    }

    private static async Task SeedPresetFunctoidsAsync(MapDocDbContext db, CancellationToken ct)
    {
        var existingByKey = await db.Functoids
            .Where(f => f.IsPreset && f.PresetKey != null)
            .ToDictionaryAsync(f => f.PresetKey!, ct);

        foreach (var preset in PresetFunctoids.All)
        {
            if (preset.PresetKey is null) continue;

            if (!existingByKey.TryGetValue(preset.PresetKey, out var existing))
            {
                db.Functoids.Add(new FunctoidDefinition
                {
                    Id = preset.Id,
                    PresetKey = preset.PresetKey,
                    IsPreset = true,
                    Name = preset.Name,
                    Category = preset.Category,
                    Symbol = preset.Symbol,
                    Description = preset.Description,
                    Inputs = preset.Inputs.Select(i => i.Clone()).ToList(),
                    IsVariadic = preset.IsVariadic,
                    VariadicInputName = preset.VariadicInputName,
                    VariadicInputType = preset.VariadicInputType?.Clone(),
                    OutputType = preset.OutputType.Clone(),
                    OutputInference = preset.OutputInference,
                    OutputInferencePinIndex = preset.OutputInferencePinIndex,
                    AggregatesRepeatingInput = preset.AggregatesRepeatingInput,
                    ConfigParams = preset.ConfigParams.Select(c => c.Clone()).ToList(),
                    CreatedAtUtc = preset.CreatedAtUtc,
                    UpdatedAtUtc = preset.UpdatedAtUtc
                });
            }
            else
            {
                existing.Name = preset.Name;
                existing.Category = preset.Category;
                existing.Symbol = preset.Symbol;
                existing.Description = preset.Description;
                existing.Inputs = preset.Inputs.Select(i => i.Clone()).ToList();
                existing.IsVariadic = preset.IsVariadic;
                existing.VariadicInputName = preset.VariadicInputName;
                existing.VariadicInputType = preset.VariadicInputType?.Clone();
                existing.OutputType = preset.OutputType.Clone();
                existing.OutputInference = preset.OutputInference;
                existing.OutputInferencePinIndex = preset.OutputInferencePinIndex;
                existing.AggregatesRepeatingInput = preset.AggregatesRepeatingInput;
                existing.ConfigParams = preset.ConfigParams.Select(c => c.Clone()).ToList();
            }
        }

        await db.SaveChangesAsync(ct);
    }

    private static async Task SeedShowcaseDemoAsync(MapDocDbContext db, CancellationToken ct)
    {
        var now = DateTime.UtcNow;

        // Custom Functoid example
        var isoCountryFunctoid = new FunctoidDefinition
        {
            Id = Guid.Parse("20000000-0000-4000-8000-000000000001"),
            IsPreset = false,
            Name = "NormalizeIsoCountry",
            Category = FunctoidCategory.Custom,
            Symbol = "ISO",
            Description = "Maps 2-letter or full country names to ISO 3166-1 alpha-3 uppercase codes for ERP ingestion.",
            Inputs =
            [
                new FunctoidInputSpec
                {
                    Index = 0,
                    Name = "country",
                    Description = "2-letter or raw country code from CRM.",
                    TypeSpec = PortTypeSpec.Of(TypeDescriptor.ScalarString())
                }
            ],
            OutputType = PortTypeSpec.Of(TypeDescriptor.ScalarString(StringSubtype.Custom, "iso-alpha-3")),
            ConfigParams =
            [
                new FunctoidConfigParamSpec
                {
                    Key = "fallbackCountry",
                    Label = "Fallback ISO-3",
                    Placeholder = "USA",
                    DefaultValue = "USA",
                    Description = "Default ISO-3 code when unrecognized."
                }
            ],
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        db.Functoids.Add(isoCountryFunctoid);

        // ==================== CRM SOURCE TYPES ====================
        var crmAddressId = Guid.Parse("30000000-0000-4000-8000-000000000001");
        var crmEmployeeId = Guid.Parse("30000000-0000-4000-8000-000000000002");
        var crmCustomerId = Guid.Parse("30000000-0000-4000-8000-000000000003");
        var crmOrderLineId = Guid.Parse("30000000-0000-4000-8000-000000000004");
        var crmOrderId = Guid.Parse("30000000-0000-4000-8000-000000000005");

        // ==================== ERP TARGET TYPES ====================
        var erpRecipientId = Guid.Parse("40000000-0000-4000-8000-000000000001");
        var erpInvoiceLineId = Guid.Parse("40000000-0000-4000-8000-000000000002");
        var erpInvoiceId = Guid.Parse("40000000-0000-4000-8000-000000000003");

        var types = new List<TypeDefinition>
        {
            new() { Id = crmAddressId, Name = "Address", Namespace = "CRM", Tags = "postal,location", Description = "Physical postal address in CRM.", CreatedAtUtc = now, UpdatedAtUtc = now },
            new() { Id = crmEmployeeId, Name = "EmployeeNode", Namespace = "CRM", Tags = "org,recursive", Description = "Self-referential sales org hierarchy node (Employee -> Manager -> Employee).", CreatedAtUtc = now, UpdatedAtUtc = now },
            new() { Id = crmCustomerId, Name = "CustomerProfile", Namespace = "CRM", Tags = "customer,account", Description = "Customer contact and account metadata.", CreatedAtUtc = now, UpdatedAtUtc = now },
            new() { Id = crmOrderLineId, Name = "OrderLineItem", Namespace = "CRM", Tags = "order,line", Description = "Individual SKU line item in a CRM sales order.", CreatedAtUtc = now, UpdatedAtUtc = now },
            new() { Id = crmOrderId, Name = "SalesOrder", Namespace = "CRM", Tags = "order,source", Description = "Inbound e-commerce/CRM sales order payload.", CreatedAtUtc = now, UpdatedAtUtc = now },

            new() { Id = erpRecipientId, Name = "InvoiceRecipient", Namespace = "ERP", Tags = "billing,party", Description = "Flattened billing recipient record in ERP.", CreatedAtUtc = now, UpdatedAtUtc = now },
            new() { Id = erpInvoiceLineId, Name = "InvoiceLine", Namespace = "ERP", Tags = "invoice,line", Description = "Posted financial line item in ERP.", CreatedAtUtc = now, UpdatedAtUtc = now },
            new() { Id = erpInvoiceId, Name = "SalesInvoice", Namespace = "ERP", Tags = "invoice,target", Description = "Target ERP sales invoice document.", CreatedAtUtc = now, UpdatedAtUtc = now }
        };

        db.Types.AddRange(types);
        await db.SaveChangesAsync(ct);

        PropertyDefinition Prop(Guid ownerId, int sort, string name, TypeDescriptor td, string desc, bool nullable = false, bool optional = false)
        {
            var p = new PropertyDefinition
            {
                Id = Guid.NewGuid(),
                OwnerTypeId = ownerId,
                SortOrder = sort,
                Name = name,
                Description = desc,
                Type = td,
                IsNullable = nullable,
                IsOptional = optional
            };
            p.SyncReferencedType();
            return p;
        }

        // CRM.Address properties
        var pAddrStreet = Prop(crmAddressId, 0, "Street", TypeDescriptor.ScalarString(), "Street line 1.");
        var pAddrCity = Prop(crmAddressId, 1, "City", TypeDescriptor.ScalarString(), "City or locality.");
        var pAddrPostal = Prop(crmAddressId, 2, "PostalCode", TypeDescriptor.ScalarString(), "ZIP / Postal code.", optional: true);
        var pAddrCountry = Prop(crmAddressId, 3, "CountryCode", TypeDescriptor.ScalarString(), "ISO-2 country code (e.g., US, DE).");

        // CRM.EmployeeNode (self-referential!)
        var pEmpId = Prop(crmEmployeeId, 0, "EmployeeId", TypeDescriptor.ScalarString(StringSubtype.Uuid), "Sales rep identifier.");
        var pEmpName = Prop(crmEmployeeId, 1, "FullName", TypeDescriptor.ScalarString(), "Employee full display name.");
        var pEmpEmail = Prop(crmEmployeeId, 2, "WorkEmail", TypeDescriptor.ScalarString(StringSubtype.Email), "Corporate email.");
        var pEmpManager = Prop(crmEmployeeId, 3, "Manager", TypeDescriptor.ObjectOf(crmEmployeeId), "Direct reporting manager (self-referential).", nullable: true, optional: true);

        // CRM.CustomerProfile
        var pCustId = Prop(crmCustomerId, 0, "CustomerId", TypeDescriptor.ScalarString(StringSubtype.Uuid), "Unique CRM customer UUID.");
        var pCustFirst = Prop(crmCustomerId, 1, "FirstName", TypeDescriptor.ScalarString(), "Given name.");
        var pCustLast = Prop(crmCustomerId, 2, "LastName", TypeDescriptor.ScalarString(), "Family name.");
        var pCustEmail = Prop(crmCustomerId, 3, "Email", TypeDescriptor.ScalarString(StringSubtype.Email), "Primary billing email.");
        var pCustPhone = Prop(crmCustomerId, 4, "Phone", TypeDescriptor.ScalarString(), "Contact phone number (may be null in CRM).", nullable: true, optional: true);
        var pCustBilling = Prop(crmCustomerId, 5, "BillingAddress", TypeDescriptor.ObjectOf(crmAddressId), "Customer billing address.");
        var pCustRep = Prop(crmCustomerId, 6, "AccountOwner", TypeDescriptor.ObjectOf(crmEmployeeId), "Assigned sales representative.");

        // CRM.OrderLineItem
        var pLineNum = Prop(crmOrderLineId, 0, "LineNumber", TypeDescriptor.ScalarNumber(NumberSubtype.Int32), "1-based line sequence integer.");
        var pLineSku = Prop(crmOrderLineId, 1, "Sku", TypeDescriptor.ScalarString(), "Product catalog SKU.");
        var pLineQty = Prop(crmOrderLineId, 2, "Quantity", TypeDescriptor.ScalarNumber(NumberSubtype.Int32), "Ordered unit quantity.");
        var pLinePrice = Prop(crmOrderLineId, 3, "UnitPrice", TypeDescriptor.ScalarNumber(NumberSubtype.Decimal), "Per-unit net price.");

        // CRM.SalesOrder
        var pOrdId = Prop(crmOrderId, 0, "OrderId", TypeDescriptor.ScalarString(StringSubtype.Uuid), "CRM order UUID.");
        var pOrdCreated = Prop(crmOrderId, 1, "CreatedUtc", TypeDescriptor.ScalarString(StringSubtype.DateTime), "UTC timestamp when order was placed.");
        var pOrdPriority = Prop(crmOrderId, 2, "IsPriority", TypeDescriptor.ScalarBoolean(), "Whether expedited fulfillment was requested.");
        var pOrdCustomer = Prop(crmOrderId, 3, "Customer", TypeDescriptor.ObjectOf(crmCustomerId), "Ordering customer profile.");
        var pOrdLines = Prop(crmOrderId, 4, "Lines", TypeDescriptor.ArrayOf(TypeDescriptor.ObjectOf(crmOrderLineId)), "Ordered line items.");
        var pOrdTags = Prop(crmOrderId, 5, "Tags", TypeDescriptor.ArrayOf(TypeDescriptor.ScalarString()), "Order campaign/routing tags.", optional: true);

        // ERP.InvoiceRecipient
        var pRecRef = Prop(erpRecipientId, 0, "ExternalCustomerRef", TypeDescriptor.ScalarString(), "External CRM customer key (plain string).");
        var pRecName = Prop(erpRecipientId, 1, "DisplayName", TypeDescriptor.ScalarString(), "Full combined customer name.");
        var pRecEmail = Prop(erpRecipientId, 2, "ContactEmail", TypeDescriptor.ScalarString(StringSubtype.Email), "Invoice delivery email.");
        var pRecPhone = Prop(erpRecipientId, 3, "ContactPhone", TypeDescriptor.ScalarString(), "Required phone or fallback placeholder.");
        var pRecStreet = Prop(erpRecipientId, 4, "StreetLine", TypeDescriptor.ScalarString(), "Billing street.");
        var pRecCity = Prop(erpRecipientId, 5, "CityName", TypeDescriptor.ScalarString(), "Billing city.");
        var pRecCountry = Prop(erpRecipientId, 6, "CountryIso3", TypeDescriptor.ScalarString(), "Normalized 3-letter ISO country code.");
        var pRecEscalationMgr = Prop(erpRecipientId, 7, "EscalationManagerName", TypeDescriptor.ScalarString(), "Account owner's reporting manager.", nullable: true, optional: true);

        // ERP.InvoiceLine
        var pInvLineSeq = Prop(erpInvoiceLineId, 0, "LineSeqCode", TypeDescriptor.ScalarString(), "Stringified line sequence code.");
        var pInvPart = Prop(erpInvoiceLineId, 1, "PartNumber", TypeDescriptor.ScalarString(), "ERP part number.");
        var pInvQty = Prop(erpInvoiceLineId, 2, "InvoicedQty", TypeDescriptor.ScalarNumber(NumberSubtype.Decimal), "Decimal invoiced quantity.");
        var pInvExt = Prop(erpInvoiceLineId, 3, "ExtendedAmount", TypeDescriptor.ScalarNumber(NumberSubtype.Decimal), "Line total (Quantity * UnitPrice).");
        var pInvCurr = Prop(erpInvoiceLineId, 4, "CurrencyCode", TypeDescriptor.ScalarString(), "ISO currency code.");

        // ERP.SalesInvoice
        var pInvId = Prop(erpInvoiceId, 0, "InvoiceId", TypeDescriptor.ScalarString(StringSubtype.Uuid), "Generated ERP invoice UUID.");
        var pInvOrderRef = Prop(erpInvoiceId, 1, "SourceOrderRef", TypeDescriptor.ScalarString(), "Originating CRM order ID.");
        var pInvDate = Prop(erpInvoiceId, 2, "InvoiceDate", TypeDescriptor.ScalarString(StringSubtype.Date), "Accounting invoice date (YYYY-MM-DD).");
        var pInvExpedite = Prop(erpInvoiceId, 3, "ExpediteFlag", TypeDescriptor.ScalarBoolean(), "Priority processing flag.");
        var pInvLineCount = Prop(erpInvoiceId, 4, "TotalLineCount", TypeDescriptor.ScalarNumber(NumberSubtype.Int32), "Count of invoice lines.");
        var pInvRecipient = Prop(erpInvoiceId, 5, "Recipient", TypeDescriptor.ObjectOf(erpRecipientId), "Billing recipient block.");
        var pInvItems = Prop(erpInvoiceId, 6, "Items", TypeDescriptor.ArrayOf(TypeDescriptor.ObjectOf(erpInvoiceLineId)), "Invoice line items.");
        var pInvTagCsv = Prop(erpInvoiceId, 7, "TagsSummary", TypeDescriptor.ScalarString(), "Comma-joined marketing tags.", optional: true);

        db.Properties.AddRange(
            pAddrStreet, pAddrCity, pAddrPostal, pAddrCountry,
            pEmpId, pEmpName, pEmpEmail, pEmpManager,
            pCustId, pCustFirst, pCustLast, pCustEmail, pCustPhone, pCustBilling, pCustRep,
            pLineNum, pLineSku, pLineQty, pLinePrice,
            pOrdId, pOrdCreated, pOrdPriority, pOrdCustomer, pOrdLines, pOrdTags,
            pRecRef, pRecName, pRecEmail, pRecPhone, pRecStreet, pRecCity, pRecCountry, pRecEscalationMgr,
            pInvLineSeq, pInvPart, pInvQty, pInvExt, pInvCurr,
            pInvId, pInvOrderRef, pInvDate, pInvExpedite, pInvLineCount, pInvRecipient, pInvItems, pInvTagCsv);

        await db.SaveChangesAsync(ct);

        // ==================== DEMO MAPPING ====================
        var presetByKey = PresetFunctoids.All.ToDictionary(f => f.PresetKey!);

        var mapping = new MappingDefinition
        {
            Id = Guid.Parse("50000000-0000-4000-8000-000000000001"),
            Name = "SalesOrder_To_SalesInvoice",
            Namespace = "OrderToCash",
            Tags = "crm,erp,orders,billing",
            Description = "Transforms inbound CRM SalesOrder documents into ERP SalesInvoice records across Header/Customer and Line Items grid pages.",
            SourceTypeId = crmOrderId,
            TargetTypeId = erpInvoiceId,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        db.Mappings.Add(mapping);
        await db.SaveChangesAsync(ct);

        var headerPage = new MappingGridPage
        {
            Id = Guid.NewGuid(),
            MappingId = mapping.Id,
            Name = "Header & Customer",
            SortOrder = 0
        };
        var linesPage = new MappingGridPage
        {
            Id = Guid.NewGuid(),
            MappingId = mapping.Id,
            Name = "Line Items",
            SortOrder = 1
        };
        db.GridPages.AddRange(headerPage, linesPage);
        await db.SaveChangesAsync(ct);

        // Functoids on Page 1 (Header & Customer)
        var fnNewUuid = new MappingFunctoidInstance
        {
            Id = Guid.NewGuid(),
            GridPageId = headerPage.Id,
            FunctoidDefinitionId = presetByKey["preset.newuuid"].Id,
            X = 190,
            Y = 24,
            Comment = "Mint fresh ERP Invoice UUID at ingestion time."
        };

        var fnConcatName = new MappingFunctoidInstance
        {
            Id = Guid.NewGuid(),
            GridPageId = headerPage.Id,
            FunctoidDefinitionId = presetByKey["preset.concat"].Id,
            X = 190,
            Y = 240,
            Comment = "Combine FirstName and LastName separated by a single space.",
            ConfigValues = new(StringComparer.OrdinalIgnoreCase) { ["separator"] = " " }
        };

        var fnCoalescePhone = new MappingFunctoidInstance
        {
            Id = Guid.NewGuid(),
            GridPageId = headerPage.Id,
            FunctoidDefinitionId = presetByKey["preset.coalesce"].Id,
            X = 190,
            Y = 335,
            Comment = "ERP requires a non-null phone field; default to 'UNLISTED' when CRM phone is null.",
            ConfigValues = new(StringComparer.OrdinalIgnoreCase) { ["defaultLiteral"] = "UNLISTED" }
        };

        var fnIsoCountry = new MappingFunctoidInstance
        {
            Id = Guid.NewGuid(),
            GridPageId = headerPage.Id,
            FunctoidDefinitionId = isoCountryFunctoid.Id,
            X = 190,
            Y = 430,
            Comment = "Convert CRM ISO-2 country code into ERP ISO-3 format.",
            ConfigValues = new(StringComparer.OrdinalIgnoreCase) { ["fallbackCountry"] = "USA" }
        };

        var fnCountLines = new MappingFunctoidInstance
        {
            Id = Guid.NewGuid(),
            GridPageId = headerPage.Id,
            FunctoidDefinitionId = presetByKey["preset.count"].Id,
            X = 190,
            Y = 140,
            Comment = "Total count of order line items for ERP control header."
        };

        var fnJoinTags = new MappingFunctoidInstance
        {
            Id = Guid.NewGuid(),
            GridPageId = headerPage.Id,
            FunctoidDefinitionId = presetByKey["preset.join"].Id,
            X = 190,
            Y = 520,
            Comment = "Flatten CRM tag list into comma-separated summary.",
            ConfigValues = new(StringComparer.OrdinalIgnoreCase) { ["separator"] = ", " }
        };

        // Functoids on Page 2 (Line Items)
        var fnLineToString = new MappingFunctoidInstance
        {
            Id = Guid.NewGuid(),
            GridPageId = linesPage.Id,
            FunctoidDefinitionId = presetByKey["preset.tostring"].Id,
            X = 190,
            Y = 60,
            Comment = "Convert integer LineNumber to string LineSeqCode."
        };

        var fnMultiplyExt = new MappingFunctoidInstance
        {
            Id = Guid.NewGuid(),
            GridPageId = linesPage.Id,
            FunctoidDefinitionId = presetByKey["preset.multiply"].Id,
            X = 190,
            Y = 195,
            Comment = "ExtendedAmount = Quantity * UnitPrice."
        };

        var fnCurrencyConst = new MappingFunctoidInstance
        {
            Id = Guid.NewGuid(),
            GridPageId = linesPage.Id,
            FunctoidDefinitionId = presetByKey["preset.constant"].Id,
            X = 190,
            Y = 305,
            Comment = "Default ERP posting currency to USD.",
            ConfigValues = new(StringComparer.OrdinalIgnoreCase) { ["value"] = "USD", ["type"] = "string" }
        };

        db.FunctoidInstances.AddRange(
            fnNewUuid, fnConcatName, fnCoalescePhone, fnIsoCountry, fnCountLines, fnJoinTags,
            fnLineToString, fnMultiplyExt, fnCurrencyConst);
        await db.SaveChangesAsync(ct);

        string PathOf(params PropertyDefinition[] chain) => string.Join("/", chain.Select(c => c.Id));

        // Links on Page 1
        db.Links.AddRange(
            // NewUuid() -> InvoiceId
            new MappingLink { Id = Guid.NewGuid(), GridPageId = headerPage.Id, SourceFunctoidInstanceId = fnNewUuid.Id, TargetPropertyPath = PathOf(pInvId) },
            // OrderId (uuid) -> SourceOrderRef (plain string: safe widening!)
            new MappingLink { Id = Guid.NewGuid(), GridPageId = headerPage.Id, SourcePropertyPath = PathOf(pOrdId), TargetPropertyPath = PathOf(pInvOrderRef), Comment = "UUID widens cleanly to ERP plain string reference." },
            // CreatedUtc (datetime) -> InvoiceDate (date) — intentional scalar subtype mismatch WARNING!
            new MappingLink { Id = Guid.NewGuid(), GridPageId = headerPage.Id, SourcePropertyPath = PathOf(pOrdCreated), TargetPropertyPath = PathOf(pInvDate), Comment = "Truncates UTC timestamp to accounting date (insert ParseDate to clear warning)." },
            // IsPriority -> ExpediteFlag
            new MappingLink { Id = Guid.NewGuid(), GridPageId = headerPage.Id, SourcePropertyPath = PathOf(pOrdPriority), TargetPropertyPath = PathOf(pInvExpedite) },
            // Lines[] -> Count -> TotalLineCount
            new MappingLink { Id = Guid.NewGuid(), GridPageId = headerPage.Id, SourcePropertyPath = PathOf(pOrdLines), TargetFunctoidInstanceId = fnCountLines.Id, TargetInputIndex = 0 },
            new MappingLink { Id = Guid.NewGuid(), GridPageId = headerPage.Id, SourceFunctoidInstanceId = fnCountLines.Id, TargetPropertyPath = PathOf(pInvLineCount) },
            // Customer.CustomerId -> Recipient.ExternalCustomerRef
            new MappingLink { Id = Guid.NewGuid(), GridPageId = headerPage.Id, SourcePropertyPath = PathOf(pOrdCustomer, pCustId), TargetPropertyPath = PathOf(pInvRecipient, pRecRef) },
            // Customer.FirstName + LastName -> Concat -> Recipient.DisplayName
            new MappingLink { Id = Guid.NewGuid(), GridPageId = headerPage.Id, SourcePropertyPath = PathOf(pOrdCustomer, pCustFirst), TargetFunctoidInstanceId = fnConcatName.Id, TargetInputIndex = 0 },
            new MappingLink { Id = Guid.NewGuid(), GridPageId = headerPage.Id, SourcePropertyPath = PathOf(pOrdCustomer, pCustLast), TargetFunctoidInstanceId = fnConcatName.Id, TargetInputIndex = 1 },
            new MappingLink { Id = Guid.NewGuid(), GridPageId = headerPage.Id, SourceFunctoidInstanceId = fnConcatName.Id, TargetPropertyPath = PathOf(pInvRecipient, pRecName) },
            // Customer.Email -> Recipient.ContactEmail
            new MappingLink { Id = Guid.NewGuid(), GridPageId = headerPage.Id, SourcePropertyPath = PathOf(pOrdCustomer, pCustEmail), TargetPropertyPath = PathOf(pInvRecipient, pRecEmail) },
            // Customer.Phone (nullable) -> Coalesce -> Recipient.ContactPhone
            new MappingLink { Id = Guid.NewGuid(), GridPageId = headerPage.Id, SourcePropertyPath = PathOf(pOrdCustomer, pCustPhone), TargetFunctoidInstanceId = fnCoalescePhone.Id, TargetInputIndex = 0 },
            new MappingLink { Id = Guid.NewGuid(), GridPageId = headerPage.Id, SourceFunctoidInstanceId = fnCoalescePhone.Id, TargetPropertyPath = PathOf(pInvRecipient, pRecPhone) },
            // Customer.BillingAddress.Street / City -> Recipient.StreetLine / CityName
            new MappingLink { Id = Guid.NewGuid(), GridPageId = headerPage.Id, SourcePropertyPath = PathOf(pOrdCustomer, pCustBilling, pAddrStreet), TargetPropertyPath = PathOf(pInvRecipient, pRecStreet) },
            new MappingLink { Id = Guid.NewGuid(), GridPageId = headerPage.Id, SourcePropertyPath = PathOf(pOrdCustomer, pCustBilling, pAddrCity), TargetPropertyPath = PathOf(pInvRecipient, pRecCity) },
            // Customer.BillingAddress.CountryCode -> NormalizeIsoCountry -> Recipient.CountryIso3 (custom subtype -> plain string safe widening!)
            new MappingLink { Id = Guid.NewGuid(), GridPageId = headerPage.Id, SourcePropertyPath = PathOf(pOrdCustomer, pCustBilling, pAddrCountry), TargetFunctoidInstanceId = fnIsoCountry.Id, TargetInputIndex = 0 },
            new MappingLink { Id = Guid.NewGuid(), GridPageId = headerPage.Id, SourceFunctoidInstanceId = fnIsoCountry.Id, TargetPropertyPath = PathOf(pInvRecipient, pRecCountry) },
            // Self-referential path! Customer.AccountOwner.Manager.FullName -> Recipient.EscalationManagerName
            new MappingLink { Id = Guid.NewGuid(), GridPageId = headerPage.Id, SourcePropertyPath = PathOf(pOrdCustomer, pCustRep, pEmpManager, pEmpName), TargetPropertyPath = PathOf(pInvRecipient, pRecEscalationMgr), Comment = "Traverses self-referential EmployeeNode.Manager relationship." },
            // Tags[] -> Join -> TagsSummary
            new MappingLink { Id = Guid.NewGuid(), GridPageId = headerPage.Id, SourcePropertyPath = PathOf(pOrdTags), TargetFunctoidInstanceId = fnJoinTags.Id, TargetInputIndex = 0 },
            new MappingLink { Id = Guid.NewGuid(), GridPageId = headerPage.Id, SourceFunctoidInstanceId = fnJoinTags.Id, TargetPropertyPath = PathOf(pInvTagCsv) }
        );

        // Links on Page 2 (Line Items)
        db.Links.AddRange(
            // Lines[].LineNumber -> ToString -> Items[].LineSeqCode
            new MappingLink { Id = Guid.NewGuid(), GridPageId = linesPage.Id, SourcePropertyPath = PathOf(pOrdLines, pLineNum), TargetFunctoidInstanceId = fnLineToString.Id, TargetInputIndex = 0 },
            new MappingLink { Id = Guid.NewGuid(), GridPageId = linesPage.Id, SourceFunctoidInstanceId = fnLineToString.Id, TargetPropertyPath = PathOf(pInvItems, pInvLineSeq) },
            // Lines[].Sku -> Items[].PartNumber
            new MappingLink { Id = Guid.NewGuid(), GridPageId = linesPage.Id, SourcePropertyPath = PathOf(pOrdLines, pLineSku), TargetPropertyPath = PathOf(pInvItems, pInvPart), Comment = "Direct SKU to PartNumber copy." },
            // Lines[].Quantity (int32) -> Items[].InvoicedQty (decimal: safe widening!)
            new MappingLink { Id = Guid.NewGuid(), GridPageId = linesPage.Id, SourcePropertyPath = PathOf(pOrdLines, pLineQty), TargetPropertyPath = PathOf(pInvItems, pInvQty), Comment = "Int32 widens safely to ERP Decimal quantity." },
            // Lines[].Quantity * Lines[].UnitPrice -> Multiply -> Items[].ExtendedAmount
            new MappingLink { Id = Guid.NewGuid(), GridPageId = linesPage.Id, SourcePropertyPath = PathOf(pOrdLines, pLineQty), TargetFunctoidInstanceId = fnMultiplyExt.Id, TargetInputIndex = 0 },
            new MappingLink { Id = Guid.NewGuid(), GridPageId = linesPage.Id, SourcePropertyPath = PathOf(pOrdLines, pLinePrice), TargetFunctoidInstanceId = fnMultiplyExt.Id, TargetInputIndex = 1 },
            new MappingLink { Id = Guid.NewGuid(), GridPageId = linesPage.Id, SourceFunctoidInstanceId = fnMultiplyExt.Id, TargetPropertyPath = PathOf(pInvItems, pInvExt) },
            // Constant("USD") -> Items[].CurrencyCode (1 -> N broadcast into array item!)
            new MappingLink { Id = Guid.NewGuid(), GridPageId = linesPage.Id, SourceFunctoidInstanceId = fnCurrencyConst.Id, TargetPropertyPath = PathOf(pInvItems, pInvCurr) }
        );

        await db.SaveChangesAsync(ct);
    }
}
