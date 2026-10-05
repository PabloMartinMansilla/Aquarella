namespace Aquarella.Services;

// No image/PDF reader is configured. A real adapter must return unit costs explicitly,
// never infer them from document totals/tax/subtotals. No documents leave the server.
public sealed record InvoiceDetectedLine(string Name, int? Quantity, decimal? UnitCost, decimal? Subtotal = null);
public sealed record InvoiceExtraction(IReadOnlyList<InvoiceDetectedLine> Lines, string? Supplier = null,
    DateOnly? Date = null, decimal? Subtotal = null, decimal? Tax = null, decimal? Discount = null, decimal? Total = null);
public interface IInvoiceExtractor
{
    Task<InvoiceExtraction> ExtractAsync(Stream document, string contentType, CancellationToken cancellationToken);
}
public sealed class UnavailableInvoiceExtractor : IInvoiceExtractor
{
    public Task<InvoiceExtraction> ExtractAsync(Stream document, string contentType, CancellationToken cancellationToken)
        => throw new InvoiceExtractionException("La lectura automática todavía no está conectada. Podés transcribir los productos para revisarlos o probar el ejemplo simulado.");
}
public sealed class InvoiceExtractionException(string message) : Exception(message);

public static class InvoiceDocument
{
    public const long MaxBytes = 10 * 1024 * 1024;
    public static string DetectType(byte[] bytes)
    {
        if (bytes.Length >= 5 && bytes.AsSpan(0, 5).SequenceEqual("%PDF-"u8)) return "application/pdf";
        if (bytes.Length >= 8 && bytes.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })) return "image/png";
        if (bytes.Length >= 3 && bytes[0] == 255 && bytes[1] == 216 && bytes[2] == 255) return "image/jpeg";
        if (bytes.Length >= 12 && bytes.AsSpan(0, 4).SequenceEqual("RIFF"u8) && bytes.AsSpan(8, 4).SequenceEqual("WEBP"u8)) return "image/webp";
        throw new InvoiceExtractionException("Archivo no válido. Usá una imagen JPG, PNG, WebP o un PDF, de hasta 10 MB.");
    }
}
