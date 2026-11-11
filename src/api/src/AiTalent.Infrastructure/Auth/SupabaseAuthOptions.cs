namespace AiTalent.Infrastructure.Auth;

public sealed class SupabaseAuthOptions
{
    /// <summary>URL del proyecto Supabase (SUPABASE_URL).</summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>Clave pública (anon/publishable). Nunca la service role key.</summary>
    public string AnonKey { get; set; } = string.Empty;
}
