using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace ERP.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class Invoicing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "Rate",
                table: "ledger_entries",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "SourceAmount",
                table: "ledger_entries",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SourceCurrency",
                table: "ledger_entries",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "document_series",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    KindGroup = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    BranchId = table.Column<Guid>(type: "uuid", nullable: false),
                    Mode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Prefix = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Year = table.Column<int>(type: "integer", nullable: false),
                    NextNumber = table.Column<long>(type: "bigint", nullable: false),
                    MinWidth = table.Column<int>(type: "integer", nullable: false),
                    IsDefault = table.Column<bool>(type: "boolean", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_document_series", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "exchange_rates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Rate = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    Source = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_exchange_rates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "exemption_codes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Name = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Group = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_exemption_codes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "withholding_codes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Name = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Tenths = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_withholding_codes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "invoices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Mode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Scenario = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    BranchId = table.Column<Guid>(type: "uuid", nullable: false),
                    SeriesId = table.Column<Guid>(type: "uuid", nullable: true),
                    Number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    ExternalNumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    CurrentAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Time = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    Rate = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    RateType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    RateOverridden = table.Column<bool>(type: "boolean", nullable: false),
                    DocDiscountPercent = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: true),
                    DocDiscountAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    Gross = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Discount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Net = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Otv = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Vat = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Withheld = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Payable = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    GrossTl = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    DiscountTl = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    NetTl = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    OtvTl = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    VatTl = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    WithheldTl = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    PayableTl = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    AffectsStock = table.Column<bool>(type: "boolean", nullable: false),
                    WarehouseId = table.Column<Guid>(type: "uuid", nullable: true),
                    PaymentTermDays = table.Column<int>(type: "integer", nullable: false),
                    Notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    ReturnOfInvoiceId = table.Column<Guid>(type: "uuid", nullable: true),
                    CancelReason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CancelledAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LedgerEntryId = table.Column<Guid>(type: "uuid", nullable: true),
                    SourceType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    SourceId = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Created = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    LastModified = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastModifiedBy = table.Column<string>(type: "text", nullable: true),
                    BuyerOrSeller = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_invoices", x => x.Id);
                    table.ForeignKey(
                        name: "FK_invoices_current_accounts_CurrentAccountId",
                        column: x => x.CurrentAccountId,
                        principalTable: "current_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_invoices_document_series_SeriesId",
                        column: x => x.SeriesId,
                        principalTable: "document_series",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_invoices_invoices_ReturnOfInvoiceId",
                        column: x => x.ReturnOfInvoiceId,
                        principalTable: "invoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_invoices_ledger_entries_LedgerEntryId",
                        column: x => x.LedgerEntryId,
                        principalTable: "ledger_entries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_invoices_warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "invoice_due_lines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    InvoiceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Sort = table.Column<int>(type: "integer", nullable: false),
                    DueDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_invoice_due_lines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_invoice_due_lines_invoices_InvoiceId",
                        column: x => x.InvoiceId,
                        principalTable: "invoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "invoice_lines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    InvoiceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Sort = table.Column<int>(type: "integer", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: true),
                    VariantId = table.Column<Guid>(type: "uuid", nullable: true),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Unit = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Factor = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    Width = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    Length = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    UnitPrice = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    VatIncluded = table.Column<bool>(type: "boolean", nullable: false),
                    DiscountPercent = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: true),
                    DiscountAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    VatRate = table.Column<int>(type: "integer", nullable: false),
                    WithholdingCode = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    ExemptionCode = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    ExemptionReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    OtvPercent = table.Column<decimal>(type: "numeric(9,4)", precision: 9, scale: 4, nullable: true),
                    OtvPerUnit = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    Gross = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    LineDiscount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    DocDiscount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Net = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Otv = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    VatBase = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Vat = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Withheld = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Total = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    NetTl = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    OtvTl = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    VatTl = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    WithheldTl = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    TotalTl = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    SourceLineId = table.Column<Guid>(type: "uuid", nullable: true),
                    Lots = table.Column<string>(type: "jsonb", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_invoice_lines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_invoice_lines_invoices_InvoiceId",
                        column: x => x.InvoiceId,
                        principalTable: "invoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_invoice_lines_product_variants_VariantId",
                        column: x => x.VariantId,
                        principalTable: "product_variants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_invoice_lines_products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "exemption_codes",
                columns: new[] { "Id", "Code", "Group", "IsActive", "Name" },
                values: new object[,]
                {
                    { new Guid("004aa1ca-9eb4-882a-5d91-443b263b94e7"), "324", "tam-istisna", true, "13/h Türkiye Kızılay Derneğine Yapılan Teslim ve Hizmetler ile Türkiye Kızılay Derneğinin Teslim ve Hizmetleri" },
                    { new Guid("0ca80582-f346-647d-ec7a-7466eee3c47b"), "250", "kismi-istisna", true, "Diğerleri" },
                    { new Guid("11b8c594-dfac-95ce-f5dc-f42c0e1d359a"), "323", "tam-istisna", true, "13/ğ 5300 Sayılı Kanuna Göre Düzenlenen Ürün Senetlerinin İhtisas/Ticaret Borsaları Aracılığıyla İlk Teslimi" },
                    { new Guid("11d17e3f-1e89-6715-740d-6365bb7e0bad"), "311", "tam-istisna", true, "14/1 Uluslararası Taşımacılık" },
                    { new Guid("12e36443-f27e-f24e-55f5-5c4fb64d276d"), "219", "kismi-istisna", true, "17/4-p Hazine ve Arsa Ofisi Genel Müdürlüğünün işlemleri" },
                    { new Guid("1fa3d95a-da93-0344-6616-186eb8d35717"), "226", "kismi-istisna", true, "17/2-b Özel Okulları, Üniversite ve Yüksekokullar Tarafından Verilen Bedelsiz Eğitim Ve Öğretim Hizmetleri" },
                    { new Guid("24572723-bd2d-7915-9ded-cec615b00b0d"), "310", "tam-istisna", true, "13/f Ulusal Güvenlik Amaçlı Teslim ve Hizmetler" },
                    { new Guid("26b2ac24-d6ee-690c-a09a-0022496e2956"), "316", "tam-istisna", true, "11/1-a Serbest Bölgelerdeki Müşteriler İçin Yapılan Fason Hizmetler" },
                    { new Guid("27c33c60-bd62-48b1-1120-1ae00bb99e68"), "201", "kismi-istisna", true, "17/1 Kültür ve Eğitim Amacı Taşıyan İşlemler" },
                    { new Guid("2aaf0b13-d937-e7ca-2acf-7c56ffed319d"), "325", "tam-istisna", true, "13/ı Yem Teslimleri" },
                    { new Guid("2d0f30f6-f246-898f-2291-2a264d38f6e0"), "207", "kismi-istisna", true, "17/3 Askeri Fabrika, Tersane ve Atölyelerin İşlemleri" },
                    { new Guid("2d9a38be-faf2-4196-2fb7-22c92e86fe5e"), "301", "tam-istisna", true, "11/1-a Mal İhracatı" },
                    { new Guid("2fa5f7fb-c812-cf7e-c751-ae825b350ae5"), "307", "tam-istisna", true, "13/c Maden Arama, Altın, Gümüş ve Platin Madenleri İçin İşletme, Zenginleştirme Ve Rafinaj Faaliyetlerine İlişkin Teslim Ve Hizmetler[KDVGUT-(II/8-4)]" },
                    { new Guid("30050a1b-3786-26fd-3224-619ce9e3973a"), "205", "kismi-istisna", true, "17/2-d Taşınmaz Kültür Varlıklarına İlişkin Teslimler ve Mimarlık Hizmetleri" },
                    { new Guid("3a69c1cc-210a-9f1f-9028-e67090440306"), "208", "kismi-istisna", true, "17/4-c Birleşme, Devir, Dönüşüm ve Bölünme İşlemleri" },
                    { new Guid("41ce9536-0c39-fd3c-5728-06f6030d1da1"), "330", "tam-istisna", true, "KDV 13/j md. Organize Sanayi Bölgeleri ile Küçük Sanayi Sitelerinin İnşasına İlişkin Teslim ve Hizmetler" },
                    { new Guid("439b2e1f-feb8-07dd-601b-a9bb9d6a24ba"), "305", "tam-istisna", true, "13/b Deniz ve Hava Taşıma Araçları İçin Liman Ve Hava Meydanlarında Yapılan Hizmetler" },
                    { new Guid("43eb9738-ba12-5cbe-561c-9efa26bc84cb"), "216", "kismi-istisna", true, "17/4-m Tasarruf Mevduatı Sigorta Fonunun İşlemleri" },
                    { new Guid("4b252964-9e21-3bee-f06f-666691df4b44"), "232", "kismi-istisna", true, "17/4-g Döviz, Para, Damga Pulu, Değerli Kağıtlar, Hisse Senedi ve Tahvil Teslimleri" },
                    { new Guid("4be33588-f4fe-8695-05ab-0ba9311eccc5"), "320", "tam-istisna", true, "Geçici 16 (6111 sayılı K.) İSMEP Kapsamında İstanbul İl Özel İdaresi'ne Bağlı Olarak Faaliyet Gösteren \"İstanbul Proje Koordinasyon Birimi\"ne Yapılacak Teslim ve Hizmetler" },
                    { new Guid("4cfb4b67-60a0-bddb-8d5c-6e7abfcfe396"), "304", "tam-istisna", true, "13/a Deniz Hava ve Demiryolu Taşıma Araçlarının Teslimi İle İnşa, Tadil, Bakım ve Onarımları" },
                    { new Guid("4d29d801-446f-37fc-6b6d-cd8a64083aba"), "217", "kismi-istisna", true, "17/4-n Basın-Yayın ve Enformasyon Genel Müdürlüğüne Verilen Haber Hizmetleri" },
                    { new Guid("4fd0f701-c060-704a-fb44-8475536d1e7e"), "218", "kismi-istisna", true, "KDV 17/4-o md. Gümrük Antrepoları, Geçici Depolama Yerleri ile Gümrüklü Sahalarda Vergisiz Satış Yapılan İşyeri, Depo ve Ardiye Gibi Bağımsız Birimlerin Kiralanması" },
                    { new Guid("55285bd9-0f1d-1ca0-e657-1d48c8292058"), "211", "kismi-istisna", true, "17/4-h Zirai Amaçlı Su Teslimleri İle Köy Tüzel Kişiliklerince Yapılan İçme Suyu teslimleri" },
                    { new Guid("577aa6eb-0c9c-a0da-01f1-574d9976c250"), "335", "tam-istisna", true, "KDV 13/n Basılı Kitap ve Süreli Yayınların Teslimleri" },
                    { new Guid("57dda6d1-2ea6-5776-a3e8-8ff34f5e150f"), "327", "tam-istisna", true, "13/ı Gıda, Tarım ve Hayvancılık Bakanlığı Tarafından Tescil Edilmiş Gübrelerin İçeriğinde Bulunan Hammaddelerin Gübre Üreticilerine Teslimi" },
                    { new Guid("5a3bcd7a-8dd2-6a7c-9913-a95f11efefb2"), "238", "kismi-istisna", true, "17/4-u Varlıkların Varlık Kiralama Şirketlerine Devri İle Bu Varlıkların Varlık Kiralama Şirketlerince Kiralanması ve Devralınan Kuruma Devri" },
                    { new Guid("5d34e251-7a31-b9c3-f186-6a46775f5659"), "319", "tam-istisna", true, "13/g Başbakanlık Merkez Teşkilatına Yapılan Araç Teslimleri" },
                    { new Guid("5e29be4e-2b0d-daf7-3216-c486d1eb7012"), "350", "tam-istisna", true, "Diğerleri" },
                    { new Guid("62d463cb-c38f-eeb3-92db-f433f237ac5b"), "309", "tam-istisna", true, "13/e Liman Ve Hava Meydanlarının İnşası, Yenilenmesi Ve Genişletilmesi" },
                    { new Guid("657e5bc3-04e0-5983-6f83-6455b4134d59"), "221", "kismi-istisna", true, "Geçici 15 Konut Yapı Kooperatifleri, Belediyeler ve Sosyal Güvenlik Kuruluşlarına Verilen İnşaat Taahhüt Hizmeti" },
                    { new Guid("6650b305-96c1-9d4f-30c2-bf9824a71832"), "326", "tam-istisna", true, "13/ı Gıda, Tarım ve Hayvancılık Bakanlığı Tarafından Tescil Edilmiş Gübrelerin Teslimi" },
                    { new Guid("6e907887-bc3e-28a5-7f19-9c2f4d059d78"), "209", "kismi-istisna", true, "17/4-e Banka ve Sigorta Muameleleri Vergisi Kapsamına Giren İşlemler" },
                    { new Guid("6fab81c1-a932-d900-56bb-dda6f10d4eb2"), "236", "kismi-istisna", true, "19/2 Usulüne Göre Yürürlüğe Girmiş Uluslararası Anlaşmalar Kapsamındaki İstisnalar (İade Hakkı Tanınmayan)" },
                    { new Guid("7b6100be-d6da-bcfc-ecd3-ad143fd5ab01"), "336", "tam-istisna", true, "Geçici 40 UEFA Müsabakaları Kapsamında Yapılacak Teslim ve Hizmetler" },
                    { new Guid("8070a6b0-17ca-1876-d8f2-18ad2fb2ab7a"), "212", "kismi-istisna", true, "17/4-ı Serbest Bölgelerde Verilen Hizmetler" },
                    { new Guid("875a0f5b-f7f2-597d-8ac4-c606dbe4b856"), "237", "kismi-istisna", true, "17/4-t 5300 Sayılı Kanuna Göre Düzenlenen Ürün Senetlerinin İhtisas/Ticaret Borsaları Aracılığıyla İlk Teslimlerinden Sonraki Teslim" },
                    { new Guid("8b846bdb-91af-5c53-f1a9-ab159c345096"), "314", "tam-istisna", true, "19/2 Usulüne Göre Yürürlüğe Girmiş Uluslar Arası Anlaşmalar Kapsamındaki İstisnalar" },
                    { new Guid("8d769ffe-9dc3-1f9c-f52a-7c2cab2d36bd"), "239", "kismi-istisna", true, "17/4-y Taşınmazların Finansal Kiralama Şirketlerine Devri, Finansal Kiralama Şirketi Tarafından Devredene Kiralanması ve Devri" },
                    { new Guid("8efe5a10-44d8-9df8-9be3-f482c02d45e8"), "204", "kismi-istisna", true, "17/2-c Yabancı Diplomatik Organ Ve Hayır Kurumlarının Yapacakları Bağışlarla İlgili Mal Ve Hizmet Alışları" },
                    { new Guid("92f0d5c6-1a6e-9e9d-834e-75cdc67875a1"), "321", "tam-istisna", true, "Geçici 26 Birleşmiş Milletler (BM) ile Kuzey Atlantik Antlaşması Teşkilatı (NATO) Temsilcilikleri ve Bu Teşkilatlara Bağlı Program, Fon ve Özel İhtisas Kuruluşları ile İktisadi İşbirliği ve Kalkınma Teşkilatına (OECD) Resmi Kullanımları İçin Yapılacak Mal Teslimi ve Hizmet İfaları, Bunların Sosyal ve Ekonomik Yardım Amacıyla Bedelsiz Olarak Yapacakları Mal Teslimi ve Hizmet İfaları İle İlgili Bunlara Yapılan Mal Teslimi ve Hizmet İfaları" },
                    { new Guid("9332edd6-d137-abd3-170d-0feba4d5d63a"), "332", "tam-istisna", true, "KDV Geçici 39. Md. İmalat Sanayiinde Kullanılmak Üzere Yapılan Yeni Makina ve Teçhizat Teslimlerinde İstisna" },
                    { new Guid("9af2d568-f1a6-dca3-f943-44a39cd5cb19"), "331", "tam-istisna", true, "KDV 13/m md. Ar-Ge, Yenilik ve Tasarım Faaliyetlerinde Kullanılmak Üzere Yapılan Yeni Makina ve Teçhizat Teslimlerinde İstisna" },
                    { new Guid("a3d7b242-1099-cd7b-1fcc-ef1947e12823"), "328", "tam-istisna", true, "13/i Konut veya İşyeri Teslimleri" },
                    { new Guid("a7340c29-d770-05d5-723d-f42c50e9d370"), "213", "kismi-istisna", true, "17/4-j Boru Hattı İle Yapılan Petrol Ve Gaz Taşımacılığı" },
                    { new Guid("b071c0c6-e429-cf51-6aa7-39b50f2bc927"), "227", "kismi-istisna", true, "17/2-b Kanunların Gösterdiği Gerek Üzerine Bedelsiz Olarak Yapılan Teslim ve Hizmetler" },
                    { new Guid("b0ad3a08-6bdb-bef0-f2de-d5f9b406c835"), "223", "kismi-istisna", true, "Geçici 20/1 Teknoloji Geliştirme Bölgelerinde Yapılan İşlemler" },
                    { new Guid("b2fb262f-0c84-c825-52c7-e6cf807e82d8"), "322", "tam-istisna", true, "11/1-a Türkiye'de İkamet Etmeyenlere Özel Fatura ile Yapılan Teslimler (Bavul Ticareti)" },
                    { new Guid("b8e874ad-2fcc-15c6-7dd0-481f48b30034"), "317", "tam-istisna", true, "17/4-s Engellilerin Eğitimleri, Meslekleri ve Günlük Yaşamlarına İlişkin Araç-Gereç ve Bilgisayar Programları" },
                    { new Guid("ba63fc73-3a85-2917-2c13-703dd1e0f873"), "225", "kismi-istisna", true, "Geçici 23 Milli Eğitim Bakanlığına Yapılan Bilgisayar Bağışları İle İlgili Teslimler" },
                    { new Guid("bdb0ae54-0ccb-1b4e-3481-d6dc6b02c1fc"), "229", "kismi-istisna", true, "17/2-b Gıda Bankacılığı Faaliyetinde Bulunan Dernek ve Vakıflara Bağışlanan Gıda, Temizlik, Giyecek ve Yakacak Maddeleri" },
                    { new Guid("c131b828-7815-85af-dea3-44bddc525484"), "241", "kismi-istisna", true, "TürkAkım Gaz Boru Hattı Projesine İlişkin Anlaşmanın (9/b) Maddesinde Yer Alan Hizmetler" },
                    { new Guid("c13ce395-a49f-b453-6343-9086249eb50a"), "351", "tam-istisna", true, "KDV - İstisna Olmayan Diğer (0 KDV'li, istisna olmayan faturalar için)" },
                    { new Guid("c28c623e-3386-c22d-ad66-d0901914eaec"), "333", "tam-istisna", true, "KDV 13/k md. Kapsamında Genel ve Özel Bütçeli Kamu İdarelerine, İl Özel İdarelerine, Belediyelere ve Köylere bağışlanan Tesislerin İnşasına İlişkin İstisna" },
                    { new Guid("c8b5e249-298c-152e-ab41-0bcf3d21ee6b"), "315", "tam-istisna", true, "14/3 İhraç Konusu Eşyayı Taşıyan Kamyon, Çekici ve Yarı Romorklara Yapılan Motorin Teslimleri" },
                    { new Guid("caed5967-dfba-4d29-42a3-0b033154eee8"), "337", "tam-istisna", true, "Türk Akım Gaz Boru Hattı Projesine İlişkin Anlaşmanın (9/h) Maddesi Kapsamındaki Gaz Taşıma Hizmetleri" },
                    { new Guid("cf32c0bf-5af9-b8b2-66e7-cc04e24176dc"), "228", "kismi-istisna", true, "17/2-b Kanunun (17/1) Maddesinde Sayılan Kurum ve Kuruluşlara Bedelsiz Olarak Yapılan Teslimler" },
                    { new Guid("d0a63a38-f183-c478-680f-2132aa81a16c"), "302", "tam-istisna", true, "11/1-a Hizmet İhracatı" },
                    { new Guid("d4c8da78-66bd-bd8b-ef17-66b221f38bb4"), "230", "kismi-istisna", true, "17/4-g Külçe Altın, Külçe Gümüş Ve Kiymetli Taşlarin Teslimi" },
                    { new Guid("d78064cc-0bea-6d0a-e58a-c3873633ffa7"), "242", "kismi-istisna", true, "KDV 17/4-ö md. Gümrük Antrepoları, Geçici Depolama Yerleri ile Gümrüklü Sahalarda, İthalat ve İhracat İşlemlerine konu mallar ile transit rejim kapsamında işlem gören mallar için verilen ardiye, depolama ve terminal hizmetleri" },
                    { new Guid("e1aead14-6e3f-60bb-e514-c054a7cb83e8"), "240", "kismi-istisna", true, "17/4-z Patentli Veya Faydalı Model Belgeli Buluşa İlişkin Gayri Maddi Hakların Kiralanması, Devri ve Satışı" },
                    { new Guid("e23e4e02-1111-44ab-93f7-9311dfa157f0"), "308", "tam-istisna", true, "13/d Teşvikli Yatırım Mallarının Teslimi" },
                    { new Guid("e3842921-7449-c23a-0796-ac709069104e"), "334", "tam-istisna", true, "KDV 13/l md. Kapsamında Yabancılara Verilen Sağlık Hizmetlerinde İstisna" },
                    { new Guid("e4294b77-8f2f-14ac-ec8e-25f029e0ff8c"), "234", "kismi-istisna", true, "17/4-ş Konut Finansmanı Amacıyla Teminat Gösterilen ve İpotek Konulan Konutların Teslimi" },
                    { new Guid("e515ef1e-681f-2633-9217-e28ea1485d34"), "220", "kismi-istisna", true, "17/4-r İki Tam Yıl Süreyle Sahip Olunan Taşınmaz ve İştirak Hisseleri Satışları" },
                    { new Guid("e5d51053-b873-f12f-c668-61b6765361ee"), "215", "kismi-istisna", true, "17/4-l Varlık Yönetim Şirketlerinin İşlemleri" },
                    { new Guid("e9b896e8-91db-e80f-1acd-619df6521412"), "206", "kismi-istisna", true, "17/2-e Mesleki Kuruluşların İşlemleri" },
                    { new Guid("ea7a4513-29d6-81d1-d630-12c247e11aed"), "202", "kismi-istisna", true, "17/2-a Sağlık, Çevre Ve Sosyal Yardım Amaçlı İşlemler" },
                    { new Guid("eaf4178e-1f62-5c3d-4268-adfa7e6804ac"), "235", "kismi-istisna", true, "16/1-c Transit ve Gümrük Antrepo Rejimleri İle Geçici Depolama ve Serbest Bölge Hükümlerinin Uygulandığiı Malların Teslimi" },
                    { new Guid("ee4f8925-561b-4c43-2888-b0bab6d1ab6c"), "303", "tam-istisna", true, "11/1-a Roaming Hizmetleri" },
                    { new Guid("ee85673f-77db-1166-71fd-f23715c06797"), "214", "kismi-istisna", true, "17/4-k Organize Sanayi Bölgelerindeki Arsa ve İşyeri Teslimleri İle Konut Yapı Kooperatiflerinin Üyelerine Konut Teslimleri" },
                    { new Guid("fb18c799-c7ef-db09-6215-7d481bee83c5"), "318", "tam-istisna", true, "Geçici 29 3996 Sayılı Kanuna Göre Yap-İşlet-Devret Modeli Çerçevesinde Gerçekleştirilecek Projeler, 3359 Sayılı Kanuna Göre Kiralama Karşılığı Yaptırılan Sağlık Tesislerine İlişkin Projeler ve 652 Sayılı Kanun Hükmünde Kararnameye Göre Kiralama Karşılığı Yaptırılan Eğitim Öğretim Tesislerine İlişkin Projelere İlişkin Teslim ve Hizmetler" },
                    { new Guid("fd23d8c7-1e83-e14a-6eb4-eb75a2bb075a"), "312", "tam-istisna", true, "15/a Diplomatik Organ Ve Misyonlara Yapılan Teslim ve Hizmetler" },
                    { new Guid("fd314823-b17e-1ad2-b775-d2d60dc2c73d"), "313", "tam-istisna", true, "15/b Uluslararası Kuruluşlara Yapılan Teslim ve Hizmetler" },
                    { new Guid("fd7eba4e-0867-8e29-b430-eb1cd5ba09a8"), "231", "kismi-istisna", true, "17/4-g Metal Plastik, Lastik, Kauçuk, Kağit, Cam Hurda Ve Atıkların Teslimi" },
                    { new Guid("ffff97fc-919b-d46e-5026-952b71a356a2"), "306", "tam-istisna", true, "13/c Petrol Aramaları ve Petrol Boru Hatlarının İnşa ve Modernizasyonuna İlişkin Yapılan Teslim ve Hizmetler" }
                });

            migrationBuilder.InsertData(
                table: "withholding_codes",
                columns: new[] { "Id", "Code", "IsActive", "Name", "Tenths" },
                values: new object[,]
                {
                    { new Guid("00fde86a-05d3-8af3-70c3-62202ba08741"), "612", true, "TEMİZLİK HİZMETİ [GT 117-Bölüm (3.2.10)]", 9 },
                    { new Guid("01ec2e2b-f8f8-4c75-0332-9c25c0338453"), "621", true, "METAL, PLASTİK, LASTİK, KAUÇUK, KÂĞIT VE CAM HURDA VE ATIKLARDAN ELDE EDİLEN HAMMADDE TESLİMİ [GT 117-Bölüm (3.3.4)]", 9 },
                    { new Guid("02c4739b-3994-f384-150c-df5aafcc4e2c"), "618", true, "HURDA METALDEN ELDE EDİLENLER DIŞINDAKİ BAKIR, ÇİNKO VE ALÜMİNYUM KÜLÇE TESLİMLERİ [GT 117-Bölüm (3.3.1)]", 7 },
                    { new Guid("03cea1c6-7c82-b4e8-b33e-f1b0f0cb9b64"), "616", true, "Diğer Hizmetler [KDVGUT-(I/C-2.1.3.2.13)]", 5 },
                    { new Guid("1408e4a4-2f41-7b17-4ae4-07cc820e1957"), "604", true, "YEMEK SERVİS HİZMETİ [GT 117-Bölüm (3.2.4)]", 5 },
                    { new Guid("1622b7a4-be95-e86c-7a99-051d934d7c6f"), "603", true, "MAKİNE, TEÇHİZAT, DEMİRBAŞ VE TAŞITLARA AİT TADİL, BAKIM VE ONARIM HİZMETLERİ [GT 117-Bölüm (3.2.3)]", 7 },
                    { new Guid("1e61d964-d73b-5b38-6837-e4030312f0fd"), "623", true, "AĞAÇ VE ORMAN ÜRÜNLERİ TESLİMİ [GT 117-Bölüm (3.3.6)]", 5 },
                    { new Guid("29a4765e-db6f-9a72-4d62-0114c871bcc5"), "615", true, "HER TÜRLÜ BASKI VE BASIM HİZMETLERİ [GT 117-Bölüm (3.2.12)]", 7 },
                    { new Guid("2c4ce241-e123-5285-ff7b-586efe7144ab"), "625", true, "TİCARİ REKLAM HİZMETLERİ [KDVGUT-(I/C-2.1.3.2.15)]", 3 },
                    { new Guid("2fc389a0-ac51-b2d5-5b1c-f9999f89d955"), "606", true, "İŞGÜCÜ TEMİN HİZMETLERİ [GT 117-Bölüm (3.2.5)]", 9 },
                    { new Guid("371254ce-8dad-32b4-6182-b1de217c30e3"), "613", true, "ÇEVRE VE BAHÇE BAKIM HİZMETLERİ [GT 117-Bölüm (3.2.10)]", 9 },
                    { new Guid("5444ade0-9099-54bb-1799-cbb22d6a351b"), "617", true, "HURDA METALDEN ELDE EDİLEN KÜLÇE TESLİMLERİ [GT 117- Bölüm (3.3.1)]", 7 },
                    { new Guid("586030e5-283c-c4e1-9b25-53983d155cc4"), "619", true, "BAKIR, ÇİNKO VE ALÜMİNYUM ÜRÜNLERİNİN TESLİMİ [GT 117- Bölüm (3.3.2)]", 7 },
                    { new Guid("69a7977f-e3cb-43f9-9845-98aed0724131"), "605", true, "ORGANİZASYON HİZMETİ [GT 117-Bölüm (3.2.4)]", 5 },
                    { new Guid("6ef74048-7e5f-c245-8715-4fd880dd0345"), "609", true, "FASON OLARAK YAPTIRILAN TEKSTİL VE KONFEKSİYON İŞLERİ, ÇANTA VE AYAKKABI DİKİM İŞLERİ VE BU İŞLERE ARACILIK HİZMETLERİ [GT 117-Bölüm (3.2.7)]", 7 },
                    { new Guid("81c13202-a32a-13b6-611e-93df61f986e3"), "622", true, "PAMUK, TİFTİK, YÜN VE YAPAĞI İLE HAM POST VE DERİ TESLİMLERİ [GT 117-Bölüm (3.3.5)]", 9 },
                    { new Guid("891aa5b8-6562-8b15-5ec5-a3a20ccad127"), "607", true, "ÖZEL GÜVENLİK HİZMETİ [GT 117-Bölüm (3.2.5)]", 9 },
                    { new Guid("a08d2f9f-da8e-33f0-dad5-707eb05ef7d9"), "610", true, "TURİSTİK MAĞAZALARA VERİLEN MÜŞTERİ BULMA / GÖTÜRME HİZMETLERİ [GT 117-Bölüm (3.2.8)]", 9 },
                    { new Guid("a60814ba-4cf0-0553-29d4-8029c1021fd2"), "608", true, "YAPI DENETİM HİZMETLERİ [GT 117-Bölüm (3.2.6)]", 9 },
                    { new Guid("ac0bdf97-8b59-2b08-2639-8613d4a94e10"), "626", true, "DİĞER TESLİMLER [KDVGUT-(I/C-2.1.3.3.7.)]", 2 },
                    { new Guid("b92ca3d8-3322-3781-38ff-8099b8d881bd"), "620", true, "İSTİSNADAN VAZGEÇENLERİN HURDA VE ATIK TESLİMİ [GT 117- Bölüm (3.3.3)]", 7 },
                    { new Guid("baae4273-392b-33b5-5063-5f6ae2ed6049"), "614", true, "SERVİS TAŞIMACILIĞI HİZMETİ [GT 117-Bölüm (3.2.11)]", 5 },
                    { new Guid("cfaadad5-0a90-f56c-92fb-0e66e7bc405f"), "601", true, "YAPIM İŞLERİ İLE BU İŞLERLE BİRLİKTE İFA EDİLEN MÜHENDİSLİK- MİMARLIK VE ETÜT-PROJE HİZMETLERİ [GT 117-Bölüm (3.2.1)]", 4 },
                    { new Guid("ebcc8e91-181c-87f1-e869-b735c566b210"), "624", true, "YÜK TAŞIMACILIĞI HİZMETİ [KDVGUT-(I/C-2.1.3.2.11)]", 2 },
                    { new Guid("ec5b38f9-5276-e2f1-2bc2-562aa5e56e95"), "602", true, "ETÜT, PLAN-PROJE, DANIŞMANLIK, DENETİM VE BENZERİ HİZMETLER[GT 117-Bölüm (3.2.2)]", 9 },
                    { new Guid("f4e233df-9f9a-89bf-77b4-fdc8da042683"), "611", true, "SPOR KULÜPLERİNİN YAYIN, REKLÂM VE İSİM HAKKI GELİRLERİNE KONU İŞLEMLERİ [GT 117-Bölüm (3.2.9)]", 9 }
                });

            migrationBuilder.CreateIndex(
                name: "IX_document_series_TenantId",
                table: "document_series",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_document_series_TenantId_BranchId_KindGroup_Mode",
                table: "document_series",
                columns: new[] { "TenantId", "BranchId", "KindGroup", "Mode" });

            migrationBuilder.CreateIndex(
                name: "IX_document_series_TenantId_KindGroup_Mode_Prefix_Year",
                table: "document_series",
                columns: new[] { "TenantId", "KindGroup", "Mode", "Prefix", "Year" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_exchange_rates_Currency_Type_Date",
                table: "exchange_rates",
                columns: new[] { "Currency", "Type", "Date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_exemption_codes_Code",
                table: "exemption_codes",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_invoice_due_lines_InvoiceId_Sort",
                table: "invoice_due_lines",
                columns: new[] { "InvoiceId", "Sort" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_invoice_due_lines_TenantId",
                table: "invoice_due_lines",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_invoice_lines_InvoiceId_Sort",
                table: "invoice_lines",
                columns: new[] { "InvoiceId", "Sort" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_invoice_lines_ProductId",
                table: "invoice_lines",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_invoice_lines_TenantId",
                table: "invoice_lines",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_invoice_lines_VariantId",
                table: "invoice_lines",
                column: "VariantId");

            migrationBuilder.CreateIndex(
                name: "IX_invoices_CurrentAccountId",
                table: "invoices",
                column: "CurrentAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_invoices_LedgerEntryId",
                table: "invoices",
                column: "LedgerEntryId");

            migrationBuilder.CreateIndex(
                name: "IX_invoices_ReturnOfInvoiceId",
                table: "invoices",
                column: "ReturnOfInvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_invoices_SeriesId",
                table: "invoices",
                column: "SeriesId");

            migrationBuilder.CreateIndex(
                name: "IX_invoices_supplier_external_number",
                table: "invoices",
                columns: new[] { "TenantId", "CurrentAccountId", "ExternalNumber" },
                unique: true,
                filter: "\"ExternalNumber\" IS NOT NULL AND \"Status\" <> 'Cancelled'");

            migrationBuilder.CreateIndex(
                name: "IX_invoices_TenantId",
                table: "invoices",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_invoices_TenantId_CurrentAccountId_Date",
                table: "invoices",
                columns: new[] { "TenantId", "CurrentAccountId", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_invoices_TenantId_Number",
                table: "invoices",
                columns: new[] { "TenantId", "Number" },
                unique: true,
                filter: "\"Number\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_invoices_TenantId_Status_Date",
                table: "invoices",
                columns: new[] { "TenantId", "Status", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_invoices_WarehouseId",
                table: "invoices",
                column: "WarehouseId");

            migrationBuilder.CreateIndex(
                name: "IX_withholding_codes_Code",
                table: "withholding_codes",
                column: "Code",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "exchange_rates");

            migrationBuilder.DropTable(
                name: "exemption_codes");

            migrationBuilder.DropTable(
                name: "invoice_due_lines");

            migrationBuilder.DropTable(
                name: "invoice_lines");

            migrationBuilder.DropTable(
                name: "withholding_codes");

            migrationBuilder.DropTable(
                name: "invoices");

            migrationBuilder.DropTable(
                name: "document_series");

            migrationBuilder.DropColumn(
                name: "Rate",
                table: "ledger_entries");

            migrationBuilder.DropColumn(
                name: "SourceAmount",
                table: "ledger_entries");

            migrationBuilder.DropColumn(
                name: "SourceCurrency",
                table: "ledger_entries");
        }
    }
}
