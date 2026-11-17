using System.ComponentModel.DataAnnotations;

namespace CareNexus.Models;

// What the Login form asks for
public class LoginViewModel
{
    [Required]
    public string Role { get; set; } = Roles.Patient;   // which role button was picked

    [Required(ErrorMessage = "Please type your ID or email.")]
    [Display(Name = "ID or email")]
    public string Login { get; set; } = "";             // "101" or "patient101@carenexus.com"

    [Required, DataType(DataType.Password)]
    [Display(Name = "Password (4 digits)")]
    public string Password { get; set; } = "";
}
