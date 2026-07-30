using System.Text.Json;
using verii_wms_api_v2.Modules.IncomingInvoice.Infrastructure;
using Xunit;

namespace verii_wms_api_v2.QueryTests;

public sealed class IncomingInvoiceOcrParserTests
{
    [Fact]
    public void Azure_result_is_converted_to_reviewable_invoice_without_creating_receipt()
    {
        using var json = JsonDocument.Parse(
            """
            {
              "status": "succeeded",
              "analyzeResult": {
                "documents": [{
                  "confidence": 0.96,
                  "fields": {
                    "InvoiceId": { "valueString": "INV-2026-42" },
                    "InvoiceDate": { "valueDate": "2026-07-30" },
                    "VendorName": { "valueString": "Örnek Tedarikçi" },
                    "VendorTaxId": { "valueString": "1234567890" },
                    "InvoiceTotal": {
                      "valueCurrency": { "amount": 120, "currencyCode": "TRY" }
                    },
                    "SubTotal": { "valueCurrency": { "amount": 100, "currencyCode": "TRY" } },
                    "TotalTax": { "valueCurrency": { "amount": 20, "currencyCode": "TRY" } },
                    "Items": {
                      "valueArray": [{
                        "confidence": 0.91,
                        "valueObject": {
                          "ProductCode": { "valueString": "TED-001" },
                          "Description": { "valueString": "Test ürünü" },
                          "Quantity": { "valueNumber": 2 },
                          "Unit": { "valueString": "KOLI" },
                          "UnitPrice": { "valueCurrency": { "amount": 50 } },
                          "Amount": { "valueCurrency": { "amount": 100 } },
                          "Tax": { "valueCurrency": { "amount": 20 } }
                        }
                      }]
                    }
                  }
                }]
              }
            }
            """);

        var result = AzureDocumentIntelligenceOcrClient.Parse(
            json.RootElement, "operation-42");

        Assert.Equal("INV-2026-42", result.Invoice.InvoiceNo);
        Assert.Equal("1234567890", result.Invoice.Supplier.VknOrTckn);
        Assert.Equal(120m, result.Invoice.PayableAmount);
        var line = Assert.Single(result.Invoice.Lines);
        Assert.Equal("TED-001", line.StockCode);
        Assert.Equal(2m, line.Quantity);
        Assert.Equal("KOLI", line.UnitCode);
        Assert.Equal(0.96m, result.Confidence);
        Assert.Equal(0.91m, Assert.Single(result.LineConfidences));
    }
}
