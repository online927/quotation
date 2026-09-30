using Microsoft.Extensions.DependencyInjection;
using Quotation.Core.Domain;
using Quotation.Data.Entities;
using Quotation.Server.Services;

namespace Quotation.Server.Tests;

/// <summary>Renderer that fails until switched on — simulates a transient PDF problem.</summary>
public sealed class FlakyRenderer(PdfQuotationRenderer real) : IQuotationDocumentRenderer
{
    public bool Fail { get; set; } = true;

    public byte[] Render(QuotationHeader quotation, CompanySettings company, QuotationSettings settings) =>
        Fail ? throw new IOException("Disk full (simulated)") : real.Render(quotation, company, settings);
}

public class PdfFailureTests : QuotationTestBase
{
    private FlakyRenderer? _renderer;

    public override async Task InitializeAsync()
    {
        Server.ConfigureServicesHook = services =>
        {
            services.AddSingleton<FlakyRenderer>();
            services.AddSingleton<IQuotationDocumentRenderer>(sp => _renderer = sp.GetRequiredService<FlakyRenderer>());
        };
        await base.InitializeAsync();
    }

    [Fact]
    public async Task Approved_quotation_is_never_lost_when_pdf_fails_and_can_be_regenerated()
    {
        var q = await Api.CreateQuotationAsync(await BevelRequest());
        var approved = await Api.ApproveQuotationAsync(q.Id);
        Assert.Equal(QuotationStatus.Approved, approved.Status);
        Assert.Contains("Disk full", approved.PdfError);
        Assert.False(approved.HasPdf);
        Assert.Equal(q.Number, approved.Number);

        // Still approved after a reload (e.g. application restart).
        Assert.Equal(QuotationStatus.Approved, (await Api.QuotationAsync(q.Id)).Status);

        _renderer!.Fail = false;
        var regenerated = await Api.RegeneratePdfAsync(q.Id);
        Assert.Equal(QuotationStatus.Generated, regenerated.Status);
        Assert.True(regenerated.HasPdf);
        Assert.Null(regenerated.PdfError);
    }
}
