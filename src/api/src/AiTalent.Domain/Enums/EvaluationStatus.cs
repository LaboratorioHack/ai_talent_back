namespace AiTalent.Domain.Enums;

/// <summary>Resultado de comparar un candidato contra el perfil del cargo.</summary>
public enum EvaluationStatus
{
    Meets = 1,
    PartiallyMeets = 2,
    DoesNotMeet = 3,
}
