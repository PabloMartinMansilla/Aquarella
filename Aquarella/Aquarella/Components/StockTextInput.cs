using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Rendering;

namespace Aquarella.Components;

// Stock entry binds as the user types: Enter submits the visible value, not a stale on-blur value.
public sealed class StockTextInput : InputText
{
    public ElementReference InputElement { get; private set; }
    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "input");
        builder.AddMultipleAttributes(1, AdditionalAttributes);
        builder.AddAttribute(2, "class", CssClass);
        builder.AddAttribute(3, "value", CurrentValueAsString);
        builder.AddAttribute(4, "oninput", EventCallback.Factory.CreateBinder<string?>(this, value => CurrentValueAsString = value, CurrentValueAsString));
        builder.SetUpdatesAttributeName("value");
        builder.AddElementReferenceCapture(5, element => InputElement = element);
        builder.CloseElement();
    }
}
