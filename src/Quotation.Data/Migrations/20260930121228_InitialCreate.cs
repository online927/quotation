using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Quotation.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AuditLog",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    AtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    User = table.Column<string>(type: "TEXT", nullable: false),
                    Machine = table.Column<string>(type: "TEXT", nullable: false),
                    Action = table.Column<string>(type: "TEXT", nullable: false),
                    EntityType = table.Column<string>(type: "TEXT", nullable: false),
                    EntityId = table.Column<string>(type: "TEXT", nullable: false),
                    Details = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditLog", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Customers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TallyGuid = table.Column<string>(type: "TEXT", nullable: false),
                    AlterId = table.Column<long>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false, collation: "NOCASE"),
                    Aliases = table.Column<string>(type: "TEXT", nullable: false),
                    MailingName = table.Column<string>(type: "TEXT", nullable: false),
                    LedgerGroup = table.Column<string>(type: "TEXT", nullable: false),
                    Address = table.Column<string>(type: "TEXT", nullable: false),
                    StateName = table.Column<string>(type: "TEXT", nullable: false),
                    StateCode = table.Column<string>(type: "TEXT", nullable: false),
                    Pincode = table.Column<string>(type: "TEXT", nullable: false),
                    Country = table.Column<string>(type: "TEXT", nullable: false),
                    Gstin = table.Column<string>(type: "TEXT", nullable: false),
                    GstRegistrationType = table.Column<string>(type: "TEXT", nullable: false),
                    Pan = table.Column<string>(type: "TEXT", nullable: false),
                    ContactPerson = table.Column<string>(type: "TEXT", nullable: false),
                    Phone = table.Column<string>(type: "TEXT", nullable: false),
                    Mobile = table.Column<string>(type: "TEXT", nullable: false),
                    Email = table.Column<string>(type: "TEXT", nullable: false),
                    IsDeleted = table.Column<bool>(type: "INTEGER", nullable: false),
                    LastSyncedUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Customers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "EmailMessages",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    GmailMessageId = table.Column<string>(type: "TEXT", nullable: false),
                    ThreadId = table.Column<string>(type: "TEXT", nullable: false),
                    ReceivedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    From = table.Column<string>(type: "TEXT", nullable: false),
                    Subject = table.Column<string>(type: "TEXT", nullable: false),
                    BodyText = table.Column<string>(type: "TEXT", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    QuotationId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ProcessingResult = table.Column<string>(type: "TEXT", nullable: false),
                    AnalysisJson = table.Column<string>(type: "TEXT", nullable: true),
                    Attempts = table.Column<int>(type: "INTEGER", nullable: false),
                    LastError = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmailMessages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "NumberSeries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SeriesKey = table.Column<string>(type: "TEXT", nullable: false),
                    FinancialYearStart = table.Column<int>(type: "INTEGER", nullable: false),
                    NextSequence = table.Column<int>(type: "INTEGER", nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NumberSeries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Products",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TallyGuid = table.Column<string>(type: "TEXT", nullable: false),
                    AlterId = table.Column<long>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false, collation: "NOCASE"),
                    Aliases = table.Column<string>(type: "TEXT", nullable: false),
                    PartNumber = table.Column<string>(type: "TEXT", nullable: false),
                    Brand = table.Column<string>(type: "TEXT", nullable: false),
                    Manufacturer = table.Column<string>(type: "TEXT", nullable: false),
                    Model = table.Column<string>(type: "TEXT", nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: false),
                    StockGroup = table.Column<string>(type: "TEXT", nullable: false),
                    Category = table.Column<string>(type: "TEXT", nullable: false),
                    Unit = table.Column<string>(type: "TEXT", nullable: false),
                    Hsn = table.Column<string>(type: "TEXT", nullable: false),
                    GstRate = table.Column<decimal>(type: "TEXT", nullable: true),
                    GstSource = table.Column<string>(type: "TEXT", nullable: false),
                    Rate = table.Column<decimal>(type: "TEXT", nullable: true),
                    RateDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    RateSource = table.Column<string>(type: "TEXT", nullable: false),
                    Moq = table.Column<string>(type: "TEXT", nullable: false),
                    IsDeleted = table.Column<bool>(type: "INTEGER", nullable: false),
                    LastSyncedUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Products", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Quotations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Number = table.Column<string>(type: "TEXT", nullable: true),
                    FinancialYearStart = table.Column<int>(type: "INTEGER", nullable: true),
                    Sequence = table.Column<int>(type: "INTEGER", nullable: true),
                    Date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    Source = table.Column<int>(type: "INTEGER", nullable: false),
                    CustomerId = table.Column<int>(type: "INTEGER", nullable: true),
                    Buyer_Name = table.Column<string>(type: "TEXT", nullable: false),
                    Buyer_Address = table.Column<string>(type: "TEXT", nullable: false),
                    Buyer_Gstin = table.Column<string>(type: "TEXT", nullable: false),
                    Buyer_StateName = table.Column<string>(type: "TEXT", nullable: false),
                    Buyer_StateCode = table.Column<string>(type: "TEXT", nullable: false),
                    Buyer_ContactPerson = table.Column<string>(type: "TEXT", nullable: false),
                    Buyer_Phone = table.Column<string>(type: "TEXT", nullable: false),
                    Buyer_Email = table.Column<string>(type: "TEXT", nullable: false),
                    Consignee_Name = table.Column<string>(type: "TEXT", nullable: false),
                    Consignee_Address = table.Column<string>(type: "TEXT", nullable: false),
                    Consignee_Gstin = table.Column<string>(type: "TEXT", nullable: false),
                    Consignee_StateName = table.Column<string>(type: "TEXT", nullable: false),
                    Consignee_StateCode = table.Column<string>(type: "TEXT", nullable: false),
                    Consignee_ContactPerson = table.Column<string>(type: "TEXT", nullable: false),
                    Consignee_Phone = table.Column<string>(type: "TEXT", nullable: false),
                    Consignee_Email = table.Column<string>(type: "TEXT", nullable: false),
                    ConsigneeSameAsBuyer = table.Column<bool>(type: "INTEGER", nullable: false),
                    BuyersReference = table.Column<string>(type: "TEXT", nullable: false),
                    BuyersReferenceDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    DispatchedThrough = table.Column<string>(type: "TEXT", nullable: false),
                    PaymentTerms = table.Column<string>(type: "TEXT", nullable: false),
                    OtherReferences = table.Column<string>(type: "TEXT", nullable: false),
                    Destination = table.Column<string>(type: "TEXT", nullable: false),
                    TermsOfDelivery = table.Column<string>(type: "TEXT", nullable: false),
                    Remarks = table.Column<string>(type: "TEXT", nullable: false),
                    TermsAndConditions = table.Column<string>(type: "TEXT", nullable: false),
                    PackingForwarding = table.Column<decimal>(type: "TEXT", nullable: false),
                    PackingForwardingPercent = table.Column<decimal>(type: "TEXT", nullable: true),
                    Subtotal = table.Column<decimal>(type: "TEXT", nullable: false),
                    TaxTotal = table.Column<decimal>(type: "TEXT", nullable: false),
                    RoundOff = table.Column<decimal>(type: "TEXT", nullable: false),
                    GrandTotal = table.Column<decimal>(type: "TEXT", nullable: false),
                    TotalQuantity = table.Column<decimal>(type: "TEXT", nullable: false),
                    AmountInWords = table.Column<string>(type: "TEXT", nullable: false),
                    PreparedBy = table.Column<string>(type: "TEXT", nullable: false),
                    VerifiedBy = table.Column<string>(type: "TEXT", nullable: false),
                    PdfPath = table.Column<string>(type: "TEXT", nullable: true),
                    PdfError = table.Column<string>(type: "TEXT", nullable: true),
                    DataFreshnessWarning = table.Column<string>(type: "TEXT", nullable: true),
                    DuplicatedFromId = table.Column<Guid>(type: "TEXT", nullable: true),
                    SourceEmailId = table.Column<int>(type: "INTEGER", nullable: true),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    UpdatedUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ApprovedBy = table.Column<string>(type: "TEXT", nullable: true),
                    ApprovedUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CancelledBy = table.Column<string>(type: "TEXT", nullable: true),
                    CancelledUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Revision = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Quotations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Settings",
                columns: table => new
                {
                    Key = table.Column<string>(type: "TEXT", nullable: false),
                    Json = table.Column<string>(type: "TEXT", nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedBy = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Settings", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "StockGroups",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TallyGuid = table.Column<string>(type: "TEXT", nullable: false),
                    AlterId = table.Column<long>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false, collation: "NOCASE"),
                    Parent = table.Column<string>(type: "TEXT", nullable: false),
                    Hsn = table.Column<string>(type: "TEXT", nullable: false),
                    GstRate = table.Column<decimal>(type: "TEXT", nullable: true),
                    IsDeleted = table.Column<bool>(type: "INTEGER", nullable: false),
                    LastSyncedUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockGroups", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SyncRuns",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Kind = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    StartedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    FinishedUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ProductsChanged = table.Column<int>(type: "INTEGER", nullable: false),
                    CustomersChanged = table.Column<int>(type: "INTEGER", nullable: false),
                    ProductsDeleted = table.Column<int>(type: "INTEGER", nullable: false),
                    CustomersDeleted = table.Column<int>(type: "INTEGER", nullable: false),
                    MaxAlterId = table.Column<long>(type: "INTEGER", nullable: true),
                    Message = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SyncRuns", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Units",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TallyGuid = table.Column<string>(type: "TEXT", nullable: false),
                    AlterId = table.Column<long>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false, collation: "NOCASE"),
                    FormalName = table.Column<string>(type: "TEXT", nullable: false),
                    DecimalPlaces = table.Column<int>(type: "INTEGER", nullable: false),
                    IsDeleted = table.Column<bool>(type: "INTEGER", nullable: false),
                    LastSyncedUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Units", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Username = table.Column<string>(type: "TEXT", nullable: false, collation: "NOCASE"),
                    DisplayName = table.Column<string>(type: "TEXT", nullable: false),
                    PasswordHash = table.Column<string>(type: "TEXT", nullable: false),
                    Role = table.Column<int>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    MustChangePassword = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastLoginUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "QuotationLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    QuotationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    LineNo = table.Column<int>(type: "INTEGER", nullable: false),
                    ProductId = table.Column<int>(type: "INTEGER", nullable: true),
                    ItemName = table.Column<string>(type: "TEXT", nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: false),
                    Hsn = table.Column<string>(type: "TEXT", nullable: false),
                    GstRate = table.Column<decimal>(type: "TEXT", nullable: false),
                    DueOn = table.Column<string>(type: "TEXT", nullable: false),
                    Quantity = table.Column<decimal>(type: "TEXT", nullable: false),
                    Unit = table.Column<string>(type: "TEXT", nullable: false),
                    Rate = table.Column<decimal>(type: "TEXT", nullable: false),
                    TallyRate = table.Column<decimal>(type: "TEXT", nullable: true),
                    DiscountPercent = table.Column<decimal>(type: "TEXT", nullable: false),
                    Amount = table.Column<decimal>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QuotationLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QuotationLines_Quotations_QuotationId",
                        column: x => x.QuotationId,
                        principalTable: "Quotations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ApiSessions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<int>(type: "INTEGER", nullable: false),
                    TokenHash = table.Column<string>(type: "TEXT", nullable: false),
                    ClientMachine = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ExpiresUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastSeenUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApiSessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ApiSessions_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ApiSessions_TokenHash",
                table: "ApiSessions",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ApiSessions_UserId",
                table: "ApiSessions",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLog_AtUtc",
                table: "AuditLog",
                column: "AtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLog_EntityType_EntityId",
                table: "AuditLog",
                columns: new[] { "EntityType", "EntityId" });

            migrationBuilder.CreateIndex(
                name: "IX_Customers_Gstin",
                table: "Customers",
                column: "Gstin");

            migrationBuilder.CreateIndex(
                name: "IX_Customers_Name",
                table: "Customers",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_Customers_TallyGuid",
                table: "Customers",
                column: "TallyGuid",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EmailMessages_GmailMessageId",
                table: "EmailMessages",
                column: "GmailMessageId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EmailMessages_Status",
                table: "EmailMessages",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_EmailMessages_ThreadId",
                table: "EmailMessages",
                column: "ThreadId");

            migrationBuilder.CreateIndex(
                name: "IX_NumberSeries_SeriesKey_FinancialYearStart",
                table: "NumberSeries",
                columns: new[] { "SeriesKey", "FinancialYearStart" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Products_Name",
                table: "Products",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_Products_TallyGuid",
                table: "Products",
                column: "TallyGuid",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_QuotationLines_ProductId",
                table: "QuotationLines",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_QuotationLines_QuotationId_LineNo",
                table: "QuotationLines",
                columns: new[] { "QuotationId", "LineNo" });

            migrationBuilder.CreateIndex(
                name: "IX_Quotations_CustomerId",
                table: "Quotations",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_Quotations_Date",
                table: "Quotations",
                column: "Date");

            migrationBuilder.CreateIndex(
                name: "IX_Quotations_FinancialYearStart_Sequence",
                table: "Quotations",
                columns: new[] { "FinancialYearStart", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Quotations_Number",
                table: "Quotations",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Quotations_Status",
                table: "Quotations",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_StockGroups_Name",
                table: "StockGroups",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_StockGroups_TallyGuid",
                table: "StockGroups",
                column: "TallyGuid",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SyncRuns_StartedUtc",
                table: "SyncRuns",
                column: "StartedUtc");

            migrationBuilder.CreateIndex(
                name: "IX_Units_TallyGuid",
                table: "Units",
                column: "TallyGuid",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Users_Username",
                table: "Users",
                column: "Username",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ApiSessions");

            migrationBuilder.DropTable(
                name: "AuditLog");

            migrationBuilder.DropTable(
                name: "Customers");

            migrationBuilder.DropTable(
                name: "EmailMessages");

            migrationBuilder.DropTable(
                name: "NumberSeries");

            migrationBuilder.DropTable(
                name: "Products");

            migrationBuilder.DropTable(
                name: "QuotationLines");

            migrationBuilder.DropTable(
                name: "Settings");

            migrationBuilder.DropTable(
                name: "StockGroups");

            migrationBuilder.DropTable(
                name: "SyncRuns");

            migrationBuilder.DropTable(
                name: "Units");

            migrationBuilder.DropTable(
                name: "Users");

            migrationBuilder.DropTable(
                name: "Quotations");
        }
    }
}
