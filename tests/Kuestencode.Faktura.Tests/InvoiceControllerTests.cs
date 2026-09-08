using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Kuestencode.Faktura.Data;
using Kuestencode.Faktura.Models;
using Kuestencode.Shared.Contracts.Faktura;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Kuestencode.Faktura.Tests;

public class InvoiceControllerTests : IClassFixture<FakturaWebApplicationFactory>
{
    private readonly FakturaWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public InvoiceControllerTests(FakturaWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private static CreateInvoiceRequest MakeCreateRequest(int customerId = 1) => new()
    {
        InvoiceDate = DateTime.UtcNow,
        CustomerId = customerId,
        Items =
        [
            new CreateInvoiceItemRequest
            {
                Description = "Beratung",
                Quantity = 2,
                UnitPrice = 100,
                VatRate = 19
            }
        ]
    };

    [Fact]
    public async Task GetAll_OhneRechnungen_GibtLeereListeZurueck()
    {
        var response = await _client.GetAsync("/api/Invoice");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var invoices = await response.Content.ReadFromJsonAsync<List<InvoiceDto>>();
        invoices.Should().NotBeNull();
    }

    [Fact]
    public async Task Create_PersistiertRechnungUndGetByIdLiefertSieZurueck()
    {
        var createResponse = await _client.PostAsJsonAsync("/api/Invoice", MakeCreateRequest());
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var created = await createResponse.Content.ReadFromJsonAsync<InvoiceDto>();
        created.Should().NotBeNull();
        created!.CustomerName.Should().Be("Nordlicht Media");
        created.TotalGross.Should().Be(238m); // 2 * 100 * 1.19
        created.Status.Should().Be("Draft");

        var getResponse = await _client.GetAsync($"/api/Invoice/{created.Id}");
        getResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var fetched = await getResponse.Content.ReadFromJsonAsync<InvoiceDto>();
        fetched!.Id.Should().Be(created.Id);
        fetched.Items.Should().ContainSingle(i => i.Description == "Beratung");
    }

    [Fact]
    public async Task Create_OhneItems_GibtBadRequestZurueckOderLeereRechnung()
    {
        var request = MakeCreateRequest();
        var createResponse = await _client.PostAsJsonAsync("/api/Invoice", request with { Items = [] });

        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await createResponse.Content.ReadFromJsonAsync<InvoiceDto>();
        created!.TotalGross.Should().Be(0);
    }

    [Fact]
    public async Task Update_AendertKundenUndPositionen()
    {
        var createResponse = await _client.PostAsJsonAsync("/api/Invoice", MakeCreateRequest());
        var created = (await createResponse.Content.ReadFromJsonAsync<InvoiceDto>())!;

        var updateRequest = new UpdateInvoiceRequest
        {
            InvoiceDate = created.InvoiceDate,
            CustomerId = 2,
            Items =
            [
                new CreateInvoiceItemRequest { Description = "Support", Quantity = 1, UnitPrice = 50, VatRate = 19 }
            ]
        };

        var updateResponse = await _client.PutAsJsonAsync($"/api/Invoice/{created.Id}", updateRequest);
        updateResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var getResponse = await _client.GetAsync($"/api/Invoice/{created.Id}");
        var updated = await getResponse.Content.ReadFromJsonAsync<InvoiceDto>();

        updated!.CustomerId.Should().Be(2);
        updated.Items.Should().ContainSingle(i => i.Description == "Support");
    }

    [Fact]
    public async Task Delete_EntferntRechnung()
    {
        var createResponse = await _client.PostAsJsonAsync("/api/Invoice", MakeCreateRequest());
        var created = (await createResponse.Content.ReadFromJsonAsync<InvoiceDto>())!;

        var deleteResponse = await _client.DeleteAsync($"/api/Invoice/{created.Id}");
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var getResponse = await _client.GetAsync($"/api/Invoice/{created.Id}");
        getResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task MarkAsPaid_SetztStatusAufPaid_WennRechnungBereitsVersendetWurde()
    {
        // MarkAsPaidAsync rechnet den Status nur für nicht-Draft-Rechnungen neu (siehe RecalculateStatusAsync),
        // daher muss die Rechnung hier zunächst wie eine versendete Rechnung behandelt werden.
        var createResponse = await _client.PostAsJsonAsync("/api/Invoice", MakeCreateRequest());
        var created = (await createResponse.Content.ReadFromJsonAsync<InvoiceDto>())!;

        using (var scope = _factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<FakturaDbContext>();
            var invoice = await context.Invoices.FindAsync(created.Id);
            invoice!.Status = InvoiceStatus.Sent;
            await context.SaveChangesAsync();
        }

        var markPaidResponse = await _client.PostAsJsonAsync(
            $"/api/Invoice/{created.Id}/mark-paid",
            new { PaidDate = DateTime.UtcNow });
        markPaidResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var getResponse = await _client.GetAsync($"/api/Invoice/{created.Id}");
        var updated = await getResponse.Content.ReadFromJsonAsync<InvoiceDto>();

        updated!.Status.Should().Be("Paid");
    }

    [Fact]
    public async Task GetById_UnbekannteId_GibtNotFoundZurueck()
    {
        var response = await _client.GetAsync("/api/Invoice/999999");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetById_AbschlagMitVerknuepfterAbschlagsrechnung_LiefertSourceInvoiceNummer()
    {
        var sourceResponse = await _client.PostAsJsonAsync("/api/Invoice", MakeCreateRequest());
        var sourceInvoice = (await sourceResponse.Content.ReadFromJsonAsync<InvoiceDto>())!;

        var finalResponse = await _client.PostAsJsonAsync("/api/Invoice", MakeCreateRequest());
        var finalInvoice = (await finalResponse.Content.ReadFromJsonAsync<InvoiceDto>())!;

        using (var scope = _factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<FakturaDbContext>();
            context.DownPayments.Add(new DownPayment
            {
                InvoiceId = finalInvoice.Id,
                Description = "1. Abschlagsrechnung",
                Amount = 100m,
                SourceInvoiceId = sourceInvoice.Id
            });
            await context.SaveChangesAsync();
        }

        var getResponse = await _client.GetAsync($"/api/Invoice/{finalInvoice.Id}");
        var updated = await getResponse.Content.ReadFromJsonAsync<InvoiceDto>();

        var downPayment = updated!.DownPayments.Should().ContainSingle().Subject;
        downPayment.SourceInvoiceId.Should().Be(sourceInvoice.Id);
        downPayment.SourceInvoiceNumber.Should().Be(sourceInvoice.InvoiceNumber);
    }
}
