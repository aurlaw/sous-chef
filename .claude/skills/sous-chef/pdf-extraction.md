---
description: PDF extraction pipeline — Tesseract setup, SkiaSharp, PdfPig, Docnet, PipelineStage enum, extracted_text/extracted_data lifecycle, keyword pre-filter, LlmExtractionService, InvalidContent status, Linux Docker deps
globs:
  - "SousChef.Infrastructure/Extraction/**"
  - "SousChef.Api/Workers/ExtractionBackgroundService.cs"
alwaysApply: false
---

# PDF Extraction Skill

## When to use this skill
Load this skill when working on any of the following:
- `SousChef.Infrastructure/Extraction/PdfDocumentExtractor.cs`
- `SousChef.Infrastructure/Extraction/LlmExtractionService.cs`
- `SousChef.Api/Workers/ExtractionBackgroundService.cs` (extraction stages)
- Any code touching `extracted_text`, `extracted_data`, or `PipelineStage`
- Tesseract, Docnet, PdfPig, or SkiaSharp

---

## PDF Type Detection

`PdfDocumentExtractor` uses PdfPig to detect text vs image PDFs:
- Count pages where word count × 5 ≥ 50 chars
- If ≥ 80% of pages pass → text path (PdfPig extraction)
- Otherwise → image path (Docnet.Core + Tesseract OCR)

## Image Path: Docnet → SkiaSharp → Tesseract

Docnet returns raw BGRA bytes per page. These must be converted to PNG via **SkiaSharp** before passing to `Pix.LoadFromMemory()`. Do NOT use `System.Drawing.Common` — not cross-platform.

```csharp
using var bitmap = new SKBitmap(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
// copy rawBytes into bitmap pixels
using var image = SKImage.FromBitmap(bitmap);
using var data = image.Encode(SKEncodedImageFormat.Png, 100);
var pngBytes = data.ToArray();
using var pix = Pix.LoadFromMemory(pngBytes);
```

## Tesseract Native Libs (macOS — Apple Silicon)

`InteropDotNet` looks for dylibs in `{entryAssemblyDir}/x64/`. The `LinkTesseractNativeLibsMacOS` MSBuild target in `SousChef.Api.csproj` creates two symlinks after every build:

```
{outputDir}/x64/libleptonica-1.82.0.dylib → /opt/homebrew/lib/libleptonica.6.dylib
{outputDir}/x64/libtesseract50.dylib       → /opt/homebrew/lib/libtesseract.5.dylib
```

Do NOT use `DYLD_LIBRARY_PATH` or Aspire env vars — macOS Hardened Runtime strips them.
One-time setup: `make setup-native-libs`.

## TESSDATA_PREFIX

Set via Aspire env var to `./tessdata`. `eng.traineddata` is gitignored — download manually:
```bash
curl -L https://github.com/tesseract-ocr/tessdata/raw/main/eng.traineddata \
  -o SousChef.Api/tessdata/eng.traineddata
```

## Recipe Keyword Pre-filter

After text extraction, before LLM:
1. Minimum 200 chars → fail `DocumentExtraction` if under
2. At least 2 of: ingredient, cup, tbsp, tsp, tablespoon, teaspoon, minutes, serves, servings, preheat, bake, cook, mix, stir, combine, chop, slice, boil, simmer, oven → fail `RecipeValidation` if under

Uses `Error.Validation` code so BackgroundService can route to the correct `PipelineStage`.

## PipelineStage Enum

```
Download | DocumentExtraction | RecipeValidation | LlmExtraction | NotARecipe | JsonParsing
```

Lives in `SousChef.Core/Models/PipelineError.cs`. `PipelineError.ToJson()` → camelCase JSON → stored in `error` column.

## extracted_text vs extracted_data

| Column | Type | Set in | Overwritten? |
|---|---|---|---|
| `extracted_text` | text | Phase 3a | Never |
| `extracted_data` | jsonb | Phase 3b | Yes — during review edits |

Phase 3b reads `extracted_text` from the job row — never re-downloads from R2.

## LlmExtractionService

- Named HttpClient `"anthropic"` — base address from `Extraction:Endpoint`, timeout 120s
- Detects `{"error":"not_a_recipe","reason":"..."}` sentinel BEFORE `RecipeDto` deserialization
- `JsonNamingPolicy.SnakeCaseLower` + `PropertyNameCaseInsensitive` for Claude's snake_case JSON
- Logs token usage as structured log on every call

## InvalidContent Status

When Claude returns not-a-recipe sentinel:
- Job → `InvalidContent` (not `Failed`)
- `error` stores `PipelineError` with stage `NotARecipe`, Claude's reason as `detail`
- Hub pushes `JobInvalidContent` with reason + `extracted_text`
- Terminal state — reject and re-upload only. No override.

## Linux Docker (Phase 7)

```dockerfile
RUN apt-get install -y \
    tesseract-ocr tesseract-ocr-eng libleptonica-dev \
    libfontconfig1 libfreetype6 \
    libglib2.0-0 libnss3 libnspr4 libatk1.0-0 \
    libxcomposite1 libxdamage1 libxfixes3 libxrandr2 \
    libgbm1 libxkbcommon0
```

`TESSDATA_PREFIX` = `/usr/share/tesseract-ocr/5/tessdata` — set as DO App Platform env var.
