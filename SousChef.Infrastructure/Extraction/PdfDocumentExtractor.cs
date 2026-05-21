using System.Runtime.InteropServices;
using System.Text;
using Docnet.Core;
using Docnet.Core.Models;
using Microsoft.Extensions.Logging;
using SkiaSharp;
using SousChef.Core.Common;
using SousChef.Core.Interfaces;
using SousChef.Core.Models;
using Tesseract;
using UglyToad.PdfPig;

namespace SousChef.Infrastructure.Extraction;

public class PdfDocumentExtractor : IDocumentExtractor
{
    private const double TextPageThreshold = 0.8;
    private const int MinCharsPerPage = 50;
    private const int MinExtractedChars = 200;
    private const int MinRecipeKeywordMatches = 2;

    private static readonly string[] RecipeKeywords =
    [
        "ingredient", "cup", "tbsp", "tsp", "tablespoon", "teaspoon",
        "minutes", "serves", "servings", "preheat", "bake", "cook",
        "mix", "stir", "combine", "chop", "slice", "boil", "simmer", "oven"
    ];

    private readonly ILogger<PdfDocumentExtractor> _logger;

    public PdfDocumentExtractor(ILogger<PdfDocumentExtractor> logger)
    {
        _logger = logger;
    }

    public async Task<Result<DocumentExtractionResult>> ExtractTextAsync(
        Stream pdfContent, CancellationToken ct = default)
    {
        using var ms = new MemoryStream();
        await pdfContent.CopyToAsync(ms, ct);
        var pdfBytes = ms.ToArray();

        try
        {
            var (documentType, pageCount) = DetectPdfType(pdfBytes);

            _logger.LogInformation(
                "PDF detected as {DocumentType}, {PageCount} pages",
                documentType, pageCount);

            var text = documentType == DocumentType.Text
                ? ExtractTextFromTextPdf(pdfBytes)
                : ExtractTextFromImagePdf(pdfBytes);

            if (string.IsNullOrWhiteSpace(text) || text.Length < MinExtractedChars)
            {
                _logger.LogWarning(
                    "Extracted text too short ({Chars} chars) — possible blank or corrupt PDF",
                    text?.Length ?? 0);
                return Result<DocumentExtractionResult>.Failure(
                    Error.Internal("Insufficient text extracted from PDF."));
            }

            var lowerText = text.ToLowerInvariant();
            var matchCount = RecipeKeywords.Count(kw => lowerText.Contains(kw));

            _logger.LogInformation(
                "Recipe keyword check: {MatchCount}/{Required} keywords found",
                matchCount, MinRecipeKeywordMatches);

            if (matchCount < MinRecipeKeywordMatches)
                return Result<DocumentExtractionResult>.Failure(
                    Error.Validation("Document does not appear to contain a recipe."));

            _logger.LogInformation(
                "Extraction complete: {Chars} chars extracted via {Type} path",
                text.Length, documentType);

            return Result<DocumentExtractionResult>.Success(
                new DocumentExtractionResult(text, documentType, pageCount));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PDF extraction failed");
            return Result<DocumentExtractionResult>.Failure(
                Error.Internal($"PDF extraction failed: {ex.Message}"));
        }
    }

    private static (DocumentType Type, int PageCount) DetectPdfType(byte[] pdfBytes)
    {
        using var document = PdfDocument.Open(pdfBytes);
        var pages = document.GetPages().ToList();
        var textPageCount = pages.Count(p =>
            p.GetWords().Count() * 5 >= MinCharsPerPage);

        var textRatio = (double)textPageCount / pages.Count;
        var type = textRatio >= TextPageThreshold ? DocumentType.Text : DocumentType.Image;

        return (type, pages.Count);
    }

    private static string ExtractTextFromTextPdf(byte[] pdfBytes)
    {
        using var document = PdfDocument.Open(pdfBytes);
        var sb = new StringBuilder();
        foreach (var page in document.GetPages())
            sb.AppendLine(string.Join(" ", page.GetWords().Select(w => w.Text)));
        return sb.ToString();
    }

    private string ExtractTextFromImagePdf(byte[] pdfBytes)
    {
        var sb = new StringBuilder();

        using var docReader = DocLib.Instance.GetDocReader(
            pdfBytes, new PageDimensions(1080, 1920));

        var pageCount = docReader.GetPageCount();

        for (var i = 0; i < pageCount; i++)
        {
            using var pageReader = docReader.GetPageReader(i);
            var rawBytes = pageReader.GetImage();
            var width = pageReader.GetPageWidth();
            var height = pageReader.GetPageHeight();

            var pngBytes = BgraToPng(rawBytes, width, height);

            using var engine = new TesseractEngine(
                GetTessDataPath(), "eng", EngineMode.Default);
            using var img = Pix.LoadFromMemory(pngBytes);
            using var page = engine.Process(img);

            var text = page.GetText();
            if (!string.IsNullOrWhiteSpace(text))
                sb.AppendLine(text);

            _logger.LogDebug(
                "Page {Page}/{Total} OCR complete, {Chars} chars",
                i + 1, pageCount, text?.Length ?? 0);
        }

        return sb.ToString();
    }

    private static string GetTessDataPath() =>
        Environment.GetEnvironmentVariable("TESSDATA_PREFIX")
            ?? Path.Combine(AppContext.BaseDirectory, "tessdata");

    // Docnet returns raw BGRA bytes; SkiaSharp converts to PNG for Tesseract Pix.
    private static byte[] BgraToPng(byte[] bgraBytes, int width, int height)
    {
        var info = new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var bitmap = new SKBitmap(info);
        Marshal.Copy(bgraBytes, 0, bitmap.GetPixels(), bgraBytes.Length);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }
}
