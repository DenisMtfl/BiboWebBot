using System.ComponentModel.DataAnnotations;

namespace BiboWebBot.VoebbParsing;

public sealed class VoebbCredentials
{
    [Required]
    public string CardId { get; set; } = string.Empty;

    [Required]
    public string Password { get; set; } = string.Empty;
}
