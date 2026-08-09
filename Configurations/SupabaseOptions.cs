using System.ComponentModel.DataAnnotations;

namespace TransportesGutierrez.Api.Configurations;

public sealed class SupabaseOptions
{
    public const string SectionName = "Supabase";

    [Required, Url]
    public string Url { get; init; } = string.Empty;

    [Required]
    public string Key { get; init; } = string.Empty;
}
