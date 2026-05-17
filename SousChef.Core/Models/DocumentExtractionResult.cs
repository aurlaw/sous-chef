namespace SousChef.Core.Models;

public record DocumentExtractionResult(
    string Text,
    DocumentType Type,
    int PageCount);

public enum DocumentType { Text, Image }
