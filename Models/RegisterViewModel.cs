using System.ComponentModel.DataAnnotations;

namespace CareNexus.Models;

// What the Register form asks for
public class RegisterViewModel
{
    [Required, Display(Name = "Full Name")]
    public string Name { get; set; } = "";

    [Required, EmailAddress]
    public string Email { get; set; } = "";

    [Required, Phone]
    public string Phone { get; set; } = "";

    [Required, RegularExpression(@"^\d{4}$", ErrorMessage = "Password must be exactly 4 digits, e.g. 1234."),
     DataType(DataType.Password), Display(Name = "Password (4 digits)")]
    public string Password { get; set; } = "";

    [Compare("Password", ErrorMessage = "Passwords do not match."),
     DataType(DataType.Password), Display(Name = "Confirm Password")]
    public string ConfirmPassword { get; set; } = "";
}
