using Microsoft.AspNetCore.Mvc.RazorPages;

namespace notes.Pages.Partials;

public class NDirectory : PageModel
{
    public string VirtualPath { get; init; } = "";
    public string PhysicalPath { get; init; } = "";
}