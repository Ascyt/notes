using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace Notes.Features.Components.Assets;

/// <summary>
/// <c>&lt;script-texts id="upload-texts" texts="@Texts"/&gt;</c>: the texts a script of a component shows, as a
/// <c>&lt;script type="application/json"&gt;</c> block that the script reads with <c>textsOf</c> (site.js). Every public text property of
/// the object is in it (but those marked <see cref="NotForScriptsAttribute"/>), by its name in camelCase, so that what a site overrides in a
/// texts class reaches the script as well. A text whose
/// parts change has them in braces (<c>"Uploaded {count} files."</c>), which the script fills in with <c>fillIn</c>.
/// </summary>
[HtmlTargetElement("script-texts", TagStructure = TagStructure.WithoutEndTag)]
public sealed class ScriptTextsTagHelper : TagHelper
{
    private static readonly JsonSerializerOptions Options = new() { DictionaryKeyPolicy = JsonNamingPolicy.CamelCase };

    [HtmlAttributeName("id")]
    public string Id { get; set; } = "";

    /// <summary>A texts class of a component, or several of them (an array), whose texts the script all gets.</summary>
    [HtmlAttributeName("texts")]
    public object Texts { get; set; } = new();

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        // (Where two classes have a text of the same name, the later one's is taken)
        Dictionary<string, string?> texts = [];
        foreach (object of in Texts as object[] ?? [Texts])
        {
            foreach (PropertyInfo property in of.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (property.PropertyType == typeof(string) && property.GetIndexParameters().Length == 0
                    && property.GetCustomAttribute<NotForScriptsAttribute>() is null)
                    texts[property.Name] = (string?)property.GetValue(of);
            }
        }

        output.TagName = "script";
        output.TagMode = TagMode.StartTagAndEndTag;
        output.Attributes.SetAttribute("type", "application/json");
        output.Attributes.SetAttribute("id", Id);
        // Serialized for a script block: < > & are escaped, so nothing in a text can end the block
        _ = output.Content.SetHtmlContent(JsonSerializer.Serialize(texts, Options));
    }
}
