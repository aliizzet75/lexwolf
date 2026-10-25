using System;

namespace LexWolf.Models;

/// <summary>
/// Ein einzelnes Live-Ereignis aus dem ReAct-SSE-Stream des Backends
/// (GET /api/search/stream). Deckt T#295 ab: Anwalt sieht pro Schritt,
/// welche Quelle durchsucht wurde, was gefunden/verworfen wurde und mit
/// welcher Konfidenz — statt einer Black Box.
/// </summary>
public sealed class ReActStepEvent
{
    public string Step { get; init; } = "";
    public string Text { get; init; } = "";
    public string? Quelle { get; init; }
    public double? Confidence { get; init; }
    public string? Status { get; init; }

    public bool IstVerworfen => string.Equals(Status, "verworfen", StringComparison.OrdinalIgnoreCase);

    public string ConfidenceLabel => Confidence.HasValue ? $"{Confidence.Value * 100:F0}%" : "";
}
