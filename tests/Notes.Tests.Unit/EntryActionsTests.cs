using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Primitives;
using Notes.Features.EntryActions;

namespace Notes.Tests.Unit;

public sealed class EntryActionsTests
{
    /// <summary>An action that answers GET and POST with <paramref name="parameter"/>, and does nothing.</summary>
    private sealed class Action(string? parameter) : IEntryGetAction, IEntryPostAction
    {
        public string? Parameter => parameter;
        public AccessNeeded Needs => AccessNeeded.Known;
        public Task<IActionResult> GetAsync(EntryRequest request) => Task.FromResult<IActionResult>(new OkResult());
        public Task<IActionResult> PostAsync(EntryRequest request) => Task.FromResult<IActionResult>(new OkResult());
    }

    /// <summary>An action that only answers DELETE, without a parameter.</summary>
    private sealed class DeleteAction : IEntryDeleteAction
    {
        public string? Parameter => null;
        public AccessNeeded Needs => AccessNeeded.Known;
        public Task<IActionResult> DeleteAsync(EntryRequest request) => Task.FromResult<IActionResult>(new OkResult());
    }

    private static QueryCollection Query(params (string Name, string Value)[] parameters)
        => new(parameters.ToDictionary(parameter => parameter.Name, parameter => new StringValues(parameter.Value)));

    [Fact]
    public void The_action_of_the_parameter_is_chosen_with_its_value()
    {
        Action rename = new("rename");
        EntryActions actions = new([rename, new Action("create")]);

        ActionChoice<IEntryPostAction> choice = actions.Choose<IEntryPostAction>(Query(("rename", "new.md"), ("unknown", "x")));

        Assert.Same(rename, choice.Action);
        Assert.Equal("new.md", choice.Value);
        Assert.False(choice.NamesSeveral);
    }

    [Fact]
    public void Parameters_are_found_ignoring_case()
    {
        Action rename = new("rename");

        Assert.Same(rename, new EntryActions([rename]).Choose<IEntryPostAction>(Query(("Rename", "x"))).Action);
    }

    [Fact]
    public void Without_a_parameter_the_default_action_of_the_method_is_chosen()
    {
        Action save = new(null);
        DeleteAction delete = new();
        EntryActions actions = new([save, new Action("rename"), delete]);

        Assert.Same(save, actions.Choose<IEntryPostAction>(Query(("edit", ""))).Action);
        Assert.Same(delete, actions.Choose<IEntryDeleteAction>(Query()).Action);
    }

    [Fact]
    public void A_method_without_a_default_action_chooses_none()
        => Assert.Null(new EntryActions([new Action("raw")]).Choose<IEntryGetAction>(Query(("edit", ""))).Action);

    [Fact]
    public void Two_actions_in_one_query_are_neither_of_them()
    {
        ActionChoice<IEntryPostAction> choice = new EntryActions([new Action(null), new Action("rename"), new Action("selection")])
            .Choose<IEntryPostAction>(Query(("selection", "add"), ("rename", "x")));

        Assert.True(choice.NamesSeveral);
        Assert.Null(choice.Action);
    }

    [Fact]
    public void Two_actions_with_the_same_parameter_or_two_defaults_are_refused()
    {
        _ = Assert.Throws<InvalidOperationException>(() => new EntryActions([new Action("raw"), new Action("RAW")]));
        _ = Assert.Throws<InvalidOperationException>(() => new EntryActions([new Action(null), new Action(null)]));
    }

    [Fact]
    public void The_same_parameter_may_be_used_by_different_methods()
        => _ = new EntryActions([new Action("selection"), new DeleteAction()]);
}
