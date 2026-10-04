using System.ComponentModel.DataAnnotations;

namespace Aquarella.Models;

public sealed class BusinessProfile
{
    [Required(ErrorMessage = "Ingresá el nombre del negocio.")]
    [StringLength(120, ErrorMessage = "El nombre admite hasta 120 caracteres.")]
    public string Name { get; set; } = "Aquarella";
    public string? LogoDataUrl { get; set; }
    [StringLength(1000, ErrorMessage = "La descripción admite hasta 1000 caracteres.")]
    public string? Description { get; set; }
    [Required(ErrorMessage = "Elegí un color HEX.")]
    [RegularExpression("^#[0-9a-fA-F]{6}$", ErrorMessage = "Usá un HEX válido, por ejemplo #28745B.")]
    public string PrimaryColor { get; set; } = "#F7F9F6";
    [Required(ErrorMessage = "Elegí un color HEX.")]
    [RegularExpression("^#[0-9a-fA-F]{6}$", ErrorMessage = "Usá un HEX válido, por ejemplo #EDF5EF.")]
    public string SecondaryColor { get; set; } = "#28745B";
    [Required(ErrorMessage = "Elegí un color HEX.")]
    [RegularExpression("^#[0-9a-fA-F]{6}$", ErrorMessage = "Usá un HEX válido, por ejemplo #203E32.")]
    public string TertiaryColor { get; set; } = "#203E32";
    public string? Phone { get; set; }
    [OptionalEmail(ErrorMessage = "Ingresá un email válido.")]
    public string? Email { get; set; }
    [OptionalUrl(ErrorMessage = "Ingresá una URL completa para el sitio web.")]
    public string? Website { get; set; }
    [OptionalUrl(ErrorMessage = "Ingresá una URL completa para Instagram.")]
    public string? Instagram { get; set; }
    [OptionalUrl(ErrorMessage = "Ingresá una URL completa para Facebook.")]
    public string? Facebook { get; set; }
    [OptionalUrl(ErrorMessage = "Ingresá una URL completa para TikTok.")]
    public string? TikTok { get; set; }
    [OptionalUrl(ErrorMessage = "Ingresá una URL completa para X.")]
    public string? X { get; set; }
    [OptionalUrl(ErrorMessage = "Ingresá una URL completa para LinkedIn.")]
    public string? LinkedIn { get; set; }
    [OptionalUrl(ErrorMessage = "Ingresá una URL completa para YouTube.")]
    public string? YouTube { get; set; }
    public string? Industry { get; set; }
    public string? Hours { get; set; }

    public BusinessProfile Copy() => (BusinessProfile)MemberwiseClone();
}

