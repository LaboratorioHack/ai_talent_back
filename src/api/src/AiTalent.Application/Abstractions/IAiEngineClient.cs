namespace AiTalent.Application.Abstractions;

/// <summary>Puerto hacia el AI Engine (Python): ingesta, extracción y evaluación de CVs.</summary>
public interface IAiEngineClient
{
    Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default);
}
