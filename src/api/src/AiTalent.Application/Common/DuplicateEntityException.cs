namespace AiTalent.Application.Common;

/// <summary>Un registro chocó con una restricción única (p. ej. dos altas simultáneas del mismo usuario).</summary>
public sealed class DuplicateEntityException(string message, Exception? inner = null) : Exception(message, inner);
